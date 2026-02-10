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

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts information about deep mining ore deposits
	/// </summary>
	internal class DeepOreMiner : IDataMiner
	{
		public string Name => "DeepOre";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ExportDeposits(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ExportDeposits(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int oreTypeNameIndex = -1, iceVariantIndex = -1, woodVariantIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_DeepOreDepositSpawn_C":
						oreTypeNameIndex = i;
						break;
					case "SpawnIceVariant":
						iceVariantIndex = i;
						break;
					case "SpawnWoodVariant":
						woodVariantIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
				}
			}

			int oreTypeClassIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				FObjectImport import = mapPackage.ImportMap[i];
				if (import.ObjectName.Index == oreTypeNameIndex)
				{
					oreTypeClassIndex = ~i;
				}
			}

			List<DepositInfo> oreDeposits = new();

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != oreTypeClassIndex) continue;

				DepositInfo deposit = new();
				deposit.SetName(export.ObjectName);
				deposit.Type = DepositType.Ore;

				UObject veinObject = export.ExportObject.Value;
				for (int i = 0; i < veinObject.Properties.Count; ++i)
				{
					FPropertyTag prop = veinObject.Properties[i];
					if (prop.Name.Index == iceVariantIndex)
					{
						bool isIce = PropertyUtil.GetByIndex<bool>(veinObject, i);
						if (isIce)
						{
							deposit.Type = DepositType.Ice;
						}
					}
					else if (prop.Name.Index == woodVariantIndex)
					{
						bool isWood = PropertyUtil.GetByIndex<bool>(veinObject, i);
						if (isWood)
						{
							deposit.Type = DepositType.Wood;
						}
					}
					else if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(veinObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag subProp = rootComponentObject.Properties[j];
							if (subProp.Name.Index == relativeLocationIndex)
							{
								deposit.Location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
								break;
							}
						}
					}
				}

				oreDeposits.Add(deposit);
			}

			if (oreDeposits.Count > 0)
			{
				oreDeposits.Sort();

				// CSV
				{
					string outputPath = Path.Combine(config.OutputDirectory, Name, "Data", $"{mapAsset.NameWithoutExtension}.csv");
					using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
					using (StreamWriter writer = new StreamWriter(outStream))
					{
						writer.WriteLine("Name,Type,LocationX,LocationY,LocationZ,Map");
						void writeNodeInfo(DepositInfo node)
						{
							writer.WriteLine($"{node.Name},{node.Type},{node.Location.X},{node.Location.Y},{node.Location.Z},{worldData.GetGridCell(node.Location)}");
						}
						foreach (DepositInfo node in oreDeposits)
						{
							writeNodeInfo(node);
						}
					}
				}

				// Images
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);

					mapBuilder.AddLocations(oreDeposits.Where(o => o.Type == DepositType.Ore).Select(d => new MapLocation(d.Location, 7.0f)));
					mapBuilder.AddLocations(oreDeposits.Where(o => o.Type == DepositType.Ice).Select(d => new RotatedMapLocation(d.Location, 45.0f, 7.0f)), new SKColor(168, 160, 255, 255), MarkerShape.Square);
					mapBuilder.AddLocations(oreDeposits.Where(o => o.Type == DepositType.Wood).Select(d => new MapLocation(d.Location, 7.0f)), new SKColor(225, 150, 100, 255), MarkerShape.Square);
					SKData outData = mapBuilder.DrawOverlay();
					mapBuilder.ClearLocations();

					string outPath = Path.Combine(config.OutputDirectory, Name, "Visual", $"{mapAsset.NameWithoutExtension}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}
		}

		private class DepositInfo : IComparable<DepositInfo>
		{
			private const int IdOffset = 1000000;

			private int mId;

#nullable disable annotations
			public string Name { get; set; }
#nullable restore annotations

			public DepositType Type { get; set; }

			public FVector Location { get; set; }

			public DepositInfo()
			{
				mId = -1;
			}

			public void SetName(FName name)
			{
				int id = int.MaxValue;
				string nameText = name.Text;

				void parseId(string nameTextValue)
				{
					int tryId;
					string[] nameParts = nameTextValue.Split('_');
					if (nameParts.Length > 0)
					{
						nameText = nameParts[0];
						if (string.IsNullOrEmpty(nameText))
						{
							if (string.IsNullOrEmpty(nameTextValue))
							{
								nameText = "1";
							}
							else if (nameParts.Length > 1)
							{
								nameText = nameParts[1];
								if (nameText.Equals("C") && nameParts.Length > 2)
								{
									nameText = $"C_{nameParts[2]}";
									if (int.TryParse(nameParts[2], out tryId))
									{
										id = IdOffset + tryId;
									}
								}
							}
							if (string.IsNullOrEmpty(nameText))
							{
								nameText = nameTextValue;
							}
						}

						if (id == int.MaxValue && int.TryParse(nameText, out tryId))
						{
							id = tryId;
						}
					}
				}

				if (nameText.StartsWith("BP_DeepOreDepositSpawn"))
				{
					parseId(nameText["BP_DeepOreDepositSpawn".Length..]);
				}

				mId = id;
				Name = nameText;
			}

			public int CompareTo(DepositInfo? other)
			{
				int idCompare = mId.CompareTo(other!.mId);
				if (idCompare == 0) return Name.CompareTo(other!.Name);
				return idCompare;
			}

			public override string ToString()
			{
				return Name;
			}
		}

		private enum DepositType
		{
			Unknown,
			Ore,
			Ice,
			Wood
		}
	}
}

