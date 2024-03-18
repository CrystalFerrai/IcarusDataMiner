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
	/// <summary>
	/// Extracts data about creature spawns
	/// </summary>
	internal class AISpawnMiner : SpawnMinerBase, IDataMiner
	{
		public string Name => "Spawns";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportSpawnConfig(providerManager, config, logger);
			return true;
		}

		private void ExportSpawnConfig(IProviderManager providerManager, Config config, Logger logger)
		{
			// Load data tables
			GameFile spawnConfigFile = providerManager.DataProvider.Files["AI/D_AISpawnConfig.json"];
			IcarusDataTable<FAISpawnConfigData> spawnConfigTable = IcarusDataTable<FAISpawnConfigData>.DeserializeTable("D_AISpawnConfig", Encoding.UTF8.GetString(spawnConfigFile.Read()));

			GameFile spawnZonesFile = providerManager.DataProvider.Files["AI/D_AISpawnZones.json"];
			IcarusDataTable<FAISpawnZones> spawnZoneTable = IcarusDataTable<FAISpawnZones>.DeserializeTable("D_AISpawnZones", Encoding.UTF8.GetString(spawnZonesFile.Read()));

			IcarusDataTable<FAISetup> setupTable = providerManager.DataTables.AISetupTable!;
			IcarusDataTable<FAICreatureType> creatureTypeTable = providerManager.DataTables.AICreatureTypeTable!;

			GameFile autonomousSpawnsFile = providerManager.DataProvider.Files["AI/D_AutonomousSpawns.json"];
			IcarusDataTable<FAutonomousSpawnData> autonomousSpawnsTable = IcarusDataTable<FAutonomousSpawnData>.DeserializeTable("D_AutonomousSpawns", Encoding.UTF8.GetString(autonomousSpawnsFile.Read()));

			// Gather data
			string getCreatureName(FRowEnum aiSetupRow)
			{
				return providerManager.DataTables.GetCreatureName(aiSetupRow, providerManager.AssetProvider);
			}

			HashSet<string> spawnConfigSet = new();
			foreach (FIcarusTerrain terrain in providerManager.DataTables.TerrainsTable!.Values)
			{
				spawnConfigSet.Add(terrain.SpawnConfig.RowName);
			}
			foreach (IList<ProspectData> prospectList in providerManager.ProspectDataUtil.ProspectsByTier.Values)
			{
				foreach (ProspectData prospect in prospectList)
				{
					if (prospect.AISpawnConfigOverride is null) continue;
					spawnConfigSet.Add(prospect.AISpawnConfigOverride);
				}
			}

			Dictionary<string, HashSet<int>> densityMap = new();
			List<AISpawnConfig> spawnConfigs = new();

			foreach (FAISpawnConfigData row in spawnConfigTable.Values.Where(r => spawnConfigSet.Contains(r.Name)))
			{
				HashSet<int> densities = new();
				densityMap.Add(row.Name, densities);

				List<AISpawnZone> spawnZones = new();
				Dictionary<int, CompositeSpawnZone> compositeZoneMap = new();

				char nextCompositeId = 'A';

				foreach (FAISpawnZoneSetup zoneSetup in row.SpawnZones)
				{
					FAISpawnZones zone = spawnZoneTable[zoneSetup.SpawnZone.RowName];

					List<WeightedItem> creatures = new();
					if (zone.Creatures.SpawnList is not null)
					{
						foreach (var creatureSpawnPair in zone.Creatures.SpawnList)
						{
							creatures.Add(new WeightedItem(getCreatureName(creatureSpawnPair.Key), creatureSpawnPair.Value));
						}
					}
					creatures.Sort();
					creatures.Reverse();

					List<string> autonomousSpawnCreatures = new();
					if (zone.Creatures.RelevantAutonomousSpawners is not null)
					{
						foreach (FRowHandle autoSpawnerRow in zone.Creatures.RelevantAutonomousSpawners)
						{
							if (autonomousSpawnsTable.TryGetValue(autoSpawnerRow.RowName, out FAutonomousSpawnData autoSpawnData))
							{
								autonomousSpawnCreatures.Add(getCreatureName(autoSpawnData.AISetup));
							}
						}
					}
					autonomousSpawnCreatures.Sort();

					densities.Add(zone.Creatures.BiomeSpawnDensity);

					AISpawnZone newSpawnZone = new(zone.Name, zoneSetup.Color, zone.MinLevel, zone.MaxLevel, zone.Creatures.BiomeSpawnDensity, creatures, autonomousSpawnCreatures);
					spawnZones.Add(newSpawnZone);

					int creatureHash = newSpawnZone.GetCreatureHash();
					CompositeSpawnZone? compositeZone;
					if (compositeZoneMap.TryGetValue(creatureHash, out compositeZone))
					{
						compositeZone.UpdateName(newSpawnZone.Name);
					}
					else
					{
						if (nextCompositeId > 'Z')
						{
							// 0 looks like O, so skip it
							nextCompositeId = '1';
						}
						if (nextCompositeId > '9' && nextCompositeId < 'A')
						{
							throw new NotImplementedException("Too many composites for single letter names. Need to expand this.");
						}
						compositeZone = new(nextCompositeId.ToString(), newSpawnZone);
						compositeZoneMap.Add(creatureHash, compositeZone);
						++nextCompositeId;
					}

					newSpawnZone.CompositeId = compositeZone.Id;

					if (newSpawnZone.SpawnDensity != compositeZone.SpawnDensity) throw new NotImplementedException("Found two spawn zones with the same creatures but different spawn density. We need to implement separating spawn composites by desnsity if this ever comes up.");
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

				List<CompositeSpawnZone> compositeZones = new(compositeZoneMap.Values);
				compositeZones.Sort();
				spawnConfigs.Add(new AISpawnConfig(row.Name, spawnMapName, spawnMap, spawnZones, compositeZones));
			}

			// Output data
			{
				string outDir = Path.Combine(config.OutputDirectory, Name, "Data");
				foreach (AISpawnConfig spawnConfig in spawnConfigs)
				{
					string outputPath = Path.Combine(outDir, $"{spawnConfig.Name}.csv");

					using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
					using (StreamWriter writer = new StreamWriter(outStream))
					{
						writer.WriteLine("Zone,Color,MinLevel,MaxLevel,Density,Creatures,AutoCreatures");
						foreach (AISpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							writer.Write($"{spawnZone.Name},\"=\"\"{spawnZone.Color.R},{spawnZone.Color.G},{spawnZone.Color.B}\"\"\",{spawnZone.MinLevel},{spawnZone.MaxLevel},{spawnZone.SpawnDensity},");

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
							writer.Write("\"\"\",\"=\"\"");
							for (int i = 0; i < spawnZone.AutonomousSpawnCreatures.Count; ++i)
							{
								writer.Write($"{spawnZone.AutonomousSpawnCreatures[i]}");
								if (i < spawnZone.AutonomousSpawnCreatures.Count - 1)
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
				foreach (AISpawnConfig spawnConfig in spawnConfigs)
				{
					if (spawnConfig.SpawnMap is null) continue;
					OutputSpawnMaps(outDir, spawnConfig.SpawnMapName!, spawnConfig.SpawnMap, logger);
				}
			}

			// Output overlay images
			{
				string outDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				foreach (AISpawnConfig spawnConfig in spawnConfigs)
				{
					bool showDesnities = densityMap[spawnConfig.Name].Count > 1;

					// Full spawn zone and composite info images
					{
						HashSet<string> seenZoneFileNames = new();

						foreach (AISpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							string zoneFileName = spawnZone.Name;
							for (int i = 1; !seenZoneFileNames.Add(zoneFileName); ++i)
							{
								zoneFileName = $"{spawnZone.Name}_{i}";
							}

							SKData outData = CreateSpawnZoneInfoBox(spawnZone, showDesnities);

							string outPath = Path.Combine(outDir, spawnConfig.Name, "Zones", $"{zoneFileName}.png");
							using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
							{
								outData.SaveTo(outStream);
							}
						}

						foreach (CompositeSpawnZone spawnZone in spawnConfig.CompositeZones)
						{
							SKData outData = CreateSpawnZoneInfoBox(spawnZone, showDesnities);

							string outPath = Path.Combine(outDir, spawnConfig.Name, "Composites", $"{spawnZone.Id}.png");
							using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
							{
								outData.SaveTo(outStream);
							}
						}
					}

					// Composite reference info images
					{
						HashSet<string> seenZoneFileNames = new();

						foreach (AISpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							string levelText = $"{spawnZone.MinLevel}-{spawnZone.MaxLevel}";

							float levelTextWidth = mBodyBoldPaint.MeasureText(levelText);

							float textHeight = mBodyBoldPaint.FontSpacing;

							float circleRadius = mBodyBoldPaint.FontSpacing * 0.5f;

							SKImageInfo surfaceInfo = new()
							{
								Width = (int)Math.Ceiling(levelTextWidth + circleRadius * 2.0f) + 14,
								Height = (int)Math.Ceiling(textHeight) + 9,
								ColorSpace = SKColorSpace.CreateSrgb(),
								ColorType = SKColorType.Rgba8888,
								AlphaType = SKAlphaType.Premul
							};

							SKData outData;
							using (SKSurface surface = SKSurface.Create(surfaceInfo))
							{
								SKCanvas canvas = surface.Canvas;

								canvas.Clear(BackgroundColor);

								SKColor bannerBGColor = ColorUtil.ToSKColor(spawnZone.Color, 255);
								bool useNegative = !IsColorCloser(mTitlePaint.Color, bannerBGColor, mTitlePaintNegative.Color);

								float textPosX = 5.0f + circleRadius;
								float textPosY = 4.0f - mBodyBoldPaint.FontMetrics.Ascent;

								// Banner
								mFillPaint.Color = bannerBGColor;
								canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width, surfaceInfo.Height, mFillPaint);

								// Outline
								canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width - 1.0f, surfaceInfo.Height - 1.0f, useNegative ? mLinePaintNegative : mLinePaint);

								// Name
								SKPoint circleCenter = new(textPosX, textPosY + mBodyBoldCenterPaint.FontMetrics.Ascent * 0.33f);
								canvas.DrawCircle(circleCenter, circleRadius, useNegative ? mNameCirclePaintNegative : mNameCirclePaint);
								canvas.DrawText(spawnZone.CompositeId, textPosX, textPosY, useNegative ? mBodyBoldCenterPaintNegative : mBodyBoldCenterPaint);
								textPosX += circleRadius + 4.0f;

								// Level
								canvas.DrawText(levelText, textPosX, textPosY, useNegative ? mBodyBoldPaintNegative : mBodyBoldPaint);

								surface.Flush();
								SKImage image = surface.Snapshot();
								outData = image.Encode(SKEncodedImageFormat.Png, 100);
							}

							string zoneFileName = $"{spawnZone.CompositeId}_{spawnZone.Color.A}_{spawnZone.Name}";
							for (int i = 1; !seenZoneFileNames.Add(zoneFileName); ++i)
							{
								zoneFileName = $"{spawnZone.Name}_{i}";
							}

							string outPath = Path.Combine(outDir, spawnConfig.Name, "CompositeRefs", $"{zoneFileName}.png");
							using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
							{
								outData.SaveTo(outStream);
							}
						}
					}
				}
			}
		}

		protected override IEnumerable<string> GetInfoBoxSubtitleLines(ISpawnZoneData spawnZone, object? userData)
		{
			if (spawnZone is AISpawnZone creatureSpawnZone)
			{
				yield return $"Level: {creatureSpawnZone.MinLevel} - {creatureSpawnZone.MaxLevel}";
			}
			else if (spawnZone is IAISpawnZoneData aiSpawnZone)
			{
				if (userData is bool showDensities && showDensities)
				{
					yield return $"Density: {aiSpawnZone.SpawnDensity}";
				}
			}
		}

		protected override IEnumerable<string> GetInfoBoxFooterLines(ISpawnZoneData spawnZone, object? userData)
		{
			if (spawnZone is IAISpawnZoneData aiSpawnZone)
			{
				for (int i = 0; i < aiSpawnZone.AutonomousSpawnCreatures.Count; ++i)
				{
					yield return $"{aiSpawnZone.AutonomousSpawnCreatures[i]}";
				}
			}
		}

		private interface IAISpawnZoneData
		{
			int SpawnDensity { get; }

			IReadOnlyList<string> AutonomousSpawnCreatures { get; }
		}

		private class AISpawnConfig : SpawnConfig
		{
			public IReadOnlyList<CompositeSpawnZone> CompositeZones { get; }

			public AISpawnConfig(string name, string? spawnMapName, SKBitmap? spawnMap, IEnumerable<AISpawnZone> spawnZones, IReadOnlyList<CompositeSpawnZone> compositeZones)
				: base(name, spawnMapName, spawnMap, spawnZones)
			{
				CompositeZones = compositeZones;
			}
		}

		private class AISpawnZone : SpawnZone, IAISpawnZoneData
		{
			public int MinLevel { get; }

			public int MaxLevel { get; }

			public int SpawnDensity { get; }

			public string? CompositeId { get; set; }

			public IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			public AISpawnZone(string name, FColor color, int minLevel, int maxLevel, int spawnDensity, IEnumerable<WeightedItem> spawns, IEnumerable<string> autonomousSpawnCreatures)
				: base(name, color, spawns)
			{
				MinLevel = minLevel;
				MaxLevel = maxLevel;
				SpawnDensity = spawnDensity;
				AutonomousSpawnCreatures = new List<string>(autonomousSpawnCreatures);
			}

			public int GetCreatureHash()
			{
				if (Spawns.Count == 0 && AutonomousSpawnCreatures.Count == 0)
				{
					return 0;
				}

				int hash = 17;
				foreach (WeightedItem creature in Spawns)
				{
					hash = hash * 23 + creature.GetHashCode();
				}
				foreach (string creature in AutonomousSpawnCreatures)
				{
					hash = hash * 23 + creature.GetHashCode();
				}
				return hash;
			}

			public override string ToString()
			{
				return $"{Name} [{Color}]: {Spawns.Count + AutonomousSpawnCreatures.Count} creatures";
			}
		}

		private class CompositeSpawnZone : SpawnZone, IAISpawnZoneData, IEquatable<CompositeSpawnZone>, IComparable<CompositeSpawnZone>
		{
			public int SpawnDensity { get; }

			public IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			public CompositeSpawnZone(string id, AISpawnZone firstZone)
				: base(TrimName(firstZone.Name), firstZone.Color, firstZone.Spawns.ToArray())
			{
				Id = id;
				AutonomousSpawnCreatures = firstZone.AutonomousSpawnCreatures.ToArray();
				SpawnDensity = firstZone.SpawnDensity;
			}

			public void UpdateName(string newName)
			{
				string name = TrimName(newName);
				if (name.Length < Name.Length)
				{
					Name = name;
				}
			}

			public override int GetHashCode()
			{
				return Id!.GetHashCode();
			}

			public bool Equals(CompositeSpawnZone? other)
			{
				return other is not null && Id!.Equals(other.Id);
			}

			public override bool Equals(object? obj)
			{
				return obj is CompositeSpawnZone other && Equals(other);
			}

			public int CompareTo(CompositeSpawnZone? other)
			{
				return other is null ? 1 : Id!.CompareTo(other.Id);
			}

			private static string TrimName(string name)
			{
				int underscoreIndex = name.LastIndexOf('_');
				if (underscoreIndex >= 0)
				{
					if (int.TryParse(name[(underscoreIndex + 1)..], out int _))
					{
						return name[..underscoreIndex];
					}
				}
				return name;
			}
		}

#pragma warning disable CS0649 // Field never assigned to

		private struct FAISpawnConfigData : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public Dictionary<FRowEnum, FAISpawnRulesList> AISpawnRules;
			public ObjectPointer SpawnMap;
			public List<FAISpawnZoneSetup> SpawnZones;
		}

		private struct FAISpawnRulesList
		{
			public List<FRowEnum> SpawnRules;
		}

		private struct FAISpawnZoneSetup
		{
			public FColor Color;
			public FRowHandle SpawnZone;
		}

		private struct FAISpawnZones : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public FBiomeAISpawnData Creatures;
			public int MinLevel;
			public int MedianLevel;
			public int MaxLevel;
		}

		private struct FBiomeAISpawnData
		{
			public Dictionary<FRowEnum, int> SpawnList;
			public int BiomeSpawnDensity;
			public List<FRowHandle> RelevantAutonomousSpawners;
		}

		private struct FAutonomousSpawnData : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public FRowEnum AISetup;
			public ObjectPointer AISpawnBehavior;
			public int MaxNumAroundPlayers;
			public int MaxSpawnCount;
			public int MaxDistanceToPlayers;

			// We don't need the following field so we are not implementing it
			//public FGameplayTagContainer GameplayTagsToApply;
		}
	}

#pragma warning restore CS0649
}
