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

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts information about prospects
	/// </summary>
	internal class ProspectsMiner : IDataMiner
	{
		public string Name => "Prospects";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportProspectList(providerManager, config, logger);
			return true;
		}

		private void ExportProspectList(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (var pair in providerManager.ProspectDataUtil.ProspectsByTree)
			{
				string outputPath = Path.Combine(config.OutputDirectory, Name, $"{pair.Key}.csv");

				using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
				using (StreamWriter writer = new(outStream))
				{
					writer.WriteLine("ID,Talent,Name,Duration,Forecast,NodeCount,NodeIds,NodeAmount");

					foreach (ProspectData prospect in pair.Value)
					{
						writer.WriteLine(SerializeProspect(providerManager.AssetProvider, prospect));
					}
				}
			}
		}

		private static string SerializeProspect(IFileProvider locProvider, ProspectData prospect)
		{
			int countMin = Math.Min(prospect.Prospect.NumMetaSpawnsMin, prospect.Prospect.MetaDepositSpawns.Count);
			int countMax = Math.Min(prospect.Prospect.NumMetaSpawnsMax, prospect.Prospect.MetaDepositSpawns.Count);
			int amountMin = prospect.Prospect.NumMetaSpawnsMin;
			int amountMax = prospect.Prospect.NumMetaSpawnsMax;

			// Weird format so that Excel won't interpret the field as a date
			string nodeCount = countMin == countMax ? countMin.ToString() : $"\"=\"\"{countMin}-{countMax}\"\"\"";
			string nodeAmount = amountMin == amountMax ? amountMin.ToString() : $"\"=\"\"{amountMin}-{amountMax}\"\"\"";
			string nodeList = $"\"{string.Join(", ", prospect.Prospect.MetaDepositSpawns.Select(d => d.SpawnLocation.Value))}\"";

			return $"{prospect.Prospect.Name},{prospect.Talent.RowName},{LocalizationUtil.GetLocalizedString(locProvider, prospect.Prospect.DropName)},{TimeToString(prospect.Prospect.TimeDuration)},{prospect.Prospect.Forecast.RowName},{nodeCount},{nodeList},{nodeAmount}";
		}

		private static string TimeToString(FIcarusTimeSpan time)
		{
			if (time.Seconds.Min == 0)
			{
				return $"{time.Days.Min}:{time.Hours.Min:00}:{time.Mins.Min:00}";
			}
			return $"{time.Days.Min}:{time.Hours.Min:00}:{time.Mins.Min:00}:{time.Seconds.Min:00}";
		}
	}
}
