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
	/// Extracts prospect dropship entry locations
	/// </summary>
	internal class DropLocationMiner : IDataMiner
	{
		public string Name => "Drops";

		private static readonly SKColor sAreaColor = new(255, 255, 255, 128);

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData worldData in providerManager.WorldDataUtil.Rows)
			{
				logger.Log(LogLevel.Information, $"Processing {worldData.Name}...");
				IEnumerable<DropZone> zones = FindDropZones(worldData).OrderBy(z => z.Index);
				if (zones.Any())
				{
					OutputData(worldData, zones, providerManager, config.OutputDirectory, logger);
					OutputOverlay(worldData, zones, providerManager, config.OutputDirectory, logger);
				}
			}
			return true;
		}

		private static IEnumerable<DropZone> FindDropZones(WorldData worldData)
		{
			// BP_IcarusGameMode gets average location of all spawns in group then runs an EQS query using
			// EQS_FindDynamicDropZoneLocation_NavMesh which has a SimpleGrid query with a radius of 25000

			foreach (var pair in worldData.DropGroups)
			{
				FVector center = pair.Value.Locations.Aggregate((a, b) => a + b) / pair.Value.Locations.Count;
				yield return new(int.Parse(pair.Key), pair.Value.GroupName!.Substring(pair.Value.GroupName.IndexOf('_') + 1), center);
			}
		}

		private void OutputData(WorldData worldData, IEnumerable<DropZone> dropZones, IProviderManager providerManager, string outputDirectory, Logger logger)
		{
			string outPath = Path.Combine(outputDirectory, Name, "Data", $"{worldData.Name}.csv");
			using (FileStream outFile = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(outFile))
			{
				writer.WriteLine("Index,Name,CenterX,CenterY,CenterZ,Grid");
				foreach (DropZone zone in dropZones)
				{
					writer.WriteLine($"{zone.Index},{zone.Name},{zone.Center.X},{zone.Center.Y},{zone.Center.Z},{worldData.GetGridCell(zone.Center)}");
				}
			}
		}

		private void OutputOverlay(WorldData worldData, IEnumerable<DropZone> dropZones, IProviderManager providerManager, string outputDirectory, Logger logger)
		{
			FVector textOffest = new(0.0f, 2500.0f, 0.0f);

			MapOverlayBuilder overlayBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
			overlayBuilder.AddLocations(dropZones.Select(z => new AreaMapLocation(z.Center, DropZone.OuterRadius)), sAreaColor);
			overlayBuilder.AddLocations(dropZones.Select(z => new AreaMapLocation(z.Center, DropZone.InnerRadius)), sAreaColor);
			overlayBuilder.AddLocations(dropZones.Select(z => new TextMapLocation(z.Center - textOffest, z.Index.ToString())), 28.0f, SKColors.Black);
			overlayBuilder.AddLocations(dropZones.Select(z => new TextMapLocation(z.Center + textOffest, z.Name!)), 16.0f, SKColors.Black);
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

			public DropZone(int index, string? name, FVector center)
			{
				Index = index;
				Name = name;
				Center = center;
			}

			public override string ToString()
			{
				return $"{Index}: {Name}";
			}
		}
	}
}
