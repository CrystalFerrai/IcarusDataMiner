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

using SkiaSharp;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts map textures for all maps which have them
	/// </summary>
	/// <remarks>
	/// This miner takes a long time to run, so it is disabled by default. You must name it explicitly on the command line to run it.
	/// Specifically, the Terrain_017 output takes a very long time to encode (over a minute) as it is currently a 16k by 16k texture.
	/// </remarks>
	[DefaultEnabled(false)]
	internal class MapMiner : IDataMiner
	{
		private string? mOutDir;

		public string Name => "Maps";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			mOutDir = Path.Combine(config.OutputDirectory, Name);

			// Export maps
			foreach (WorldData worldData in providerManager.WorldDataUtil.Rows)
			{
				logger.Log(LogLevel.Information, $"Processing {worldData.Name}...");

				IReadOnlyList<Tile>? tiles = GetTiles(providerManager, worldData, logger);
				if (tiles == null) continue;

				ExportMap(worldData.Name!, tiles, config, logger);
			}

			// Export map grid overlay images in common sizes
			ExportMapGrids(config, logger);

			return true;
		}

		private static IReadOnlyList<Tile>? GetTiles(IProviderManager providerManager, WorldData worldData, Logger logger)
		{
			if (worldData.Name == null) return null;
			if (worldData.MinimapData == null) return null;

			IReadOnlyList<string> texturePaths = worldData.MinimapData.MapTextures;
			if (texturePaths.Count == 0) return null;

			List<Tile> tiles = new();

			int rows, cols;
			{
				float worldWidth = worldData.MinimapData.WorldBoundaryMax.X - worldData.MinimapData.WorldBoundaryMin.X;
				float worldHeight = worldData.MinimapData.WorldBoundaryMax.Y - worldData.MinimapData.WorldBoundaryMin.Y;

				cols = (int)(worldWidth / WorldDataUtil.WorldTileSize);
				rows = (int)(worldHeight / WorldDataUtil.WorldTileSize);
			}

			if (cols * rows > texturePaths.Count)
			{
				logger.Log(LogLevel.Warning, $"Map '{worldData.Name}' does not appear to contain enough map tiles. Expected: {cols * rows}, Found: {texturePaths.Count}");
				return null;
			}

			for (int x = 0; x < rows; ++x)
			{
				for (int y = 0; y < cols; ++y)
				{
					string rawPath = texturePaths[y + x * rows];
					SKBitmap? bitmap = AssetUtil.LoadAndDecodeTexture(worldData.Name, rawPath, providerManager.AssetProvider, logger);
					if (bitmap == null) continue;

					tiles.Add(new Tile() { X = x, Y = y, Bitmap = bitmap });
				}
			}

			return tiles;
		}

		private void ExportMap(string name, IReadOnlyList<Tile> tiles, Config config, Logger logger)
		{
			if (tiles.Count == 0)
			{
				logger.Log(LogLevel.Information, $"No map textures located for {name}");
				return;
			}

			int maxX = 0, maxY = 0;
			foreach (Tile tile in tiles)
			{
				if (tile.X > maxX) maxX = tile.X;
				if (tile.Y > maxY) maxY = tile.Y;
			}

			SKBitmap firstTile = tiles[0].Bitmap;
			int tileWidth = firstTile.Width;
			int tileHeight = firstTile.Height;

			int totalWidth = (maxX + 1) * tileWidth;
			int totalHeight = (maxY + 1) * tileHeight;

			SKImageInfo surfaceInfo = new()
			{
				Width = totalWidth,
				Height = totalHeight,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			SKData outData;

			logger.Log(LogLevel.Debug, "Creating output texture...");
			using (SKSurface surface = SKSurface.Create(surfaceInfo))
			{
				SKCanvas canvas = surface.Canvas;

				canvas.RotateDegrees(90.0f, totalWidth * 0.5f, totalHeight * 0.5f);

				foreach (Tile tile in tiles)
				{
					SKPoint position = new(totalWidth - (tile.Y + 1) * tileWidth, totalHeight - (tile.X + 1) * tileHeight);
					canvas.DrawBitmap(tile.Bitmap, position);
				}

				surface.Flush();
				SKImage image = surface.Snapshot();
				outData = image.Encode(SKEncodedImageFormat.Png, 100);
			}

			logger.Log(LogLevel.Debug, "Saving output texture...");
			string outPath = Path.Combine(mOutDir!, $"{name}.png");
			using (FileStream outFile = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outFile);
			}
		}

		private void ExportMapGrids(Config config, Logger logger)
		{
			logger.Log(LogLevel.Information, "Gnerating map grid overlay images...");

			IntPoint[] mapSizes = new[]
			{
				new IntPoint(2048, 2048),
				new IntPoint(4096, 4096),
				new IntPoint(8192, 8192)
			};

			IntPoint mapCellCount = new(16, 16);
			foreach (IntPoint size in mapSizes)
			{
				SKData outData = CreateMapGrid(size, mapCellCount);

				string outPath = Path.Combine(mOutDir!, $"MapGrid_{size.X}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}

			IntPoint[] outpostMapSizes = new[]
			{
				new IntPoint(1024, 1024),
				new IntPoint(2048, 2048)
			};

			IntPoint outpostMapCellCount = new(4, 4);
			foreach (IntPoint size in outpostMapSizes)
			{
				SKData outData = CreateMapGrid(size, outpostMapCellCount);

				string outPath = Path.Combine(mOutDir!, $"MapGrid_Outpost_{size.X}.png");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				{
					outData.SaveTo(outStream);
				}
			}
		}

		private static SKData CreateMapGrid(IntPoint mapSize, IntPoint cellCount)
		{
			SKPoint mapScale = new(mapSize.X / (cellCount.X * 256.0f), mapSize.Y / (cellCount.Y * 256.0f));

			SKImageInfo surfaceInfo = new()
			{
				Width = mapSize.X,
				Height = mapSize.Y,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			SKTypeface typeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

			using SKPaint linePaint = new()
			{
				Color = new SKColor(255, 255, 255, 128),
				IsAntialias = false,
				Style = SKPaintStyle.Stroke,
				IsStroke = true,
				BlendMode = SKBlendMode.Src
			};

			using SKPaint textPaint = new()
			{
				Color = SKColors.White,
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Typeface = typeFace,
				TextSize = Math.Max(18.0f * mapScale.X, 10.0f),
				TextAlign = SKTextAlign.Left
			};

			SKData outData;
			using (SKSurface surface = SKSurface.Create(surfaceInfo))
			{
				SKCanvas canvas = surface.Canvas;

				SKPoint gridCellSize = new(surfaceInfo.Width / (float)cellCount.X, surfaceInfo.Height / (float)cellCount.Y);

				for (int y = 1; y < cellCount.Y; ++y)
				{
					canvas.DrawLine(0.0f, y * gridCellSize.Y, surfaceInfo.Width, y * gridCellSize.Y, linePaint);
				}
				for (int x = 1; x < cellCount.X; ++x)
				{
					canvas.DrawLine(x * gridCellSize.X, 0.0f, x * gridCellSize.X, surfaceInfo.Height, linePaint);
				}

				for (int y = 0; y < cellCount.Y; ++y)
				{
					for (int x = 0; x < cellCount.X; ++x)
					{
						string label = $"{(char)('A' + x)}{y + 1}";
						canvas.DrawText(label, (float)Math.Floor(x * gridCellSize.X + 5.0f + 2.0f * (mapScale.X * 1.5f)) + 0.5f, y * gridCellSize.Y + 4.0f - textPaint.FontMetrics.Ascent, textPaint);
					}
				}

				surface.Flush();
				SKImage image = surface.Snapshot();
				outData = image.Encode(SKEncodedImageFormat.Png, 100);
			}

			return outData;
		}

		private struct Tile
		{
			public int X;
			public int Y;
			public SKBitmap Bitmap;
		}

		private readonly struct IntPoint : IEquatable<IntPoint>
		{
			public readonly int X;
			public readonly int Y;

			public static readonly IntPoint Zero;

			static IntPoint()
			{
				Zero = new IntPoint(0, 0);
			}

			public IntPoint(int x, int y)
			{
				X = x;
				Y = y;
			}

			public override readonly int GetHashCode()
			{
				return HashCode.Combine(X, Y);
			}

			public readonly bool Equals(IntPoint other)
			{
				return X.Equals(other.X) && Y.Equals(other.Y);
			}

			public override readonly bool Equals([NotNullWhen(true)] object? obj)
			{
				return obj is IntPoint other && Equals(other);
			}
		}
	}
}
