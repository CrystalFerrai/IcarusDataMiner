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

using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.UObject;

namespace IcarusDataMiner
{
	/// <summary>
	/// Proxy for a UGameplayTexture asset to assist with accessing the source UTexture2D
	/// </summary>
	internal class UGameplayTexture : UObject
	{
		public FPackageIndex? SourceTexture { get; private set; }

		public override void Deserialize(FAssetArchive Ar, long validPos)
		{
			base.Deserialize(Ar, validPos);
			SourceTexture = PropertyUtil.GetOrDefault<FPackageIndex>(this, "SourceTexture");
		}
	}
}
