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
using SkiaSharp;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts data about the locations of exotics veins
	/// </summary>
	internal class ExoticVeinMiner : IDataMiner
	{
		public string Name => "Exotics";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;

				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ExportExotics(packageFile, providerManager, world, config, logger);
			}

			return true;
		}

		private void ExportExotics(GameFile mapAsset, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int typeNameIndex = -1, plantTypeNameIndex = -1, spawnIdentifierIndex = -1, rootComponentIndex = -1, metaNodeIndex = -1, relativeLocationIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_IcarusMetaSpawn_C":
						typeNameIndex = i;
						break;
					case "BP_ExoticPlantSpawn_C":
						plantTypeNameIndex = i;
						break;
					case "Spawn_Identifier":
						spawnIdentifierIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "Meta Node Handle":
						metaNodeIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
				}
			}

			int typeClassIndex = -1, plantTypeClassIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				FObjectImport import = mapPackage.ImportMap[i];
				if (import.ObjectName.Index == typeNameIndex)
				{
					typeClassIndex = ~i;
				}
				else if (import.ObjectName.Index == plantTypeNameIndex)
				{
					plantTypeClassIndex = ~i;
				}
			}

			List<ExoticNodeInfo> exoticNodes = new();

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != typeClassIndex && export.ClassIndex.Index != plantTypeClassIndex) continue;

				ExoticNodeInfo node = new();

				if (export.ClassIndex.Index == plantTypeClassIndex)
				{
					node.NodeType = ExoticNodeType.Plant;
				}

				UObject veinObject = export.ExportObject.Value;
				for (int i = 0; i < veinObject.Properties.Count; ++i)
				{
					FPropertyTag prop = veinObject.Properties[i];
					if (prop.Name.Index == spawnIdentifierIndex)
					{
						IPropertyHolder spawnIdentifierProperty = PropertyUtil.GetByIndex<IPropertyHolder>(veinObject, i);
						FName value = PropertyUtil.Get<FName>(spawnIdentifierProperty, "Value");

						node.SpawnIdentifier = value.Text;// [(value.Text.IndexOf('_') + 1)..];
					}
					else if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(veinObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						for (int j = 0; j < rootComponentObject.Properties.Count; ++j)
						{
							FPropertyTag subProp = rootComponentObject.Properties[j];
							if (subProp.Name.Index == relativeLocationIndex)
							{
								node.Location = PropertyUtil.GetByIndex<FVector>(rootComponentObject, j);
								break;
							}
						}
					}
					else if (prop.Name.Index == metaNodeIndex)
					{
						IPropertyHolder metaNodeProperty = PropertyUtil.GetByIndex<IPropertyHolder>(veinObject, i);
						FName value = PropertyUtil.Get<FName>(metaNodeProperty, "RowName");

						string nodeType = value.PlainText;
						switch (nodeType)
						{
							case "MetaDeposit_Arctic":
							case "MetaDeposit_Conifer":
							case "MetaDeposit_Desert":
								node.NodeType = ExoticNodeType.Deposit;
								break;
							case "MetaDeposit_Volcanic":
								node.NodeType = ExoticNodeType.RedDeposit;
								break;
						}
					}
				}

				if (node.NodeType != ExoticNodeType.Plant && node.SpawnIdentifier.Equals("None")) continue;

				exoticNodes.Add(node);
			}

			if (exoticNodes.Count > 0)
			{
				exoticNodes.Sort();

				// CSV
				{
					string outputPath = Path.Combine(config.OutputDirectory, Name, $"{mapAsset.NameWithoutExtension}.csv");
					using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
					using (StreamWriter writer = new(outStream))
					{
						writer.WriteLine("Node ID,Type,LocationX,LocationY,Map");
						foreach (var node in exoticNodes)
						{
							writer.WriteLine($"{node.SpawnIdentifier},{node.NodeType},{node.Location.X},{node.Location.Y},{worldData.GetGridCell(node.Location)}");
						}
					}
				}

				// Image
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
					mapBuilder.AddLocations(exoticNodes.Where(n => n.NodeType == ExoticNodeType.Deposit).Select(n => new MapLocation(n.Location)), Resources.Icon_Exotic);
					mapBuilder.AddLocations(exoticNodes.Where(n => n.NodeType == ExoticNodeType.RedDeposit).Select(n => new MapLocation(n.Location)), Resources.Icon_RedExotic);
					mapBuilder.AddLocations(exoticNodes.Where(n => n.NodeType == ExoticNodeType.Plant).Select(n => new MapLocation(n.Location)), Resources.Icon_ExoticSeed);
					SKData outData = mapBuilder.DrawOverlay();

					string outPath = Path.Combine(config.OutputDirectory, Name, $"{mapAsset.NameWithoutExtension}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}
		}

		private class ExoticNodeInfo : IComparable<ExoticNodeInfo>
		{
			private const string DefaultIdentifier = "None";

			public string SpawnIdentifier { get; set; } = DefaultIdentifier;

			public FVector Location { get; set; }

			public ExoticNodeType NodeType { get; set; }

			public int CompareTo(ExoticNodeInfo? other)
			{
				if (other is null) return 1;

				if (SpawnIdentifier.Equals(DefaultIdentifier))
				{
					return other!.SpawnIdentifier.Equals(DefaultIdentifier) ? 0 : -1;
				}

				if (other!.SpawnIdentifier.Equals(DefaultIdentifier))
				{
					return 1;
				}

				return SpawnIdentifier.CompareTo(other!.SpawnIdentifier);
			}

			public override string ToString()
			{
				return SpawnIdentifier;
			}
		}

		private enum ExoticNodeType
		{
			Unknown,
			Deposit,
			RedDeposit,
			Plant
		}
	}
}
