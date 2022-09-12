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
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts information about deep mining ore deposits
	/// </summary>
	internal class DeepOreMiner : IDataMiner
	{
		public string Name => "DeepOre";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ExportDeposits(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ExportDeposits(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int typeNameIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_DeepOreDepositSpawn_C":
						typeNameIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
				}
			}

			int typeClassIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				FObjectImport import = mapPackage.ImportMap[i];
				if (import.ObjectName.Index == typeNameIndex)
				{
					typeClassIndex = ~i;
					break;
				}
			}

			List<DepositInfo> deposits = new List<DepositInfo>();

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != typeClassIndex) continue;

				DepositInfo deposit = new();
				deposit.SetName(export.ObjectName);

				UObject veinObject = export.ExportObject.Value;
				for (int i = 0; i < veinObject.Properties.Count; ++i)
				{
					FPropertyTag prop = veinObject.Properties[i];
					if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(veinObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag subProp = rootComponentObject.Properties[j];
							if (subProp.Name.Index == relativeLocationIndex)
							{
								deposit.Location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
								break;
							}
						}
					}
				}

				deposits.Add(deposit);
			}

			if (deposits.Count > 0)
			{
				deposits.Sort();

				string outputPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}.csv");
				using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					writer.WriteLine("Name,LocationX,LocationY,LocationZ,Map");
					foreach (var node in deposits)
					{
						writer.WriteLine($"{node.Name},{node.Location.X},{node.Location.Y},{node.Location.Z},{worldData.GetGridCell(node.Location)}");
					}
				}
			}
		}

		private class DepositInfo : IComparable<DepositInfo>
		{
			private int mId;

#nullable disable annotations
			public string Name { get; set; }
#nullable restore annotations

			public FVector Location { get; set; }

			public DepositInfo()
			{
				mId = -1;
			}

			public void SetName(FName name)
			{
				int id = int.MaxValue;
				string nameText = name.Text;
				if (nameText.StartsWith("BP_DeepOreDepositSpawn"))
				{
					nameText = nameText["BP_DeepOreDepositSpawn".Length..];
					string[] nameParts = nameText.Split('_');
					if (nameParts.Length > 0)
					{
						nameText = nameParts[0];
						if (string.IsNullOrEmpty(nameText)) nameText = "1";

						if (int.TryParse(nameText, out int tryId))
						{
							id = tryId;
						}
					}
				}
				mId = id;
				Name = nameText;
			}

			public int CompareTo(DepositInfo? other)
			{
				int idCompare = mId.CompareTo(other!.mId);
				if (idCompare == 0) return Name.CompareTo(other!.Name);
				return idCompare;
			}

			public override string ToString()
			{
				return Name;
			}
		}
	}
}

