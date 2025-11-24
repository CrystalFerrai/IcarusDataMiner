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
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts prospect dropship entry locations
	/// </summary>
	internal class DropLocationMiner : IDataMiner
	{
		public string Name => "Drops";

		private static readonly SKColor sAreaColor = new(255, 255, 255, 128);
		private static readonly SKColor sSelectableAreaColor = new(255, 255, 0, 128);
		private static readonly SKColor sDefaultAreaColor = new(0, 255, 0, 128);

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			IcarusDataTable<FDropGroupCosmeticData> dropGroupsTable = DataTables.LoadDataTable<FDropGroupCosmeticData>(providerManager.DataProvider, "World/D_DropGroups.json");

			foreach (WorldData worldData in providerManager.WorldDataUtil.Rows)
			{
				logger.Log(LogLevel.Information, $"Processing {worldData.Name}...");
				IEnumerable<DropZone> zones = FindDropZones(providerManager, worldData, dropGroupsTable).OrderBy(z => z.Index);
				if (zones.Any())
				{
					OutputData(worldData, zones, providerManager, config.OutputDirectory, logger);
					OutputOverlay(worldData, zones, providerManager, config.OutputDirectory, logger);
				}
			}
			return true;
		}

		private static IEnumerable<DropZone> FindDropZones(IProviderManager providerManager, WorldData worldData, IcarusDataTable<FDropGroupCosmeticData> dropGroupsTable)
		{
			Dictionary<int, FDropGroupCosmeticData> relevantDropGroups = dropGroupsTable.Values.Where(g => g.AssociatedTerrain.RowName.Equals(worldData.TerrainName)).ToDictionary(g => g.DropGroupIndex);

			FDropGroupCosmeticData? getGroupData(int groupIndex)
			{
				return relevantDropGroups.TryGetValue(groupIndex, out FDropGroupCosmeticData group) ? group : null;
			}

			foreach (var pair in worldData.DropGroups)
			{
				// BP_IcarusGameMode gets average location of all spawns in group then runs an EQS query using
				// EQS_FindDynamicDropZoneLocation_NavMesh which has a SimpleGrid query with a radius of 25000
				FVector center = pair.Value.Locations.Aggregate((a, b) => a + b) / pair.Value.Locations.Count;

				int dropIndex = int.Parse(pair.Key);
				FDropGroupCosmeticData? group = getGroupData(dropIndex);

				string name;
				if (group.HasValue)
				{
					name = LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, group.Value.DropGroupName);
				}
				else
				{
					name = pair.Value.GroupName!.Substring(pair.Value.GroupName.IndexOf('_') + 1);
				}

				yield return new(dropIndex, name, center, group);
			}
		}

		private void OutputData(WorldData worldData, IEnumerable<DropZone> dropZones, IProviderManager providerManager, string outputDirectory, Logger logger)
		{
			string outPath = Path.Combine(outputDirectory, Name, "Data", $"{worldData.Name}.csv");
			using (FileStream outFile = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(outFile))
			{
				writer.WriteLine("Index,Name,CenterX,CenterY,CenterZ,Grid,Selectable,Default");
				foreach (DropZone zone in dropZones)
				{
					writer.WriteLine($"{zone.Index},{zone.Name},{zone.Center.X},{zone.Center.Y},{zone.Center.Z},{worldData.GetGridCell(zone.Center)},{zone.GroupData.HasValue},{zone.GroupData.HasValue && zone.GroupData.Value.bIsRecommended}");
				}
			}
		}

		private void OutputOverlay(WorldData worldData, IEnumerable<DropZone> dropZones, IProviderManager providerManager, string outputDirectory, Logger logger)
		{
			FVector textOffest = new(0.0f, 3000.0f, 0.0f);

			List<DropZone> zones = new();
			List<DropZone> selectableZones = new();
			DropZone? defaultZone = null;

			foreach (DropZone zone in dropZones)
			{
				if (zone.GroupData.HasValue)
				{
					if (zone.GroupData.Value.bIsRecommended && defaultZone is null) defaultZone = zone;
					else selectableZones.Add(zone);
				}
				else
				{
					zones.Add(zone);
				}
			}

			MapOverlayBuilder overlayBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);

			overlayBuilder.AddLocations(zones.Select(z => new AreaMapLocation(z.Center, DropZone.OuterRadius)), sAreaColor);
			overlayBuilder.AddLocations(zones.Select(z => new AreaMapLocation(z.Center, DropZone.InnerRadius)), sAreaColor);

			overlayBuilder.AddLocations(selectableZones.Select(z => new AreaMapLocation(z.Center, DropZone.OuterRadius)), sAreaColor);
			overlayBuilder.AddLocations(selectableZones.Select(z => new AreaMapLocation(z.Center, DropZone.InnerRadius)), sSelectableAreaColor);

			if (defaultZone is not null)
			{
				overlayBuilder.AddLocations(new AreaMapLocation(defaultZone.Center, DropZone.OuterRadius).AsEnumerable(), sAreaColor);
				overlayBuilder.AddLocations(new AreaMapLocation(defaultZone.Center, DropZone.InnerRadius).AsEnumerable(), sDefaultAreaColor);
			}

			overlayBuilder.AddLocations(dropZones.Select(z => new TextMapLocation(z.Center - 200.0f - textOffest, z.Index.ToString())), 60.0f, SKColors.Black);
			overlayBuilder.AddLocations(dropZones.Select(z => new TextMapLocation(z.Center - 200.0f + textOffest, z.Name!)), 40.0f, SKColors.Black);

			SKData outData = overlayBuilder.DrawOverlay();

			string outPath = Path.Combine(outputDirectory, Name, "Visual", $"{worldData.Name}.png");
			using (FileStream outFile = IOUtil.CreateFile(outPath, logger))
			{
				outData.SaveTo(outFile);
			}
		}

		private class DropZone
		{
			// 25000 from BP/AI/GOAP/EQS/EQS_FindDynamicDropZoneLocation_NavMesh
			// Minus 1000 from random offset in BP_IcarusGameMode
			public const float InnerRadius = 24000;

			// 25000 from BP/AI/GOAP/EQS/EQS_FindDynamicDropZoneLocation_NavMesh
			// Plus 1000 from random offset in BP_IcarusGameMode
			public const float OuterRadius = 26000;

			public int Index { get; }

			public string? Name { get; }

			public FVector Center { get; }

			public FDropGroupCosmeticData? GroupData { get; }

			public DropZone(int index, string? name, FVector center, FDropGroupCosmeticData? groupData)
			{
				Index = index;
				Name = name;
				Center = center;
				GroupData = groupData;
			}

			public override string ToString()
			{
				return $"{Index}: {Name}";
			}
		}
	}

#pragma warning disable CS0649 // Field never assigned to

	internal struct FDropGroupCosmeticData : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string DropGroupName;
		public string DropGroupDescription;
		public ObjectPointer DropGroupIcon;
		public ObjectPointer DropGroupBackground;
		public bool bVisibleInDropSelectionScreen;
		public FRowHandle AssociatedTerrain;
		public int DropGroupIndex;
		public bool bIsRecommended;
		public EDropTemperature Temperature;
		public EDropAbundance Food;
		public EDropAbundance Water;
		public EDropAbundance Oxygen;
		public EDropAbundance Wood;
		public EDropAbundance Rock;
		public EDropAbundance Ore;
		public EDropAbundance AggressiveCreatures;
		public EDropAbundance PassiveCreatures;
	}

	internal enum EDropTemperature
	{
		Cold,
		Normal,
		Hot
	}

	internal enum EDropAbundance
	{
		Low,
		Medium,
		High
	}

#pragma warning restore CS0649
}
