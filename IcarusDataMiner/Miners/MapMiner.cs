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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

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
		private const int TileSize = 100800;

		public string Name => "Maps";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData worldData in providerManager.WorldDataUtil.Rows)
			{
				logger.Log(LogLevel.Information, $"Processing {worldData.Name}...");

				IReadOnlyList<Tile>? tiles = GetTiles(providerManager, worldData, logger);
				if (tiles == null) continue;

				ExportMap(worldData.Name!, tiles, config, logger);
			}

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

				cols = (int)(worldWidth / TileSize);
				rows = (int)(worldHeight / TileSize);
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
					SKBitmap? bitmap = DecodeTexture(worldData.Name, rawPath, providerManager.AssetProvider, logger);
					if (bitmap == null) continue;

					tiles.Add(new Tile() { X = x, Y = y, Bitmap = bitmap });
				}
			}

			return tiles;
		}

		private static void ExportMap(string name, IReadOnlyList<Tile> tiles, Config config, Logger logger)
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
			string outPath = Path.Combine(config.OutputDirectory, $"{name}.png");
			using (FileStream outFile = File.Create(outPath))
			{
				outData.SaveTo(outFile);
			}
		}

		private static SKBitmap? DecodeTexture(string name, string assetPath, IFileProvider provider, Logger logger)
		{
			string path = $"{assetPath[..assetPath.LastIndexOf('.')]}.uasset";
			if (path.StartsWith("/Game/")) path = $"Icarus/Content/{path["/Game/".Length..]}";

			GameFile? assetFile;
			if (!provider.Files.TryGetValue(path, out assetFile))
			{
				logger.Log(LogLevel.Warning, $"Could not find map tile '{path}' for map '{name}'");
				return null;
			}

			string fileName = Path.GetFileNameWithoutExtension(path);
			logger.Log(LogLevel.Debug, $"Reading {fileName}");

			Package assetPackage = (Package)provider.LoadPackage(assetFile);
			foreach (FObjectExport export in assetPackage.ExportMap)
			{
				UObject obj = export.ExportObject.Value;
				UTexture2D? texture = obj as UTexture2D;
				if (texture == null) continue;

				SKBitmap? bitmap = texture.Decode();
				if (bitmap == null)
				{
					logger.Log(LogLevel.Warning, $"Failed to decode texture '{fileName}'");
					continue;
				}

				// Assuming only one texture per asset
				return bitmap;
			}

			logger.Log(LogLevel.Warning, $"'{path}' does not appear to be a valid texture asset");
			return null;
		}

		private struct Tile
		{
			public int X;
			public int Y;
			public SKBitmap Bitmap;
		}
	}
}
