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
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Text;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines data related to storms and forecasts
	/// </summary>
	internal class WeatherMiner : IDataMiner
	{
		private const int VerticalBarPad = 4;
		private const int BarWidthPerMinute = 6;
		private const int BarWidthPerSecond = 4;
		private const int BarSpacing = 4;
		private const int TierIconSize = 48;

		private static readonly SKColor BackgroundColor = new(0xff101010);
		private static readonly SKColor LineColor = new(0x80f0f0f0);
		private static readonly SKColor TextColor = new(0xfff0f0f0);

		private static readonly SKTypeface TitleTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float TitleTextSize = 24.0f;

		private static readonly SKTypeface Title2TypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float Title2TextSize = 16.0f;

		private static readonly SKTypeface BodyTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float BodyTextSize = 14.0f;

		public string Name => "Weather";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			IReadOnlyList<WeatherTierIcon> tierIcons = LoadTierIcons(providerManager, logger);

			ProspectForecastTable? forecasts = LoadJsonAsset<ProspectForecastTable>("Weather/D_ProspectForecast.json", providerManager.DataProvider);
			if (forecasts?.Rows == null) throw new DataMinerException("Error reading prospect forecast table");

			HashSet<string> activeForecasts = new HashSet<string>(providerManager.ProspectDataUtil.ProspectsByTier.Values.SelectMany(l => l.Where(p => p.Forecast != null).Select(p => p.Forecast!)));

			ExportLegend(tierIcons, providerManager, config, logger);
			ExportForecasts(forecasts, activeForecasts, tierIcons, providerManager, config, logger);

			ExportStorms(providerManager, config, logger);

			return true;
		}

		private IReadOnlyList<WeatherTierIcon> LoadTierIcons(IProviderManager providerManager, Logger logger)
		{
			WeatherTierIconTable? table = LoadJsonAsset<WeatherTierIconTable>("Weather/D_WeatherTierIcon.json", providerManager.DataProvider);
			if (table?.Rows == null) throw new DataMinerException("Error reading weather tier icon table");

			for (int i = 0; i < table.Rows.Length; ++i)
			{
				if (table.Rows[i].TierIcon == "None") continue;

				table.Rows[i].Image = AssetUtil.LoadAndDecodeTexture(table.Rows[i].TierIcon, table.Rows[i].TierIcon, providerManager.AssetProvider, logger);
			}

			return table.Rows;
		}

		private void ExportLegend(IReadOnlyList<WeatherTierIcon> tierIcons, IProviderManager providerManager, Config config, Logger logger)
		{
			using SKPaint titlePaint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = TitleTypeFace,
				TextSize = TitleTextSize,
				TextAlign = SKTextAlign.Center
			};

			using SKPaint title2Paint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = Title2TypeFace,
				TextSize = Title2TextSize,
				TextAlign = SKTextAlign.Left
			};

			SKImageInfo surfaceInfo = new()
			{
				Width = 200,
				Height = 450,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			SKData outData;

			using (SKSurface surface = SKSurface.Create(surfaceInfo))
			{
				SKCanvas canvas = surface.Canvas;

				canvas.Clear(BackgroundColor);

				// Title
				canvas.DrawText("Storm Tiers", surfaceInfo.Width * 0.5f, TitleTextSize + 5.0f, titlePaint);

				// Tiers
				const int PadX = 50;
				const int PadY = 50;
				int currentY = PadY;
				for (int i = 0; i < tierIcons.Count; ++i)
				{
					canvas.DrawText($"Tier {i}", PadX, currentY + (Title2TextSize + TierIconSize) * 0.5f, title2Paint);

					if (tierIcons[i].Image != null)
					{
						canvas.DrawBitmap(tierIcons[i].Image, new SKRect(PadX + 60.0f, currentY, PadX + 60.0f + TierIconSize, currentY + TierIconSize));
					}

					currentY += 50;
				}

				surface.Flush();
				SKImage image = surface.Snapshot();
				outData = image.Encode(SKEncodedImageFormat.Png, 100);
			}

			string outPath = Path.Combine(config.OutputDirectory, Name, "Legend.png");
			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outStream);
			}
		}

		private void ExportForecasts(ProspectForecastTable forecasts, IReadOnlySet<string> activeForecasts, IReadOnlyList<WeatherTierIcon> tierIcons, IProviderManager providerManager, Config config, Logger logger)
		{	
			int tierCount = tierIcons.Count;

			using SKPaint linePaint = new SKPaint()
			{
				Color = LineColor,
				IsStroke = true,
				IsAntialias = false,
				Style = SKPaintStyle.Stroke
			};

			using SKPaint titlePaint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = TitleTypeFace,
				TextSize = TitleTextSize,
				TextAlign = SKTextAlign.Center
			};

			using SKPaint title2Paint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = Title2TypeFace,
				TextSize = Title2TextSize,
				TextAlign = SKTextAlign.Center
			};

			using SKPaint centerBodyPaint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = BodyTypeFace,
				TextSize = BodyTextSize,
				TextAlign = SKTextAlign.Center
			};

			using SKPaint rightBodyPaint = new SKPaint()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = BodyTypeFace,
				TextSize = BodyTextSize,
				TextAlign = SKTextAlign.Right
			};

			SKPaint[] paints = new SKPaint[tierCount];
			for (int i = 0; i < tierCount; ++i)
			{
				paints[i] = new SKPaint()
				{
					Color = tierIcons[i].BarColor.GetColor(0.5f),
					IsStroke = false,
					IsAntialias = false,
					Style = SKPaintStyle.Fill
				};
			}

			const int StartX = 50;
			const int StartY = 70;

			const int PadX = 50;
			const int PadY = 50;

			SKImageInfo surfaceInfo = new()
			{
				Height = StartY + TierIconSize + VerticalBarPad * 2 + PadY,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			foreach (ProspectForecast row in forecasts.Rows!.Where(r => activeForecasts.Contains(r.Name)))
			{
				surfaceInfo.Width = StartX + row.Pattern.Sum(p => p.DurationMinutes * BarWidthPerMinute + BarSpacing) + BarSpacing + PadX;

				SKData outData;

				using (SKSurface surface = SKSurface.Create(surfaceInfo))
				{
					SKCanvas canvas = surface.Canvas;

					canvas.Clear(BackgroundColor);

					// Title
					canvas.DrawText(row.Name, surfaceInfo.Width * 0.5f, TitleTextSize + 3.0f, titlePaint);
					canvas.DrawText($"Pool: {row.WeatherPool.RowName}", surfaceInfo.Width * 0.5f, Title2TextSize + Title2TextSize + 18.0f, title2Paint);

					// Duration header
					string durationLabel = "Duration in Minutes";
					if (row.Pattern.Length > 1)
					{
						durationLabel += $" - Total {row.Pattern.Sum(p => p.DurationMinutes)} Minutes";
					}
					canvas.DrawText(durationLabel, (StartX + surfaceInfo.Width - PadX) * 0.5f, surfaceInfo.Height - PadY + 25.0f + BodyTextSize, title2Paint);

					// Graph lines
					canvas.DrawRect(StartX, StartY - 1, surfaceInfo.Width - PadX - StartX, surfaceInfo.Height - PadY - StartY + 1, linePaint);

					// Graph bars, duration labels and tier icons
					const float BarHeight = TierIconSize + VerticalBarPad * 2;
					int currentX = StartX + BarSpacing;
					for (int i = 0; i < row.Pattern.Length; ++i)
					{
						int barWidth = row.Pattern[i].DurationMinutes * BarWidthPerMinute;

						// Bar
						canvas.DrawRect(currentX, StartY, barWidth, BarHeight, paints[row.Pattern[i].Tier]);

						// Duration
						canvas.DrawText(row.Pattern[i].DurationMinutes.ToString(), currentX + barWidth * 0.5f, surfaceInfo.Height - PadY + 20.0f, centerBodyPaint);

						//Tier
						SKBitmap? icon = tierIcons[row.Pattern[i].Tier].Image;
						if (icon != null)
						{
							float x = currentX + barWidth / 2 - TierIconSize / 2;
							float y = StartY + VerticalBarPad;
							canvas.DrawBitmap(icon, new SKRect(x, y, x + TierIconSize, y + TierIconSize));
						}

						currentX += barWidth + BarSpacing;
					}

					surface.Flush();
					SKImage image = surface.Snapshot();
					outData = image.Encode(SKEncodedImageFormat.Png, 100);
				}

				string outPath = Path.Combine(config.OutputDirectory, Name, "Forecasts", $"{row.Name}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}

			for (int i = 0; i < tierCount; ++i)
			{
				paints[i].Dispose();
			}
		}

		private void ExportStorms(IProviderManager providerManager, Config config, Logger logger)
		{
			IReadOnlyDictionary<string, WeatherActionData> actionMap = LoadWeatherActions(providerManager, logger);
			IReadOnlyList<WeatherEvent> events = LoadWeatherEvents(actionMap, providerManager, logger);

			using SKPaint linePaint = new()
			{
				Color = LineColor,
				IsStroke = true,
				IsAntialias = false,
				Style = SKPaintStyle.Stroke
			};

			using SKPaint titlePaint = new()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = TitleTypeFace,
				TextSize = TitleTextSize,
				TextAlign = SKTextAlign.Left
			};

			using SKPaint title2Paint = new()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = Title2TypeFace,
				TextSize = Title2TextSize,
				TextAlign = SKTextAlign.Left
			};

			using SKPaint title2RightPaint = title2Paint.Clone();
			title2RightPaint.TextAlign = SKTextAlign.Right;

			using SKPaint bodyPaint = new()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = BodyTypeFace,
				TextSize = BodyTextSize,
				TextAlign = SKTextAlign.Center
			};

			using SKPaint tierPaint = new()
			{
				Color = TextColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = TitleTypeFace,
				TextSize = TitleTextSize,
				TextAlign = SKTextAlign.Center
			};

			Dictionary<string, SKPaint> paints = new();
			foreach (WeatherActionData action in actionMap.Values)
			{
				paints.Add(action.Name!, new()
				{
					Color = action.Color.GetColor(2.0f),
					IsStroke = false,
					IsAntialias = false,
					Style = SKPaintStyle.Fill
				});
			}

			const int StartX = 50;
			const int StartY = 70;

			const int PadX = 50;
			const int PadY = 50;

			SKImageInfo surfaceInfo = new()
			{
				Height = StartY + TierIconSize + VerticalBarPad * 2 + PadY,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			foreach (WeatherEvent storm in events)
			{
				surfaceInfo.Width = StartX + storm.Actions.Sum(a => (int)Math.Round(a.TimeInSeconds * BarWidthPerSecond) + BarSpacing) + BarSpacing + PadX;

				SKData outData;

				using (SKSurface surface = SKSurface.Create(surfaceInfo))
				{
					SKCanvas canvas = surface.Canvas;

					canvas.Clear(BackgroundColor);

					// Title
					canvas.DrawText($"{storm.DisplayName} ({storm.Name})", StartX + BarSpacing, TitleTextSize + 26.0f, titlePaint);

					// Graph lines
					canvas.DrawRect(StartX, StartY - 1, surfaceInfo.Width - PadX - StartX, surfaceInfo.Height - PadY - StartY + 1, linePaint);

					// Graph bars, duration labels and tier labels
					const float BarHeight = TierIconSize + VerticalBarPad * 2;
					int currentX = StartX + BarSpacing;
					for (int i = 0; i < storm.Actions.Count; ++i)
					{
						int barWidth = (int)Math.Round(storm.Actions[i].TimeInSeconds * BarWidthPerSecond);

						// Bar
						canvas.DrawRect(currentX, StartY, barWidth, BarHeight, paints[storm.Actions[i].Name!]);

						// Duration
						canvas.DrawText($"{Math.Round(storm.Actions[i].TimeInSeconds)}s", currentX + barWidth * 0.5f, surfaceInfo.Height - PadY + 20.0f, bodyPaint);

						//Tier
						canvas.DrawText(storm.Actions[i].Action!.Tier.ToString("0.#"), currentX + barWidth / 2, StartY + VerticalBarPad + BarHeight / 2 + 4, tierPaint);

						currentX += barWidth + BarSpacing;
					}

					float footerPosY = surfaceInfo.Height - PadY + 26.0f + BodyTextSize;

					// Biomes
					string biomes = $"Biomes: {string.Join(", ", storm.Biomes)}";
					canvas.DrawText(biomes, StartX + BarSpacing, footerPosY, title2Paint);

					// Total duration
					string durationLabel = $"Total {Math.Round(storm.Actions.Sum(a => a.TimeInSeconds))} seconds";
					canvas.DrawText(durationLabel, currentX, footerPosY, title2RightPaint);

					surface.Flush();
					SKImage image = surface.Snapshot();
					outData = image.Encode(SKEncodedImageFormat.Png, 100);
				}

				string outPath = Path.Combine(config.OutputDirectory, Name, "Storms", $"{storm.Tier}_{storm.Name}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}

			foreach(SKPaint paint in paints.Values)
			{
				paint.Dispose();
			}
		}

		private IReadOnlyDictionary<string, WeatherActionData> LoadWeatherActions(IProviderManager providerManager, Logger logger)
		{
			Dictionary<string, WeatherActionData> weatherActions = new();

			GameFile file = providerManager.DataProvider.Files["Weather/D_WeatherActions.json"];

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				WeatherActionParseState state = WeatherActionParseState.SearchingForRows;
				int objectDepth = 0, colorDepth = 0;

				WeatherActionData action = new();
				FLinearColor color = new();

				while (state != WeatherActionParseState.Done && reader.Read())
				{
					switch (state)
					{
						case WeatherActionParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = WeatherActionParseState.InRows;
							break;
						case WeatherActionParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = WeatherActionParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = WeatherActionParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case WeatherActionParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									action.Name = reader.ReadAsString();
								}
								else if (reader.Value!.Equals("StormTier"))
								{
									action.Tier = (float)reader.ReadAsDouble()!.Value;
								}
								else if (reader.Value!.Equals("SpecifiedColor"))
								{
									reader.Read();
									colorDepth = reader.Depth;
									state = WeatherActionParseState.InColor;
								}
							}
							if (reader.Depth < objectDepth)
							{
								if (string.IsNullOrEmpty(action.Name))
								{
									logger.Log(LogLevel.Warning, "Unexpected end of object before finding storm action name");
									break;
								}

								weatherActions.Add(action.Name, action);
								action = new();

								state = WeatherActionParseState.InRows;
							}
							break;
						case WeatherActionParseState.InColor:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("R"))
								{
									color.R = (float)reader.ReadAsDouble()!.Value;
								}
								else if (reader.Value!.Equals("G"))
								{
									color.G = (float)reader.ReadAsDouble()!.Value;
								}
								else if (reader.Value!.Equals("B"))
								{
									color.B = (float)reader.ReadAsDouble()!.Value;
								}
								else if (reader.Value!.Equals("A"))
								{
									color.A = (float)reader.ReadAsDouble()!.Value;
								}
							}
							if (reader.Depth < colorDepth)
							{
								action.Color = new SlateColor() { SpecifiedColor = color };
								color = new();
								state = WeatherActionParseState.InObject;
							}
							break;
					}
				}
			}

			return weatherActions;
		}

		private enum WeatherActionParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InColor,
			Done
		}

		private IReadOnlyList<WeatherEvent> LoadWeatherEvents(IReadOnlyDictionary<string, WeatherActionData> actionMap, IProviderManager providerManager, Logger logger)
		{
			List<WeatherEvent> storms = new();

			IReadOnlyDictionary<string, IReadOnlyList<string>> poolMap = BuildPoolMap(providerManager);

			GameFile file = providerManager.DataProvider.Files["Weather/D_WeatherEvents.json"];

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				WeatherEventParseState state = WeatherEventParseState.SearchingForRows;
				int objectDepth = 0, biomesDepth = 0, actionsDepth = 0;

				WeatherEvent storm = new();
				WeatherAction action = new();

				while (state != WeatherEventParseState.Done && reader.Read())
				{
					switch (state)
					{
						case WeatherEventParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = WeatherEventParseState.InRows;
							break;
						case WeatherEventParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = WeatherEventParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = WeatherEventParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case WeatherEventParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									storm.Name = reader.ReadAsString();
								}
								else if (reader.Value!.Equals("Tier"))
								{
									storm.Tier = reader.ReadAsInt32()!.Value;
								}
								else if (reader.Value!.Equals("WeatherName"))
								{
									storm.DisplayName = LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, reader.ReadAsString()!);
								}
								else if (reader.Value!.Equals("BiomeGroups"))
								{
									reader.Read();
									biomesDepth = reader.Depth + 1;
									state = WeatherEventParseState.InBiomes;
								}
								else if (reader.Value!.Equals("WeatherActions"))
								{
									reader.Read();
									actionsDepth = reader.Depth + 1;
									state = WeatherEventParseState.InActions;
								}
							}
							if (reader.Depth < objectDepth)
							{
								if (poolMap.TryGetValue(storm.Name!, out IReadOnlyList<string>? stormPools))
								{
									storm.Pools.AddRange(stormPools);
								}

								storms.Add(storm);
								storm = new();

								state = WeatherEventParseState.InRows;
							}
							break;
						case WeatherEventParseState.InBiomes:
							if (reader.TokenType == JsonToken.StartObject)
							{
								state = WeatherEventParseState.InBiome;
							}
							if (reader.Depth < biomesDepth)
							{
								state = WeatherEventParseState.InObject;
							}
							break;
						case WeatherEventParseState.InBiome:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									string name = reader.ReadAsString()!;
									storm.Biomes.Add(name);
								}
							}
							if (reader.Depth < biomesDepth + 1)
							{
								state = WeatherEventParseState.InBiomes;
							}
							break;
						case WeatherEventParseState.InActions:
							if (reader.TokenType == JsonToken.StartObject)
							{
								state = WeatherEventParseState.InAction;
							}
							if (reader.Depth < actionsDepth)
							{
								state = WeatherEventParseState.InObject;
							}
							break;
						case WeatherEventParseState.InAction:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									action.Name = reader.ReadAsString();
									if (actionMap.TryGetValue(action.Name!, out WeatherActionData? actionData))
									{
										action.Action = actionData;
									}
									else
									{
										logger.Log(LogLevel.Warning, $"Could not find weather action {action.Name} referenced by weather event {storm.Name}");
									}
								}
								else if (reader.Value!.Equals("TimeInSeconds"))
								{
									action.TimeInSeconds = (float)reader.ReadAsDouble()!.Value;
								}
							}
							if (reader.Depth < actionsDepth + 1)
							{
								if (!action.Name!.Equals("GenericStormEnd"))
								{
									storm.Actions.Add(action);
								}
								action.Reset();

								state = WeatherEventParseState.InActions;
							}
							break;
					}
				}
			}

			return storms;
		}

		private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildPoolMap(IProviderManager providerManager)
		{
			GameFile weatherPoolFile = providerManager.DataProvider.Files["Weather/D_WeatherPools.json"];
			IcarusDataTable<FIcarusWeatherPoolData> weatherPoolTable = IcarusDataTable<FIcarusWeatherPoolData>.DeserializeTable("D_WeatherPools", Encoding.UTF8.GetString(weatherPoolFile.Read()));

			Dictionary<string, IReadOnlyList<string>> map = new();

			foreach (FIcarusWeatherPoolData pool in weatherPoolTable.Values)
			{
				foreach (FWeatherPoolEntry entry in pool.WeatherEvents)
				{
					List<string> stormPools;
					if (map.TryGetValue(entry.Event.RowName, out IReadOnlyList<string>? list))
					{
						stormPools = (List<string>)list;
					}
					else
					{
						stormPools = new();
						map.Add(entry.Event.RowName, stormPools);
					}

					stormPools.Add(pool.Name);
				}
			}

			return map;
		}

		private enum WeatherEventParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InBiomes,
			InBiome,
			InActions,
			InAction,
			Done
		}

		private static T? LoadJsonAsset<T>(string assetPath, IFileProvider provider)
		{
			GameFile file = provider.Files[assetPath];

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				JsonSerializer serializer = new();
				return serializer.Deserialize<T>(reader);
			}
		}

		#region Forecast Data
		private class WeatherTierIconTable
		{
			public WeatherTierIcon Defaults { get; set; }
			public WeatherTierIcon[]? Rows { get; set; }
		}

		private struct WeatherTierIcon
		{
			public string Name;
			public string TierIcon;
			public SlateColor BarColor;

			[JsonIgnore]
			public SKBitmap? Image;

			public WeatherTierIcon()
			{
				Name = "None";
				TierIcon = "None";
				BarColor = new SlateColor();
				Image = null;
			}
		}

		private class ProspectForecastTable
		{
			public ProspectForecast Defaults { get; set; }
			public ProspectForecast[]? Rows { get; set; }
		}

		private struct ProspectForecast
		{
			public string Name;
			public RowHandle WeatherPool;
			public ForecastPattern[] Pattern;

			public ProspectForecast()
			{
				Name = "None";
				WeatherPool = new RowHandle();
				Pattern = Array.Empty<ForecastPattern>();
			}
		}

		private struct ForecastPattern
		{
			public int Tier;
			public int DurationMinutes;

			public ForecastPattern()
			{
				Tier = 0;
				DurationMinutes = 0;
			}
		}
		#endregion

		#region Storm Data

		private struct WeatherEvent
		{
			public string? Name;
			public string? DisplayName; // WeatherName
			public int Tier;
			public List<string> Pools;
			public List<string> Biomes; //BiomeGroups
			public List<WeatherAction> Actions; // WeatherActions

			public WeatherEvent()
			{
				Name = null;
				DisplayName = null;
				Tier = 0;
				Pools = new();
				Biomes = new();
				Actions = new();
			}

			public override string? ToString()
			{
				return Name;
			}
		}

		private struct WeatherAction
		{
			public string? Name;
			public WeatherActionData? Action;
			public float TimeInSeconds;

			public void Reset()
			{
				Name = null;
				Action = null;
				TimeInSeconds = 0.0f;
			}

			public override string? ToString()
			{
				return Name;
			}
		}

		private class WeatherActionData
		{
			public string? Name { get; set; }

			public float Tier { get; set; }

			public SlateColor Color { get; set; }
		}

		#endregion

		private struct SlateColor
		{
			public FLinearColor SpecifiedColor;

			public SlateColor()
			{
				SpecifiedColor = new FLinearColor();
			}

			public SKColor GetColor(float opacity)
			{
				return ColorUtil.ToSKColor(SpecifiedColor, (byte)(SpecifiedColor.A * opacity * 255.0f));
			}
		}

		private struct RowHandle
		{
			public string RowName;
			public string DataTableName;

			public RowHandle()
			{
				RowName = "None";
				DataTableName = "None";
			}

			public override string ToString()
			{
				return RowName;
			}
		}

#pragma warning disable CS0649 // Field never assigned to

		private struct FIcarusWeatherPoolData : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public List<FWeatherPoolEntry> WeatherEvents;
			public List<FWeatherPoolEntryMeta> ContainedTiers;
		}

		private struct FWeatherPoolEntry
		{
			public FRowHandle Event;
			public float Weight;
		}

		private struct FWeatherPoolEntryMeta
		{
			public int Tier;
			public int NumEvents;
			public int MinEventDurationMinutes;
			public List<string> BiomesGroupsIncluded;
		}
	}

#pragma warning restore CS0649
}
