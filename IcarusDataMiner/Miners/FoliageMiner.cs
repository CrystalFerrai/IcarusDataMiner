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
		// Foliage instances less than this distance apart will be grouped. Distance calculation based on Manhattan distance.
		// Tweaking this affects how output data is grouped. 1008 = 1/50 of a map grid cell
		private const float GroupDistanceThreshold = 1008.0f;

		// Size to break world into for collision detection algorithm.
		// Tweaking this affects running time and and memory usage of this miner.
		// Must be at least double the value of GroupDistanceThreshold to not break the algorithm.
		private const float PartitionSize = 10080.0f;

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

			foreach (string levelPath in worldData.DeveloperLevels.Concat(worldData.HeightmapLevels))
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
				FindFoliage(packageFile, foliageData, meshMap, worldData, providerManager, logger);
			}

			if (foliageData.Count == 0) return;

			foreach (FoliageData foliage in foliageData.Values)
			{
				foliage.BuildClusters();
			}

			string outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}.csv");

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

		private void FindFoliage(GameFile mapAsset, IDictionary<string, FoliageData> foliageData, IReadOnlyDictionary<string, string> meshMap, WorldData worldData, IProviderManager providerManager, Logger logger)
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

				FoliageData? foliage;
				if (!foliageData.TryGetValue(foliageType, out foliage))
				{
					foliage = new FoliageData(worldData.MinimapData!);
					foliageData.Add(foliageType, foliage);
				}

				foreach (FInstancedStaticMeshInstanceData instanceData in fismObject.PerInstanceSMData)
				{
					foliage.AddInstance(instanceData.TransformData.Translation);
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

				if (x < 0 || x > mCells.GetLength(0) ||
					y < 0 || y > mCells.GetLength(1))
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
