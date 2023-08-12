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

using SkiaSharp;
using System.Reflection;

namespace IcarusDataMiner
{
	/// <summary>
	/// Provides access to embedded assembly resources
	/// </summary>
	internal static class Resources
	{
		public static SKImage Icon_Cave { get; }
		public static SKImage Icon_Exotic { get; }
		public static SKImage Icon_RedExotic { get; }

		static Resources()
		{
			Icon_Cave = LoadImage("IcarusDataMiner.Resources.icon_cave.png");
			Icon_Exotic = LoadImage("IcarusDataMiner.Resources.icon_exotic.png");
			Icon_RedExotic = LoadImage("IcarusDataMiner.Resources.icon_redexotic.png");
		}

		private static SKImage LoadImage(string resourcePath)
		{
			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)!)
			{
				return SKImage.FromEncodedData(stream);
			}
		}
	}
}
