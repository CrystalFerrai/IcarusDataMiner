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
using CUE4Parse.UE4.Objects.Core.Math;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Text;

namespace IcarusDataMiner.Miners
{
	internal class FishSpawnMiner : SpawnMinerBase, IDataMiner
	{
		public string Name => "Fish";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportSpawnConfig(providerManager, config, logger);
			return true;
		}

		private void ExportSpawnConfig(IProviderManager providerManager, Config config, Logger logger)
		{
			// Load data tables
			GameFile spawnConfigFile = providerManager.DataProvider.Files["Fish/D_FishSpawnConfig.json"];
			IcarusDataTable<FFishSpawnConfig> spawnConfigTable = IcarusDataTable<FFishSpawnConfig>.DeserializeTable("D_FishSpawnConfig", Encoding.UTF8.GetString(spawnConfigFile.Read()));

			GameFile spawnZonesFile = providerManager.DataProvider.Files["Fish/D_FishSpawnZones.json"];
			IcarusDataTable<FFishSpawnZones> spawnZoneTable = IcarusDataTable<FFishSpawnZones>.DeserializeTable("D_FishSpawnZones", Encoding.UTF8.GetString(spawnZonesFile.Read()));

			GameFile fishFile = providerManager.DataProvider.Files["Fish/D_FishData.json"];
			IcarusDataTable<FFishData> fishTable = IcarusDataTable<FFishData>.DeserializeTable("D_FishData", Encoding.UTF8.GetString(fishFile.Read()));

			HashSet<string> spawnConfigSet = new();
			foreach (FIcarusTerrain terrain in providerManager.DataTables.TerrainsTable!.Values)
			{
				spawnConfigSet.Add(terrain.FishConfig.RowName);
			}

			string getFishName(FRowHandle itemTemplateRow)
			{
				FItemableData itemData = providerManager.DataTables.GetItemableData(providerManager.DataTables.ItemTemplateTable![itemTemplateRow.RowName]);
				return LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, itemData.DisplayName);
			};

			List<SpawnConfig> spawnConfigs = new();

			foreach (FFishSpawnConfig row in spawnConfigTable.Values.Where(r => spawnConfigSet.Contains(r.Name)))
			{
				List<FishSpawnZone> spawnZones = new();

				foreach (FFIshSpawnZoneSetup zoneSetup in row.SpawnZones)
				{
					FFishSpawnZones zone = spawnZoneTable[zoneSetup.SpawnZone.RowName];

					List<WeightedItem> fish = new();
					FishType fishType = FishType.None;
					if (zone.SpawnList is not null)
					{
						foreach (var fishSpawnPair in zone.SpawnList)
						{
							FFishData fishData = fishTable[fishSpawnPair.Key.Value];
							FishType currentFishType = ConvertFishType(fishData.Type);
							if (fishType == FishType.None)
							{
								fishType = currentFishType;
							}
							else if (fishType != currentFishType)
							{
								fishType = FishType.Mixed;
							}
							fish.Add(new WeightedItem(getFishName(fishData.Fish), fishSpawnPair.Value));
						}
					}
					fish.Sort();
					fish.Reverse();

					FishSpawnZone newSpawnZone = new(zone.Name, zoneSetup.Color, fish, fishType);
					spawnZones.Add(newSpawnZone);
				}

				SKBitmap? spawnMap = null;
				string? spawnMapName = null;
				string? spawnMapPath = row.SpawnMap.GetAssetPath();
				if (spawnMapPath is not null)
				{
					spawnMapName = Path.GetFileNameWithoutExtension(spawnMapPath);
					if (spawnMapName is not null)
					{
						spawnMap = AssetUtil.LoadAndDecodeTexture(spawnMapName, spawnMapPath, providerManager.AssetProvider, logger);
					}

					if (spawnMap is null)
					{
						logger.Log(LogLevel.Warning, $"Encountered an error extracting the spawn map for config '{row.Name}'. This map will not be output.");
					}
				}

				spawnConfigs.Add(new SpawnConfig(row.Name, spawnMapName, spawnMap, spawnZones));
			}

			// Output data
			{
				string outDir = Path.Combine(config.OutputDirectory, Name, "Data");
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					string outputPath = Path.Combine(outDir, $"{spawnConfig.Name}.csv");

					using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
					using (StreamWriter writer = new StreamWriter(outStream))
					{
						writer.WriteLine("Zone,Color,Fish,AutoCreatures");
						foreach (SpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							writer.Write($"{spawnZone.Name},\"=\"\"{spawnZone.Color.R},{spawnZone.Color.G},{spawnZone.Color.B}\"\"\",");

							writer.Write("\"=\"\"");
							float weightSum = spawnZone.Spawns.Sum(c => c.Weight);
							for (int i = 0; i < spawnZone.Spawns.Count; ++i)
							{
								WeightedItem creature = spawnZone.Spawns[i];
								writer.Write($"{creature.Name}: {creature.Weight / weightSum * 100.0f:0.}%");
								if (i < spawnZone.Spawns.Count - 1)
								{
									writer.Write("\n");
								}
							}
							writer.WriteLine("\"\"\"");
						}
					}
				}
			}

			// Output spawn map textures
			{
				string outDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					if (spawnConfig.SpawnMap is null) continue;
					OutputSpawnMaps(outDir, spawnConfig.SpawnMapName!, spawnConfig.SpawnMap, logger);
				}
			}

			// Output overlay images
			{
				string outDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					HashSet<string> seenZoneFileNames = new();

					foreach (SpawnZone spawnZone in spawnConfig.SpawnZones)
					{
						string zoneFileName = spawnZone.Name;
						for (int i = 1; !seenZoneFileNames.Add(zoneFileName); ++i)
						{
							zoneFileName = $"{spawnZone.Name}_{i}";
						}

						SKData outData = CreateSpawnZoneInfoBox(spawnZone);

						string outPath = Path.Combine(outDir, spawnConfig.Name, "Zones", $"{zoneFileName}.png");
						using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
						{
							outData.SaveTo(outStream);
						}
					}
				}
			}
		}

		protected override IEnumerable<string> GetInfoBoxSubtitleLines(ISpawnZoneData spawnZone, object? userData)
		{
			FishSpawnZone fishSpawnZone = (FishSpawnZone)spawnZone;
			if (fishSpawnZone.FishType != FishType.None)
			{
				yield return fishSpawnZone.FishType.ToString();
			}
		}

		private class FishSpawnZone : SpawnZone
		{
			public FishType FishType { get; }

			public FishSpawnZone(string name, FColor color, IEnumerable<WeightedItem> spawns, FishType fishType)
				: base(name, color, spawns)
			{
				FishType = fishType;
			}
		}

		private static FishType ConvertFishType(EFishType type)
		{
			return type switch
			{
				EFishType.None => FishType.None,
				EFishType.Saltwater => FishType.Saltwater,
				EFishType.Freshwater => FishType.Freshwater,
				_ => FishType.None
			};
		}

#pragma warning disable CS0649 // Field never assigned to

		private struct FFishSpawnConfig : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public ObjectPointer SpawnMap;
			public List<FFIshSpawnZoneSetup> SpawnZones;
		}

		private struct FFishSpawnZones : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public Dictionary<FRowEnum, int> SpawnList;
			public float ZoneFishQuality;
		}

		private struct FFishData : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public FRowHandle Fish;
			public ObjectPointer Image;
			public string Lore;
			public EFishRarity Rarity;
			public EFishType Type;
			public List<FRowHandle> Maps;
			public List<FRowEnum> Biomes;
			public int MinWeight;
			public int MaxWeight;
			public int MinLength;
			public int MaxLength;
			public List<FRowHandle> Lures;
			public FRowEnum CaptureStat;
		}

		private struct FFIshSpawnZoneSetup
		{
			public FColor Color;
			public FRowHandle SpawnZone;
		}

		private enum EFishRarity
		{
			None,
			Common,
			Uncommon,
			Rare,
			unique
		}

		private enum EFishType
		{
			None,
			Saltwater,
			Freshwater
		}
	}

#pragma warning restore CS0649
}
