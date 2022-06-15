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
using System.Text.RegularExpressions;

namespace IcarusDataMiner
{
	/// <summary>
	/// Utility to assist with localized text
	/// </summary>
	internal static class LocalizationUtil
	{
		private static Regex sLocTextRegex;

		static LocalizationUtil()
		{
			sLocTextRegex = new Regex(@"NSLOCTEXT\(\""(.+)\""\, \""(.+)\""\, \""(.+)\""\)");
		}

		/// <summary>
		/// Retrieves a localized string for a serialized FText, such as those found in json files from Data.pak
		/// </summary>
		public static string GetLocalizedString(IFileProvider provider, string locText)
		{
			Match match = sLocTextRegex.Match(locText);
			if (match.Success)
			{
				return provider.GetLocalizedString(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
			}

			return locText;
		}
	}
}
