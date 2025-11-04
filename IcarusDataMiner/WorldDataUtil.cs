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

using CUE4Parse.UE4.Objects.Core.Math;

namespace IcarusDataMiner
{
	/// <summary>
	/// Utility for reading world data from D_WorldData.json
	/// </summary>
	internal class WorldDataUtil
	{
		/// <summary>
		/// When maps are split into tiles, this is the size (width/height), in world coordinates, of one tile
		/// </summary>
		public const int WorldTileSize = 100800;

		/// <summary>
		/// The size of a map grid cell, in world coordinates
		/// </summary>
		public const int WorldCellSize = WorldTileSize / 2;

		/// <summary>
		/// The size of a map grid cell, in map image coordinates
		/// </summary>
		public const int MapCellSize = 256;

		/// <summary>
		/// Translate a distance value from map coordinates to world coordinates by multiplying this value
		/// </summary>
		public const double MapToWorld = (double)WorldCellSize / (double)MapCellSize;

		/// <summary>
		/// Translate a distance value from world coordinates to map coordinates by multiplying this value
		/// </summary>
		public const double WorldToMap = (double)MapCellSize / (double)WorldCellSize;

		public string RowStruct { get; set; }

		public WorldData Defaults { get; set; }

		public IList<WorldData> Rows { get; }

		public WorldDataUtil(string rowStruct, WorldData defaults)
		{
			RowStruct = rowStruct;
			Defaults = defaults;
			Rows = new List<WorldData>();
		}

		public static string GetPackageName(string objectName, string extension = "umap")
		{
			return AssetUtil.GetPackageName(objectName, extension);
		}

		public void ComputeMapTiles(Logger logger)
		{
			foreach (WorldData world in Rows)
			{
				if (world.MinimapData is null) continue;

				int rows, cols;
				{
					float worldWidth = world.MinimapData.WorldBoundaryMax.X - world.MinimapData.WorldBoundaryMin.X;
					float worldHeight = world.MinimapData.WorldBoundaryMax.Y - world.MinimapData.WorldBoundaryMin.Y;

					cols = (int)(worldWidth / WorldTileSize);
					rows = (int)(worldHeight / WorldTileSize);
				}
				int calculatedTileCount = rows * cols;

				int mapTextureCount = world.MinimapData.MapTextures.Count;
				int heightmapTextureCount = world.MinimapData.HeightMapTextures.Count;
				int heightmapLevelCount = world.HeightmapLevels.Count;
				int generatedLevelCount = world.GeneratedLevels.Count;

				if (calculatedTileCount != (mapTextureCount & heightmapTextureCount & heightmapLevelCount & generatedLevelCount))
				{
					if (mapTextureCount == (heightmapTextureCount & heightmapLevelCount & generatedLevelCount) && mapTextureCount != 0)
					{
						string outputMessage = $"Map '{world.Name}' does not have the expected number of tiles. Expected = {calculatedTileCount}, Found = {mapTextureCount}";

						double dimension = Math.Sqrt(mapTextureCount);
						if (double.IsInteger(dimension))
						{
							rows = cols = (int)dimension;

							float newMin = -WorldTileSize * (rows * 0.5f);
							float newMax = WorldTileSize * (rows * 0.5f);

							world.MinimapData!.WorldBoundaryMin = new(newMin, newMin);
							world.MinimapData!.WorldBoundaryMax = new(newMax, newMax);

							logger.Log(LogLevel.Information, $"{outputMessage} This map will be interpreted as a {rows}x{cols} tile layout.");

							world.TileRowCount = rows;
							world.TileColumnCount = cols;
						}
						else
						{
							logger.Log(LogLevel.Warning, $"{outputMessage} Miners will not output images for this map.");
							world.TileRowCount = 0;
							world.TileColumnCount = 0;
						}
					}
					else
					{
						string outputMessage = $"Map '{world.Name}' has mismatched tile counts. Calculated = {calculatedTileCount}, Map Textures = {mapTextureCount}, Heightmap textures = {heightmapTextureCount}, Heightmap level = {heightmapLevelCount}, Generated levels = {generatedLevelCount}.";
						logger.Log(LogLevel.Warning, $"{outputMessage} Miners will not output images for this map.");
						world.TileRowCount = 0;
						world.TileColumnCount = 0;
					}
				}
				else
				{
					world.TileRowCount = rows;
					world.TileColumnCount = cols;
				}
			}
		}
	}

	/// <summary>
	/// Data associated with a specific world
	/// </summary>
	internal class WorldData
	{
		public string? Name { get; set; }

		public string? TerrainName { get; set; }

		public string? FileTag { get; set; }

		public string? MainLevel { get; set; }

		public string? DeveloperNotesLevel { get; set; }

		public IList<string> HeightmapLevels { get; } = new List<string>();

		public IList<string> GeneratedLevels { get; } = new List<string>();

		public string? GeneratedVistaLevel { get; set; }

		public IList<string> DeveloperLevels { get; } = new List<string>();

		public IList<FBoxSphereBounds> GridBounds { get; } = new List<FBoxSphereBounds>();

		public IList<WorldCollection> WorldCollections { get; } = new List<WorldCollection>();

		public MinimapData? MinimapData { get; set; }

		public IDictionary<string, DropGroup> DropGroups { get; } = new Dictionary<string, DropGroup>();

		public int TileRowCount { get; set; }

		public int TileColumnCount { get; set; }

		public WorldData()
		{
		}

		public WorldData(WorldData other)
		{
			Name = other.Name;
			TerrainName = other.TerrainName;
			FileTag = other.FileTag;
			MainLevel = other.MainLevel;
			DeveloperNotesLevel = other.DeveloperNotesLevel;
			HeightmapLevels = other.HeightmapLevels.ToList();
			GeneratedLevels = other.GeneratedLevels.ToList();
			GeneratedVistaLevel = other.GeneratedVistaLevel;
			DeveloperLevels = other.DeveloperLevels.ToList();
			GridBounds = other.GridBounds.Select(g => new FBoxSphereBounds(g.Origin, g.BoxExtent, g.SphereRadius)).ToList();
			WorldCollections = other.WorldCollections.Select(w => new WorldCollection(w)).ToList();
			MinimapData = other.MinimapData is null ? null : new(other.MinimapData);
			DropGroups = other.DropGroups.ToDictionary(kv => kv.Key, kv => new DropGroup(kv.Value));
			TileRowCount = other.TileRowCount;
			TileColumnCount = other.TileColumnCount;
		}

		public string GetGridCell(FVector location)
		{
			if (MinimapData == null) return string.Empty;

			int X = (int)Math.Floor((location.X - MinimapData.WorldBoundaryMin.X) / WorldDataUtil.WorldCellSize);
			int Y = (int)Math.Floor((location.Y - MinimapData.WorldBoundaryMin.Y) / WorldDataUtil.WorldCellSize);

			return $"{(char)('A' + X)}{Y + 1}";
		}

		public override string? ToString()
		{
			return Name;
		}
	}

	/// <summary>
	/// Collection of sublevels from a world
	/// </summary>
	internal class WorldCollection
	{
		public string? CollectionName { get; set; }

		public IList<string> HeightmapLevels { get; } = new List<string>();

		public string? DeveloperLevel { get; set; }

		public WorldCollection()
		{
		}

		public WorldCollection(WorldCollection other)
		{
			CollectionName = other.CollectionName;
			HeightmapLevels = other.HeightmapLevels.ToList();
			DeveloperLevel = other.DeveloperLevel;
		}

		public override string? ToString()
		{
			return CollectionName;
		}
	}

	/// <summary>
	/// Mapping data for a world
	/// </summary>
	internal class MinimapData
	{
		public IList<string> MapTextures { get; } = new List<string>();

		public IList<string> HeightMapTextures { get; } = new List<string>();

		public FVector2D WorldBoundaryMin { get; set; }

		public FVector2D WorldBoundaryMax { get; set; }

		public MinimapData()
		{
		}

		public MinimapData(MinimapData other)
		{
			MapTextures = other.MapTextures.ToList();
			HeightMapTextures = other.HeightMapTextures.ToList();
			WorldBoundaryMin = other.WorldBoundaryMin;
			WorldBoundaryMax = other.WorldBoundaryMax;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	internal class DropGroup
	{
		public IList<FVector> Locations { get; } = new List<FVector>();

		public string? GroupName { get; set; }

		public DropGroup()
		{
		}

		public DropGroup(DropGroup other)
		{
			Locations = other.Locations.ToList();
			GroupName = other.GroupName;
		}

		public override string? ToString()
		{
			return GroupName;
		}
	}
}
