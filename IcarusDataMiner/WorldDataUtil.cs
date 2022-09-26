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
		public const int TileSize = 100800;

		public string RowStruct { get; set; }

		public WorldData Defaults { get; set; }

		public List<WorldData> Rows { get; }

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

		public List<string> HeightmapLevels { get; } = new List<string>();

		public List<string> GeneratedLevels { get; } = new List<string>();

		public string? GeneratedVistaLevel { get; set; }

		public List<string> DeveloperLevels { get; } = new List<string>();

		public List<FBoxSphereBounds> GridBounds { get; } = new List<FBoxSphereBounds>();

		public List<WorldCollection> WorldCollections { get; } = new List<WorldCollection>();

		public MinimapData? MinimapData { get; set; }

		public string GetGridCell(FVector location)
		{
			if (MinimapData == null) return string.Empty;

			// A single grid cell always represents 50400 units along each axis
			const float CellSize = 50400.0f;

			int X = (int)Math.Floor((location.X - MinimapData.WorldBoundaryMin.X) / CellSize);
			int Y = (int)Math.Floor((location.Y - MinimapData.WorldBoundaryMin.Y) / CellSize);

			return $"{(char)('A' + X)}{Y + 1}";
		}
	}

	/// <summary>
	/// Collection of sublevels from a world
	/// </summary>
	internal class WorldCollection
	{
		public string? CollectionName { get; set; }

		public List<string> HeightmapLevels { get; } = new List<string>();

		public string? DeveloperLevel { get; set; }
	}

	/// <summary>
	/// Mapping data for a world
	/// </summary>
	internal class MinimapData
	{
		public List<string> MapTextures { get; } = new List<string>();

		public List<string> HeightMapTextures { get; } = new List<string>();

		public FVector2D WorldBoundaryMin { get; set; }

		public FVector2D WorldBoundaryMax { get; set; }
	}
}
