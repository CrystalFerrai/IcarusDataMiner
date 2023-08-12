// Copyright 2023 Crystal Ferrai
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
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines location data for various voxel instances
	/// </summary>
	/// <remarks>
	/// This miner takes a long time to run, so it is disabled by default. You must name it explicitly on the command line to run it.
	/// </remarks>
	[DefaultEnabled(false)]
	internal class VoxelMiner : IDataMiner
	{
		private static readonly HashSet<string> sActorNames;

		public string Name => "Voxels";

		static VoxelMiner()
		{
			// From Icarus/Content/BP/Objects/World/Resources/Voxels/Shapes
			sActorNames = new HashSet<string>()
			{
				"BP_Voxel_GEN_01_C",
				"BP_Voxel_GEN_02_C",
				"BP_Voxel_GEN_03_C",
				"BP_Voxel_GEN_04_C",
				"BP_Voxel_GEN_05_C",
			};
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
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
				ProcessMap(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ProcessMap(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			Dictionary<string, List<FVector>> voxelMap = new();

			foreach (string levelPath in worldData.GeneratedLevels)
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
				FindVoxels(packageFile, FVector.ZeroVector, voxelMap, worldData, providerManager, logger);
			}

			ExportData(mapAsset.NameWithoutExtension, voxelMap, config, logger);
			ExportImages(mapAsset.NameWithoutExtension, providerManager, worldData, voxelMap, config, logger);
		}

		private void ExportData(string mapName, Dictionary<string, List<FVector>> voxelMap, Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, Name, "Data", $"{mapName}.csv");

			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(outStream))
			{
				writer.WriteLine("Pool,X,Y,Z");

				foreach (var pair in voxelMap)
				{
					foreach (FVector location in pair.Value)
					{
						writer.WriteLine($"{pair.Key},{location.X},{location.Y},{location.Z}");
					}
				}
			}
		}

		private void ExportImages(string mapName, IProviderManager providerManager, WorldData worldData, Dictionary<string, List<FVector>> voxelMap, Config config, Logger logger)
		{
			MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
			foreach (var pair in voxelMap)
			{
				logger.Log(LogLevel.Debug, $"Generating image for {pair.Key}");

				mapBuilder.AddLocations(pair.Value.Select(l => new MapLocation(l, 3.0f))); // Chaange radius from 3 to 1 if wanting to count voxels on map
				SKData outData = mapBuilder.DrawOverlay();
				mapBuilder.ClearLocations();

				string outPath = Path.Combine(config.OutputDirectory, Name, "Visual", $"{mapName}_{pair.Key}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}
		}

		private void FindVoxels(GameFile mapAsset, FVector origin, IDictionary<string, List<FVector>> voxelMap, WorldData worldData, IProviderManager providerManager, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			HashSet<int> actorNameIndices = new();

			int rootComponentNameIndex = -1, relativeLocationNameIndex = -1, resourcePoolNameIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "RootComponent":
						rootComponentNameIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationNameIndex = i;
						break;
					case "ResourcePool":
						resourcePoolNameIndex = i;
						break;
					default:
						if (name.Name != null && sActorNames.Contains(name.Name))
						{
							actorNameIndices.Add(i);
						}
						break;
				}
			}

			if (actorNameIndices.Count == 0) return; // No voxels

			HashSet<int> actorTypeIndices = new();
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (actorNameIndices.Contains(mapPackage.ImportMap[i].ObjectName.Index))
				{
					actorTypeIndices.Add(~i);
				}
			}

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;

				if (!actorTypeIndices.Contains(export.ClassIndex.Index))
				{
					continue;
				}

				FVector? location = null;
				string voxelPool = "DefaultPool";

				UObject actorObject = export.ExportObject.Value;

				for (int i = 0; i < actorObject.Properties.Count; ++i)
				{
					FPropertyTag prop = actorObject.Properties[i];
					if (prop.Name.Index == rootComponentNameIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(actorObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag parentProp = rootComponentObject.Properties[j];
							if (parentProp.Name.Index == relativeLocationNameIndex)
							{
								location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
								break;
							}
						}
					}
					else if (prop.Name.Index == resourcePoolNameIndex)
					{
						IPropertyHolder resourcePoolProperty = PropertyUtil.GetByIndex<IPropertyHolder>(actorObject, i);
						voxelPool = PropertyUtil.Get<FName>(resourcePoolProperty, "RowName").Text;
					}
				}

				if (!location.HasValue)
				{
					logger.Log(LogLevel.Debug, $"Could not find location for actor {actorObject.Name}");
					continue;
				}

				List<FVector>? locations;
				if (!voxelMap.TryGetValue(voxelPool, out locations))
				{
					locations = new List<FVector>();
					voxelMap.Add(voxelPool, locations);
				}
				locations.Add(location.Value);
			}
		}
	}
}
