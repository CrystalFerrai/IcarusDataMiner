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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines location data for various voxel instances
	/// </summary>
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
			string outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapName}.csv");

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

			foreach (var pair in voxelMap)
			{
				logger.Log(LogLevel.Debug, $"Generating image for {pair.Key}");

				SKData outData;
				using (SKSurface surface = SKSurface.Create(surfaceInfo))
				{
					SKCanvas canvas = surface.Canvas;
					using SKPaint paint = new SKPaint()
					{
						Color = SKColors.White,
						IsStroke = false,
						IsAntialias = true,
						Style = SKPaintStyle.Fill
					};

					canvas.DrawCircle(0.0f, 0.0f, 1.0f, paint);
					canvas.DrawCircle(imageWidth - 1.0f, imageHeight - 1.0f, 1.0f, paint);

					foreach (FVector location in pair.Value)
					{
						float radius = 3.0f;
						canvas.DrawCircle((location.X - offsetX) * scaleX, (location.Y - offsetY) * scaleY, radius, paint);
					}

					// Single pixel output variant for ore counting purposes
					//foreach (FVector location in pair.Value)
					//{
					//	canvas.DrawPoint((float)Math.Floor((location.X - offsetX) * scaleX) + 0.5f, (float)Math.Round((location.Y - offsetY) * scaleY) + 0.5f, paint);
					//}

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
