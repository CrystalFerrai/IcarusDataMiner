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
		private static readonly SKColor BannerBaseColor = new(0xff303030);
		private static readonly SKColor ForegroundColor = new(0xfff0f0f0);

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

			Dictionary<string, HashSet<int>> densityMap = new();
			List<SpawnConfig> spawnConfigs = new();

			foreach (FAISpawnConfigData row in spawnConfigTable.Values.Where(r => spawnConfigSet.Contains(r.Name)))
			{
				HashSet<int> densities = new();
				densityMap.Add(row.Name, densities);

				List<SpawnZone> spawnZones = new();
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
							FAutonomousSpawnData autoSpawnData = autonomousSpawnsTable[autoSpawnerRow.RowName];
							autonomousSpawnCreatures.Add(getCreatureName(autoSpawnData.AISetup));
						}
					}
					autonomousSpawnCreatures.Sort();

					densities.Add(zone.Creatures.BiomeSpawnDensity);

					SpawnZone newSpawnZone = new(zone.Name, zoneSetup.Color, zone.MinLevel, zone.MaxLevel, zone.Creatures.BiomeSpawnDensity, creatures, autonomousSpawnCreatures);
					spawnZones.Add(newSpawnZone);

					int creatureHash = newSpawnZone.GetCreatureHash();
					CompositeSpawnZone? compositeZone;
					if (compositeZoneMap.TryGetValue(creatureHash, out compositeZone))
					{
						compositeZone.UpdateName(newSpawnZone.Name);
					}
					else
					{
						if (nextCompositeId > 'Z') throw new NotImplementedException("Too many composites for single letter names. Need to expand this.");
						compositeZone = new(nextCompositeId.ToString(), creatureHash, newSpawnZone);
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
				spawnConfigs.Add(new SpawnConfig(row.Name, spawnMapName, spawnMap, spawnZones, compositeZones));
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
						writer.WriteLine("Zone,Color,MinLevel,MaxLevel,Density,Creatures,AutoCreatures");
						foreach (SpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							writer.Write($"{spawnZone.Name},\"=\"\"{spawnZone.Color.R},{spawnZone.Color.G},{spawnZone.Color.B}\"\"\",{spawnZone.MinLevel},{spawnZone.MaxLevel},{spawnZone.SpawnDensity},");

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

					// Another copy with all pixels set to full alpha for easier image composition
					SKColor[] pixels = spawnConfig.SpawnMap.Pixels.ToArray();
					for (int i = 0; i < pixels.Length; ++i)
					{
						pixels[i] = new SKColor((uint)pixels[i] | 0xff000000u);
					}

					SKBitmap opaque = new(spawnConfig.SpawnMap.Info)
					{
						Pixels = pixels
					};
					outData = opaque.Encode(SKEncodedImageFormat.Png, 100);

					outPath = Path.Combine(outDir, $"{spawnConfig.SpawnMapName}_Opaque.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}

				}
			}

			// Output overlay images
			{
				SKColor foregroundColorNegative = new((byte)(255 - ForegroundColor.Red), (byte)(255 - ForegroundColor.Green), (byte)(255 - ForegroundColor.Blue), ForegroundColor.Alpha);

				using SKPaint titlePaint = new()
				{
					Color = ForegroundColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = TitleTypeFace,
					TextSize = TitleTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint titlePaintNegative = titlePaint.Clone();
				titlePaintNegative.Color = foregroundColorNegative;

				using SKPaint bodyPaint = new()
				{
					Color = ForegroundColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = BodyTypeFace,
					TextSize = BodyTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint bodyBoldPaint = new()
				{
					Color = ForegroundColor,
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Typeface = BodyBoldTypeFace,
					TextSize = BodyTextSize,
					TextAlign = SKTextAlign.Left
				};

				using SKPaint bodyBoldPaintNegative = bodyBoldPaint.Clone();
				bodyBoldPaintNegative.Color = foregroundColorNegative;

				using SKPaint bodyBoldCenterPaint = bodyBoldPaint.Clone();
				bodyBoldCenterPaint.TextAlign = SKTextAlign.Center;

				using SKPaint bodyBoldCenterPaintNegative = bodyBoldPaintNegative.Clone();
				bodyBoldCenterPaintNegative.TextAlign = SKTextAlign.Center;

				using SKPaint nameCirclePaint = new()
				{
					Color = ForegroundColor,
					IsStroke = true,
					IsAntialias = true,
					Style = SKPaintStyle.Stroke,
					StrokeWidth = 1.5f
				};

				using SKPaint nameCirclePaintNegative = nameCirclePaint.Clone();
				nameCirclePaintNegative.Color = foregroundColorNegative;

				using SKPaint linePaint = new()
				{
					Color = ForegroundColor,
					IsStroke = true,
					IsAntialias = false,
					Style = SKPaintStyle.Stroke
				};

				using SKPaint linePaintNegative = linePaint.Clone();
				linePaintNegative.Color = foregroundColorNegative;

				using SKPaint fillPaint = new()
				{
					IsStroke = false,
					IsAntialias = false,
					Style = SKPaintStyle.Fill
				};

				string outDir = Path.Combine(config.OutputDirectory, Name, "Visual");
				foreach (SpawnConfig spawnConfig in spawnConfigs)
				{
					bool showDensities = densityMap[spawnConfig.Name].Count > 1;

					// Helper to create an image for a spawn zone or spawn composite
					SKData CreateSpawnZoneInfoBox(ISpawnZoneData spawnZone)
					{
						bool showLevelInfo = spawnZone.MinLevel >= 0;

						string titleText = spawnZone.Name[(spawnZone.Name.IndexOf("_") + 1)..];
						string levelText = $"Level: {spawnZone.MinLevel} - {spawnZone.MaxLevel}";
						string densityText = $"Density: {spawnZone.SpawnDensity}";
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

						float circleRadius = bodyBoldPaint.FontSpacing * 0.5f;

						float titleWidth = titlePaint.MeasureText(titleText);
						if (spawnZone.Id is not null)
						{
							titleWidth += circleRadius * 2.0f + 4.0f;
						}

						float textWidth = titleWidth;
						if (showLevelInfo) textWidth = Math.Max(textWidth, bodyBoldPaint.MeasureText(levelText));
						if (showDensities) textWidth = Math.Max(textWidth, bodyBoldPaint.MeasureText(densityText));
						foreach (string text in body1Text.Concat(body2Text))
						{
							textWidth = Math.Max(textWidth, bodyPaint.MeasureText(text));
						}

						float textHeight = titlePaint.FontSpacing + (body1Text.Count + body2Text.Count) * bodyPaint.FontSpacing;
						if (showLevelInfo) textHeight += bodyBoldPaint.FontSpacing;
						if (showDensities) textHeight += bodyBoldPaint.FontSpacing;

						bool hasBothBodyTexts = body1Text.Count > 0 && body2Text.Count > 0;

						SKImageInfo surfaceInfo = new()
						{
							Width = (int)Math.Ceiling(textWidth) + 20,
							Height = (int)Math.Ceiling(textHeight) + (hasBothBodyTexts ? 40 : 30),
							ColorSpace = SKColorSpace.CreateSrgb(),
							ColorType = SKColorType.Rgba8888,
							AlphaType = SKAlphaType.Premul
						};

						using (SKSurface surface = SKSurface.Create(surfaceInfo))
						{
							SKCanvas canvas = surface.Canvas;

							canvas.Clear(BackgroundColor);

							float posY = 20.0f + titlePaint.FontSpacing;
							if (showLevelInfo) posY += bodyBoldPaint.FontSpacing;
							if (showDensities) posY += bodyBoldPaint.FontSpacing;

							SKColor bannerBGColor = BannerBaseColor;
							if (spawnZone.Id is null)
							{
								bannerBGColor = ColorUtil.ToSKColor(spawnZone.Color, 255);
							}
							bool useNegative = !IsColorCloser(titlePaint.Color, bannerBGColor, titlePaintNegative.Color);

							// Banner
							fillPaint.Color = bannerBGColor;
							canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width, posY, fillPaint);

							// Outline
							canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width - 1.0f, surfaceInfo.Height - 1.0f, linePaint);

							// Title
							float textPosX = 10.0f;
							float textPosY = 10.0f - titlePaint.FontMetrics.Ascent;

							if (spawnZone.Id is not null)
							{
								float textYOffset = Math.Abs((titlePaint.FontMetrics.Ascent - bodyBoldCenterPaint.FontMetrics.Ascent) * 0.5f);
								SKPoint circleCenter = new(textPosX + circleRadius, textPosY + (bodyBoldCenterPaint.FontMetrics.Ascent * 0.33f - textYOffset));
								canvas.DrawCircle(circleCenter, circleRadius, useNegative ? nameCirclePaintNegative : nameCirclePaint);
								canvas.DrawText(spawnZone.Id, textPosX + circleRadius, textPosY - textYOffset, useNegative ? bodyBoldCenterPaintNegative : bodyBoldCenterPaint);

								canvas.DrawText(titleText, textPosX + circleRadius * 2.0f + 4.0f, textPosY, useNegative ? titlePaintNegative : titlePaint);
							}
							else
							{
								canvas.DrawText(titleText, textPosX, textPosY, useNegative ? titlePaintNegative : titlePaint);
							}
							textPosY += titlePaint.FontSpacing;

							// Level
							if (showLevelInfo)
							{
								canvas.DrawText(levelText, textPosX, textPosY, useNegative ? bodyBoldPaintNegative : bodyBoldPaint);
								textPosY += bodyPaint.FontSpacing;
							}

							// Density
							if (showDensities)
							{
								canvas.DrawText(densityText, textPosX, textPosY, useNegative ? bodyBoldPaintNegative : bodyBoldPaint);
								textPosY += bodyPaint.FontSpacing;
							}

							// Divider
							textPosY += 10.0f;
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
							return image.Encode(SKEncodedImageFormat.Png, 100);
						}
					}

					// Full spawn zone and composite info images
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

						foreach (CompositeSpawnZone spawnZone in spawnConfig.CompositeZones)
						{
							SKData outData = CreateSpawnZoneInfoBox(spawnZone);

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

						foreach (SpawnZone spawnZone in spawnConfig.SpawnZones)
						{
							string levelText = $"{spawnZone.MinLevel}-{spawnZone.MaxLevel}";

							float levelTextWidth = bodyBoldPaint.MeasureText(levelText);

							float textHeight = bodyBoldPaint.FontSpacing;

							float circleRadius = bodyBoldPaint.FontSpacing * 0.5f;

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
								bool useNegative = !IsColorCloser(titlePaint.Color, bannerBGColor, titlePaintNegative.Color);

								float textPosX = 5.0f + circleRadius;
								float textPosY = 4.0f - bodyBoldPaint.FontMetrics.Ascent;

								// Banner
								fillPaint.Color = bannerBGColor;
								canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width, surfaceInfo.Height, fillPaint);

								// Outline
								canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width - 1.0f, surfaceInfo.Height - 1.0f, useNegative ? linePaintNegative : linePaint);

								// Name
								SKPoint circleCenter = new(textPosX, textPosY + bodyBoldCenterPaint.FontMetrics.Ascent * 0.33f);
								canvas.DrawCircle(circleCenter, circleRadius, useNegative ? nameCirclePaintNegative : nameCirclePaint);
								canvas.DrawText(spawnZone.CompositeId, textPosX, textPosY, useNegative ? bodyBoldCenterPaintNegative : bodyBoldCenterPaint);
								textPosX += circleRadius + 4.0f;

								// Level
								canvas.DrawText(levelText, textPosX, textPosY, useNegative ? bodyBoldPaintNegative : bodyBoldPaint);

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

		// Is test closer to target than source is to target based on perceived luminance?
		private static bool IsColorCloser(SKColor source, SKColor target, SKColor test)
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

			public IReadOnlyList<SpawnZone> SpawnZones { get; }

			public IReadOnlyList<CompositeSpawnZone> CompositeZones { get; }

			public string? SpawnMapName { get; }

			public SKBitmap? SpawnMap { get; }

			public SpawnConfig(string name, string? spawnMapName, SKBitmap? spawnMap, IEnumerable<SpawnZone> spawnZones, IReadOnlyList<CompositeSpawnZone> compositeZones)
			{
				Name = name;
				SpawnMapName = spawnMapName;
				SpawnMap = spawnMap;
				SpawnZones = new List<SpawnZone>(spawnZones);
				CompositeZones = compositeZones;
			}

			public override string ToString()
			{
				return $"{Name}: {SpawnZones.Count} zones";
			}
		}

		private interface ISpawnZoneData
		{
			string? Id { get; }

			string Name { get; }

			FColor Color { get; }

			IReadOnlyList<WeightedItem> Creatures { get; }

			IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			int MinLevel { get; }

			int MaxLevel { get; }

			int SpawnDensity { get; }
		}

		private class SpawnZone : ISpawnZoneData
		{
			public string? Id => null;

			public string Name { get; }

			public FColor Color { get; }

			public int MinLevel { get; }

			public int MaxLevel { get; }

			public int SpawnDensity { get; }

			public string? CompositeId { get; set; }

			public IReadOnlyList<WeightedItem> Creatures { get; }

			public IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			public SpawnZone(string name, FColor color, int minLevel, int maxLevel, int spawnDensity, IEnumerable<WeightedItem> creatures, IEnumerable<string> autonomousSpawnCreatures)
			{
				Name = name;
				Color = color;
				MinLevel = minLevel;
				MaxLevel = maxLevel;
				SpawnDensity = spawnDensity;
				Creatures = new List<WeightedItem>(creatures);
				AutonomousSpawnCreatures = new List<string>(autonomousSpawnCreatures);
			}

			public int GetCreatureHash()
			{
				if (Creatures.Count == 0 && AutonomousSpawnCreatures.Count == 0)
				{
					return 0;
				}

				int hash = 17;
				foreach (WeightedItem creature in Creatures)
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
				return $"{Name} [{Color}]: {Creatures.Count + AutonomousSpawnCreatures.Count} creatures";
			}
		}

		private class CompositeSpawnZone : ISpawnZoneData, IEquatable<CompositeSpawnZone>, IComparable<CompositeSpawnZone>
		{
			public string? Id { get; }

			public string Name { get; private set; }

			public FColor Color { get; }

			public IReadOnlyList<WeightedItem> Creatures { get; }

			public IReadOnlyList<string> AutonomousSpawnCreatures { get; }

			public int MinLevel => -1;

			public int MaxLevel => -1;

			public int SpawnDensity { get; }

			public int CreatureHash { get; }

			public CompositeSpawnZone(string id, int creatureHash, SpawnZone firstZone)
			{
				Id = id;
				Name = TrimName(firstZone.Name);
				Color = firstZone.Color;
				Creatures = firstZone.Creatures.ToArray();
				AutonomousSpawnCreatures = firstZone.AutonomousSpawnCreatures.ToArray();
				SpawnDensity = firstZone.SpawnDensity;
				CreatureHash = creatureHash;
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

		private class WeightedItem : IEquatable<WeightedItem>, IComparable<WeightedItem>
		{
			public string Name { get; }

			public int Weight { get; }

			public WeightedItem(string name, int weight)
			{
				Name = name;
				Weight = weight;
			}

			public override int GetHashCode()
			{
				int hash = 17;
				hash = hash * 23 + Name.GetHashCode();
				hash = hash * 23 + Weight.GetHashCode();
				return hash;
			}

			public bool Equals(WeightedItem? other)
			{
				return other is not null && Name.Equals(other.Name) && Weight.Equals(other.Weight);
			}

			public override bool Equals(object? obj)
			{
				return obj is WeightedItem other && Equals(other);
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
