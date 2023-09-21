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

using CUE4Parse.UE4.Objects.Core.Math;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Base class for miners which extract spawn related data
	/// </summary>
	internal class SpawnMinerBase : IDisposable
	{
		protected static readonly SKColor BackgroundColor = new(0xff101010);
		protected static readonly SKColor BannerBaseColor = new(0xff303030);
		protected static readonly SKColor ForegroundColor = new(0xfff0f0f0);

		protected static readonly SKColor ForegroundColorNegative = new((byte)(255 - ForegroundColor.Red), (byte)(255 - ForegroundColor.Green), (byte)(255 - ForegroundColor.Blue), ForegroundColor.Alpha);

		protected static readonly SKTypeface TitleTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		protected const float TitleTextSize = 18.0f;

		protected static readonly SKTypeface BodyTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		protected static readonly SKTypeface BodyBoldTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		protected const float BodyTextSize = 14.0f;

		protected readonly SKPaint mTitlePaint;
		protected readonly SKPaint mTitlePaintNegative;
		protected readonly SKPaint mBodyPaint;
		protected readonly SKPaint mBodyBoldPaint;
		protected readonly SKPaint mBodyBoldPaintNegative;
		protected readonly SKPaint mBodyBoldCenterPaint;
		protected readonly SKPaint mBodyBoldCenterPaintNegative;
		protected readonly SKPaint mNameCirclePaint;
		protected readonly SKPaint mNameCirclePaintNegative;
		protected readonly SKPaint mLinePaint;
		protected readonly SKPaint mLinePaintNegative;
		protected readonly SKPaint mFillPaint;

		protected SpawnMinerBase()
		{
			mTitlePaint = new()
			{
				Color = ForegroundColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = TitleTypeFace,
				TextSize = TitleTextSize,
				TextAlign = SKTextAlign.Left
			};

			mTitlePaintNegative = mTitlePaint.Clone();
			mTitlePaintNegative.Color = ForegroundColorNegative;

			mBodyPaint = new()
			{
				Color = ForegroundColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = BodyTypeFace,
				TextSize = BodyTextSize,
				TextAlign = SKTextAlign.Left
			};

			mBodyBoldPaint = new()
			{
				Color = ForegroundColor,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = BodyBoldTypeFace,
				TextSize = BodyTextSize,
				TextAlign = SKTextAlign.Left
			};

			mBodyBoldPaintNegative = mBodyBoldPaint.Clone();
			mBodyBoldPaintNegative.Color = ForegroundColorNegative;

			mBodyBoldCenterPaint = mBodyBoldPaint.Clone();
			mBodyBoldCenterPaint.TextAlign = SKTextAlign.Center;

			mBodyBoldCenterPaintNegative = mBodyBoldPaintNegative.Clone();
			mBodyBoldCenterPaintNegative.TextAlign = SKTextAlign.Center;

			mNameCirclePaint = new()
			{
				Color = ForegroundColor,
				IsStroke = true,
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 1.5f
			};

			mNameCirclePaintNegative = mNameCirclePaint.Clone();
			mNameCirclePaintNegative.Color = ForegroundColorNegative;

			mLinePaint = new()
			{
				Color = ForegroundColor,
				IsStroke = true,
				IsAntialias = false,
				Style = SKPaintStyle.Stroke
			};

			mLinePaintNegative = mLinePaint.Clone();
			mLinePaintNegative.Color = ForegroundColorNegative;

			mFillPaint = new()
			{
				IsStroke = false,
				IsAntialias = false,
				Style = SKPaintStyle.Fill
			};
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~SpawnMinerBase()
		{
			Dispose(false);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				mTitlePaint.Dispose();
				mTitlePaintNegative.Dispose();
				mBodyPaint.Dispose();
				mBodyBoldPaint.Dispose();
				mBodyBoldPaintNegative.Dispose();
				mBodyBoldCenterPaint.Dispose();
				mBodyBoldCenterPaintNegative.Dispose();
				mNameCirclePaint.Dispose();
				mNameCirclePaintNegative.Dispose();
				mLinePaint.Dispose();
				mLinePaintNegative.Dispose();
				mFillPaint.Dispose();
			}
		}

		/// <summary>
		/// Called from <see cref="CreateSpawnZoneInfoBox"/> to gather subtitle lines
		/// </summary>
		protected virtual IEnumerable<string> GetInfoBoxSubtitleLines(ISpawnZoneData spawnZone, object? userData)
		{
			return Enumerable.Empty<string>();
		}

		/// <summary>
		/// Called from <see cref="CreateSpawnZoneInfoBox"/> to gather footer lines
		/// </summary>
		protected virtual IEnumerable<string> GetInfoBoxFooterLines(ISpawnZoneData spawnZone, object? userData)
		{
			return Enumerable.Empty<string>();
		}

		/// <summary>
		/// Outputs a spawn map texture as well as a full alpha version of the same texture
		/// </summary>
		protected void OutputSpawnMaps(string outDir, string name, SKBitmap spawnMap, Logger logger)
		{
			SKData outData = spawnMap.Encode(SKEncodedImageFormat.Png, 100);

			string outPath = Path.Combine(outDir, $"{name}.png");
			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outStream);
			}

			// Another copy with all pixels set to full alpha for easier image composition
			SKColor[] pixels = spawnMap.Pixels.ToArray();
			for (int i = 0; i < pixels.Length; ++i)
			{
				pixels[i] = new SKColor((uint)pixels[i] | 0xff000000u);
			}

			SKBitmap opaque = new(spawnMap.Info)
			{
				Pixels = pixels
			};
			outData = opaque.Encode(SKEncodedImageFormat.Png, 100);

			outPath = Path.Combine(outDir, $"{name}_Opaque.png");
			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outStream);
			}
		}

		/// <summary>
		/// Creates an image of spawn information box
		/// </summary>
		/// <remarks>
		/// Will call <see cref="GetInfoBoxSubtitleLines"/> and <see cref="GetInfoBoxFooterLines"/> to gather optional data
		/// for the info box. Override them to customise. <paramref name="userData"/> will be passed through to those calls.
		/// </remarks>
		protected SKData CreateSpawnZoneInfoBox(ISpawnZoneData spawnZone, object? userData = null)
		{
			string titleText = spawnZone.Name[(spawnZone.Name.IndexOf("_") + 1)..];

			string[] subtitleLines = GetInfoBoxSubtitleLines(spawnZone, userData).ToArray();
			List<string> bodyLines = new();
			string[] footerLines = GetInfoBoxFooterLines(spawnZone, userData).ToArray();

			float weightSum = spawnZone.Spawns.Sum(c => c.Weight);
			foreach (WeightedItem spawn in spawnZone.Spawns)
			{
				bodyLines.Add($"{spawn.Name}: {spawn.Weight / weightSum * 100.0f:0.}%");
			}

			float circleRadius = mBodyBoldPaint.FontSpacing * 0.5f;

			float titleWidth = mTitlePaint.MeasureText(titleText);
			if (spawnZone.Id is not null)
			{
				titleWidth += circleRadius * 2.0f + 4.0f;
			}

			float textWidth = titleWidth;
			foreach (string text in subtitleLines.Concat(bodyLines).Concat(footerLines))
			{
				textWidth = Math.Max(textWidth, mBodyPaint.MeasureText(text));
			}

			float textHeight = mTitlePaint.FontSpacing + (subtitleLines.Length + bodyLines.Count + footerLines.Length) * mBodyPaint.FontSpacing;

			bool hasBothBodyTexts = bodyLines.Count > 0 && footerLines.Length > 0;

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

				float posY = 20.0f + mTitlePaint.FontSpacing + subtitleLines.Length * mBodyBoldPaint.FontSpacing;

				SKColor bannerBGColor = BannerBaseColor;
				if (spawnZone.Id is null)
				{
					bannerBGColor = ColorUtil.ToSKColor(spawnZone.Color, 255);
				}
				bool useNegative = !IsColorCloser(mTitlePaint.Color, bannerBGColor, mTitlePaintNegative.Color);

				// Banner
				mFillPaint.Color = bannerBGColor;
				canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width, posY, mFillPaint);

				// Outline
				canvas.DrawRect(0.0f, 0.0f, surfaceInfo.Width - 1.0f, surfaceInfo.Height - 1.0f, mLinePaint);

				// Title
				float textPosX = 10.0f;
				float textPosY = 10.0f - mTitlePaint.FontMetrics.Ascent;

				if (spawnZone.Id is not null)
				{
					float textYOffset = Math.Abs((mTitlePaint.FontMetrics.Ascent - mBodyBoldCenterPaint.FontMetrics.Ascent) * 0.5f);
					SKPoint circleCenter = new(textPosX + circleRadius, textPosY + (mBodyBoldCenterPaint.FontMetrics.Ascent * 0.33f - textYOffset));
					canvas.DrawCircle(circleCenter, circleRadius, useNegative ? mNameCirclePaintNegative : mNameCirclePaint);
					canvas.DrawText(spawnZone.Id, textPosX + circleRadius, textPosY - textYOffset, useNegative ? mBodyBoldCenterPaintNegative : mBodyBoldCenterPaint);

					canvas.DrawText(titleText, textPosX + circleRadius * 2.0f + 4.0f, textPosY, useNegative ? mTitlePaintNegative : mTitlePaint);
				}
				else
				{
					canvas.DrawText(titleText, textPosX, textPosY, useNegative ? mTitlePaintNegative : mTitlePaint);
				}
				textPosY += mTitlePaint.FontSpacing;

				// Subtitles
				foreach (string subtitle in subtitleLines)
				{
					canvas.DrawText(subtitle, textPosX, textPosY, useNegative ? mBodyBoldPaintNegative : mBodyBoldPaint);
					textPosY += mBodyPaint.FontSpacing;
				}

				// Divider
				textPosY += 10.0f;
				canvas.DrawLine(1.0f, posY, surfaceInfo.Width - 1.0f, posY, mLinePaint);
				posY += 5.0f;

				// Body 1
				foreach (string text in bodyLines)
				{
					canvas.DrawText(text, textPosX, textPosY, mBodyPaint);
					posY += mBodyPaint.FontSpacing;
					textPosY += mBodyPaint.FontSpacing;
				}

				// Divider
				if (hasBothBodyTexts)
				{
					posY += 5.0f;
					canvas.DrawLine(1.0f, posY, surfaceInfo.Width - 1.0f, posY, mLinePaint);
					posY += 5.0f;

					textPosY += 10.0f;
				}

				// Body 2
				foreach (string text in footerLines)
				{
					canvas.DrawText(text, textPosX, textPosY, mBodyPaint);
					posY += mBodyPaint.FontSpacing;
					textPosY += mBodyPaint.FontSpacing;
				}

				surface.Flush();
				SKImage image = surface.Snapshot();
				return image.Encode(SKEncodedImageFormat.Png, 100);
			}
		}

		// Is test closer to target than source is to target based on perceived luminance?
		protected static bool IsColorCloser(SKColor source, SKColor target, SKColor test)
		{
			const float rl = 0.2126f, gl = 0.7152f, bl = 0.0722f;

			float sourceLum = source.Red * rl + source.Green * gl + source.Blue * bl;
			float targetLum = target.Red * rl + target.Green * gl + target.Blue * bl;
			float testLum = test.Red * rl + test.Green * gl + test.Blue * bl;

			return Math.Abs(testLum - targetLum) < Math.Abs(sourceLum - targetLum);
		}

		protected interface ISpawnZoneData
		{
			string? Id { get; }

			string Name { get; }

			FColor Color { get; }

			IReadOnlyList<WeightedItem> Spawns { get; }
		}

		protected class SpawnConfig
		{
			public string Name { get; }

			public IReadOnlyList<SpawnZone> SpawnZones { get; }

			public string? SpawnMapName { get; }

			public SKBitmap? SpawnMap { get; }

			public SpawnConfig(string name, string? spawnMapName, SKBitmap? spawnMap, IEnumerable<SpawnZone> spawnZones)
			{
				Name = name;
				SpawnMapName = spawnMapName;
				SpawnMap = spawnMap;
				SpawnZones = new List<SpawnZone>(spawnZones);
			}

			public override string ToString()
			{
				return $"{Name}: {SpawnZones.Count} zones";
			}
		}

		protected class SpawnZone : ISpawnZoneData
		{
			public string? Id { get; protected set; }

			public string Name { get; protected set; }

			public FColor Color { get; }

			public IReadOnlyList<WeightedItem> Spawns { get; }

			public SpawnZone(string name, FColor color, IEnumerable<WeightedItem> spawns)
			{
				Name = name;
				Color = color;
				Spawns = new List<WeightedItem>(spawns);
			}

			public override string ToString()
			{
				return $"{Name} [{Color}]: {Spawns.Count} spawns";
			}
		}

		protected class WeightedItem : IEquatable<WeightedItem>, IComparable<WeightedItem>
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

		protected enum FishType
		{
			None,
			Saltwater,
			Freshwater,
			Mixed
		}
	}
}
