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
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace IcarusDataMiner
{
	/// <summary>
	/// Helper functions for working with game assets
	/// </summary>
	internal static class AssetUtil
	{
		/// <summary>
		/// Converts a game object path into a package path
		/// </summary>
		public static string GetPackageName(string objectName, string extension)
		{
			int dotIndex = objectName.LastIndexOf('.');
			string packageName = dotIndex >= 0 ? objectName[..dotIndex] : objectName;
			if (packageName.StartsWith("/Game/"))
			{
				packageName = $"Icarus/Content{packageName[5..]}.{extension}";
			}
			else
			{
				packageName = $"{packageName}.{extension}";
			}
			return packageName;
		}

		/// <summary>
		/// Loads a texture asset
		/// </summary>
		/// <param name="assetPath">The asset path</param>
		/// <param name="provider">The asset provider</param>
		/// <returns>The loaded asset or null if loading failed</returns>
		public static UTexture2D? LoadTexture(string assetPath, IFileProvider provider)
		{
			string path = GetPackageName(assetPath, "uasset");

			GameFile? assetFile;
			if (!provider.Files.TryGetValue(path, out assetFile))
			{
				return null;
			}

			Package assetPackage = (Package)provider.LoadPackage(assetFile);
			foreach (FObjectExport export in assetPackage.ExportMap)
			{
				UObject obj = export.ExportObject.Value;
				UTexture2D? texture = obj as UTexture2D;
				if (texture == null)
				{
					UGameplayTexture? gt = obj as UGameplayTexture;
					if (gt == null) continue;

					texture = gt.SourceTexture?.ResolvedObject?.Object?.Value as UTexture2D;
					if (texture == null) continue;
				}

				// Assuming only one texture per asset
				return texture;
			}

			return null;
		}

		/// <summary>
		/// Loads and decodes a texture asset
		/// </summary>
		/// <param name="displayName">The name to display in log messages when a problem occurs</param>
		/// <param name="assetPath">The asset path</param>
		/// <param name="provider">The asset provider</param>
		/// <param name="logger">For logging messages about issues encountered</param>
		/// <returns>The decoded texture, or null if there waas a problem</returns>
		public static SKBitmap? LoadAndDecodeTexture(string displayName, string assetPath, IFileProvider provider, Logger logger)
		{
			UTexture2D? texture = LoadTexture(assetPath, provider);
			if (texture == null)
			{
				logger.Log(LogLevel.Warning, $"Failed to load texture asset for '{displayName}'");
				return null;
			}

			SKBitmap? bitmap = texture.Decode();
			if (bitmap == null)
			{
				logger.Log(LogLevel.Warning, $"Failed to decode texture '{displayName}'");
			}
			else if (!texture.SRGB)
			{
				SKColor[] pixels = bitmap.Pixels;
				for (int i = 0; i < pixels.Length; ++i)
				{
					pixels[i] = ColorUtil.LinearToSrgb(pixels[i]);
				}
				bitmap.Pixels = pixels;
			}

			return bitmap;
		}

		/// <summary>
		/// Decode and export a texture asset to a file
		/// </summary>
		/// <param name="assetPath">The asset path</param>
		/// <param name="provider">The asset provider</param>
		/// <param name="outDir">The output directory for the exported file</param>
		/// <param name="logger">For logging messages about issues encountered</param>
		/// <param name="outName">Name of output file without extension. In not supplied, will use name of asset.</param>
		/// <returns>The path to the output file</returns>
		public static string ExportTexture(string assetPath, IFileProvider provider, string outDir, Logger logger, string? outName = null)
		{
			string displayName = Path.GetFileNameWithoutExtension(assetPath);
			SKBitmap? texture = LoadAndDecodeTexture(displayName, assetPath, provider, logger);
			if (texture is null)
			{
				logger.Log(LogLevel.Error, $"Error loading texture '{assetPath}'");
				return string.Empty;
			}
			SKData outData = texture.Encode(SKEncodedImageFormat.Png, 100);

			string outPath = Path.Combine(outDir, $"{outName ?? Path.GetFileNameWithoutExtension(assetPath)}.png");
			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outStream);
			}

			return outPath;
		}

		/// <summary>
		/// Decode and export a texture asset to a file
		/// </summary>
		/// <param name="assetPath">The asset path</param>
		/// <param name="provider">The asset provider</param>
		/// <param name="outDir">The output directory for the exported file</param>
		/// <param name="logger">For logging messages about issues encountered</param>
		/// <param name="tint">Multiply all pixels by this color value</param>
		/// <param name="outName">Name of output file without extension. In not supplied, will use name of asset.</param>
		/// <returns>The path to the output file</returns>
		public static string ExportTexture(string assetPath, IFileProvider provider, string outDir, Logger logger, FLinearColor tint, string? outName = null)
		{
			return ExportTexture(assetPath, provider, outDir, logger, ColorUtil.ToSKColor(tint), outName);
		}

		/// <summary>
		/// Decode and export a texture asset to a file
		/// </summary>
		/// <param name="assetPath">The asset path</param>
		/// <param name="provider">The asset provider</param>
		/// <param name="outDir">The output directory for the exported file</param>
		/// <param name="logger">For logging messages about issues encountered</param>
		/// <param name="tint">Multiply all pixels by this color value</param>
		/// <param name="outName">Name of output file without extension. In not supplied, will use name of asset.</param>
		/// <returns>The path to the output file</returns>
		public static string ExportTexture(string assetPath, IFileProvider provider, string outDir, Logger logger, SKColor tint, string? outName = null)
		{
			string displayName = Path.GetFileNameWithoutExtension(assetPath);
			SKBitmap? texture = LoadAndDecodeTexture(displayName, assetPath, provider, logger);
			if (texture is null)
			{
				logger.Log(LogLevel.Error, $"Error loading texture '{assetPath}'");
				return string.Empty;
			}

			int imageWidth = texture.Width;
			int imageHeight = texture.Height;

			SKImageInfo surfaceInfo = new()
			{
				Width = imageWidth,
				Height = imageHeight,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			SKData outData;
			using (SKSurface surface = SKSurface.Create(surfaceInfo))
			{
				SKCanvas canvas = surface.Canvas;
				using SKPaint paint = new()
				{
					ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn)
				};

				canvas.DrawBitmap(texture, SKPoint.Empty, paint);

				surface.Flush();
				SKImage image = surface.Snapshot();
				outData = image.Encode(SKEncodedImageFormat.Png, 100);
			}

			string outPath = Path.Combine(outDir, $"{outName ?? Path.GetFileNameWithoutExtension(assetPath)}.png");
			using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outStream);
			}

			return outPath;
		}
	}
}
