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
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SkiaSharp;
using System.Text.RegularExpressions;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts area location information for all maps
	/// </summary>
	internal class AreaEffectMiner : IDataMiner
	{
		// To extract a area ID from a area name
		private static readonly Regex sRadiationIdRegex;

		public string Name => "AreaEffects";

		static AreaEffectMiner()
		{
			sRadiationIdRegex = new Regex(@"BP_Uranium_Emitter(\d*)");
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ExportAreas(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ExportAreas(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			List<AreaData> areas = FindAreas(mapAsset, providerManager, logger).ToList();
			areas.Sort();

			if (areas.Count > 0)
			{
				// CSV
				{
					string outCustomPath = Path.Combine(config.OutputDirectory, Name, $"{mapAsset.NameWithoutExtension}.csv");
					using (FileStream outStream = IOUtil.CreateFile(outCustomPath, logger))
					using (StreamWriter writer = new StreamWriter(outStream))
					{
						writer.WriteLine("ID,Active,Radius,Location X,Location Y,Location Z,Grid");

						foreach (AreaData area in areas)
						{
							writer.WriteLine($"{area.ID},{area.IsActive},{area.Radius},{area.Location.X},{area.Location.Y},{area.Location.Z},{worldData.GetGridCell(area.Location)}");
						}
					}
				}

				// Image
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
					mapBuilder.AddLocations(areas.Where(a => a.IsActive).Select(a => new AreaMapLocation(a.Location, a.Radius)), new SKColor(192, 255, 128, 192));
					mapBuilder.AddLocations(areas.Where(a => !a.IsActive).Select(a => new AreaMapLocation(a.Location, a.Radius)), new SKColor(136, 144, 128, 192));
					SKData outData = mapBuilder.DrawOverlay();

					string outPath = Path.Combine(config.OutputDirectory, Name, $"{mapAsset.NameWithoutExtension}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}
		}

		private IEnumerable<AreaData> FindAreas(GameFile mapAsset, IProviderManager providerManager, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int radiationNameIndex = -1, radiationRadiusIndex = -1, radiationEnabledIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_Uranium_Emitter_C":
						radiationNameIndex = i;
						break;
					case "RadiationDistance":
						radiationRadiusIndex = i;
						break;
					case "EmittingRadiation":
						radiationEnabledIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
				}
			}

			if (radiationNameIndex < 0) yield break; // No areas

			int radiationTypeIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (mapPackage.ImportMap[i].ObjectName.Index == radiationNameIndex)
				{
					radiationTypeIndex = ~i;
				}
			}

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != radiationTypeIndex) continue;

				UObject areaObject = export.ExportObject.Value;

				Match match = sRadiationIdRegex.Match(areaObject.Name);
				if (!match.Success)
				{
					logger.Log(LogLevel.Warning, $"Error parsing area name {areaObject.Name} in {mapAsset.NameWithoutExtension}. Area will be skipped");
					continue;
				}

				AreaData areaData = new();
				areaData.ID = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 1;

				for (int i = 0; i < areaObject.Properties.Count; ++i)
				{
					FPropertyTag prop = areaObject.Properties[i];
					if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(areaObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag subProp = rootComponentObject.Properties[j];
							if (subProp.Name.Index == relativeLocationIndex)
							{
								areaData.Location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
							}
						}
					}
					else if (prop.Name.Index == radiationRadiusIndex)
					{
						areaData.Radius = PropertyUtil.GetByIndex<float>(areaObject, i);
					}
					else if (prop.Name.Index == radiationEnabledIndex)
					{
						areaData.IsActive = PropertyUtil.GetByIndex<bool>(areaObject, i);
					}
				}

				yield return areaData;
			}
		}

		private class AreaData : IComparable<AreaData>
		{
			public int ID { get; set; }

			public FVector Location { get; set; }

			public float Radius { get; set; }

			public bool IsActive { get; set; }

			public AreaData()
			{
				ID = 0;
				Location = FVector.ZeroVector;
				Radius = 3500.0f;
				IsActive = false;
			}

			public int CompareTo(AreaData? other)
			{
				return other == null ? 1 : ID.CompareTo(other.ID);
			}

			public override string ToString()
			{
				return ID.ToString();
			}
		}
	}
}
