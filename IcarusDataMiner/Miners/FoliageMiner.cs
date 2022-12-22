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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines location data for various foliage instances
	/// </summary>
	internal class FoliageMiner : IDataMiner
	{
		// Foliage instances less than this distance apart will be grouped. Distance calculation based on Manhattan distance.
		// Tweaking this affects how output data is grouped.
		private const float GroupDistanceThreshold = WorldDataUtil.WorldCellSize * 0.04f; // 25x25 points per cell
		//private const float GroupDistanceThreshold = WorldDataUtil.WorldCellSize * 0.16f;

		// Size to break world into for collision detection algorithm.
		// Tweaking this affects running time and and memory usage of this miner.
		// Must be at least double the value of GroupDistanceThreshold to not break the algorithm.
		private const float PartitionSize = WorldDataUtil.WorldCellSize * 0.2f;
		//private const float PartitionSize = WorldDataUtil.WorldCellSize * 0.4f;

		// Maximum size of a cluster point for exported map images
		private const float MaxPointSize = (float)(GroupDistanceThreshold * WorldDataUtil.WorldToMap);

		private static Dictionary<string, string> sPlantMap;

		public string Name => "Foliage";

		static FoliageMiner()
		{
			ObjectTypeRegistry.RegisterClass("FLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));
			ObjectTypeRegistry.RegisterClass("IcarusFLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));

			// Map of plant types to collect data about.
			// Refer to Data/FLOD/D_FLODDescriptions.json for all possible types
			sPlantMap = new Dictionary<string, string>()
			{
				// NOTE: Commented out entries exist in the data but no instances have been found on any maps so not sure precisely what they are.
				// Specualtion: They might be new crop types and variations coming in a future DLC.

				{ "FT_Alpine_Lily_01", "Lily" },

				{ "FT_Beans", "Beans" },
				//{ "FT_LC_Beans", "LC_Beans" },

				{ "FT_Carrot", "Carrot" },

				{ "FT_Cocoa", "Cocoa" },
				//{ "FT_LC_Cocoa", "LC_Cocoa" },

				{ "FT_Coffee", "Coffee" },

				// There are many instances of this, but sure what it actually is
				//{ "FT_ConiferFlower_01", "Flower" },

				// Corn cobs and corn stalks always appear in the same places and have the same loot so we combine them as just "Corn"
				{ "FT_CornCob_01", "Corn" },
				{ "FT_CropCorn_01", "Corn" },

				{ "FT_GreenTea", "GreenTea" },

				//{ "FT_SW_Potato_Wild", "SW_Potato" },
				//{ "FT_TU_Potato_Wild", "TU_Potato" },

				{ "FT_Pumpkin", "Pumpkin" },
				//{ "FT_TU_Pumpkin", "TU_Pumpkin" },

				{ "FT_ReedFlower_01", "Reed" },
				
				{ "FT_Sponge_01", "Sponge" },

				{ "FT_Squash", "Squash" },
				//{ "FT_LC_Squash", "LC_Squash" },

				//{ "FT_Tomatoes_Wild", "Tomato" },
				
				{ "FT_Watermelon", "Watermelon" },
				
				{ "FT_Wheat_03", "Wheat" },

				{ "FT_WildTea", "WildTea" },
				//{ "FT_LC_WildTea", "LC_WildTea" },

				{ "FT_YeastPlant_01", "Yeast" }
			};
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			IReadOnlyDictionary<string, string> meshMap = LoadFoliageMeshMap(providerManager, logger);

			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile))
				{
					logger.Log(LogLevel.Information, $"Skipping {packageName} due to missing map assets.");
					continue;
				}

				if (world.MinimapData == null)
				{
					logger.Log(LogLevel.Information, $"Skipping {packageName} due to missing map boundary data.");
					continue;
				}

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ProcessMap(packageFile, providerManager, world, meshMap, config, logger);
			}

			return true;
		}

		private void ProcessMap(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, IReadOnlyDictionary<string, string> meshMap, Config config, Logger logger)
		{
			Dictionary<string, FoliageData> foliageData = new();

			foreach (string levelPath in worldData.DeveloperLevels)
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
				FindFoliage(packageFile, FVector.ZeroVector, foliageData, meshMap, worldData, providerManager, logger);
			}

			int rows, cols;
			{
				float worldWidth = worldData.MinimapData!.WorldBoundaryMax.X - worldData.MinimapData!.WorldBoundaryMin.X;
				float worldHeight = worldData.MinimapData!.WorldBoundaryMax.Y - worldData.MinimapData!.WorldBoundaryMin.Y;

				cols = (int)(worldWidth / WorldDataUtil.WorldTileSize);
				rows = (int)(worldHeight / WorldDataUtil.WorldTileSize);
			}
			for (int x = 0; x < rows; ++x)
			{
				for (int y = 0; y < cols; ++y)
				{
					string packagePath = WorldDataUtil.GetPackageName(worldData.HeightmapLevels[y + x * cols]);

					GameFile? packageFile;
					if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

					FVector origin = new FVector(
						x * WorldDataUtil.WorldTileSize + worldData.MinimapData!.WorldBoundaryMin.X,
						-((y + 1) * WorldDataUtil.WorldTileSize + worldData.MinimapData!.WorldBoundaryMin.Y),
						0.0f);

					logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
					FindFoliage(packageFile, origin, foliageData, meshMap, worldData, providerManager, logger);
				}
			}

			if (foliageData.Count == 0) return;

			foreach (FoliageData foliage in foliageData.Values)
			{
				foliage.BuildClusters();
			}

			ExportData(mapAsset.NameWithoutExtension, foliageData, config, logger);

			ExportImages(mapAsset.NameWithoutExtension, providerManager, worldData, foliageData, config, logger);
		}

		private void ExportData(string mapName, Dictionary<string, FoliageData> foliageData, Config config, Logger logger)
		{
			// The first output is for code parsing and DB importing for IcarusIntel
			string outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapName}.csv");

			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new StreamWriter(outStream))
			{
				writer.WriteLine("map,x,y,variety,count");

				foreach (var pair in foliageData)
				{
					for (int i = 0; i < pair.Value.Clusters!.Count; ++i)
					{
						writer.WriteLine($"{mapName},{pair.Value.Clusters![i].CenterX},{pair.Value.Clusters![i].CenterY},{pair.Key},{pair.Value.Clusters![i].Count}");
					}
				}
			}

			// The second output is meant to be human readable
			outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapName}_Readable.csv");

			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new StreamWriter(outStream))
			{
				int j = 0;
				foreach (string ft in foliageData.Keys)
				{
					writer.Write($"{ft}.X,{ft}.Y,{ft}.Count");
					++j;
					if (j < foliageData.Count) writer.Write(",");
				}
				writer.WriteLine();

				int most = foliageData.Values.Max(v => v.Clusters!.Count);

				for (int i = 0; i < most; ++i)
				{
					j = 0;
					foreach (var pair in foliageData)
					{
						if (i < pair.Value.Clusters!.Count)
						{
							writer.Write($"{pair.Value.Clusters![i].CenterX},{pair.Value.Clusters![i].CenterY},{pair.Value.Clusters![i].Count}");
						}
						else
						{
							writer.Write(",,");
						}
						++j;
						if (j < foliageData.Count) writer.Write(",");
					}
					writer.WriteLine();
				}
			}
		}

		private void ExportImages(string mapName, IProviderManager providerManager, WorldData worldData, IReadOnlyDictionary<string, FoliageData> foliageData, Config config, Logger logger)
		{
			float offsetX = worldData.MinimapData!.WorldBoundaryMin.X;
			float offsetY = worldData.MinimapData!.WorldBoundaryMin.Y;

			float mapWidth = worldData.MinimapData.WorldBoundaryMax.X - offsetX;
			float mapHeight = worldData.MinimapData.WorldBoundaryMax.Y - offsetY;

			float scaleX = (float)(1.0f / WorldDataUtil.MapToWorld);
			float scaleY = (float)(1.0f / WorldDataUtil.MapToWorld);

			UTexture2D? firstTileTexture = AssetUtil.LoadTexture(worldData.MinimapData!.MapTextures[0], providerManager.AssetProvider);
			if (firstTileTexture != null)
			{
				scaleX = (float)firstTileTexture.SizeX / (float)WorldDataUtil.WorldTileSize;
				scaleY = (float)firstTileTexture.SizeY / (float)WorldDataUtil.WorldTileSize;
			}

			int imageWidth = (int)Math.Ceiling(mapWidth * scaleX);
			int imageHeight = (int)Math.Ceiling(mapHeight * scaleY);

			SKImageInfo surfaceInfo = new()
			{
				Width = imageWidth,
				Height = imageHeight,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			foreach (var pair in foliageData)
			{
				logger.Log(LogLevel.Debug, $"Generating image for {pair.Key}");

				SKData outData;
				using (SKSurface surface = SKSurface.Create(surfaceInfo))
				{
					SKCanvas canvas = surface.Canvas;
					SKPaint paint = new SKPaint()
					{
						Color = SKColors.White,
						IsStroke = false,
						IsAntialias = true,
						Style = SKPaintStyle.Fill
					};

					canvas.DrawCircle(0.0f, 0.0f, 1.0f, paint);
					canvas.DrawCircle(imageWidth - 1.0f, imageHeight - 1.0f, 1.0f, paint);

					foreach (Cluster cluster in pair.Value.Clusters!)
					{
						float radius = Math.Min((float)Math.Log2(cluster.Count) + 3.0f, 10.0f);
						canvas.DrawCircle((cluster.CenterX - offsetX) * scaleX, (cluster.CenterY - offsetY) * scaleY, radius, paint);
					}

					surface.Flush();
					SKImage image = surface.Snapshot();
					outData = image.Encode(SKEncodedImageFormat.Png, 100);
				}

				string outDir = Path.Combine(config.OutputDirectory, Name);
				Directory.CreateDirectory(outDir);
				string outPath = Path.Combine(outDir, $"{mapName}_{pair.Key}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}
		}

		private IReadOnlyDictionary<string, string> LoadFoliageMeshMap(IProviderManager providerManager, Logger logger)
		{
			Dictionary<string, string> meshPaths = new();

			GameFile file = providerManager.DataProvider.Files["FLOD/D_FLODDescriptions.json"];

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				string? foliageType = null, meshPath = null;

				FlodParseState state = FlodParseState.SearchingForRows;
				int objectDepth = 0;

				while (state != FlodParseState.Done && reader.Read())
				{
					switch (state)
					{
						case FlodParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = FlodParseState.InRows;
							break;
						case FlodParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = FlodParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = FlodParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case FlodParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									foliageType = reader.ReadAsString();
								}
								else if (reader.Value!.Equals("FoliageType"))
								{
									meshPath = reader.ReadAsString();
								}
								else
								{
									reader.Skip();
								}
							}
							if (reader.Depth < objectDepth)
							{
								if (foliageType != null && meshPath != null)
								{
									if (sPlantMap.TryGetValue(foliageType, out string? plantName))
									{
										meshPaths.Add(GetMeshNameFromFoliage(meshPath, providerManager, logger), plantName);
									}
									foliageType = null;
									meshPath = null;
								}
								state = FlodParseState.InRows;
							}
							break;
						case FlodParseState.ExitObject:
							if (reader.Depth < objectDepth)
							{
								state = FlodParseState.InRows;
							}
							else
							{
								reader.Skip();
							}
							break;
					}
				}
			}

			return meshPaths;
		}

		private enum FlodParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			ExitObject,
			Done
		}

		private string GetMeshNameFromFoliage(string foliageAssetPath, IProviderManager providerManager, Logger logger)
		{
			GameFile asset = providerManager.AssetProvider.Files[AssetUtil.GetPackageName(foliageAssetPath, "uasset")];
			Package package = (Package)providerManager.AssetProvider.LoadPackage(asset);

			FObjectExport export = package.ExportMap[0];
			UObject obj = export.ExportObject.Value;
			FPackageIndex meshIndex = PropertyUtil.GetOrDefault<FPackageIndex>(obj, "Mesh");
			return meshIndex.Name;
		}

		private void FindFoliage(GameFile mapAsset, FVector origin, IDictionary<string, FoliageData> foliageData, IReadOnlyDictionary<string, string> meshMap, WorldData worldData, IProviderManager providerManager, Logger logger)
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

				string? foliageType;
				if (!meshMap.TryGetValue(meshName, out foliageType)) continue;

				FoliageData? foliage;
				if (!foliageData.TryGetValue(foliageType, out foliage))
				{
					foliage = new FoliageData(worldData.MinimapData!);
					foliageData.Add(foliageType, foliage);
				}

				foreach (FInstancedStaticMeshInstanceData instanceData in fismObject.PerInstanceSMData)
				{
					foliage.AddInstance(origin + parentTransform.TransformPosition(instanceData.TransformData.Translation));
				}
			}
		}

		private class FoliageData
		{
			private FVector2D mWorldBoundaryMin;
			private FVector2D mWorldBoundaryMax;

			private int mCellCountX;
			private int mCellCountY;

			private readonly List<Cluster>[,] mCells;

			public IReadOnlyList<Cluster>? Clusters { get; private set; }

			public FoliageData(MinimapData mapData)
			{
				mWorldBoundaryMin = mapData.WorldBoundaryMin;
				mWorldBoundaryMax = mapData.WorldBoundaryMax;

				float worldSizeX = mWorldBoundaryMax.X - mWorldBoundaryMin.X;
				float worldSizeY = mWorldBoundaryMax.Y - mWorldBoundaryMin.Y;

				mCellCountX = (int)Math.Ceiling(worldSizeX / PartitionSize);
				mCellCountY = (int)Math.Ceiling(worldSizeY / PartitionSize);

				mCells = new List<Cluster>[mCellCountX, mCellCountY];
				
				for (int y = 0; y < mCellCountY; ++y)
				{
					for (int x = 0; x < mCellCountX; ++x)
					{
						mCells[x, y] = new List<Cluster>();
					}
				}
			}

			public bool AddInstance(FVector location)
			{
				int x = (int)Math.Floor((location.X - mWorldBoundaryMin.X) / PartitionSize);
				int y = (int)Math.Floor((location.Y - mWorldBoundaryMin.Y) / PartitionSize);

				if (x < 0 || x >= mCells.GetLength(0) ||
					y < 0 || y >= mCells.GetLength(1))
				{
					return false; // Discard if outside the map bounds
				}

				List<Cluster> clusters = mCells[x, y];

				bool added = false;
				for (int i = 0; i < clusters.Count; ++i)
				{
					Cluster cluster = clusters[i];
					if (cluster.AddLocation(location))
					{
						clusters[i] = cluster;
						added = true;
						break;
					}
				}
				if (!added)
				{
					clusters.Add(new Cluster(location));
				}

				return true;
			}

			public void BuildClusters()
			{
				for (int y = 0; y < mCellCountY - 1; ++y)
				{
					for (int x = 0; x < mCellCountX - 1; ++x)
					{
						List<Cluster> targets = mCells[x, y];
						for (int y2 = 0; y2 <= 1; ++y2)
						{
							for (int x2 = 0; x2 <= 1; ++x2)
							{
								if (x2 == 0 && y2 == 0) continue;

								List<Cluster> sources = mCells[x + x2, y + y2];
								
								for (int t = 0; t < targets.Count; ++t)
								{
									Cluster target = targets[t];
									for (int s = 0; s < sources.Count; ++s)
									{
										if (target.CombineWith(sources[s]))
										{
											targets[t] = target;
											sources.RemoveAt(s--);
										}
									}
								}
							}
						}
					}
				}

				List<Cluster> clusters = new List<Cluster>();
				for (int y = 0; y < mCellCountY; ++y)
				{
					for (int x = 0; x < mCellCountX; ++x)
					{
						clusters.AddRange(mCells[x, y]);
					}
				}
				Clusters = clusters;
			}

			public override string ToString()
			{
				var cast = mCells.Cast<List<Cluster>>();
				return $"{cast.Count(v => v.Count > 0)} cells | {cast.Sum(v => v.Count)} instances";
			}
		}

		private struct Cluster
		{
			public float MinX;
			public float MaxX;
			public float MinY;
			public float MaxY;

			public int Count;

			public float CenterX => (MinX + MaxX) * 0.5f;
			public float CenterY => (MinY + MaxY) * 0.5f;

			public Cluster(FVector initialLocation)
			{
				MinX = MaxX = initialLocation.X;
				MinY = MaxY = initialLocation.Y;
				Count = 1;
			}

			public bool AddLocation(FVector location)
			{
				if (location.X < MaxX + GroupDistanceThreshold &&
					location.X > MinX - GroupDistanceThreshold &&
					location.Y < MaxY + GroupDistanceThreshold &&
					location.Y > MinY - GroupDistanceThreshold)
				{
					if (location.X < MinX) MinX = location.X;
					else if (location.X > MaxX) MaxX = location.X;

					if (location.Y < MinY) MinY = location.Y;
					else if (location.Y > MaxY) MaxY = location.Y;

					++Count;

					return true;
				}

				return false;
			}

			public bool CombineWith(Cluster other)
			{
				if (Math.Abs(MaxX - other.MinX) < GroupDistanceThreshold &&
					Math.Abs(MinX - other.MaxX) < GroupDistanceThreshold &&
					Math.Abs(MaxY - other.MinY) < GroupDistanceThreshold &&
					Math.Abs(MinY - other.MaxY) < GroupDistanceThreshold)
				{
					if (other.MinX < MinX) MinX = other.MinX;
					else if (other.MaxX > MaxX) MaxX = other.MaxX;

					if (other.MinY < MinY) MinY = other.MinY;
					else if (other.MaxY > MaxY) MaxY = other.MaxY;

					Count += other.Count;

					return true;
				}

				return false;
			}

			public override string ToString()
			{
				return $"({CenterX},{CenterY}) Count={Count}";
			}
		}
	}
}
