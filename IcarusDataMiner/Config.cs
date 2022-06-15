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
	/// General configuration information the program needs to be given to run
	/// </summary>
	internal class Config
	{
#nullable disable annotations
		/// <summary>
		/// The location of the "Icarus/Content" directory within an Icarus installation
		/// </summary>
		public string GameContentDirectory { get; set; }

		/// <summary>
		/// The directory to write all output files
		/// </summary>
		public string OutputDirectory { get; set; }
#nullable restore annotations
	}
}
