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

namespace IcarusDataMiner
{
	/// <summary>
	/// Manages asset providers and shared utilities
	/// </summary>
	internal interface IProviderManager
	{
		/// <summary>
		/// Gets the provider associated witht he game's "Data" directory
		/// </summary>
		IFileProvider DataProvider { get; }

		/// <summary>
		/// Gets the provider associated witht he game's "Paks" directory
		/// </summary>
		IFileProvider AssetProvider { get; }

		/// <summary>
		/// Provides info obtained from the D_WorldData table
		/// </summary>
		WorldDataUtil WorldDataUtil { get; }
	}
}
