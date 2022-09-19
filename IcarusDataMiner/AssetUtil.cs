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
			string packageName = objectName[..objectName.LastIndexOf('.')];
			if (packageName.StartsWith("/Game/"))
			{
				packageName = $"Icarus/Content{packageName[5..]}.{extension}";
			}
			return packageName;
		}
	}
}
