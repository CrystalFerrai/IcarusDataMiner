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
using System.Text.RegularExpressions;

namespace IcarusDataMiner.Miners
{
	internal class GeyserMiner : IDataMiner
	{
		// To extract a geyser ID from a geyser name
		private static readonly Regex sGeyserIdRegex;

		public string Name => "Geysers";

		static GeyserMiner()
		{
			sGeyserIdRegex = new Regex(@"BP_EnzymeGeyser(\d*)");
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
				ExportGeysers(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ExportGeysers(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			List<GeyserData> geysers = FindGeysers(mapAsset, providerManager, logger).ToList();
			geysers.Sort();

			if (geysers.Count > 0)
			{
				string outCustomPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}.csv");
				using (FileStream outStream = IOUtil.CreateFile(outCustomPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					writer.WriteLine("ID,Type,Location X,Location Y,Location Z,Grid");

					foreach (GeyserData geyser in geysers)
					{
						writer.WriteLine($"{geyser.ID},{geyser.Type.Text[0..geyser.Type.Text.LastIndexOf('_')]},{geyser.Location.X},{geyser.Location.Y},{geyser.Location.Z},{worldData.GetGridCell(geyser.Location)}");
					}
				}
			}
		}

		private IEnumerable<GeyserData> FindGeysers(GameFile mapAsset, IProviderManager providerManager, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int geyserNameIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1, hordeRowHandleIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_EnzymeGeyser_C":
						geyserNameIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
					case "HordeRowHandle":
						hordeRowHandleIndex = i;
						break;
				}
			}

			if (geyserNameIndex < 0) yield break; // No geysers

			int geyserTypeIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (mapPackage.ImportMap[i].ObjectName.Index == geyserNameIndex)
				{
					geyserTypeIndex = ~i;
				}
			}

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != geyserTypeIndex) continue;

				UObject geyserObject = export.ExportObject.Value;

				Match match = sGeyserIdRegex.Match(geyserObject.Name);
				if (!match.Success)
				{
					logger.Log(LogLevel.Warning, $"Error parsing geyser name {geyserObject.Name} in {mapAsset.NameWithoutExtension}. Geyser will be skipped");
					continue;
				}

				GeyserData geyserData = new GeyserData();
				geyserData.ID = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 1;

				for (int i = 0; i < geyserObject.Properties.Count; ++i)
				{
					FPropertyTag prop = geyserObject.Properties[i];
					if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(geyserObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag subProp = rootComponentObject.Properties[j];
							if (subProp.Name.Index == relativeLocationIndex)
							{
								geyserData.Location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
							}
						}
					}
					else if (prop.Name.Index == hordeRowHandleIndex)
					{
						UScriptStruct hordeRowHandleStruct = PropertyUtil.GetByIndex<UScriptStruct>(geyserObject, i);
						geyserData.Type = PropertyUtil.GetByIndex<FName>((IPropertyHolder)hordeRowHandleStruct.StructType, 0);
					}
				}

				yield return geyserData;
			}
		}

		private class GeyserData : IComparable<GeyserData>
		{
			public int ID { get; set; }

			public FName Type { get; set; }

			public FVector Location { get; set; }

			public int CompareTo(GeyserData? other)
			{
				return other == null ? 1 : ID.CompareTo(other.ID);
			}

			public override string ToString()
			{
				return $"{ID} - {Type}";
			}
		}
	}
}
