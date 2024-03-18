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
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.IO.Enumeration;
using System.Text;
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
			sCaveIdRegex = new Regex(@"BP_Cave(?:Prefab)?(?:_C_)*(\d*).*");
		}

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			logger.Log(LogLevel.Information, "Loading cave templates...");
			Dictionary<string, CaveTemplate> templates = new();

			GameFile waterSetupFile = providerManager.DataProvider.Files["World/D_WaterSetup.json"];
			IcarusDataTable<FWaterSetup> waterSetupTable = IcarusDataTable<FWaterSetup>.DeserializeTable("D_WaterSetup", Encoding.UTF8.GetString(waterSetupFile.Read()));

			const string TemplateMatch = "Icarus/Content/Prefabs/Cave/*.uasset";

			foreach (var pair in providerManager.AssetProvider.Files)
			{
				if (FileSystemName.MatchesSimpleExpression(TemplateMatch, pair.Key))
				{
					CaveTemplate? template = LoadCaveTemplate(pair.Value, providerManager, waterSetupTable, config, logger);
					if (template != null)
					{
						templates.Add(pair.Value.NameWithoutExtension, template);
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

		private CaveTemplate? LoadCaveTemplate(GameFile templateAsset, IProviderManager providerManager, IcarusDataTable<FWaterSetup> waterSetupTable, Config config, Logger logger)
		{
			Package templatePackage = (Package)providerManager.AssetProvider.LoadPackage(templateAsset);

			FObjectExport export = templatePackage.ExportMap[0];
			if (!export.ClassName.Equals("CavePrefabAsset"))
			{
				logger.Log(LogLevel.Warning, $"Asset {templateAsset.NameWithoutExtension} does not appear to be a CavePrefabAsset");
				return null;
			}
			UObject templateObject = export.ExportObject.Value;

			CaveTemplate template = new CaveTemplate();
			template.Name = templateObject.Name;

			List<Locator> entranceLocators = new();

			const string oreFoliagePrefix = "BPHV_Foilage_"; // (sp)

			for (int i = 0; i < templateObject.Properties.Count; ++i)
			{
				FPropertyTag property = templateObject.Properties[i];
				switch (property.Name.PlainText)
				{
					case "Entrances":
						{
							UScriptArray entrancesArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							for (int j = 0; j < entrancesArray.Properties.Count; ++j)
							{
								UScriptStruct entranceObject = (UScriptStruct)entrancesArray.Properties[j].GetValue(typeof(UScriptStruct))!;
								FPropertyTag transformProperty = ((FStructFallback)entranceObject.StructType).Properties[0];
								FTransform transform = new((FStructFallback)transformProperty.Tag!.GetValue(typeof(FStructFallback))!);
								template.Entrances.Add(new Locator(transform.Translation, transform.Rotator()));
							}
							break;
						}
					case "Lakes":
						{
							UScriptArray lakesArray = PropertyUtil.GetByIndex<UScriptArray>(templateObject, i);
							int waterCount = 0, lavaCount = 0;
							for (int j = 0; j < lakesArray.Properties.Count; ++j)
							{
								UScriptStruct lakeObject = (UScriptStruct)lakesArray.Properties[j].GetValue(typeof(UScriptStruct))!;
								FStructFallback waterSetupProperty = PropertyUtil.Get<FStructFallback>((IPropertyHolder)lakeObject.StructType, "WaterSetup");
								string waterSetupRowName = PropertyUtil.Get<FName>(waterSetupProperty, "RowName").Text;

								bool isWater = false;
								bool isLava = false;
								foreach (FRowHandle mod in waterSetupTable[waterSetupRowName].WetModifiers)
								{
									switch (mod.RowName)
									{
										case "Wet":
											isWater = true;
											break;
										case "Lava":
											isLava = true;
											break;
									}
								}

								if (isWater) ++waterCount;
								if (isLava) ++lavaCount;
							}
							template.TotalLakeCount = lakesArray.Properties.Count;
							template.WaterCount = waterCount;
							template.LavaCount = lavaCount;
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
							for (int j = 0; j < veinArray.Properties.Count; ++j)
							{
								UScriptStruct veinObject = (UScriptStruct)veinArray.Properties[j].GetValue(typeof(UScriptStruct))!;

								FPropertyTag transformProperty = ((FStructFallback)veinObject.StructType).Properties[0];
								FTransform transform = new((FStructFallback)transformProperty.Tag!.GetValue(typeof(FStructFallback))!);
								template.DeepOreLocations.Add(new Locator(transform.Translation, transform.Rotator()));
							}

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

			// Build lists of caves by searching developer sublevels for the map
			List<CaveData> templateCaves = new List<CaveData>();
			List<CaveData> customCaves = new List<CaveData>();

			foreach (string levelPath in worldData.DeveloperLevels)
			{
				string packagePath = WorldDataUtil.GetPackageName(levelPath);

				GameFile? packageFile;
				if (!providerManager.AssetProvider.Files.TryGetValue(packagePath, out packageFile)) continue;

				logger.Log(LogLevel.Debug, $"Searching {packageFile.NameWithoutExtension}");

				string quadName = packageFile.NameWithoutExtension.Substring(packageFile.NameWithoutExtension.LastIndexOf('_') + 1);
				foreach (CaveData cave in FindCaves(packageFile, quadName, templates, providerManager, config, logger))
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
				// CSV
				{
					string outPath = Path.Combine(config.OutputDirectory, Name, "Data", $"{mapAsset.NameWithoutExtension}.csv");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					using (StreamWriter writer = new StreamWriter(outStream))
					{
						writer.WriteLine("Quad,ID,Template,Ore Foliage Type,Ore Count,Exotics,DeepOres,DO1X,DO1Y,DO1Z,DO2X,DO2Y,DO2Z,DO3X,DO3Y,DO3Z,Worms,Lakes,Water,Lava,Mushrooms,Entrance X,Entrance Y,Entrance Z,Entrance R,Entrance Grid");

						foreach (CaveData cave in templateCaves)
						{
							if (cave.Template!.OreData.Count != 1) throw new NotImplementedException();
							if (cave.Template!.Entrances.Count != 1) throw new NotImplementedException();
							if (cave.DeepOreLocations.Count > 3) throw new NotImplementedException();

							string wormCount = $"\"=\"\"{cave.Template.WormCountMin}-{cave.Template.WormCountMax}\"\"\""; // Weird format so that Excel won't interpret the field as a date

							Locator entrance = cave.Entrances[0].Location;
							string[] deepOrePos = new string[] { ",,", ",,", ",," };
							for (int i = 0; i < cave.DeepOreLocations.Count; ++i)
							{
								Locator deepOre = cave.DeepOreLocations[i];
								deepOrePos[i] = $"{deepOre.Position.X},{deepOre.Position.Y},{deepOre.Position.Z}";
							}

							writer.WriteLine($"{cave.QuadName},{cave.QuadName[0]}-{cave.ID},{cave.Template.Name},{cave.Template.OreData[0].Pool},{cave.Template.OreData[0].Count},{cave.Template.ExoticCount},{cave.DeepOreLocations.Count},{deepOrePos[0]},{deepOrePos[1]},{deepOrePos[2]},{wormCount},{cave.Template.TotalLakeCount},{cave.Template.WaterCount},{cave.Template.LavaCount},{cave.Template.MushroomCount},{entrance.Position.X},{entrance.Position.Y},{entrance.Position.Z},{entrance.Rotation.Yaw},{worldData.GetGridCell(entrance.Position)}");
						}
					}
				}

				// Image
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
					mapBuilder.AddLocations(templateCaves.SelectMany(c => c.Entrances.Select(e => new RotatedMapLocation(e.Location.Position, e.Location.Rotation.Yaw))), Resources.Icon_Cave);
					SKData outData = mapBuilder.DrawOverlay();

					string outPath = Path.Combine(config.OutputDirectory, Name, "Visual", $"{mapAsset.NameWithoutExtension}.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
				// Labeled image
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
					mapBuilder.AddLocations(templateCaves.SelectMany(c => c.Entrances.Select(e => new TextMapLocation(e.Location.Position, $"{c.QuadName[0]}-{c.ID}"))));
					SKData outData = mapBuilder.DrawOverlay();

					string outPath = Path.Combine(config.OutputDirectory, Name, "Visual", $"{mapAsset.NameWithoutExtension}-IDs.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}

			// Write custom caves
			if (customCaves.Count > 0)
			{
				// CSV
				{
					string outCustomPath = Path.Combine(config.OutputDirectory, Name, "Data", $"{mapAsset.NameWithoutExtension}_Custom.csv");
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

						writer.Write("Quad,ID");
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
							writer.Write($"{cave.QuadName},{cave.ID}");
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

				// Image
				{
					MapOverlayBuilder mapBuilder = MapOverlayBuilder.Create(worldData, providerManager.AssetProvider);
					mapBuilder.AddLocations(customCaves.SelectMany(c => c.Entrances.Select(e => new RotatedMapLocation(e.Location.Position, e.Location.Rotation.Yaw))), Resources.Icon_Cave);
					SKData outData = mapBuilder.DrawOverlay();

					string outPath = Path.Combine(config.OutputDirectory, Name, "Visual", $"{mapAsset.NameWithoutExtension}_Custom.png");
					using (FileStream outStream = IOUtil.CreateFile(outPath, logger))
					{
						outData.SaveTo(outStream);
					}
				}
			}
		}

		private IEnumerable<CaveData> FindCaves(GameFile mapAsset, string quadName, IReadOnlyDictionary<string, CaveTemplate> templates, IProviderManager providerManager, Config config, Logger logger)
		{
			CaveTemplate defaultTemplate = new CaveTemplate() { Name = "Unknown" };

			Package mapPackage = (Package)providerManager.AssetProvider.LoadPackage(mapAsset);

			int templateCaveTypeNameIndex = -1, customCaveTypeNameIndex = -1, prefabAssetNameIndex = -1, caveEntranceNameIndex = -1, instanceComponentsIndex = -1, entranceRefsIndex = -1, rootComponentIndex = -1, relativeLocationIndex = -1, relativeRotationIndex = -1, attachParentIndex = -1;
			for (int i = 0; i < mapPackage.NameMap.Length; ++i)
			{
				FNameEntrySerialized name = mapPackage.NameMap[i];
				switch (name.Name)
				{
					case "BP_CavePrefab_C":
						templateCaveTypeNameIndex = i;
						break;
					case "BP_Cave_C":
						customCaveTypeNameIndex = i;
						break;
					case "PrefabAsset":
						prefabAssetNameIndex = i;
						break;
					case "BP_CaveEntranceComponent_C":
						caveEntranceNameIndex = i;
						break;
					case "InstanceComponents":
						instanceComponentsIndex = i;
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

			Func<UObject, CaveEntranceData> parseEntrance = (entranceComponentObject) =>
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

				CaveData caveData = new(quadName);
				caveData.ID = match.Groups[1].Value.Length > 0 ? int.Parse(match.Groups[1].Value) : 0;

				for (int i = 0; i < caveObject.Properties.Count; ++i)
				{
					FPropertyTag prop = caveObject.Properties[i];
					if (prop.Name.Index == prefabAssetNameIndex)
					{
						FPackageIndex prefabAssetProperty = (FPackageIndex)prop.Tag!.GetValue(typeof(FPackageIndex))!;
						if (!templates.TryGetValue(prefabAssetProperty.Name, out CaveTemplate? template))
						{
							logger.Log(LogLevel.Warning, $"Template cave references template {prefabAssetProperty.Name} which has not been loaded. Cave will be missing information.");
							continue;
						}

						caveData.Template = template;
					}
					else if (prop.Name.Index == entranceRefsIndex)
					{
						UScriptArray entrances = PropertyUtil.GetByIndex<UScriptArray>(caveObject, i);
						for (int j = 0; j < entrances.Properties.Count; ++j)
						{
							FPackageIndex entranceComponentProperty = (FPackageIndex)entrances.Properties[j].GetValue(typeof(FPackageIndex))!;
							UObject entranceComponentObject = entranceComponentProperty.ResolvedObject!.Object!.Value;

							caveData.Entrances.Add(parseEntrance(entranceComponentObject));
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

							caveData.SpeculativeEntrances.Add(parseEntrance(entranceComponentObject));
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

				if (caveData.RootComponent == null)
				{
					logger.Log(LogLevel.Error, $"Failed to find root component for cave {caveObject.Name}");
					continue;
				}

				if (caveData.Template != null)
				{
					//if (caveData.Template.Name == "CAVE_CF_MED_004") System.Diagnostics.Debugger.Break();
					foreach (Locator entranceLocation in caveData.Template.Entrances)
					{
						caveData.Entrances.Add(new CaveEntranceData(caveData.RootComponent) { Location = entranceLocation });
					}
				}

				// Convert entrance locations from local space to world space
				foreach (CaveEntranceData entrance in caveData.Entrances.Concat(caveData.SpeculativeEntrances))
				{
					Stack<Locator> locators = new();
					locators.Push(entrance.Location);

					if (caveData.Template != null)
					{
						locators.Push(caveData.Location);
					}
					else
					{
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
					}

					Locator entranceLocation = new();
					while (locators.Count > 0)
					{
						entranceLocation.Transform(locators.Pop());
					}
					entrance.Location = entranceLocation;
				}

				if (caveData.Template is not null)
				{
					// Convert deep ore locations from local space to world space and add to cave data
					foreach (Locator deepOreLocal in caveData.Template.DeepOreLocations)
					{
						Locator deepOreGlobal = deepOreLocal;
						deepOreGlobal.Transform(caveData.Location);
						caveData.DeepOreLocations.Add(deepOreGlobal);
					}
				}

				yield return caveData;
			}
		}

		private class CaveData : IComparable<CaveData>
		{
			public string QuadName { get; }

			public int ID { get; set; }

			public CaveTemplate? Template { get; set; }

			public Locator Location { get; set; }

			public IList<CaveEntranceData> Entrances { get; } = new List<CaveEntranceData>();

			public IList<CaveEntranceData> SpeculativeEntrances { get; } = new List<CaveEntranceData>();

			public IList<Locator> DeepOreLocations { get; } = new List<Locator>();

			internal UObject? RootComponent { get; set; }

			public CaveData(string quadName)
			{
				QuadName = quadName;
			}

			public int CompareTo(CaveData? other)
			{
				if (other is null) return 1;
				
				int quadCompare = QuadName.CompareTo(other.QuadName);
				if (quadCompare != 0) return quadCompare;

				return ID.CompareTo(other.ID);
			}

			public override string ToString()
			{
				return $"[{QuadName} {ID}] {Template} | {Location.Position} | {Location.Rotation} | {Entrances.Count}+{SpeculativeEntrances.Count} entrance{(Entrances.Count == 1 ? "" : "s")}";
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

			public IList<Locator> Entrances { get; } = new List<Locator>();

			public IList<OreData> OreData { get; } = new List<OreData>();

			public int TotalLakeCount { get; set; }

			public int WaterCount { get; set; }

			public int LavaCount { get; set; }

			public int ExoticCount { get; set; }

			public IList<Locator> DeepOreLocations { get; } = new List<Locator>();

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

			public Locator()
			{
				Position = new FVector();
				Rotation = new FRotator();
			}

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

#pragma warning disable CS0649 // Field never assigned to

		struct FWaterSetup : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public ObjectPointer Material;
			public List<FRowHandle> Fish;
			public float FishDensity;
			public ObjectPointer Sound;
			public bool IsInCave;
			public bool IsDrinkable;
			public List<FRowHandle> WetModifiers;
			//public FGameplayTagContainer GameplayTags;
		}

#pragma warning restore CS0649
	}
}
