// Copyright 2025 Crystal Ferrai
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
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IcarusDataMiner.Miners
{
	internal class QuestMiner : IDataMiner
	{
		public string Name => "Quests";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			IcarusDataTable<FFactionMission> missionsTable = DataTables.LoadDataTable<FFactionMission>(providerManager.DataProvider, "Factions/D_FactionMissions.json");
			IcarusDataTable<FQuestSetup> questsTable = DataTables.LoadDataTable<FQuestSetup>(providerManager.DataProvider, "Quests/D_Quests.json");

			logger.Log(LogLevel.Information, "Locating quest markers...");
			IcarusDataTable<FQuestQueries> questQueriesTable = DataTables.LoadDataTable<FQuestQueries>(providerManager.DataProvider, "Quests/D_QuestQueries.json");
			IReadOnlyDictionary<string, IList<FVector>> tagLocations = FindQuestMarkers(providerManager, logger);
			QuestLocationQueryData locationQueryData = new(questQueriesTable, tagLocations);

			logger.Log(LogLevel.Information, "Processing quests...");
			List<QuestData> allQuests = new();
			foreach (var pair in providerManager.ProspectDataUtil.ProspectsByTree)
			{
				List<QuestData> quests = new();
				foreach (ProspectData prospect in pair.Value)
				{
					FFactionMission mission;
					if (!missionsTable.TryGetValue(prospect.Prospect.FactionMission.RowName, out mission))
					{
						continue;
					}

					QuestData quest = QuestData.Create(prospect.Prospect);

					ProcessQuest(quest, questsTable[mission.InitialQuest.RowName], questsTable, locationQueryData, providerManager, logger);
					foreach (FMissionObjectiveEntry subMission in mission.MissionObjectives)
					{
						FQuestSetup questSetup = questsTable[subMission.QuestRow.RowName];
						QuestData subQuest = QuestData.Create(questSetup);
						ProcessQuest(subQuest, questSetup, questsTable, locationQueryData, providerManager, logger);
						quest.SubQuests.Add(subQuest);
					}
					quests.Add(quest);
				}
				allQuests.AddRange(quests);

				ExportQuestList(pair.Key, quests, providerManager, config, logger);
			}
			ExportPrebuiltStructures(allQuests, providerManager, config, logger);

			return true;
		}

		private IReadOnlyDictionary<string, IList<FVector>> FindQuestMarkers(IProviderManager providerManager, Logger logger)
		{
			Dictionary<string, IList<FVector>> tagLocations = new();

			foreach (WorldData world in providerManager.WorldDataUtil.Rows)
			{
				if (world.MainLevel is null)
				{
					continue;
				}

				if (!providerManager.AssetProvider.Files.TryGetValue(WorldDataUtil.GetPackageName(world.MainLevel), out GameFile? file))
				{
					continue;
				}

				Package package = (Package)providerManager.AssetProvider.LoadPackage(file);

				foreach (FObjectExport export in package.ExportMap)
				{
					if (!export.ClassName.Equals("BP_QuestMarker_C"))
					{
						continue;
					}

					UObject obj = export.ExportObject.Value;

					FPropertyTag? tagsProperty = null;
					FPropertyTag? rootComponentProperty = null;
					foreach (FPropertyTag property in obj.Properties)
					{
						switch (property.Name.Text)
						{
							case "GameplayTags":
								tagsProperty = property;
								break;
							case "RootComponent":
								rootComponentProperty = property;
								break;
						}
					}

					if (tagsProperty is null || rootComponentProperty is null)
					{
						continue;
					}

					CUE4Parse.UE4.Objects.GameplayTags.FGameplayTagContainer tags = (CUE4Parse.UE4.Objects.GameplayTags.FGameplayTagContainer)((UScriptStruct)tagsProperty.Tag!.GenericValue!).StructType;

					FVector location = FVector.ZeroVector;

					UObject rootComponentObject = ((FPackageIndex)rootComponentProperty.Tag!.GenericValue!).ResolvedObject!.Object!.Value;

					FPropertyTag? locationProperty = rootComponentObject.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
					if (locationProperty is not null)
					{
						location = (FVector)((UScriptStruct)locationProperty.Tag!.GenericValue!).StructType;
					}

					foreach (FName tag in tags.GameplayTags)
					{
						IList<FVector>? locations;
						if (!tagLocations.TryGetValue(tag.Text, out locations))
						{
							locations = new List<FVector>();
							tagLocations.Add(tag.Text, locations);
						}
						locations.Add(location);
					}
				}
			}

			return tagLocations;
		}

		private void ProcessQuest(QuestData quest, FQuestSetup questSetup, IcarusDataTable<FQuestSetup> questsTable, QuestLocationQueryData locationQueryData, IProviderManager providerManager, Logger logger)
		{
			string? bpPath = questSetup.Class.GetAssetPath(true);
			if (bpPath is null)
			{
				logger.Log(LogLevel.Debug, $"Quest '{questSetup.Name}' has no Class associated with it.");
				return;
			}

			ProcessQuestBlueprint(bpPath, quest, questsTable, locationQueryData, providerManager, logger);
		}

		private void ProcessQuestBlueprint(string bpPath, QuestData quest, IcarusDataTable<FQuestSetup> questsTable, QuestLocationQueryData locationQueryData, IProviderManager providerManager, Logger logger)
		{
			IFileProvider provider = providerManager.AssetProvider;

			if (bpPath.Equals("/Script/Icarus.Quest"))
			{
				// Reached base class
				return;
			}

			GameFile? file;
			provider.Files.TryGetValue(bpPath, out file);
			if (file is null)
			{
				logger.Log(LogLevel.Debug, $"Cannot load quest class {bpPath}.");
				return;
			}

			bool wasReadScriptData = provider.ReadScriptData;
			provider.ReadScriptData = true;
			Package package = (Package)provider.LoadPackage(file);

			foreach (FObjectExport? export in package.ExportMap)
			{
				if (export is null) continue;

				if (export.ExportObject.Value is UBlueprintGeneratedClass questClass)
				{
					quest.QuestClass = questClass;
					foreach (FPropertyTag property in quest.Properties!)
					{
						if (property.PropertyType.Text.Equals("QuestQueriesRowHandle"))
						{
							string rowName = ((NameProperty)property.Tag!).Value.Text;
							string tableName = ((NameProperty)property.Tag!).Value.Text;
							AddQuestLocation(quest, tableName, rowName, locationQueryData, logger);
						}
						else if (property.PropertyType.Text.Equals("ArrayProperty") && (((ArrayProperty)property.Tag!).Value.InnerTagData?.StructType?.Equals("QuestQueriesRowHandle") ?? false))
						{
							foreach (FPropertyTagType item in ((ArrayProperty)property.Tag!).Value.Properties)
							{
								FStructFallback itemData = (FStructFallback)((UScriptStruct)item.GenericValue!).StructType;
								string rowName = ((NameProperty)itemData.Properties[1].Tag!).Value.Text;
								string tableName = ((NameProperty)itemData.Properties[2].Tag!).Value.Text;
								AddQuestLocation(quest, tableName, rowName, locationQueryData, logger);
							}
						}
					}
				}
				else if (export.ExportObject.Value is UFunction function)
				{
					DisassembledFunction disassembledFunction = UFunctionDisassembler.Process(package, function);

					foreach (Operation op in disassembledFunction.GetFlattenedOperations())
					{
						if (op.OpCode == EExprToken.FinalFunction || op.OpCode == EExprToken.CallMath)
						{
							Operation<string> ffOp = (Operation<string>)op;
							if (ffOp.Operand.Equals("Quest::RunQuest"))
							{
								if (ffOp.ChildOperations[0].OpCode != EExprToken.StructConst)
								{
									// This means the quest to run is not hardcoded here
									quest.SubQuests.Add(QuestData.Unknown);
									continue;
								}

								Operation<string> nameOp = (Operation<string>)ffOp.ChildOperations[0].ChildOperations[0];
								if (questsTable.TryGetValue(nameOp.Operand, out FQuestSetup questSetup))
								{
									QuestData subQuest = QuestData.Create(questSetup);
									ProcessQuest(subQuest, questSetup, questsTable, locationQueryData, providerManager, logger);
									quest.SubQuests.Add(subQuest);
								}
							}
							else if (ffOp.Operand.Equals("IcarusQuestFunctionLibrary::GetQuestMarker"))
							{
								if (ffOp.ChildOperations.Count > 1 && ffOp.ChildOperations[1].OpCode == EExprToken.StructConst)
								{
									if (ffOp.ChildOperations[1].ChildOperations.Count < 1 || ffOp.ChildOperations[1].ChildOperations[0].OpCode != EExprToken.NameConst || ffOp.ChildOperations[1].ChildOperations[1].OpCode != EExprToken.NameConst)
									{
										logger.Log(LogLevel.Debug, "Unexpected value for quest query struct const");
										continue;
									}

									Operation<string> queryRowOp = (Operation<string>)ffOp.ChildOperations[1].ChildOperations[0];
									Operation<string> queryTableOp = (Operation<string>)ffOp.ChildOperations[1].ChildOperations[1];
									AddQuestLocation(quest, queryTableOp.Operand, queryRowOp.Operand, locationQueryData, logger);
								}
							}
							else if (ffOp.Operand.Equals("PrebuiltStructure::BuildStructure"))
							{
								ProcessPrebuiltStructureOperation(ffOp, quest, providerManager, logger);
							}
						}
						else if (op.OpCode == EExprToken.Context)
						{
							Operation<ContextOperand> cOp = (Operation<ContextOperand>)op;
							if (cOp.Operand.ContextExpression.OpCode == EExprToken.FinalFunction)
							{
								Operation<string> ffOp = (Operation<string>)cOp.Operand.ContextExpression;
								if (ffOp.Operand.Equals("PrebuiltStructure::BuildStructure"))
								{
									ProcessPrebuiltStructureOperation(ffOp, quest, providerManager, logger);
								}
							}
						}
					}
				}
			}

			provider.ReadScriptData = wasReadScriptData;
		}

		private static void AddQuestLocation(QuestData quest, string tableName, string rowName, QuestLocationQueryData locationQueryData, Logger logger)
		{
			if (!tableName.Equals(locationQueryData.QuestQueriesTable.Name))
			{
				logger.Log(LogLevel.Debug, $"Unexpected quest query table name: {tableName}");
				return;
			}

			if (!locationQueryData.QuestQueriesTable.TryGetValue(rowName, out FQuestQueries queryValue))
			{
				logger.Log(LogLevel.Debug, $"Could not locate quest query row: {rowName}");
				return;
			}

			foreach (FGameplayTag tag in queryValue.Query.TagDictionary)
			{
				List<FVector>? list;
				if (!quest.Locations.TryGetValue(tag.TagName, out list))
				{
					list = new();
					quest.Locations.Add(tag.TagName, list);
				}

				if (locationQueryData.TagLocations.TryGetValue(tag.TagName, out IList<FVector>? values))
				{
					list.AddRange(values);
				}
			}
		}

		private void ProcessPrebuiltStructureOperation(Operation<string> op, QuestData quest, IProviderManager providerManager, Logger logger)
		{
			if (op.ChildOperations[0].OpCode != EExprToken.StructConst)
			{
				// This means the structure name is not hardcoded here
				logger.Log(LogLevel.Information, "Quest spawns a prebuilt structure using a runtime-determiend name. Unable to determine which structure will be spawned.");
				return;
			}

			Operation<string> nameOp = (Operation<string>)op.ChildOperations[0].ChildOperations[0];

			quest.PrebuiltStructures.Add(nameOp.Operand);
		}

		private void ExportQuestList(string treeName, IReadOnlyList<QuestData> quests, IProviderManager providerManager, Config config, Logger logger)
		{
			string outputPath = Path.Combine(config.OutputDirectory, Name, $"{treeName}.json");

			using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
			using (StreamWriter writer = new(outStream))
			using (JsonTextWriter json = new(writer)
			{
				CloseOutput = false,
				Formatting = Formatting.Indented,
				Indentation = 2,
				IndentChar = ' '
			})
			{
				json.WriteStartObject();
				foreach (QuestData quest in quests.OrderBy(q => q.Name))
				{
					json.WritePropertyName(quest.Name!);
					quest.WriteJson(json, providerManager.AssetProvider);
				}
				json.WriteEndObject();
			}
		}

		private void ExportPrebuiltStructures(IReadOnlyList<QuestData> quests, IProviderManager providerManager, Config config, Logger logger)
		{
			string outputPath = Path.Combine(config.OutputDirectory, Name, "PrebuiltStructures.csv");

			using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
			using (StreamWriter writer = new(outStream))
			{
				writer.WriteLine("ID,Name,Prebuilts");

				foreach (QuestData quest in quests.OrderBy(q => q.Name))
				{
					IEnumerable<string> prebuilts = quest.GetAllPrebuiltStructures();
					if (!prebuilts.Any()) continue;

					string? name = null;
					if (quest.Description is not null)
					{
						name = LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, quest.Description);
					}

					writer.WriteLine($"{quest.Name},{name},{string.Join('|', prebuilts)}");
				}
			}
		}

	}

	internal class QuestData
	{
		private static HashSet<string> sIgnoreProperties;

		public static QuestData Unknown;

		public string? Name { get; set; }
		public string? Description { get; set; }
		public List<QuestData> SubQuests { get; }

		public Dictionary<string, List<FVector>> Locations { get; }
		public List<string> PrebuiltStructures { get; }

		public UBlueprintGeneratedClass? QuestClass
		{
			get => _questClass;
			set
			{
				_questClass = value;
				Properties = value?.ClassDefaultObject.ResolvedObject?.Object?.Value.Properties;
			}
		}
		private UBlueprintGeneratedClass? _questClass;

		public List<FPropertyTag>? Properties { get; private set; }

		static QuestData()
		{
			sIgnoreProperties = new()
			{
				"UberGraphFrame",
				"StatContainer",
				"ActorState"
			};
			Unknown = new("Unknown", "Unknown");
		}

		public QuestData()
		{
			SubQuests = new();
			Locations = new();
			PrebuiltStructures = new();
		}

		public QuestData(string name, string description)
			: this()
		{
			Name = name;
			Description = description;
		}

		public static QuestData Create(FIcarusProspect prospect)
		{
			return new(prospect.Name, prospect.DropName);
		}

		public static QuestData Create(FQuestSetup quest)
		{
			return new(quest.Name, quest.Description);
		}

		public IEnumerable<string> GetAllPrebuiltStructures()
		{
			foreach (string structure in PrebuiltStructures)
			{
				yield return structure;
			}
			foreach (QuestData subQuest in SubQuests)
			{
				foreach (string structure in subQuest.GetAllPrebuiltStructures())
				{
					yield return structure;
				}
			}
		}

		public void WriteJson(JsonWriter writer, IFileProvider locProvider, bool isSubQuest = false)
		{
			writer.WriteStartObject();

			if (isSubQuest)
			{
				writer.WritePropertyName(nameof(Name));
				writer.WriteValue(Name);
			}

			writer.WritePropertyName(nameof(Description));
			if (Description is null)
			{
				writer.WriteNull();
			}
			else
			{
				writer.WriteValue(LocalizationUtil.GetLocalizedString(locProvider, Description));
			}

			WriteProperties(writer);

			WriteLocations(writer);

			if (SubQuests.Count > 0)
			{
				writer.WritePropertyName(nameof(SubQuests));
				writer.WriteStartArray();
				foreach (QuestData subQuest in SubQuests)
				{
					subQuest.WriteJson(writer, locProvider, true);
				}
				writer.WriteEndArray();
			}

			writer.WriteEndObject();
		}

		private void WriteProperties(JsonWriter writer)
		{
			if (Properties is not null)
			{
				JsonSerializer serializer = new() { NullValueHandling = NullValueHandling.Include };

				IEnumerable<FPropertyTag> props = Properties.Where(p => !sIgnoreProperties.Contains(p.Name.Text));
				if (props.Any())
				{
					writer.WritePropertyName("Properties");
					writer.WriteStartObject();
					foreach (FPropertyTag prop in props)
					{
						writer.WritePropertyName(prop.Name.Text);
						serializer.Serialize(writer, prop.Tag);
					}
					writer.WriteEndObject();
				}
			}
		}

		private void WriteLocations(JsonWriter writer)
		{
			if (Locations.Count == 0)
			{
				return;
			}

			JsonSerializer serializer = new() { NullValueHandling = NullValueHandling.Include };

			writer.WritePropertyName(nameof(Locations));
			writer.WriteStartObject();
			foreach (var pair in Locations)
			{
				writer.WritePropertyName(pair.Key);
				writer.WriteStartArray();
				foreach (FVector location in pair.Value)
				{
					serializer.Serialize(writer, location);
				}
				writer.WriteEndArray();
			}
			writer.WriteEndObject();
		}

		public override string? ToString()
		{
			return Name;
		}
	}

	internal class QuestLocationQueryData
	{
		public IcarusDataTable<FQuestQueries> QuestQueriesTable { get; }

		public IReadOnlyDictionary<string, IList<FVector>> TagLocations { get; }

		public QuestLocationQueryData(IcarusDataTable<FQuestQueries> questQueriesTable, IReadOnlyDictionary<string, IList<FVector>> tagLocations)
		{
			QuestQueriesTable = questQueriesTable;
			TagLocations = tagLocations;
		}
	}

#pragma warning disable CS0649 // Field never assigned to

	internal struct FFactionMission : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public List<FMissionObjectiveEntry> MissionObjectives;
		public FRowHandle Faction;
		public List<FRowHandle> Types;
		public List<FWorkshopCost> CurrencyCost;
		public FRowHandle InitialQuest;
		public bool bUseOpenWorldRetryTimeout;
		public List<FRowHandle> AdditionalRulesets;
		public List<FRowHandle> ItemsRewarded;
		public List<FRowHandle> AccountFlagsRewarded;
		public List<FRowHandle> CharacterFlagsRewarded;
		public List<FRowHandle> TalentsRewarded;
		public List<FWorkshopCost> CurrencyRewarded;
		public List<FRowHandle> GreatHuntReward;
		public int AccountExperience;
		public int FactionExperience;
	};

	struct FQuestSetup : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public ObjectPointer Class;
		public int Variation;
		public string Description;
		public string InfoText;
		public FRowHandle LocationQuery;
		public Dictionary<EDialogueEvents, FRowHandle> DialogueEvents;
		public Dictionary<EDialogueEvents, FRowHandle> DialoguePoolEvents;
		public FRowHandle MusicCondition;
		public bool bPlayAudioCueOnCompletion;
		public List<FRowHandle> Modifiers;
		public bool bPreloadQuestClass;
	};

	internal struct FQuestQueries : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FGameplayTagQuery Query;
	};

	internal struct FMissionObjectiveEntry
	{
		public FRowHandle QuestRow;
		public int Depth;
	};

	internal enum EDialogueEvents : byte
	{
		None,
		QuestStart,
		QuestEnd
	};

#pragma warning restore CS0649
}
