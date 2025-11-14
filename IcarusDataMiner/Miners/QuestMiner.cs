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
using CUE4Parse.UE4.Objects.UObject;
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

			List<QuestData> quests = new();
			foreach (var pair in providerManager.DataTables.ProspectsTable!)
			{
				FFactionMission mission;
				if (!missionsTable.TryGetValue(pair.Value.FactionMission.RowName, out mission))
				{
					continue;
				}

				QuestData quest = QuestData.Create(pair.Value);

				ProcessQuest(ref quest, questsTable[mission.InitialQuest.RowName], questsTable, providerManager, logger);
				foreach (FMissionObjectiveEntry subMission in mission.MissionObjectives)
				{
					ProcessQuest(ref quest, questsTable[subMission.QuestRow.RowName], questsTable, providerManager, logger);
				}
				quests.Add(quest);
			}

			ExportQuestList(quests, providerManager, config, logger);

			return true;
		}

		private void ProcessQuest(ref QuestData quest, FQuestSetup questSetup, IcarusDataTable<FQuestSetup> questsTable, IProviderManager providerManager, Logger logger)
		{
			string? bpPath = questSetup.Class.GetAssetPath(true);
			if (bpPath is null)
			{
				logger.Log(LogLevel.Debug, $"Quest '{questSetup.Name}' has no Class associated with it.");
				return;
			}

			ProcessQuestBlueprint(bpPath, ref quest, questsTable, providerManager, logger);
		}

		private void ProcessQuestBlueprint(string bpPath, ref QuestData quest, IcarusDataTable<FQuestSetup> questsTable, IProviderManager providerManager, Logger logger)
		{
			IFileProvider provider = providerManager.AssetProvider;

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

				if (export.ExportObject.Value is UFunction function)
				{
					DisassembledFunction disassembledFunction = UFunctionDisassembler.Process(package, function);

					foreach (Operation op in disassembledFunction.GetFlattenedOperations())
					{
						if (op.OpCode == EExprToken.FinalFunction)
						{
							Operation<string> ffOp = (Operation<string>)op;
							if (ffOp.Operand.Equals("Quest::RunQuest"))
							{
								if (ffOp.ChildOperations[0].OpCode != EExprToken.StructConst)
								{
									// This means the quest to run is not hardcoded here
									continue;
								}

								Operation<string> nameOp = (Operation<string>)ffOp.ChildOperations[0].ChildOperations[0];
								if (questsTable.TryGetValue(nameOp.Operand, out FQuestSetup questSetup))
								{
									QuestData subQuest = QuestData.Create(questSetup);
									ProcessQuest(ref subQuest, questSetup, questsTable, providerManager, logger);
									quest.SubQuests.Add(subQuest);
								}
							}
							else if (ffOp.Operand.Equals("PrebuiltStructure::BuildStructure"))
							{
								ProcessPrebuiltStructureOperation(ffOp, ref quest, providerManager, logger);
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
									ProcessPrebuiltStructureOperation(ffOp, ref quest, providerManager, logger);
								}
							}
						}
					}
				}
			}

			provider.ReadScriptData = wasReadScriptData;
		}

		private void ProcessPrebuiltStructureOperation(Operation<string> op, ref QuestData quest, IProviderManager providerManager, Logger logger)
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

		private void ExportQuestList(IReadOnlyList<QuestData> quests, IProviderManager providerManager, Config config, Logger logger)
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
					if (quest.DisplayName is not null)
					{
						name = LocalizationUtil.GetLocalizedString(providerManager.AssetProvider, quest.DisplayName);
					}

					writer.WriteLine($"{quest.Name},{name},{string.Join('|', prebuilts)}");
				}
			}
		}
	}

	internal struct QuestData
	{
		public string? Name;
		public string? DisplayName;
		public List<QuestData> SubQuests;

		public List<string> PrebuiltStructures;

		public QuestData()
		{
			SubQuests = new();
			PrebuiltStructures = new();
		}

		public QuestData(string name, string displayName)
			: this()
		{
			Name = name;
			DisplayName = displayName;
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

		public override string? ToString()
		{
			return Name;
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
