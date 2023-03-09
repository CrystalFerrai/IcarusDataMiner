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
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;

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
			string outputPath = Path.Combine(config.OutputDirectory, $"{Name}.csv");

			List<string> tiers = new List<string>(providerManager.ProspectDataUtil.ProspectsByTier.Keys);
			tiers.Sort();

			using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
			using (StreamWriter writer = new StreamWriter(outStream))
			{
				writer.WriteLine("ID,Talent,Name,Duration,Forecast,NodeCount,NodeIds,NodeAmount");

				foreach (string tier in tiers)
				{
					foreach (ProspectData prospect in providerManager.ProspectDataUtil.ProspectsByTier[tier])
					{
						writer.WriteLine(SerializeProspect(prospect));
					}
				}
			}
		}

		private enum ProspectParseState
		{
			SearchingForDefaults,
			InDefaults,
			InDefaultForecast,
			SearchingForRows,
			InRows,
			InObject,
			InDuration,
			InMetaDeposits,
			InForecast,
			InDifficultyArray,
			InDifficulty,
			InDifficultyForecast,
			ExitObject,
			Done
		}

		private static string SerializeProspect(ProspectData prospect)
		{
			int countMin = Math.Min(prospect.NodeCountMin, prospect.Nodes.Count);
			int countMax = Math.Min(prospect.NodeCountMax, prospect.Nodes.Count);
			int amountMin = prospect.NodeAmountMin == int.MaxValue ? 0 : prospect.NodeAmountMin;
			int amountMax = prospect.NodeAmountMax == int.MinValue ? 0 : prospect.NodeAmountMax;

			// Weird format so that Excel won't interpret the field as a date
			string nodeCount = countMin == countMax ? countMin.ToString() : $"\"=\"\"{countMin}-{countMax}\"\"\"";
			string nodeAmount = amountMin == amountMax ? amountMin.ToString() : $"\"=\"\"{amountMin}-{amountMax}\"\"\"";
			string nodeList = $"\"{string.Join(", ", prospect.Nodes)}\"";

			return $"{prospect.ID},{prospect.Talent},{prospect.Name},{prospect.Duration},{prospect.Forecast},{nodeCount},{nodeList},{nodeAmount}";
		}
	}
}
