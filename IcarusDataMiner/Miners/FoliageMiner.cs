using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines location data for various foliage instances
	/// </summary>
	internal class FoliageMiner : IDataMiner
	{
		private static HashSet<string> sPlantList;

		public string Name => "Foliage";

		static FoliageMiner()
		{
			ObjectTypeRegistry.RegisterClass("FLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));
			ObjectTypeRegistry.RegisterClass("IcarusFLODFISMComponent", typeof(UHierarchicalInstancedStaticMeshComponent));

			// List of plant types to collect data about.
			// Refer to Data/FLOD/D_FLODDescriptions.json for all possible types
			sPlantList = new HashSet<string>()
			{
				"FT_Beans",
				"FT_Carrot",
				"FT_Cocoa",
				"FT_Coffee",
				"FT_CornCob_01",
				"FT_CropCorn_01",
				"FT_GreenTea",
				"FT_Pumpkin",
				"FT_ReedFlower_01",
				"FT_Sponge_01",
				"FT_Squash",
				"FT_Watermelon",
				"FT_Wheat_03",
				"FT_WildTea",
				"FT_YeastPlant_01"
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
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ProcessMap(packageFile, providerManager, world, meshMap, config, logger);
			}

			return true;
		}

		private void ProcessMap(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, IReadOnlyDictionary<string, string> meshMap, Config config, Logger logger)
		{
			Dictionary<string, IList<FVector>> foliageData = new();

			foreach (string levelPath in worldData.DeveloperLevels.Concat(worldData.HeightmapLevels))
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
				FindFoliage(packageFile, foliageData, meshMap, providerManager, config, logger);
			}

			if (foliageData.Count == 0) return;

			string outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}.csv");

			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new StreamWriter(outStream))
			{
				int j = 0;
				foreach (string ft in foliageData.Keys)
				{
					writer.Write($"{ft}.X,{ft}.Y,{ft}.Z");
					++j;
					if (j < foliageData.Count) writer.Write(",");
				}
				writer.WriteLine();

				int most = foliageData.Values.Max(v => v.Count);

				for (int i = 0; i < most ; ++i)
				{
					j = 0;
					foreach (var pair in foliageData)
					{
						if (i < pair.Value.Count)
						{
							writer.Write($"{pair.Value[i].X},{pair.Value[i].Y},{pair.Value[i].Z}");
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

		private IReadOnlyDictionary<string, string> LoadFoliageMeshMap(IProviderManager providerManager,Logger logger)
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
									if (sPlantList.Contains(foliageType))
									{
										meshPaths.Add(GetMeshNameFromFoliage(meshPath, providerManager, logger), foliageType);
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

		private void FindFoliage(GameFile mapAsset, IDictionary<string, IList<FVector>> foliageData, IReadOnlyDictionary<string, string> meshMap, IProviderManager providerManager, Config config, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int flodFismNameIndex = -1, icarusFlodFismNameIndex = -1, staticMeshNameIndex = -1;
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
				for (int i = 0; i < fismObject.Properties.Count; ++i)
				{
					FPropertyTag prop = fismObject.Properties[i];
					if (prop.Name.Index == staticMeshNameIndex)
					{
						FPackageIndex staticMeshProperty = PropertyUtil.GetByIndex<FPackageIndex>(fismObject, i);
						meshName = staticMeshProperty.Name;
					}
				}
				if (meshName == null) continue;

				string? foliageType;
				if (!meshMap.TryGetValue(meshName, out foliageType)) continue;

				IList<FVector>? locations;
				if (!foliageData.TryGetValue(foliageType, out locations))
				{
					locations = new List<FVector>();
					foliageData.Add(foliageType, locations);
				}

				foreach (FInstancedStaticMeshInstanceData instanceData in fismObject.PerInstanceSMData)
				{
					locations.Add(instanceData.TransformData.Translation);
				}
			}
		}
	}
}
