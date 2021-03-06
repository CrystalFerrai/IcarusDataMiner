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
using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts data about caves such as entrance locations and ore counts
	/// </summary>
	internal class CaveMiner : IDataMiner
	{
		// To extract a cave ID from a cave name
		private static readonly Regex sCaveIdRegex;

		public string Name => "Caves";

		static CaveMiner()
		{
			sCaveIdRegex = new Regex(@"BP_Cave(?:Instance)?(\d*)_GENERATED_.+");
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			logger.Log(LogLevel.Information, "Loading cave templates...");
			Dictionary<string, CaveTemplate> templates = new Dictionary<string, CaveTemplate>();

			const string TemplateMatch = "Icarus/Content/BP/World/CaveTemplates/Templates/*.uasset";

			foreach (var pair in providerManager.AssetProvider.Files)
			{
				if (FileSystemName.MatchesSimpleExpression(TemplateMatch, pair.Key))
				{
					CaveTemplate? template = LoadCaveTemplate(pair.Value, providerManager, config, logger);
					if (template != null)
					{
						// Convert file path to asset path pointing to template object in file. This is what references will be looking for later.
						// "Icarus/Content/BP/World/CaveTemplates/Templates/CAVE_CF_MED_002" -> "/Game/BP/World/CaveTemplates/Templates/CAVE_CF_MED_002.CAVE_CF_MED_002"
						string objectPath = $"{pair.Value.PathWithoutExtension.Replace("Icarus/Content", "/Game")}.{template.Name}";
						templates.Add(objectPath, template);
					}
				}
			}

			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel == null) continue;

				string packageName = WorldDataUtil.GetPackageName(world.MainLevel, "umap");

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packageName, out packageFile)) continue;
				
				logger.Log(LogLevel.Information, $"Processing {packageFile.NameWithoutExtension}...");
				ExportCaves(packageFile, templates, providerManager, world, config, logger);
			}

			return true;
		}

		private CaveTemplate? LoadCaveTemplate(GameFile templateAsset, IProviderManager providerManager, Config config, Logger logger)
		{
			Package templatePackage = (Package)providerManager.AssetProvider.LoadPackage(templateAsset);

			FObjectExport export = templatePackage.ExportMap[0];
			if (!export.ClassName.Equals("CaveTemplateAsset"))
			{
				logger.Log(LogLevel.Warning, $"Asset {templateAsset.NameWithoutExtension} does not appear to be a CaveTemplateAsset");
				return null;
			}
			UObject templateObject = export.ExportObject.Value;

			CaveTemplate template = new CaveTemplate();
			template.Name = templateObject.Name;

			const string oreFoliagePrefix = "BPHV_Foilage_"; // (sp)

			for (int i = 0; i < templateObject.Properties.Count; ++i)
			{
				FPropertyTag property = templateObject.Properties[i];
				switch (property.Name.PlainText)
				{
					case "Lakes":
						{
							UScriptArray lakesArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							template.LakeCount = lakesArray.Properties.Count;
							break;
						}
					case "Foliage":
						{
							UScriptArray foliageArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							for (int j = 0; j < foliageArray.Properties.Count; ++j)
							{
								UScriptStruct foliageObject = (UScriptStruct)foliageArray.Properties[j].GetValue(typeof(UScriptStruct))!;

								FSoftObjectPath foliageType = PropertyUtil.Get<FSoftObjectPath>((IPropertyHolder)foliageObject.StructType, "FoliageType");
								string foliageTypeName = foliageType.AssetPathName.Text[(foliageType.AssetPathName.PlainText.LastIndexOf('.') + 1)..];

								UScriptArray instances = PropertyUtil.Get<UScriptArray>((IPropertyHolder)foliageObject.StructType, "Instances");
								int instanceCount = instances.Properties.Count;

								if (foliageTypeName.StartsWith(oreFoliagePrefix))
								{
									OreData oreData = new()
									{
										Pool = foliageTypeName[oreFoliagePrefix.Length..],
										Count = instanceCount
									};
									template.OreData.Add(oreData);
								}
								else if (foliageTypeName.StartsWith("FT_Mushroom"))
								{
									template.MushroomCount = instanceCount;
								}
							}
							break;
						}
					case "ExoticVoxels":
						{
							UScriptArray exoticsArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							template.ExoticCount = exoticsArray.Properties.Count;
							break;
						}
					case "DeepMiningOreDeposit":
						{
							UScriptArray veinArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							template.VeinCount = veinArray.Properties.Count;
							break;
						}
					case "CaveActorSpawnMap":
						{
							UScriptMap actorMap = PropertyUtil.GetByIndex<UScriptMap>(templateObject, i);
							foreach (var pair in actorMap.Properties)
							{
								FPackageIndex key = (FPackageIndex)pair.Key!.GetValue(typeof(FPackageIndex))!;
								if (!key.Name.Equals("BP_CRE_CaveWorm_C")) continue;

								UScriptStruct value = (UScriptStruct)pair.Value!.GetValue(typeof(UScriptStruct))!;
								IPropertyHolder spawnProperties = (IPropertyHolder)value.StructType;
								for (int j = 0; j < spawnProperties.Properties.Count; ++j)
								{
									FPropertyTag spawnProperty = spawnProperties.Properties[j];

									switch (spawnProperty.Name.PlainText)
									{
										case "MinSpawnNumber":
											template.WormCountMin = PropertyUtil.GetByIndex<int>(spawnProperties, j);
											break;
										case "MaxSpawnNumber":
											template.WormCountMax = PropertyUtil.GetByIndex<int>(spawnProperties, j);
											break;
									}
								}
							}

							break;
						}
				}
			}

			return template;
		}

		private void ExportCaves(GameFile mapAsset, IReadOnlyDictionary<string, CaveTemplate> templates, IProviderManager providerManager, WorldData worldData, Config config, Logger logger)
		{
			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int worldSettingsTypeNameIndex = -1, caveLocationsIndex = -1, templateIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_IcarusWorldSettings_C":
						worldSettingsTypeNameIndex = i;
						break;
					case "CaveLocations":
						caveLocationsIndex = i;
						break;
					case "Template":
						templateIndex = i;
						break;
				}
			}

			int worldSettingsTypeIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (mapPackage.ImportMap[i].ObjectName.Index == worldSettingsTypeNameIndex)
				{
					worldSettingsTypeIndex = ~i;
					break;
				}
			}

			// Find the CaveLocations array in the map and build an array of cave templates in the same order that can be indexed later.
			CaveTemplate[]? orderedTemplates = null;
			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;
				if (export.ClassIndex.Index != worldSettingsTypeIndex) continue;

				UObject settingsObject = export.ExportObject.Value;
				for (int i = 0; i < settingsObject.Properties.Count; ++i)
				{
					FPropertyTag prop = settingsObject.Properties[i];
					if (prop.Name.Index == caveLocationsIndex)
					{
						UScriptArray caveLocationsArray = PropertyUtil.GetByIndex<UScriptArray>(settingsObject, i);
						orderedTemplates = new CaveTemplate[caveLocationsArray.Properties.Count];
						for (int j = 0; j < caveLocationsArray.Properties.Count; ++j)
						{
							UScriptStruct caveLocationStruct = (UScriptStruct)caveLocationsArray.Properties[j].GetValue(typeof(UScriptStruct))!;
							IPropertyHolder caveLocationProperties = (IPropertyHolder)caveLocationStruct.StructType;
							for (int k = 0; k < caveLocationProperties.Properties.Count; ++k)
							{
								FPropertyTag clProp = caveLocationProperties.Properties[k];
								if (clProp.Name.Index == templateIndex)
								{
									FSoftObjectPath templatePath = PropertyUtil.GetByIndex<FSoftObjectPath>(caveLocationProperties, k);
									orderedTemplates[j] = templates[templatePath.AssetPathName.Text];
									break;
								}
							}
						}

						break;
					}
				}
			}
			if (orderedTemplates == null)
			{
				logger.Log(LogLevel.Information, $"Could not locate array CaveLocations in map {mapAsset.NameWithoutExtension}. If any template caves are present, their details will be missing from the output.");
			}

			// Build lists of caves by searching all generated sublevels for the map
			List<CaveData> templateCaves = new List<CaveData>();
			List<CaveData> customCaves = new List<CaveData>();

			foreach (string levelPath in worldData.GeneratedLevels)
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");
				foreach (CaveData cave in FindCaves(packageFile, orderedTemplates, providerManager, config, logger))
				{
					if (cave.Template != null) templateCaves.Add(cave);
					else customCaves.Add(cave);
				}
			}

			templateCaves.Sort();
			customCaves.Sort();

			// Write template caves
			if (templateCaves.Count > 0)
			{
				string outPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}.csv");
				using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					writer.WriteLine("ID,Template,Ore Pool,Ore Count,Exotics,Veins,Worms,Lakes,Mushrooms,Entrance X,Entrance Y,Entrance Z,Entrance R,Entrance Grid");

					foreach (CaveData cave in templateCaves)
					{
						if (cave.Template!.OreData.Count != 1) throw new NotImplementedException();
						if (cave.Entrances.Count != 1) throw new NotImplementedException();

						string wormCount = $"\"=\"\"{cave.Template.WormCountMin}-{cave.Template.WormCountMax}\"\"\""; // Weird format so that Excel won't interpret the field as a date

						CaveEntranceData entrance = cave.Entrances[0];

						writer.WriteLine($"{cave.ID},{cave.Template.Name},{cave.Template.OreData[0].Pool},{cave.Template.OreData[0].Count},{cave.Template.ExoticCount},{cave.Template.VeinCount},{wormCount},{cave.Template.LakeCount},{cave.Template.MushroomCount},{entrance.Location.Position.X},{entrance.Location.Position.Y},{entrance.Location.Position.Z},{entrance.Location.Rotation.Yaw},{worldData.GetGridCell(entrance.Location.Position)}");
					}
				}
			}

			// Write custom caves
			if (customCaves.Count > 0)
			{
				string outCustomPath = Path.Combine(config.OutputDirectory, $"{Name}_{mapAsset.NameWithoutExtension}_Custom.csv");
				using (FileStream outStream = IOUtil.CreateFile(outCustomPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					int maxEntranceCount = 0;
					foreach (CaveData cave in customCaves)
					{
						if (cave.Entrances.Count > maxEntranceCount) maxEntranceCount = cave.Entrances.Count;
					}
					int maxSpecCount = 0;
					foreach (CaveData cave in customCaves)
					{
						if (cave.SpeculativeEntrances.Count > maxSpecCount) maxSpecCount = cave.SpeculativeEntrances.Count;
					}

					writer.Write("ID");
					for (int i = 0; i < maxEntranceCount; ++i)
					{
						writer.Write($",Entrance {i} X,Entrance {i} Y,Entrance {i} Z,Entrance {i} R,Entrance {i} Grid");
					}
					for (int i = 0; i < maxSpecCount; ++i)
					{
						writer.Write($",Spec {i} X,Spec {i} Y,Spec {i} Z,Spec {i} R,Spec {i} Grid");
					}
					writer.WriteLine();

					foreach (CaveData cave in customCaves)
					{
						writer.Write(cave.ID.ToString());
						Action<int, IList<CaveEntranceData>> writeEntrances = (max, entrances) =>
						{
							for (int i = 0; i < max; ++i)
							{
								if (i < entrances.Count)
								{
									CaveEntranceData entrance = entrances[i];
									writer.Write($",{entrance.Location.Position.X},{entrance.Location.Position.Y},{entrance.Location.Position.Z},{entrance.Location.Rotation.Yaw},{worldData.GetGridCell(entrance.Location.Position)}");
								}
								else
								{
									writer.Write($",,,,,");
								}
							}
						};
						writeEntrances(maxEntranceCount, cave.Entrances);
						writeEntrances(maxSpecCount, cave.SpeculativeEntrances);
						writer.WriteLine();
					}
				}
			}
		}

		private IEnumerable<CaveData> FindCaves(GameFile mapAsset, IReadOnlyList<CaveTemplate>? orderedTemplates, IProviderManager providerManager, Config config, Logger logger)
		{
			CaveTemplate defaultTemplate = new CaveTemplate() { Name = "Unknown" };

			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int templateCaveTypeNameIndex = -1, customCaveTypeNameIndex = -1, caveEntranceNameIndex = -1, instanceComponentsIndex = -1, entrancesIndex = -1, entranceRefsIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1, relativeRotationIndex = -1, attachParentIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_CaveInstance_C":
						templateCaveTypeNameIndex = i;
						break;
					case "BP_Cave_C":
						customCaveTypeNameIndex = i;
						break;
					case "BP_CaveEntranceComponent_C":
						caveEntranceNameIndex = i;
						break;
					case "InstanceComponents":
						instanceComponentsIndex = i;
						break;
					case "Entrances":
						entrancesIndex = i;
						break;
					case "EntranceRefs":
						entranceRefsIndex = i;
						break;
					case "RootComponent":
						rootComponentIndex = i;
						break;
					case "RelativeLocation":
						relativeLocationIndex = i;
						break;
					case "RelativeRotation":
						relativeRotationIndex = i;
						break;
					case "AttachParent":
						attachParentIndex = i;
						break;
				}
			}

			if (templateCaveTypeNameIndex < 0 && customCaveTypeNameIndex < 0) yield break; // No caves

			int templateCaveTypeIndex = -1, customCaveTypeIndex = -1, caveEntranceTypeIndex = -1;
			for (int i = 0; i < mapPackage.ImportMap.Length; ++i)
			{
				if (mapPackage.ImportMap[i].ObjectName.Index == templateCaveTypeNameIndex)
				{
					templateCaveTypeIndex = ~i;
				}
				else if (mapPackage.ImportMap[i].ObjectName.Index == customCaveTypeNameIndex)
				{
					customCaveTypeIndex = ~i;
				}
				else if (mapPackage.ImportMap[i].ObjectName.Index == caveEntranceNameIndex)
				{
					caveEntranceTypeIndex = ~i;
				}
			}

			Func<UObject, Locator> parseLocation = (obj) =>
			{
				Locator location = new();
				for (int j = 0; j < obj.Properties.Count; ++j)
				{
					FPropertyTag subProp = obj.Properties[j];
					if (subProp.Name.Index == relativeLocationIndex)
					{
						location.Position = PropertyUtil.GetByIndex<FVector>(obj, j);
					}
					else if (subProp.Name.Index == relativeRotationIndex)
					{
						location.Rotation = PropertyUtil.GetByIndex<FRotator>(obj, j);
					}
				}
				return location;
			};

			Func<UObject, CaveEntranceData> parseCave = (entranceComponentObject) =>
			{
				return new CaveEntranceData(entranceComponentObject) { Location = parseLocation(entranceComponentObject) };
			};

			foreach (FObjectExport? export in mapPackage.ExportMap)
			{
				if (export == null) continue;

				if ((templateCaveTypeNameIndex < 0 || export.ClassIndex.Index != templateCaveTypeIndex) &&
					(customCaveTypeNameIndex < 0 || export.ClassIndex.Index != customCaveTypeIndex))
				{
					continue;
				}

				UObject caveObject = export.ExportObject.Value;

				Match match = sCaveIdRegex.Match(caveObject.Name);
				if (!match.Success)
				{
					logger.Log(LogLevel.Warning, $"Error parsing cave name {caveObject.Name} in {mapAsset.NameWithoutExtension}. Cave will be skipped");
					continue;
				}

				CaveData caveData = new CaveData();
				caveData.ID = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 0;

				int entranceNameIndex;
				if (export.ClassIndex.Index == templateCaveTypeIndex)
				{
					entranceNameIndex = entrancesIndex;
					caveData.Template = orderedTemplates?[caveData.ID] ?? defaultTemplate;
				}
				else
				{
					entranceNameIndex = entranceRefsIndex;
				}

				for (int i = 0; i < caveObject.Properties.Count; ++i)
				{
					FPropertyTag prop = caveObject.Properties[i];
					if (prop.Name.Index == entranceNameIndex)
					{
						UScriptArray entrances = PropertyUtil.GetByIndex<UScriptArray>(caveObject, i);
						for (int j = 0; j < entrances.Properties.Count; ++j)
						{
							FPackageIndex entranceComponentProperty = (FPackageIndex)entrances.Properties[j].GetValue(typeof(FPackageIndex))!;
							UObject entranceComponentObject = entranceComponentProperty.ResolvedObject!.Object!.Value;

							caveData.Entrances.Add(parseCave(entranceComponentObject));
						}
					}
					else if (prop.Name.Index == instanceComponentsIndex)
					{
						// This block only runs for custom caves

						UScriptArray components = PropertyUtil.GetByIndex<UScriptArray>(caveObject, i);
						for (int j = 0; j < components.Properties.Count; ++j)
						{
							FPackageIndex componentPath = (FPackageIndex)components.Properties[j].GetValue(typeof(FPackageIndex))!;
							FObjectExport componentExport = mapPackage.ExportMap[componentPath.ResolvedObject!.ExportIndex];

							if (componentExport.ClassIndex.Index != caveEntranceTypeIndex) continue;

							UObject entranceComponentObject = componentPath.ResolvedObject!.Object!.Value;

							caveData.SpeculativeEntrances.Add(parseCave(entranceComponentObject));
						}
					}
					else if (prop.Name.Index == rootComponentIndex)
					{
						FPackageIndex rootComponentProperty = PropertyUtil.GetByIndex<FPackageIndex>(caveObject, i);
						UObject rootComponentObject = rootComponentProperty.ResolvedObject!.Object!.Value;

						caveData.RootComponent = rootComponentObject;
						caveData.Location = parseLocation(rootComponentObject);
					}
				}

				// Convert entrance locations from local space to world space
				foreach (CaveEntranceData entrance in caveData.Entrances.Concat(caveData.SpeculativeEntrances))
				{
					Stack<Locator> locators = new();
					locators.Push(entrance.Location);

					Stack<UObject> parentStack = new();
					parentStack.Push(entrance.Component);
					while (parentStack.Count > 0)
					{
						UObject currentObject = parentStack.Pop();

						for (int i = 0; i < currentObject.Properties.Count; ++i)
						{
							FPropertyTag prop = currentObject.Properties[i];
							if (prop.Name.Index == attachParentIndex)
							{
								FPackageIndex componentProperty = PropertyUtil.GetByIndex<FPackageIndex>(currentObject, i);
								UObject componentObject = componentProperty.ResolvedObject!.Object!.Value;

								if (componentObject != caveData.RootComponent)
								{
									parentStack.Push(componentObject);
								}
								locators.Push(parseLocation(componentObject));

								break;
							}
						}
					}

					Locator entranceLocation = new();
					while (locators.Count > 0)
					{
						entranceLocation.Transform(locators.Pop());
					}
					entrance.Location = entranceLocation;
				}

				yield return caveData;
			}
		}

		private class CaveData : IComparable<CaveData>
		{
			public int ID { get; set; }

			public CaveTemplate? Template { get; set; }

			public Locator Location { get; set; }

			public IList<CaveEntranceData> Entrances { get; } = new List<CaveEntranceData>();

			public IList<CaveEntranceData> SpeculativeEntrances { get; } = new List<CaveEntranceData>();

			internal UObject? RootComponent { get; set; }

			public int CompareTo(CaveData? other)
			{
				return other == null ? 1 : ID.CompareTo(other.ID);
			}

			public override string ToString()
			{
				return $"[{ID}] {Template} | {Location.Position} | {Location.Rotation} | {Entrances.Count}+{SpeculativeEntrances.Count} entrance{(Entrances.Count == 1 ? "" : "s")}";
			}
		}

		private class CaveEntranceData
		{
			public Locator Location { get; set; }

			internal UObject Component { get; }

			public CaveEntranceData(UObject component)
			{
				Component = component;
			}

			public override string ToString()
			{
				return $"{Location.Position}; {Location.Rotation}";
			}
		}

		private class CaveTemplate
		{
#nullable disable annotations
			public string Name { get; set; }
#nullable restore annotations

			public IList<OreData> OreData { get; } = new List<OreData>();

			public int LakeCount { get; set; }

			public int ExoticCount { get; set; }

			public int VeinCount { get; set; }

			public int WormCountMin { get; set; }

			public int WormCountMax { get; set; }

			public int MushroomCount { get; set; }

			public override string ToString()
			{
				return Name;
			}
		}

		private struct OreData
		{
			public string Pool;

			public int Count;

			public override string ToString()
			{
				return $"{Pool}, {Count}";
			}
		}

		private struct Locator
		{
			public FVector Position;
			public FRotator Rotation;

			public Locator(FVector position, FRotator rotation)
			{
				Position = position;
				Rotation = rotation;
			}

			internal void Transform(Locator locator)
			{
				Position += Rotation.RotateVector(locator.Position);
				Rotation += locator.Rotation;
				Rotation.Normalize();
			}

			public override string ToString()
			{
				return $"{Position}, {Rotation}";
			}
		}
	}
}
