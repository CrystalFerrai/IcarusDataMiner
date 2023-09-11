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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using SkiaSharp;

namespace IcarusDataMiner
{
	/// <summary>
	/// Utility for generating overlay images for game maps
	/// </summary>
	internal class MapOverlayBuilder
	{
		// Used to adjust scaling for map images that do not use the standard world to map ratio.
		private const float MapScaleFactor = (float)WorldDataUtil.MapToWorld * 0.5f;

		private static readonly SKTypeface sTextTypeFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
		private const float sTextSize = 18.0f;

		private readonly List<LocationCollection> mLocationCollections;

		private readonly float mOriginOffsetX, mOriginOffsetY;
		private readonly int mMapWidth, mMapHeight;
		private readonly float mWorldToMapX, mWorldToMapY;

		private MapOverlayBuilder(float originOffsetX, float originOffsetY, int mapWidth, int mapHeight, float worldToMapX, float worldToMapY)
		{
			mLocationCollections = new();
			mOriginOffsetX = originOffsetX;
			mOriginOffsetY = originOffsetY;
			mMapWidth = mapWidth;
			mMapHeight = mapHeight;
			mWorldToMapX = worldToMapX;
			mWorldToMapY = worldToMapY;
		}

		/// <summary>
		/// Create a new instance using the given world data to determine the size of the overlay
		/// </summary>
		/// <param name="worldData">The world data for the map the overlay will be used with</param>
		/// <param name="assetFileProvider">Provider for game assets</param>
		public static MapOverlayBuilder Create(WorldData worldData, IFileProvider assetFileProvider)
		{
			float offsetX = worldData.MinimapData!.WorldBoundaryMin.X;
			float offsetY = worldData.MinimapData!.WorldBoundaryMin.Y;

			float mapWidth = worldData.MinimapData.WorldBoundaryMax.X - offsetX;
			float mapHeight = worldData.MinimapData.WorldBoundaryMax.Y - offsetY;

			float scaleX = (float)(1.0f / WorldDataUtil.MapToWorld);
			float scaleY = (float)(1.0f / WorldDataUtil.MapToWorld);

			UTexture2D? firstTileTexture = AssetUtil.LoadTexture(worldData.MinimapData!.MapTextures[0], assetFileProvider);
			if (firstTileTexture != null)
			{
				scaleX = (float)firstTileTexture.SizeX / (float)WorldDataUtil.WorldTileSize;
				scaleY = (float)firstTileTexture.SizeY / (float)WorldDataUtil.WorldTileSize;
			}

			int imageWidth = (int)Math.Ceiling(mapWidth * scaleX);
			int imageHeight = (int)Math.Ceiling(mapHeight * scaleY);

			return new MapOverlayBuilder(offsetX, offsetY, imageWidth, imageHeight, scaleX, scaleY);
		}

		/// <summary>
		/// Add "White Shape" locations to the overlay
		/// </summary>
		/// <param name="locations">Locations to add</param>
		/// <param name="shape">The shape to use for location markers</param>
		public void AddLocations(IEnumerable<MapLocation> locations, MarkerShape shape = MarkerShape.Circle)
		{
			mLocationCollections.Add(new LocationCollection(locations, SKColors.White, null, shape));
		}

		/// <summary>
		/// Add "Colored Shape" locations to the overlay
		/// </summary>
		/// <param name="locations">Locations to add</param>
		/// <param name="color">The color to use for the locations</param>
		/// <param name="shape">The shape to use for location markers</param>
		public void AddLocations(IEnumerable<MapLocation> locations, SKColor color, MarkerShape shape = MarkerShape.Circle)
		{
			mLocationCollections.Add(new LocationCollection(locations, color, null, shape));
		}

		/// <summary>
		/// Add "Icon" locations to the overlay
		/// </summary>
		/// <param name="locations">Locations to add</param>
		/// <param name="icon">The icon to use for the locations</param>
		/// <remarks>
		/// Icons should typically be size 64x64 for standard markers and 32x32 for small markers.
		/// Icons will be scaled down to half size for the standard world to map ratio. This scaling
		/// changes as the world to map ratio changes to keep icon sizes relative to world scale.
		/// </remarks>
		public void AddLocations(IEnumerable<MapLocation> locations, SKImage icon)
		{
			mLocationCollections.Add(new LocationCollection(locations, SKColors.White, icon, MarkerShape.None));
		}

		/// <summary>
		/// Clears all added locations. Typically called after DrawOverlay if you want to re-use the
		/// same instance to draw a new overlay for the same map.
		/// </summary>
		/// <param name="disposeIcons">Whether to dispose icons associated with locations</param>
		public void ClearLocations(bool disposeIcons = false)
		{
			if (disposeIcons)
			{
				foreach (LocationCollection collection in mLocationCollections)
				{
					collection.Icon?.Dispose();
				}
			}

			mLocationCollections.Clear();
		}

		/// <summary>
		/// Creates an overlay texture with the current locations drawn to it
		/// </summary>
		/// <param name="format">The output format of the texture</param>
		/// <param name="quality">Qualty setting to pass to the image encoder, typically 1-100. Meaning varies based on encoder.</param>
		/// <returns>The encoded texture data, ready to be saved to a file</returns>
		public SKData DrawOverlay(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
		{
			SKImageInfo surfaceInfo = new()
			{
				Width = mMapWidth,
				Height = mMapHeight,
				ColorSpace = SKColorSpace.CreateSrgb(),
				ColorType = SKColorType.Rgba8888,
				AlphaType = SKAlphaType.Premul
			};

			using (SKSurface surface = SKSurface.Create(surfaceInfo))
			{
				SKCanvas canvas = surface.Canvas;

				using (SKPaint anchorPaint = new()
				{
					Color = SKColors.White,
					IsStroke = false,
					IsAntialias = false,
					Style = SKPaintStyle.Fill
				})
				{
					// Setting pixels in opposite corners to anchor the image. This helps
					// keep things in place when pasting it into certain paint programs.
					canvas.DrawPoint(0.5f, 0.5f, anchorPaint);
					canvas.DrawPoint(mMapWidth - 0.5f, mMapHeight - 0.5f, anchorPaint);
				}

				SKMatrix baseMatrix = canvas.TotalMatrix;
				foreach (LocationCollection collection in mLocationCollections)
				{
					if (collection.Icon is null)
					{
						float sizeScale = (mWorldToMapX + mWorldToMapY) * MapScaleFactor;

						using SKPaint paint = new()
						{
							Color = collection.Color,
							IsStroke = false,
							IsAntialias = true,
							Style = SKPaintStyle.Fill
						};

						using SKPaint textPaint = new()
						{
							Color = collection.Color,
							IsAntialias = true,
							Style = SKPaintStyle.Fill,
							Typeface = sTextTypeFace,
							TextSize = (float)Math.Round(sTextSize * sizeScale, MidpointRounding.AwayFromZero),
							TextAlign = SKTextAlign.Center
						};

						foreach (MapLocation location in collection.Locations)
						{
							SKMatrix transform = baseMatrix;
							transform = transform.PreConcat(SKMatrix.CreateTranslation((location.Position.X - mOriginOffsetX) * mWorldToMapX, (location.Position.Y - mOriginOffsetY) * mWorldToMapY));

							float locationRadius = location.Size * sizeScale;
							if (location is RotatedMapLocation rotatedLocation)
							{
								transform = transform.PreConcat(SKMatrix.CreateRotationDegrees(rotatedLocation.RotationDegrees + 90.0f, locationRadius, locationRadius));
							}

							canvas.SetMatrix(transform);

							if (location is TextMapLocation textLocation)
							{
								canvas.DrawText(textLocation.Text, SKPoint.Empty, textPaint);
							}
							else
							{
								switch (collection.Shape)
								{
									case MarkerShape.Circle:
										canvas.DrawCircle(SKPoint.Empty, locationRadius, paint);
										break;
									case MarkerShape.Square:
										canvas.DrawRect(-locationRadius, -locationRadius, locationRadius * 2.0f, locationRadius * 2.0f, paint);
										break;
								}
							}
						}
					}
					else
					{
						int iconWidth = (int)Math.Round(collection.Icon.Width * (mWorldToMapX * MapScaleFactor));
						int iconHeight = (int)Math.Round(collection.Icon.Height * (mWorldToMapY * MapScaleFactor));

						float halfIconWidth = iconWidth * 0.5f;
						float halfIconHeight = iconHeight * 0.5f;

						float iconScaleX = (float)iconWidth / collection.Icon.Width;
						float iconScaleY = (float)iconHeight / collection.Icon.Height;

						foreach (MapLocation location in collection.Locations)
						{
							SKMatrix transform = baseMatrix;
							transform = transform.PreConcat(SKMatrix.CreateTranslation((location.Position.X - mOriginOffsetX) * mWorldToMapX - halfIconWidth, (location.Position.Y - mOriginOffsetX) * mWorldToMapY - halfIconHeight));

							if (location is RotatedMapLocation rotatedLocation)
							{
								transform = transform.PreConcat(SKMatrix.CreateRotationDegrees(rotatedLocation.RotationDegrees + 90.0f, halfIconWidth, halfIconHeight));
							}

							transform = transform.PreConcat(SKMatrix.CreateScale(iconScaleX, iconScaleY));
							canvas.SetMatrix(transform);

							canvas.DrawImage(collection.Icon, SKPoint.Empty);
						}
					}
					canvas.SetMatrix(baseMatrix);
				}

				canvas.SetMatrix(baseMatrix);

				surface.Flush();
				SKImage image = surface.Snapshot();
				return image.Encode(format, quality);
			}
		}

		private class LocationCollection
		{
			public List<MapLocation> Locations;

			public SKColor Color;

			public SKImage? Icon;

			public MarkerShape Shape;

			public LocationCollection(IEnumerable<MapLocation> locations, SKColor color, SKImage? icon, MarkerShape shape)
			{
				Locations = new(locations);
				Color = color;
				Icon = icon;
				Shape = shape;
			}
		}
	}

	/// <summary>
	/// Represents a location on a game map
	/// </summary>
	internal class MapLocation
	{
		/// <summary>
		/// The position in world coordinates
		/// </summary>
		public FVector Position { get; }

		/// <summary>
		/// The pixel diameter of the rendered shape
		/// </summary>
		/// <remarks>
		/// The size only applies to "Shape" and "Text" style locations. "Icon" locations will instead be
		/// sized based on the size of the icon itself, ignoring this parameter.
		/// 
		/// Even though the size is represented in texture pixels, it will be scaled for cases where the
		/// world to map ratio is non-standard such that the size will remain fixed relative to the world
		/// scale.
		/// </remarks>
		public float Size { get; }

		public MapLocation(FVector position, float size = 1.0f)
		{
			Position = position;
			Size = size;
		}
	}

	/// <summary>
	/// Represents a rotated map location
	/// </summary>
	internal class RotatedMapLocation : MapLocation
	{
		/// <summary>
		/// The rotation of the location, in degrees.
		/// </summary>
		/// <remarks>
		/// Rotation is pivoted around the center of the location marker.
		/// </remarks>
		public float RotationDegrees { get; }

		public RotatedMapLocation(FVector position, float rotationDegrees, float size = 1.0f)
			: base(position, size)
		{
			RotationDegrees = rotationDegrees;
		}
	}

	/// <summary>
	/// A map location represented with text
	/// </summary>
	internal class TextMapLocation : MapLocation
	{
		/// <summary>
		/// Text to display at the location
		/// </summary>
		public string Text { get; }

		public TextMapLocation(FVector position, string text, float size = 1.0f)
			: base(position, size)
		{
			Text = text;
		}
	}

	/// <summary>
	/// The shape of a location marker on a map overlay
	/// </summary>
	internal enum MarkerShape
	{
		None,
		Circle,
		Square
	}
}
