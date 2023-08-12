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
	internal class AISpawnMiner : IDataMiner
	{
		public string Name => "Spawns";

		private static readonly SKColor BackgroundColor = new(0xff101010);
		private static readonly SKColor LineColor = new(0x80f0f0f0);
		private static readonly SKColor TextColor = new(0xfff0f0f0);

		private static readonly SKTypeface TitleTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float TitleTextSize = 18.0f;

		private static readonly SKTypeface BodyTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private static readonly SKTypeface BodyBoldTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float BodyTextSize = 14.0f;

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportSpawnConfig(providerManager, config, logger);
			return true;
		}

		private void ExportSpawnConfig(IProviderManager providerManager, Config config, Logger logger)
		{
			// Load data tables
			GameFile terrainsFile = providerManager.DataProvider.Files["Prospects/D_Terrains.json"];
			IcarusDataTable<FIcarusTerrain> terrainsTable = IcarusDataTable<FIcarusTerrain>.DeserializeTable("D_Terrains", Encoding.UTF8.GetString(terrainsFile.Read()));

			GameFile spawnConfigFile = providerManager.DataProvider.Files["AI/D_AISpawnConfig.json"];
			IcarusDataTable<FAISpawnConfigData> spawnConfigTable = IcarusDataTable<FAISpawnConfigData>.DeserializeTable("D_AISpawnConfig", Encoding.UTF8.GetString(spawnConfigFile.Read()));

			GameFile spawnZonesFile = providerManager.DataProvider.Files["AI/D_AISpawnZones.json"];
			IcarusDataTable<FAISpawnZones> spawnZoneTable = IcarusDataTable<FAISpawnZones>.DeserializeTable("D_AISpawnZones", Encoding.UTF8.GetString(spawnZonesFile.Read()));

			GameFile setupFile = providerManager.DataProvider.Files["AI/D_AISetup.json"];
			IcarusDataTable<FAISetup> setupTable = IcarusDataTable<FAISetup>.DeserializeTable("D_AISetup", Encoding.UTF8.GetString(setupFile.Read()));

			GameFile creatureTypeFile = providerManager.DataProvider.Files["AI/D_AICreatureType.json"];
			IcarusDataTable<FAICreatureType> creatureTypeTable = IcarusDataTable<FAICreatureType>.DeserializeTable("D_AICreatureType", Encoding.UTF8.GetString(creatureTypeFile.Read()));

			GameFile autonomousSpawnsFile = providerManager.DataProvider.Files["AI/D_AutonomousSpawns.json"];
			IcarusDataTable<FAutonomousSpawnData> autonomousSpawnsTable = IcarusDataTable<FAutonomousSpawnData>.DeserializeTable("D_AutonomousSpawns", Encoding.UTF8.GetString(autonomousSpawnsFile.Read()));

			// Gather data
			string getCreatureName(FRowEnum aiSetupRow)
			{
				FAISetup aiSetup = setupTable[aiSetupRow.Value];
				FAICreatureType creatureType = creatureTypeTable[aiSetup.CreatureType.RowName];
				return LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, creatureType.CreatureName);
			}

			HashSet<string> spawnConfigSet = new();
			foreach (FIcarusTerrain terrain in terrainsTable.Values)
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

			List<SpawnConfig> spawnConfigs = new();
			foreach (FAISpawnConfigData row in spawnConfigTable.Values.Where(r => spawnConfigSet.Contains(r.Name)))
			{
				List<SpawnZoneData> spawnZones = new();

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
							FAutonomousSpawnData autoSpawnData = autonomousSpawnsTable[autoSpawnerRow.RowName];
							autonomousSpawnCreatures.Add(getCreatureName(autoSpawnData.AISetup));
						}
					}
					autonomousSpawnCreatures.Sort();

					spawnZones.Add(new SpawnZoneData(zone.Name, zoneSetup.Color, zone.MinLevel, zone.MaxLevel, creatures, autonomousSpawnCreatures));
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
						writer.WriteLine("Zone,Color,MinLevel,MaxLevel,Creatures,AutoCreatures");
						foreach (SpawnZoneData spawnZone in spawnConfig.SpawnZones)
						{
							writer.Write($"{spawnZone.Name},\"=\"\"{spawnZone.Color.R},{spawnZone.Color.G},{spawnZone.Color.B}\"\"\",{spawnZone.MinLevel},{spawnZone.MaxLevel},");

							writer.Write("\"=\"\"");
							float weightSum = spawnZone.Creatures.Sum(c => c.Weight);
							for (int i = 0; i < spawnZone.Creatures.Count; ++i)
							{
								WeightedItem creature = spawnZone.Creatures[i];
								writer.Write($"{creature.Name}: {creature.Weight / weightSum * 100.0f:0.}%");
								if (i < spawnZone.Creatures.Count - 1)
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
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					if (spawnConfig.SpawnMap is null) continue;

					SKData outData = spawnConfig.SpawnMap.Encode(SKEncodedImageFormat.Png, 100);

					string outPath = Path.Combine(outDir, $"{spawnConfig.SpawnMapName}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}

			// Output overlay images
			{
				SKColor textColorNegative = new((byte)(255 - TextColor.Red), (byte)(255 - TextColor.Green), (byte)(255 - TextColor.Blue), TextColor.Alpha);

				using SKPaint titlePaint = new()
				{
					Color = TextColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = TitleTypeFace,
					TextSize = TitleTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint titlePaintNegative = new()
				{
					Color = textColorNegative,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = TitleTypeFace,
					TextSize = TitleTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint bodyPaint = new()
				{
					Color = TextColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = BodyTypeFace,
					TextSize = BodyTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint bodyBoldPaint = new()
				{
					Color = TextColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = BodyBoldTypeFace,
					TextSize = BodyTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint bodyBoldPaintNegative = new()
				{
					Color = textColorNegative,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = BodyBoldTypeFace,
					TextSize = BodyTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint linePaint = new SKPaint()
				{
					Color = LineColor,
					IsStroke = true,
					IsAntialias = false,
					Style = SKPaintStyle.Stroke
				};

				using SKPaint fillPaint = new SKPaint()
				{
					IsStroke = false,
					IsAntialias = false,
					Style = SKPaintStyle.Fill
				};

				string outDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					HashSet<string> seenZoneFileNames = new();

					foreach (SpawnZoneData spawnZone in spawnConfig.SpawnZones)
					{
						string titleText = spawnZone.Name[(spawnZone.Name.IndexOf("_") + 1)..];
						string levelText = $"Level: {spawnZone.MinLevel} - {spawnZone.MaxLevel}";
						List<string> body1Text = new();
						List<string> body2Text = new();

						float weightSum = spawnZone.Creatures.Sum(c => c.Weight);
						foreach (WeightedItem creature in spawnZone.Creatures)
						{
							body1Text.Add($"{creature.Name}: {creature.Weight / weightSum * 100.0f:0.}%");
						}
						for (int i = 0; i < spawnZone.AutonomousSpawnCreatures.Count; ++i)
						{
							body2Text.Add($"{spawnZone.AutonomousSpawnCreatures[i]}");
						}

						float textWidth = titlePaint.MeasureText(titleText);
						foreach (string text in body1Text.Concat(body2Text))
						{
							textWidth = Math.Max(textWidth, bodyPaint.MeasureText(text));
						}

						float textHeight = titlePaint.FontSpacing + bodyBoldPaint.FontSpacing + (body1Text.Count + body2Text.Count) * bodyPaint.FontSpacing;

						bool hasBothBodyTexts = body1Text.Count > 0 && body2Text.Count > 0;

						SKImageInfo surfaceInfo = new()
						{
							Width = (int)Math.Ceiling(textWidth) + 20,
							Height = (int)Math.Ceiling(textHeight) + (hasBothBodyTexts ? 40 : 30),
							ColorSpace = SKColorSpace.CreateSrgb(),
							ColorType = SKColorType.Rgba8888,
							AlphaType = SKAlphaType.Premul
						};

						SKData outData;

						using (SKSurface surface = SKSurface.Create(surfaceInfo))
						{
							SKCanvas canvas = surface.Canvas;

							canvas.Clear(BackgroundColor);

							float posY = 20.0f + titlePaint.FontSpacing + bodyPaint.FontSpacing;

							SKColor bannerBGColor = ColorUtil.ToSKColor(spawnZone.Color, 255);
							bool useNegative = !IsColorCloser(titlePaint.Color, bannerBGColor, titlePaintNegative.Color);

							// Banner
							fillPaint.Color = bannerBGColor;
							canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width, posY, fillPaint);

							// Outline
							canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width - 1.0f, surfaceInfo.Height - 1.0f, linePaint);

							// Title
							float textPosX = 10.0f;
							float textPosY = 10.0f - titlePaint.FontMetrics.Ascent;
							
							canvas.DrawText(titleText, textPosX, textPosY, useNegative ? titlePaintNegative : titlePaint);
							textPosY += titlePaint.FontSpacing;

							// Level
							canvas.DrawText(levelText, textPosX, textPosY, useNegative ? bodyBoldPaintNegative : bodyBoldPaint);
							textPosY += bodyPaint.FontSpacing + 10.0f;

							// Divider
							canvas.DrawLine(1.0f, posY, surfaceInfo.Width - 1.0f, posY, linePaint);
							posY += 5.0f;

							// Body 1
							foreach (string text in body1Text)
							{
								canvas.DrawText(text, textPosX, textPosY, bodyPaint);
								posY += bodyPaint.FontSpacing;
								textPosY += bodyPaint.FontSpacing;
							}

							// Divider
							if (hasBothBodyTexts)
							{
								posY += 5.0f;
								canvas.DrawLine(1.0f, posY, surfaceInfo.Width - 1.0f, posY, linePaint);
								posY += 5.0f;

								textPosY += 10.0f;
							}

							// Body 2
							foreach (string text in body2Text)
							{
								canvas.DrawText(text, textPosX, textPosY, bodyPaint);
								posY += bodyPaint.FontSpacing;
								textPosY += bodyPaint.FontSpacing;
							}

							surface.Flush();
							SKImage image = surface.Snapshot();
							outData = image.Encode(SKEncodedImageFormat.Png, 100);
						}

						string zoneFileName = spawnZone.Name;
						for (int i = 1; !seenZoneFileNames.Add(zoneFileName); ++i)
						{
							zoneFileName = $"{spawnZone.Name}_{i}";
						}

						string outPath = Path.Combine(outDir, spawnConfig.Name, $"{zoneFileName}.png");
						using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
						{
							outData.SaveTo(outStream);
						}
					}
				}
			}
		}

		// Is test closer to target than source is to target based on perceived luminance?
		private bool IsColorCloser(SKColor source, SKColor target, SKColor test)
		{
			const float rl = 0.2126f, gl = 0.7152f, bl = 0.0722f;

			float sourceLum = source.Red * rl + source.Green * gl + source.Blue * bl;
			float targetLum = target.Red * rl + target.Green * gl + target.Blue * bl;
			float testLum = test.Red * rl + test.Green * gl + test.Blue * bl;

			return Math.Abs(testLum - targetLum) < Math.Abs(sourceLum - targetLum);
		}

		private class SpawnConfig
		{
			public string Name { get; }

			public IReadOnlyList<SpawnZoneData> SpawnZones { get; }

			public string? SpawnMapName { get; }

			public SKBitmap? SpawnMap { get; }

			public SpawnConfig(string name, string? spawnMapName, SKBitmap? spawnMap, IEnumerable<SpawnZoneData> spawnZones)
			{
				Name = name;
				SpawnMapName = spawnMapName;
				SpawnMap = spawnMap;
				SpawnZones = new List<SpawnZoneData>(spawnZones);
			}

			public override string ToString()
			{
				return $"{Name}: {SpawnZones.Count} zones";
			}
		}

		private class SpawnZoneData
		{
			public string Name { get; }

			public FColor Color { get; }

			public int MinLevel { get; }

			public int MaxLevel { get; }

			public IReadOnlyList<WeightedItem> Creatures { get; }

			public IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			public SpawnZoneData(string name, FColor color, int minLevel, int maxLevel, IEnumerable<WeightedItem> creatures, IEnumerable<string> autonomousSpawnCreatures)
			{
				Name = name;
				Color = color;
				MinLevel = minLevel;
				MaxLevel = maxLevel;
				Creatures = new List<WeightedItem>(creatures);
				AutonomousSpawnCreatures = new List<string>(autonomousSpawnCreatures);
			}

			public override string ToString()
			{
				return $"{Name} [{Color}]: {Creatures.Count + AutonomousSpawnCreatures.Count} creatures";
			}
		}

		private class WeightedItem : IComparable<WeightedItem>
		{
			public string Name { get; }

			public int Weight { get; }

			public WeightedItem(string name, int weight)
			{
				Name = name;
				Weight = weight;
			}

			public override string ToString()
			{
				return $"{Name}: {Weight}";
			}

			public int CompareTo(WeightedItem? other)
			{
				return other is null ? 1 : Weight.CompareTo(other.Weight);
			}
		}

#pragma warning disable CS0649 // Field never assigned to

		private struct FIcarusTerrain : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public string TerrainName;
			public ObjectPointer Level;
			public ObjectPointer TemperatureMap;
			public FVector2D TemperatureMapRange;
			public ObjectPointer BiomeMap;
			public ObjectPointer Bounds;
			public FRowHandle SpawnConfig;
			public FRowHandle FishConfig;
			public ObjectPointer AudioZoneMap;
		}

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

		private struct FAISetup : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public FRowHandle CreatureType;

			// NOTE: There is a lot more to this struct we are leaving out because we don't care about it
		}

		private struct FAICreatureType : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public string CreatureName;

			// NOTE: There is a lot more to this struct we are leaving out because we don't care about it
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
