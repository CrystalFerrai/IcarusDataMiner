// Copyright 2022 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines location data for various foliage instances
	/// </summary>
	/// <remarks>
	/// This miner takes a long time to run, so it is disabled by default. You must name it explicitly on the command line to run it.
	/// </remarks>
	[DefaultEnabled(false)]
	internal class FoliageMiner : IDataMiner
	{
		// Items in this list will be ignored when searching for foliage. Values defined below.
		private static HashSet<string> sIgnoredItems;

		// The distance at which foliage instances will group together, in world units.
		// Smaller values create more smaller clusters while larger values create fewer larger clusters.
		private const float ClusterDistanceThreshold = WorldDataUtil.WorldCellSize * 0.1f;

		// The partition size used by the clustering algorithm, in world units. This value tunes the performance
		// of the algorithm by influencing the number of distance chacks between foliage instances.
		// The value must be at least double the value of ClusterDistanceThreshold. Larger values may improve
		// or worsen perfoamance depending on the distribution of foliage instances.
		private const float PartitionSize = WorldDataUtil.WorldCellSize * 0.25f;

		public string Name => "Foliage";

		static FoliageMiner()
		{
			ObjectTypeRegistry.RegisterClass("FLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));
			ObjectTypeRegistry.RegisterClass("IcarusFLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));

			// Using names from D_Itemable
			sIgnoredItems = new(StringComparer.OrdinalIgnoreCase)
			{
				"Item_Ice",
				"Item_Fiber",
				"Item_Organic_Resin",
				"Item_Oxite",
				"Item_Seed",
				"Item_Stick",
				"Item_Stone",
				"Item_Tree_Sap", 
				"Item_Wood"
			};
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			IDictionary<string, ISet<FItemableData>> meshItemRewardMap = BuildMeshItemRewardMap(providerManager, logger);

			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile))
				{
					logger.Information($"Skipping {packageName} due to missing map assets.");
					continue;
				}

				if (world.MinimapData == null)
				{
					logger.Information($"Skipping {packageName} due to missing map boundary data.");
					continue;
				}

				logger.Information($"Processing {packageFile.NameWithoutExtension}...");
				ProcessMap(packageFile, providerManager, world, meshItemRewardMap, config, logger);
			}

			return true;
		}

		private IDictionary<string, ISet<FItemableData>> BuildMeshItemRewardMap(IProviderManager providerManager, Logger logger)
		{
			IcarusDataTable<FFLODDescription> flodDescriptionTable = DataTables.LoadDataTable<FFLODDescription>(providerManager.DataProvider, "FLOD/D_FLODDescriptions.json");

			Dictionary<string, ISet<FItemableData>> meshItemRewardMap = new(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in flodDescriptionTable)
			{
				string? path = pair.Value.ViewTraceActor.GetAssetPath();
				if (path is null)
				{
					logger.Warning($"FLOD entry {pair.Key} missing view trace actor");
					continue;
				}

				if (!providerManager.AssetProvider.Files.TryGetValue(AssetUtil.GetPackageName(path, "uasset"), out GameFile? file))
				{
					logger.Warning($"FLOD entry {pair.Key} view trace actor not found at path {path}");
					continue;
				}

				Package package = (Package)providerManager.AssetProvider.LoadPackage(file);
				UBlueprintGeneratedClass? bpClass = (UBlueprintGeneratedClass?)package.ExportMap.FirstOrDefault(e => e.ClassName.Equals("BlueprintGeneratedClass"))?.ExportObject.Value;
				if (bpClass is null)
				{
					logger.Warning($"FLOD entry {pair.Key} view trace actor at path {path} does not contain a BlueprintGeneratedClass");
					continue;
				}

				UObject? bpDefaults = bpClass.ClassDefaultObject.ResolvedObject?.Object?.Value;
				if (bpDefaults is null)
				{
					logger.Warning($"FLOD entry {pair.Key} view trace actor at path {path} does not contain a ClassDefaultObject");
					continue;
				}

				HashSet<FRowHandle> itemRewards = new();

				foreach (FPropertyTag property in bpDefaults.Properties)
				{
					switch (property.Name.Text)
					{
						case "ResourceRewardRow":
							itemRewards.Add(FRowHandle.FromProperty(property, "D_ItemRewards"));
							break;
						case "HitableRewardRow":
							itemRewards.Add(FRowHandle.FromProperty(property, "D_ItemRewards"));
							break;
						case "TreePrimitiveTypesToItemRewards":
							{
								UScriptMap treeMap = ((MapProperty)property.Tag!).Value;
								foreach (var treePair in treeMap.Properties)
								{
									itemRewards.Add(FRowHandle.FromProperty(treePair.Value!));
								}
							}
							break;
					}
				}

				if (!pair.Value.ViewTraceActorItemRewards.IsNone)
				{
					itemRewards.Add(pair.Value.ViewTraceActorItemRewards);
				}

				if (itemRewards.Count == 0)
				{
					continue;
				}

				string? meshName = GetMeshNameFromFoliage(pair.Value.FoliageType.GetAssetPath()!, providerManager, logger);
				if (meshName is null)
				{
					logger.Warning($"FLOD entry {pair.Key} - unable to load foliage mesh asset {pair.Value.FoliageType.GetAssetPath()!}");
					continue;
				}

				HashSet<FItemableData> items = new(new IDataTableRowComparer<FItemableData>());
				foreach (FRowHandle treeItemReward in itemRewards)
				{
					AddItemRewards(meshName, treeItemReward, items, providerManager, logger);
				}

				if (items.Count > 0)
				{
					meshItemRewardMap.Add(meshName, items);
				}
			}

			return meshItemRewardMap;
		}

		private static string? GetMeshNameFromFoliage(string foliageAssetPath, IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.AssetProvider.Files.TryGetValue(AssetUtil.GetPackageName(foliageAssetPath, "uasset"), out GameFile? file))
			{
				logger.Warning($"Unable to load foliage mesh asset {foliageAssetPath}");
				return null;
			}
			Package package = (Package)providerManager.AssetProvider.LoadPackage(file);

			FObjectExport export = package.ExportMap[0];
			UObject obj = export.ExportObject.Value;
			FPackageIndex meshIndex = PropertyUtil.GetOrDefault<FPackageIndex>(obj, "Mesh");
			return meshIndex.Name;
		}

		private void AddItemRewards(string entryName, FRowHandle itemRewardRowHandle, HashSet<FItemableData> itemNames, IProviderManager providerManager, Logger logger)
		{
			if (!itemRewardRowHandle.IsNone)
			{
				if (providerManager.DataTables.TryResolveHandle(itemRewardRowHandle, out FItemRewards itemRewards))
				{
					foreach (FItemRewardEntry itemRewardEntry in itemRewards.Rewards)
					{
						FItemableData itemData = providerManager.DataTables.GetItemableData(itemRewardEntry);
						if (Equals(itemData, default(FItemableData)))
						{
							logger.Warning($"FLOD entry {entryName} has invalid item reward entry {itemRewardEntry.Item.RowName}");
							continue;
						}
						if (!sIgnoredItems.Contains(itemData.Name))
						{
							itemNames.Add(itemData);
						}
					}
				}
				else
				{
					logger.Warning($"FLOD entry {entryName} has invalid item rewards {itemRewardRowHandle}");
				}
			}
		}

		private void ProcessMap(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, IDictionary<string, ISet<FItemableData>> meshItemRewardMap, Config config, Logger logger)
		{
			Dictionary<string, FoliageData> foliageData = new();

			foreach (string levelPath in worldData.DeveloperLevels)
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Debug($"Searching {packageFile.NameWithoutExtension}");
				FindFoliage(packageFile, FVector.ZeroVector, foliageData, meshItemRewardMap, worldData, providerManager, logger);
			}

			if (worldData.TileRowCount == 0 || worldData.TileColumnCount == 0) return;

			for (int x = 0; x < worldData.TileRowCount; ++x)
			{
				for (int y = 0; y < worldData.TileColumnCount; ++y)
				{
					string packagePath = WorldDataUtil.GetPackageName(worldData.HeightmapLevels[y + x * worldData.TileColumnCount]);

					GameFile? packageFile;
					if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

					FVector origin = new(
						x * WorldDataUtil.WorldTileSize + worldData.MinimapData!.WorldBoundaryMin.X,
						-((y + 1) * WorldDataUtil.WorldTileSize + worldData.MinimapData!.WorldBoundaryMin.Y),
						0.0f);

					logger.Debug($"Searching {packageFile.NameWithoutExtension}");
					FindFoliage(packageFile, origin, foliageData, meshItemRewardMap, worldData, providerManager, logger);
				}
			}

			if (foliageData.Count == 0) return;

			foreach (FoliageData foliage in foliageData.Values)
			{
				foliage.ClusterBuilder.BuildClusters();
			}

			ExportData(mapAsset.NameWithoutExtension, foliageData, config, logger);

			ExportImages(mapAsset.NameWithoutExtension, providerManager, worldData, foliageData, config, logger);
		}

		private static void FindFoliage(GameFile mapAsset, FVector origin, IDictionary<string, FoliageData> foliageData, IDictionary<string, ISet<FItemableData>> flodItemRewardMap, WorldData worldData, IProviderManager providerManager, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int flodFismNameIndex = -1, icarusFlodFismNameIndex = -1, staticMeshNameIndex = -1, attachParentNameIndex = -1, relativeLocationNameIndex = -1, relativeRotationNameIndex = -1, relativeScaleIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "FLODFISMComponent":
						flodFismNameIndex = i;
						break;
					case "IcarusFLODFISMComponent":
						icarusFlodFismNameIndex = i;
						break;
					case "StaticMesh":
						staticMeshNameIndex = i;
						break;
					case "AttachParent":
						attachParentNameIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationNameIndex = i;
						break;
					case "RelativeRotation":
						relativeRotationNameIndex = i;
						break;
					case "RelativeScale3D":
						relativeScaleIndex = i;
						break;
				}
			}

			if (flodFismNameIndex < 0 && icarusFlodFismNameIndex < 0) return; // No foliage

			int flodFismTypeIndex = 0, icarusFlodFismTypeIndex = 0;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (mapPackage.ImportMap[i].ObjectName.Index == flodFismNameIndex)
				{
					flodFismTypeIndex = ~i;
				}
				else if (mapPackage.ImportMap[i].ObjectName.Index == icarusFlodFismNameIndex)
				{
					icarusFlodFismTypeIndex = ~i;
				}
			}

			Dictionary<int, FTransform> parentMap = new();

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;

				if ((flodFismNameIndex < 0 || export.ClassIndex.Index != flodFismTypeIndex) &&
					(icarusFlodFismNameIndex < 0 || export.ClassIndex.Index != icarusFlodFismTypeIndex))
				{
					continue;
				}

				UInstancedStaticMeshComponent fismObject = (UInstancedStaticMeshComponent)export.ExportObject.Value;

				if (fismObject.PerInstanceSMData == null || fismObject.PerInstanceSMData.Length == 0) continue;

				string? meshName = null;
				FTransform parentTransform = FTransform.Identity;
				for (int i = 0; i < fismObject.Properties.Count; ++i)
				{
					FPropertyTag prop = fismObject.Properties[i];
					if (prop.Name.Index == staticMeshNameIndex)
					{
						FPackageIndex staticMeshProperty = PropertyUtil.GetByIndex<FPackageIndex>(fismObject, i);
						meshName = staticMeshProperty.Name;
					}
					else if (prop.Name.Index == attachParentNameIndex)
					{
						FPackageIndex attachParentProperty = PropertyUtil.GetByIndex<FPackageIndex>(fismObject, i);

						if (parentMap.TryGetValue(attachParentProperty.Index, out FTransform? t))
						{
							parentTransform = t;
						}
						else
						{
							FVector translation = FVector.ZeroVector;
							FRotator rotation = FRotator.ZeroRotator;
							FVector scale = FVector.OneVector;

							UObject parentObject = attachParentProperty.ResolvedObject!.Object!.Value;
							for (int j = 0; j < parentObject.Properties.Count; ++j)
							{
								FPropertyTag parentProp = parentObject.Properties[j];
								if (parentProp.Name.Index == relativeLocationNameIndex)
								{
									translation = PropertyUtil.GetByIndex<FVector>(parentObject, j);
								}
								else if (parentProp.Name.Index == relativeRotationNameIndex)
								{
									rotation = PropertyUtil.GetByIndex<FRotator>(parentObject, j);
								}
								else if (parentProp.Name.Index == relativeScaleIndex)
								{
									scale = PropertyUtil.GetByIndex<FVector>(parentObject, j);
								}
							}

							parentTransform = new FTransform(rotation, translation, scale);
							parentMap.Add(attachParentProperty.Index, parentTransform);
						}
					}
				}
				if (meshName == null) continue;

				ISet<FItemableData>? itemRewards;
				if (!flodItemRewardMap.TryGetValue(meshName, out itemRewards))
				{
					continue;
				}

				foreach (FItemableData itemReward in itemRewards)
				{
					FoliageData? foliage;
					if (!foliageData.TryGetValue(itemReward.Name, out foliage))
					{
						foliage = new(worldData, itemReward, LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, itemReward.DisplayName));
						foliageData.Add(itemReward.Name, foliage);
					}

					foreach (FInstancedStaticMeshInstanceData instanceData in fismObject.PerInstanceSMData)
					{
						foliage.ClusterBuilder.AddLocation(origin + parentTransform.TransformPosition(instanceData.TransformData.Translation));
					}
				}
			}
		}

		private void ExportData(string mapName, Dictionary<string, FoliageData> foliageData, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, "Data", mapName);

			foreach (var pair in foliageData)
			{
				string outPath = Path.Combine(config.OutputDirectory, outDir, $"{pair.Value.DisplayName}.csv");

				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					writer.WriteLine("x,y,radius,count");
					for (int i = 0; i < pair.Value.Clusters!.Count; ++i)
					{
						writer.WriteLine($"{pair.Value.Clusters![i].CenterX},{pair.Value.Clusters![i].CenterY},{pair.Value.Clusters![i].MaxX - pair.Value.Clusters![i].MinX},{pair.Value.Clusters![i].Count}");
					}
				}
			}
		}

		private void ExportImages(string mapName, IProviderManager providerManager, WorldData worldData, IReadOnlyDictionary<string, FoliageData> foliageData, Config config, Logger logger)
		{
			MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
			MapOverlayBuilder compositeMapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);

			string outDir = Path.Combine(config.OutputDirectory, Name, "Visual", mapName);

			SKColor areasColor = new(255, 255, 255, 208);

			foreach (var pair in foliageData)
			{
				logger.Debug($"Generating image for {pair.Value.DisplayName}");

				// Create "Areas" map
				{
					mapBuilder.AddLocations(pair.Value.Clusters!.Select(c => new MapLocation(new FVector(c.CenterX, c.CenterY, 0.0f), Math.Max(3.0f, (float)(WorldDataUtil.WorldToMap * (c.MaxX - c.MinX))))), areasColor);
					SKData outData = mapBuilder.DrawOverlay();
					mapBuilder.ClearLocations();

					Directory.CreateDirectory(outDir);
					string outPath = Path.Combine(outDir, "Areas", $"{pair.Value.DisplayName}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}

				// Create "Icons" map
				string? iconPath = pair.Value.RewardItem.Icon.GetAssetPath();
				using SKBitmap? bitmap = iconPath is null ? null : AssetUtil.LoadAndDecodeTexture(pair.Key, iconPath, providerManager.AssetProvider, logger);
				if (bitmap is not null)
				{
					const int size = 32;

					SKImageInfo surfaceInfo = new()
					{
						Width = size,
						Height = size,
						ColorSpace = SKColorSpace.CreateSrgb(),
						ColorType = SKColorType.Rgba8888,
						AlphaType = SKAlphaType.Premul
					};

					using SKBitmap scaled = new(surfaceInfo);
					bitmap.ScalePixels(scaled, SKFilterQuality.High);

					using SKPaint paint = new()
					{
						ImageFilter = SKImageFilter.CreateDropShadow(0.0f, 0.0f, 2.0f, 2.0f, SKColors.White)
					};

					SKImage icon;
					using (SKSurface surface = SKSurface.Create(surfaceInfo))
					{
						SKCanvas canvas = surface.Canvas;

						canvas.DrawBitmap(scaled, 0.0f, 0.0f, paint);

						surface.Flush();
						icon = surface.Snapshot();
					}

					IEnumerable<MapLocation> locations = pair.Value.Clusters!.Select(c => new MapLocation(new FVector(c.CenterX, c.CenterY, 0.0f)));
					{
						mapBuilder.AddLocations(locations, icon);
						SKData outData = mapBuilder.DrawOverlay();
						mapBuilder.ClearLocations();

						Directory.CreateDirectory(outDir);
						string outPath = Path.Combine(outDir, "Icons", $"{pair.Value.DisplayName}.png");
						using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
						{
							outData.SaveTo(outStream);
						}
					}

					// Also add to composite map
					compositeMapBuilder.AddLocations(locations, icon);
				}
			}

			// Output composite icons map
			{
				SKData outData = compositeMapBuilder.DrawOverlay();
				compositeMapBuilder.ClearLocations(true);

				string compOutDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				Directory.CreateDirectory(compOutDir);
				string outPath = Path.Combine(compOutDir, mapName, $"{mapName}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}
		}

		private class FoliageData
		{
			public ClusterBuilder ClusterBuilder { get; }

			public FItemableData RewardItem { get; }

			public string DisplayName { get; }

			public IReadOnlyList<Cluster>? Clusters => ClusterBuilder.Clusters;

			public FoliageData(WorldData worldData, FItemableData rewardItem, string displayName)
			{
				ClusterBuilder = new(worldData, ClusterDistanceThreshold, PartitionSize);
				RewardItem = rewardItem;
				DisplayName = displayName;
			}

			public override string ToString()
			{
				return DisplayName;
			}
		}
	}

#pragma warning disable CS0649 // Field never assigned to

	internal struct FFLODDescription : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public ObjectPointer FoliageType;
		public FGameplayTagContainer FoliageTags;
		public bool bDisabled;
		public bool bUseViewTraceInfluence;
		public ObjectPointer ViewTraceActor;
		public FRowHandle ViewTraceActorItemTemplate;
		public FRowHandle ViewTraceActorItemable;
		public FRowHandle ViewTraceActorItemRewards;
		public bool bViewTraceClientPredictive;
		public bool bUseDistanceInfluence;
		public List<FFLODDistanceLevelDescription> DistanceLevels;
		public bool bIsFlammable;
		public FRowHandle Flammable;
		public FRowHandle BurntFLODEntry;
		public int RecordIndex;
		public List<FFLODLevelDescription> Levels;
	}

	internal struct FFLODDistanceLevelDescription
	{
		public ObjectPointer Actor;
		public float InfluenceDistance;
		public FRowHandle ActorItemTemplate;
		public FRowHandle ActorItemRewards;
	}

	internal struct FFLODLevelDescription
	{
		public int LevelIndex;
		public EFLODLevelInfluenceType InfluenceType;
		public bool bClientPredictive;
		public float InfluenceDistance;
		public int ActorPoolBufferSize;
		public ObjectPointer ActorReplacementClass;
		public FRowHandle ItemTemplate;
		public FRowHandle ItemRewards;
	}

	internal enum EFLODLevelInfluenceType
	{
		None,
		ViewTrace,
		Distance
	}

#pragma warning restore CS0649
}
