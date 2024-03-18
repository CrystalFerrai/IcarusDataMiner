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
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Versions;
using System.Text.RegularExpressions;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Exports all localization databases to a readable text format
	/// </summary>
	internal partial class LocalizationMiner : IDataMiner
	{
		public string Name => "Localization";

		[GeneratedRegex(@"^Icarus/.+/(.+)/.+\.locres$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
		private static partial Regex LocResRegex();

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name);
			Directory.CreateDirectory(outDir);

			IReadOnlyDictionary<ELanguage, List<FTextLocalizationResource>> allLangauges = LoadAllLanguages(providerManager.AssetProvider);
			ExportLanguages(allLangauges, outDir, logger);

			return true;
		}

		private static IReadOnlyDictionary<ELanguage, List<FTextLocalizationResource>> LoadAllLanguages(IFileProvider provider)
		{
			Dictionary<ELanguage, List<FTextLocalizationResource>> allLanguages = new();

			Regex regex = LocResRegex();

			foreach (var file in provider.Files)
			{
				Match match = regex.Match(file.Key);
				if (!match.Success) continue;

				if (!file.Value.TryCreateReader(out var archive)) continue;

				ELanguage language = GetLanguage(match.Groups[1].Value);

				List<FTextLocalizationResource>? tables;
				if (!allLanguages.TryGetValue(language, out tables))
				{
					tables = new List<FTextLocalizationResource>();
					allLanguages.Add(language, tables);
				}
				tables.Add(new FTextLocalizationResource(archive));
			}

			return allLanguages;
		}

		private static void ExportLanguages(IReadOnlyDictionary<ELanguage, List<FTextLocalizationResource>> languages, string outDir, Logger logger)
		{
			const string divider = "----------------------------------------";

			foreach (var language in languages)
			{
				string outPath = Path.Combine(outDir, $"{language.Key}.txt");
				using (FileStream file = IOUtil.CreateFile(outPath, logger))
				using (StreamWriter writer = new(file))
				{
					foreach (var tables in language.Value)
					{
						foreach (var table in tables.Entries)
						{
							writer.WriteLine(divider);
							writer.WriteLine(table.Key.Str);
							writer.WriteLine(divider);

							foreach (var item in table.Value)
							{
								writer.WriteLine($"{item.Key.Str}={item.Value.LocalizedString}");
							}
						}
					}
				}
			}
		}

		private static ELanguage GetLanguage(string languageCode)
		{
			return languageCode.ToLowerInvariant() switch
			{
				"en" => ELanguage.English,
				"fr-fr" => ELanguage.French,
				"de-de" => ELanguage.German,
				"ja-jp" => ELanguage.Japanese,
				"ko-kr" => ELanguage.Korean,
				"pt-br" => ELanguage.PortugueseBrazil,
				"es-419" => ELanguage.SpanishLatin,
				"ru-ru" => ELanguage.Russian,
				"zh-hans" => ELanguage.Chinese,
				"zh-hant" => ELanguage.TraditionalChinese,
				_ => throw new DataMinerException($"Found an unhandled language: {languageCode}")
			};
		}
	}
}
