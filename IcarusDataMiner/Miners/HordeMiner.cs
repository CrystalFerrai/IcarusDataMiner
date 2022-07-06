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
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts information about horde spawns and rewards
	/// </summary>
	internal class HordeMiner : IDataMiner
	{
		// Notes
		// BP_EnzymeGeyser is the geyser actor. Its base class, EnzymeGeyser, is in c++ where presumably the completion count is stored.
		// BP_Vapour_Condenser is the condenser actor which attaches to a geyser and handles giving out rewards.
		// BPQC_HordeMode is a component attached to a condenser which manages hordes.
		// BP_HordeSpawner is an actor spawned by BPQC_HordeMode to manage spawning horde creatures.

		// For the sake of performance, Json reading functions in this class use a forward-only stream reader rather than
		// fully loading the Json and using random access.

		// The number of completions to calculate and export in the rewards tables
		private const int NumRewardCompletions = 30;

		public string Name => "Hordes";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportHordeData(providerManager, config, logger);
			return true;
		}

		private IReadOnlyList<HordeData> GetHordes(IProviderManager providerManager, Logger logger)
		{
			IReadOnlyDictionary<string, HordeWaveData> waves = GetHordeWaves(providerManager, logger);

			GameFile file = providerManager.DataProvider.Files["Horde/D_Horde.json"];

			List<HordeData> hordes = new();
			HashSet<string> rewardRows = new HashSet<string>();

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				HordeParseState state = HordeParseState.SearchingForRows;
				int objectDepth = 0, wavesDepth = 0, rewardsDepth = 0, inertRewardsDepth = 0;
				HordeData? currentHorde = null;

				while (state != HordeParseState.Done && reader.Read())
				{
					switch (state)
					{
						case HordeParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = HordeParseState.InRows;
							break;
						case HordeParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = HordeParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = HordeParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case HordeParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								switch (reader.Value!)
								{
									case "Name":
										if (currentHorde != null) currentHorde.Name = reader.ReadAsString()!;
										else currentHorde = new HordeData(reader.ReadAsString()!);
										hordes.Add(currentHorde);
										break;
									case "Waves":
										state = HordeParseState.InWaveArray;
										wavesDepth = reader.Depth + 1;
										reader.Read();
										break;
									case "CompletionsBeforeInert":
										currentHorde!.CompletionsBeforeInert = reader.ReadAsInt32()!.Value;
										break;
									case "ItemReward":
										state = HordeParseState.InRewards;
										rewardsDepth = reader.Depth + 1;
										reader.Read();
										break;
									case "InertItemReward":
										state = HordeParseState.InInertRewards;
										inertRewardsDepth = reader.Depth + 1;
										reader.Read();
										break;
								}
							}
							if (reader.Depth < objectDepth)
							{
								state = HordeParseState.InRows;
								currentHorde = null;
							}
							break;
						case HordeParseState.InWaveArray:
							if (reader.TokenType == JsonToken.StartObject)
							{
								string waveName = ReadRowName(reader)!;
								currentHorde!.Waves.Add(waves[waveName]);
							}
							if (reader.Depth < wavesDepth)
							{
								state = HordeParseState.InObject;
							}
							break;
						case HordeParseState.InRewards:
							if (reader.TokenType == JsonToken.StartObject)
							{
								string rewardName = ReadRowName(reader)!;
								currentHorde!.AddRewardRow(rewardName);
								rewardRows.Add(rewardName);
							}
							if (reader.Depth < rewardsDepth)
							{
								state = HordeParseState.InObject;
							}
							break;
						case HordeParseState.InInertRewards:
							if (reader.TokenType == JsonToken.StartObject)
							{
								string rewardName = ReadRowName(reader)!;
								currentHorde!.AddInertRewardRow(rewardName);
								rewardRows.Add(rewardName);
							}
							if (reader.Depth < rewardsDepth)
							{
								state = HordeParseState.InObject;
							}
							break;
					}
				}
			}

			IReadOnlyDictionary<string, RewardData> rewardData = GetRewardData(rewardRows, providerManager, logger);
			ResolveItemNames(rewardData.Values, providerManager, logger);

			foreach (HordeData horde in hordes)
			{
				horde.ResolveRewards(rewardData);
			}

			return hordes;
		}

		private enum HordeParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InWaveArray,
			InRewards,
			InInertRewards,
			Done
		}

		private IReadOnlyDictionary<string, HordeWaveData> GetHordeWaves(IProviderManager providerManager, Logger logger)
		{
			GameFile file = providerManager.DataProvider.Files["Horde/D_HordeWave.json"];

			Dictionary<string, HordeWaveData> waves = new();

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				HordeWaveData? currentWave = null;
				HordeCreatureData? currentCreature = null;

				HordeWaveParseState state = HordeWaveParseState.SearchingForRows;
				int objectDepth = 0, creatureArrayDepth = 0, creatureDepth = 0;

				while (state != HordeWaveParseState.Done && reader.Read())
				{
					switch (state)
					{
						case HordeWaveParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = HordeWaveParseState.InRows;
							break;
						case HordeWaveParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = HordeWaveParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = HordeWaveParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case HordeWaveParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									if (currentWave != null) currentWave.Name = reader.ReadAsString()!;
									else currentWave = new HordeWaveData(reader.ReadAsString()!); // Encountered name before creatures

									waves.Add(currentWave!.Name, currentWave);
								}
								else if (reader.Value!.Equals("Creatures"))
								{
									if (currentWave == null) currentWave = new HordeWaveData("pending"); // Encountered creatures before name

									state = HordeWaveParseState.InCreatureArray;
									creatureArrayDepth = reader.Depth + 1;
									reader.Read();
								}
							}
							if (reader.Depth < objectDepth)
							{
								state = HordeWaveParseState.InRows;
								currentWave = null;
							}
							break;
						case HordeWaveParseState.InCreatureArray:
							if (reader.TokenType == JsonToken.StartObject)
							{
								state = HordeWaveParseState.InCreature;
								creatureDepth = reader.Depth + 1;
								currentCreature = new HordeCreatureData();
								currentWave!.Creatures.Add(currentCreature);
							}
							if (reader.Depth < creatureArrayDepth)
							{
								state = HordeWaveParseState.InObject;
							}
							break;
						case HordeWaveParseState.InCreature:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								switch (reader.Value!)
								{
									case "Creature":
										currentCreature!.Name = ReadRowName(reader);
										break;
									case "LevelOverride":
										currentCreature!.Level = reader.ReadAsInt32()!.Value;
										break;
									case "NumberToSpawnAtATime":
										currentCreature!.SpawnAmount = Range<float>.Parse(reader);
										break;
									case "TotalToSpawn":
										currentCreature!.SpawnTotal = reader.ReadAsInt32()!.Value;
										break;
									case "ExtraSpawnCountPerAdditionalNearbyPlayer":
										currentCreature!.ExtraTotalPerPlayer = reader.ReadAsInt32()!.Value;
										break;
									case "InitialSpawnDelay":
										currentCreature!.InitialDelay = (float)reader.ReadAsDouble()!.Value;
										break;
									case "TimeBetweenSpawns":
										currentCreature!.SpawnInterval = Range<float>.Parse(reader);
										break;
								}
							}
							if (reader.Depth < creatureDepth)
							{
								state = HordeWaveParseState.InCreatureArray;
								currentCreature = null;
							}
							break;
					}
				}
			}

			return waves;
		}

		private enum HordeWaveParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InCreatureArray,
			InCreature,
			Done
		}

		private IReadOnlyDictionary<string, RewardData> GetRewardData(IReadOnlySet<string> rows, IProviderManager providerManager, Logger logger)
		{
			GameFile file = providerManager.DataProvider.Files["Items/D_ItemRewards.json"];

			Dictionary<string, RewardData> rewardData = new Dictionary<string, RewardData>();

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				RewardData? currentReward = null;
				int currentRewardMin = 0, currentRewardMax = 0;

				RewardParseState state = RewardParseState.SearchingForRows;
				int objectDepth = 0, rewardDepth = 0;

				while (state != RewardParseState.Done && reader.Read())
				{
					switch (state)
					{
						case RewardParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = RewardParseState.InRows;
							break;
						case RewardParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = RewardParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = RewardParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case RewardParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									string name = reader.ReadAsString()!;

									if (rows.Contains(name))
									{
										currentReward = new RewardData();
										rewardData.Add(name, currentReward);
									}
									else
									{
										state = RewardParseState.ExitObject;
										reader.Skip();
									}
								}
								else if (currentReward != null && reader.Value!.Equals("Rewards"))
								{
									state = RewardParseState.InRewards;
									rewardDepth = reader.Depth + 1;
									reader.Read();
								}
								else
								{
									state = RewardParseState.ExitObject;
									reader.Skip();
								}
							}
							if (reader.Depth < objectDepth)
							{
								state = RewardParseState.InRows;
								currentReward = null;
							}
							break;
						case RewardParseState.InRewards:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								switch (reader.Value!)
								{
									case "Item":
										{
											currentReward!.Item = ReadRowName(reader);
										}
										break;
									case "MinRandomStackCount":
										currentRewardMin = reader.ReadAsInt32()!.Value;
										break;
									case "MaxRandomStackCount":
										currentRewardMax = reader.ReadAsInt32()!.Value;
										break;
								}
							}
							if (reader.Depth < rewardDepth)
							{
								currentReward!.Amount = new Range<int>(currentRewardMin, currentRewardMax);
								state = RewardParseState.InObject;
							}
							break;
						case RewardParseState.ExitObject:
							if (reader.Depth < objectDepth)
							{
								state = RewardParseState.InRows;
							}
							else
							{
								reader.Skip();
							}
							break;
					}
				}
			}

			return rewardData;
		}

		private enum RewardParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InRewards,
			ExitObject,
			Done
		}

		private void ResolveItemNames(IEnumerable<RewardData> rewards, IProviderManager providerManager, Logger logger)
		{
			Dictionary<string, RewardData> templateToReward = new(rewards
				.Where(r => r.Item != null)
				.Select(r => new KeyValuePair<string, RewardData>(r.Item!, r)));

			Dictionary<string, string> staticToTemplate = new();
			{
				GameFile file = providerManager.DataProvider.Files["Items/D_ItemTemplate.json"];

				using (FArchive archive = file.CreateReader())
				using (StreamReader stream = new StreamReader(archive))
				using (JsonReader reader = new JsonTextReader(stream))
				{
					ItemParseState state = ItemParseState.SearchingForRows;
					int objectDepth = 0;
					string? currentTemplateName = null;

					while (state != ItemParseState.Done && reader.Read())
					{
						switch (state)
						{
							case ItemParseState.SearchingForRows:
								if (reader.TokenType != JsonToken.PropertyName) break;

								if (!reader.Value!.Equals("Rows"))
								{
									reader.Skip();
									break;
								}

								reader.Read();
								state = ItemParseState.InRows;
								break;
							case ItemParseState.InRows:
								if (reader.TokenType == JsonToken.EndArray)
								{
									state = ItemParseState.Done;
								}
								else if (reader.TokenType == JsonToken.StartObject)
								{
									state = ItemParseState.InObject;
									objectDepth = reader.Depth + 1;
								}
								break;
							case ItemParseState.InObject:
								if (reader.TokenType == JsonToken.PropertyName)
								{
									if (reader.Value!.Equals("Name"))
									{
										string? templateName = reader.ReadAsString();
										if (templateName != null && templateToReward.ContainsKey(templateName))
										{
											currentTemplateName = templateName;
											state = ItemParseState.FindProperty;
										}
										else
										{
											reader.Skip();
										}
									}
								}
								if (reader.Depth < objectDepth)
								{
									state = ItemParseState.InRows;
								}
								break;
							case ItemParseState.FindProperty:
								if (reader.TokenType == JsonToken.PropertyName)
								{
									if (reader.Value!.Equals("ItemStaticData"))
									{
										string? staticName = ReadRowName(reader);
										if (staticName != null)
										{
											staticToTemplate.Add(staticName, currentTemplateName!);
										}
									}
								}
								if (reader.Depth < objectDepth)
								{
									state = ItemParseState.InRows;
								}
								break;
						}
					}
				}
			}

			Dictionary<string, string> itemableToStatic = new();
			{
				GameFile file = providerManager.DataProvider.Files["Items/D_ItemsStatic.json"];

				using (FArchive archive = file.CreateReader())
				using (StreamReader stream = new StreamReader(archive))
				using (JsonReader reader = new JsonTextReader(stream))
				{
					ItemParseState state = ItemParseState.SearchingForRows;
					int objectDepth = 0;
					string? currentStaticName = null;

					while (state != ItemParseState.Done && reader.Read())
					{
						switch (state)
						{
							case ItemParseState.SearchingForRows:
								if (reader.TokenType != JsonToken.PropertyName) break;

								if (!reader.Value!.Equals("Rows"))
								{
									reader.Skip();
									break;
								}

								reader.Read();
								state = ItemParseState.InRows;
								break;
							case ItemParseState.InRows:
								if (reader.TokenType == JsonToken.EndArray)
								{
									state = ItemParseState.Done;
								}
								else if (reader.TokenType == JsonToken.StartObject)
								{
									state = ItemParseState.InObject;
									objectDepth = reader.Depth + 1;
								}
								break;
							case ItemParseState.InObject:
								if (reader.TokenType == JsonToken.PropertyName)
								{
									if (reader.Value!.Equals("Name"))
									{
										string? staticName = reader.ReadAsString();
										if (staticName != null && staticToTemplate.ContainsKey(staticName))
										{
											currentStaticName = staticName;
											state = ItemParseState.FindProperty;
										}
										else
										{
											reader.Skip();
										}
									}
								}
								if (reader.Depth < objectDepth)
								{
									state = ItemParseState.InRows;
								}
								break;
							case ItemParseState.FindProperty:
								if (reader.TokenType == JsonToken.PropertyName)
								{
									if (reader.Value!.Equals("Itemable"))
									{
										string? itemableName = ReadRowName(reader);
										if (itemableName != null)
										{
											itemableToStatic.Add(itemableName, currentStaticName!);
										}
									}
								}
								if (reader.Depth < objectDepth)
								{
									state = ItemParseState.InRows;
								}
								break;
						}
					}
				}
			}

			foreach (var pair in itemableToStatic)
			{
				RewardData? target = templateToReward[staticToTemplate[pair.Value]];
#pragma warning disable CS8604 // Possible null reference argument. Null is a valid argument for "defaultValue"
				target!.ItemDisplayName = providerManager.AssetProvider.GetLocalizedString("D_Itemable", $"{pair.Key}-DisplayName", target!.Item);
#pragma warning restore CS8604 // Possible null reference argument.
			}
		}

		private enum ItemParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			FindProperty,
			Done
		}

		private CreatureLevelMultipliers GetCreatureLevelMultipliers(IProviderManager providerManager, Logger logger)
		{
			GameFile file = providerManager.AssetProvider.Files["Icarus/Content/BP/Quests/Components/BP_HordeSpawner.uasset"];
			Package package = (Package)providerManager.AssetProvider.LoadPackage(file);

			providerManager.AssetProvider.ReadScriptData = true;

			IReadOnlyList<float>? difficultyMultipliers = null;

			try
			{
				foreach (FObjectExport? export in package.ExportMap)
				{
					if (export == null) continue;

					if (export.ObjectName.Text.Equals("GetLevelForAI"))
					{
						UFunction function = (UFunction)export.ExportObject.Value;
						DisassembledFunction script = UFunctionDisassembler.Process(package, function);

						Dictionary<string, float> floatVars = new();
						Dictionary<string, float[]> switchResultVars = new();

						foreach (Operation op in script.Script)
						{
							if (op.OpCode != EExprToken.Let) continue;

							if (op.ChildOperations[1].OpCode == EExprToken.FloatConst)
							{
								string? varName = ((Operation<MemberReference?>)op.ChildOperations[0]).Operand?.MemberName;
								if (varName == null) continue;
								floatVars[varName] = ((Operation<float>)op.ChildOperations[1]).Operand;
								continue;
							}

							if (op.ChildOperations[1].OpCode == EExprToken.CallMath)
							{
								Operation<string?> expression = (Operation<string?>)op.ChildOperations[1];
								if (expression.Operand?.Equals("KismetMathLibrary::Multiply_FloatFloat") ?? false &&
									expression.ChildOperations[0].OpCode == EExprToken.LocalVariable &&
									expression.ChildOperations[1].OpCode == EExprToken.SwitchValue)
								{
									string? varName = ((Operation<MemberReference?>)op.ChildOperations[0]).Operand?.MemberName;
									if (varName == null) continue;

									bool casesValid = true;
									SwitchOperand switchOperand = ((Operation<SwitchOperand>)expression.ChildOperations[1]).Operand;
									float[] switchResults = new float[switchOperand.Cases.Count];
									for (int i = 0; i < switchOperand.Cases.Count; ++i)
									{
										Operation<MemberReference?>? resultOp = switchOperand.Cases[i].Result as Operation<MemberReference?>;
										if (resultOp == null || resultOp.Operand == null)
										{
											casesValid = false;
											break;
										}

										float switchResult;
										if (!floatVars.TryGetValue(resultOp.Operand.MemberName!, out switchResult))
										{
											throw new DataMinerException($"Expected to find a local float variable named {resultOp.Operand}");
										}

										switchResults[i] = switchResult;
									}

									if (!casesValid) continue;

									switchResultVars[varName] = switchResults;
								}
								else if (expression.Operand?.Equals("KismetMathLibrary::FCeil") ?? false &&
									expression.ChildOperations[2].OpCode == EExprToken.LocalVariable)
								{
									string varName = ((Operation<MemberReference?>)expression.ChildOperations[0]).Operand!.MemberName!;
									difficultyMultipliers = switchResultVars[varName];
									break;
								}
							}
						}

						if (difficultyMultipliers == null)
						{
							throw new DataMinerException("Unexpected implementation of BP_Vapour_Condenser::GrantRewards");
						}
					}
				}
			}
			finally
			{
				providerManager.AssetProvider.ReadScriptData = false;
			}

			if (difficultyMultipliers == null) throw new DataMinerException("Could not locate function BP_HordeSpawner::GetLevelForAI");

			return new CreatureLevelMultipliers(difficultyMultipliers);
		}

		private RewardMultipliers GetRewardMultipliers(IProviderManager providerManager, Logger logger)
		{
			GameFile file = providerManager.AssetProvider.Files["Icarus/Content/BP/Objects/World/Items/Deployables/Communication/BP_Vapour_Condenser.uasset"];
			Package package = (Package)providerManager.AssetProvider.LoadPackage(file);

			providerManager.AssetProvider.ReadScriptData = true;

			float? completionMultiplier = null;
			IReadOnlyList<float>? difficultyMultipliers = null;

			try
			{
				HashSet<string> functionsToExport = new()
				{
					"GetMultiplierFromCompletions",
					"GrantRewards"
				};
				foreach (FObjectExport? export in package.ExportMap)
				{
					if (export == null) continue;

					if (functionsToExport.Contains(export.ObjectName.Text))
					{
						UFunction function = (UFunction)export.ExportObject.Value;
						DisassembledFunction script = UFunctionDisassembler.Process(package, function);

						switch (export.ObjectName.Text)
						{
							case "GetMultiplierFromCompletions":
								{
									foreach (Operation op in script.Script)
									{
										if (op.OpCode != EExprToken.Let ||
											op.ChildOperations[0].OpCode != EExprToken.LocalVariable ||
											op.ChildOperations[1].OpCode != EExprToken.CallMath)
										{
											continue;
										}

										Operation<string?> expression = (Operation<string?>)op.ChildOperations[1];
										if (!(expression.Operand?.Equals("KismetMathLibrary::Multiply_IntFloat") ?? false) ||
											expression.ChildOperations[0].OpCode != EExprToken.LocalVariable ||
											expression.ChildOperations[1].OpCode != EExprToken.FloatConst)
										{
											continue;
										}

										Operation<MemberReference?> intParam = (Operation<MemberReference?>)expression.ChildOperations[0];
										if (!(intParam.Operand?.MemberName?.Equals("Completions") ?? false))
										{
											continue;
										}

										Operation<float> floatParam = (Operation<float>)expression.ChildOperations[1];
										completionMultiplier = floatParam.Operand;

										break;
									}

									if (!completionMultiplier.HasValue)
									{
										throw new DataMinerException("Unexpected implementation of BP_Vapour_Condenser::GetMultiplierFromCompletions");
									}

									break;
								}
							case "GrantRewards":
								{
									Dictionary<string, float> floatVars = new();
									Dictionary<string, float[]> switchResultVars = new();

									foreach (Operation op in script.Script)
									{
										if (op.OpCode != EExprToken.Let) continue;

										if (op.ChildOperations[1].OpCode == EExprToken.FloatConst)
										{
											string? varName = ((Operation<MemberReference?>)op.ChildOperations[0]).Operand?.MemberName;
											if (varName == null) continue;
											floatVars[varName] = ((Operation<float>)op.ChildOperations[1]).Operand;
											continue;
										}

										if (op.ChildOperations[1].OpCode == EExprToken.CallMath)
										{
											Operation<string?> expression = (Operation<string?>)op.ChildOperations[1];
											if (expression.Operand?.Equals("KismetMathLibrary::Multiply_FloatFloat") ?? false &&
												expression.ChildOperations[0].OpCode == EExprToken.LocalVariable &&
												expression.ChildOperations[1].OpCode == EExprToken.SwitchValue)
											{
												string? varName = ((Operation<MemberReference?>)op.ChildOperations[0]).Operand?.MemberName;
												if (varName == null) continue;

												bool casesValid = true;
												SwitchOperand switchOperand = ((Operation<SwitchOperand>)expression.ChildOperations[1]).Operand;
												float[] switchResults = new float[switchOperand.Cases.Count];
												for (int i = 0; i < switchOperand.Cases.Count; ++i)
												{
													Operation<MemberReference?>? resultOp = switchOperand.Cases[i].Result as Operation<MemberReference?>;
													if (resultOp == null || resultOp.Operand == null)
													{
														casesValid = false;
														break;
													}

													float switchResult;
													if (!floatVars.TryGetValue(resultOp.Operand.MemberName!, out switchResult))
													{
														throw new DataMinerException($"Expected to find a local float variable named {resultOp.Operand}");
													}

													switchResults[i] = switchResult;
												}

												if (!casesValid) continue;

												switchResultVars[varName] = switchResults;
											}
											else if (expression.Operand?.Equals("InventoryItemLibrary::GenerateRewardStackSize") ?? false &&
												expression.ChildOperations[2].OpCode == EExprToken.LocalVariable)
											{
												string varName = ((Operation<MemberReference?>)expression.ChildOperations[2]).Operand!.MemberName!;
												difficultyMultipliers = switchResultVars[varName];
												break;
											}
										}
									}

									if (difficultyMultipliers == null)
									{
										throw new DataMinerException("Unexpected implementation of BP_Vapour_Condenser::GrantRewards");
									}

									break;
								}
						}
					}

					if (completionMultiplier.HasValue && difficultyMultipliers != null) break;
				}
			}
			finally
			{
				providerManager.AssetProvider.ReadScriptData = false;
			}

			if (!completionMultiplier.HasValue) throw new DataMinerException("Could not locate function BP_Vapour_Condenser::GetMultiplierFromCompletions");
			if (difficultyMultipliers == null) throw new DataMinerException("Could not locate function BP_Vapour_Condenser::GrantRewards");

			return new RewardMultipliers(completionMultiplier.Value, difficultyMultipliers);
		}

		private void ExportHordeData(IProviderManager providerManager, Config config, Logger logger)
		{
			IReadOnlyList<HordeData> hordes = GetHordes(providerManager, logger);
			CreatureLevelMultipliers creatureMultipliers = GetCreatureLevelMultipliers(providerManager, logger);
			RewardMultipliers rewardMultipliers = GetRewardMultipliers(providerManager, logger);

			Func<HordeData, string> getDisplayName = new(horde =>
				horde.Name.StartsWith("Horde_")	? horde.Name["Horde_".Length..]
				: horde.Name.EndsWith("_Horde")	? horde.Name[..(horde.Name.Length - "_Horde".Length)]
				: horde.Name);

			for (ProspectDifficulty difficulty = ProspectDifficulty.Easy; difficulty < ProspectDifficulty.Count; ++difficulty)
			{
				int difficultyIndex = (int)difficulty;

				string outputPath = Path.Combine(config.OutputDirectory, $"{Name}_Waves_{difficulty}.csv");

				using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					writer.WriteLine("Horde,Wave,Creatures,Level,Total Count,Extra,Initial Delay,Spawn Amount,Spawn Interval");

					foreach(HordeData horde in hordes)
					{
						for (int waveNumber = 0; waveNumber < horde.Waves.Count; ++waveNumber)
						{
							HordeWaveData wave = horde.Waves[waveNumber];
							foreach (HordeCreatureData creature in wave.Creatures)
							{
								// Note: Creature level is rounded up after applying multiplier (see FCeil call in BP_HordeSpawner::GetLevelForAI)
								int level = (int)Math.Ceiling(creature.Level * creatureMultipliers.DifficultyMultipliers[difficultyIndex]);

								writer.WriteLine($"{getDisplayName(horde)},{waveNumber + 1},{creature.Name},{level},{creature.SpawnTotal},{creature.ExtraTotalPerPlayer},{creature.InitialDelay},{creature.SpawnAmount.ToExcelSafeString()},{creature.SpawnInterval.ToExcelSafeString()}");//,{horde.CompletionsBeforeInert}");
							}
						}
					}
				}

				outputPath = Path.Combine(config.OutputDirectory, $"{Name}_Rewards_{difficulty}.csv");

				using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
				using (StreamWriter writer = new StreamWriter(outStream))
				{
					string?[] rewardNames = hordes.SelectMany(h => h.Rewards).Select(r => r.ItemDisplayName).Distinct().ToArray();
					string?[] inertRewardNames = hordes.SelectMany(h => h.InertRewards).Select(r => r.ItemDisplayName).Distinct().ToArray();
					IList<RewardData> noRewards = Array.Empty<RewardData>();

					writer.WriteLine($"Horde,Completions,{string.Join(',', rewardNames)},{string.Join(',', inertRewardNames)}");

					foreach (HordeData horde in hordes)
					{
						for (int completions = 0; completions < NumRewardCompletions; ++completions)
						{
							writer.Write($"{getDisplayName(horde)},{completions + 1}");

							float multiplier = (1.0f + completions * rewardMultipliers.CompletionMultiplier) * rewardMultipliers.DifficultyMultipliers[difficultyIndex];

							Action<string?[], IList<RewardData>> writeRewards = new((names, data) =>
							{
								for (int rewardIndex = 0; rewardIndex < names.Length; ++rewardIndex)
								{
									RewardData? reward = data.FirstOrDefault(r => r.ItemDisplayName == names[rewardIndex]);
									if (reward == null)
									{
										writer.Write(",");
										continue;
									}

									Range<int> amount = reward.Amount * multiplier;
									writer.Write($",{amount.ToExcelSafeString()}");
								}
							});

							if (completions < horde.CompletionsBeforeInert)
							{
								writeRewards(rewardNames, horde.Rewards);
							}
							else
							{
								writeRewards(rewardNames, noRewards);
							}

							// Always print inert rewards since outposts award them starting from the first round
							writeRewards(inertRewardNames, horde.InertRewards);

							writer.WriteLine();
						}
					}
				}
			}
		}

		private static string? ReadRowName(JsonReader reader)
		{
			string? rowName = null;

			while(reader.Read())
			{
				if (reader.TokenType == JsonToken.PropertyName)
				{
					if (reader.Value!.Equals("RowName"))
					{
						rowName = reader.ReadAsString();
					}
				}
				else if (reader.TokenType == JsonToken.EndObject)
				{
					return rowName;
				}
			}

			throw new FormatException("Unexpected end of reader before end ofobject");
		}

		private class HordeData
		{
			private readonly List<string> mRewardRowNames;

			private readonly List<string> mInertRewardRowNames;

			public string Name { get; set; }

			public IList<HordeWaveData> Waves { get; }

			public int CompletionsBeforeInert { get; set; }

			public IList<RewardData> Rewards { get; }

			public IList<RewardData> InertRewards { get; }

			public HordeData(string name)
			{
				mRewardRowNames = new List<string>();
				mInertRewardRowNames = new List<string>();

				Name = name;
				Waves = new List<HordeWaveData>();
				Rewards = new List<RewardData>();
				InertRewards = new List<RewardData>();
			}

			public void AddRewardRow(string rowName)
			{
				mRewardRowNames.Add(rowName);
			}

			public void AddInertRewardRow(string rowName)
			{
				mInertRewardRowNames.Add(rowName);
			}

			public void ResolveRewards(IReadOnlyDictionary<string, RewardData> rewardMap)
			{
				foreach (string rowName in mRewardRowNames)
				{
					Rewards.Add(rewardMap[rowName]);
				}
				foreach (string rowName in mInertRewardRowNames)
				{
					InertRewards.Add(rewardMap[rowName]);
				}
			}

			public override string ToString()
			{
				return Name;
			}
		}

		private class HordeWaveData
		{
			public string Name { get; set; }

			public IList<HordeCreatureData> Creatures { get; }

			public HordeWaveData(string name)
			{
				Name = name;
				Creatures = new List<HordeCreatureData>();
			}

			public override string ToString()
			{
				return Name;
			}
		}

		private class HordeCreatureData
		{
			public string? Name { get; set; }

			public int Level { get; set; }

			public Range<float> SpawnAmount { get; set; }

			public int SpawnTotal { get; set; }

			public int ExtraTotalPerPlayer { get; set; }

			public float InitialDelay { get; set; }

			public Range<float> SpawnInterval { get; set; }

			public HordeCreatureData()
			{
				// Defaults copied from BP_HordeSpawner. Should probably auto-mine these values at some point.
				Level = 0;
				SpawnAmount = new Range<float>(3.0f, 5.0f);
				SpawnTotal = 15;
				ExtraTotalPerPlayer = 5;
				InitialDelay = 5.0f;
				SpawnInterval = new Range<float>(20.0f, 25.0f);
			}

			public override string ToString()
			{
				return $"{SpawnTotal} Level {Level} {Name}";
			}
		}

		private class CreatureLevelMultipliers
		{
			public IReadOnlyList<float> DifficultyMultipliers { get; }

			public CreatureLevelMultipliers(IReadOnlyList<float> difficultyMultipliers)
			{
				DifficultyMultipliers = difficultyMultipliers;
			}
		}

		private class RewardData
		{
			public string? Item { get; set; }

			public Range<int> Amount { get; set; }

			public string? ItemDisplayName
			{
				get => _itemDisplayName ?? Item;
				set => _itemDisplayName = value;
			}
			private string? _itemDisplayName;

			public override string ToString()
			{
				return $"{Amount} {ItemDisplayName}";
			}
		}

		private class RewardMultipliers
		{
			public float CompletionMultiplier { get; }

			public IReadOnlyList<float> DifficultyMultipliers { get; }

			public RewardMultipliers(float completionMultiplier, IReadOnlyList<float> difficultyMultipliers)
			{
				CompletionMultiplier = completionMultiplier;
				DifficultyMultipliers = difficultyMultipliers;
			}
		}

		private enum ProspectDifficulty
		{
			None,

			Easy,
			Normal,
			Hard,
			Extreme,

			Count
		}

		private struct Range<T> : IEquatable<Range<T>>
			where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>
		{
			public T Min { get; }

			public T Max { get; }

			public Range(T min, T max)
			{
				Min = min;
				Max = max;
			}

			public static Range<T> Parse(JsonReader reader)
			{
				double min = 0.0, max = 0.0;
				while (reader.Read())
				{
					if (reader.TokenType == JsonToken.PropertyName)
					{
						if (reader.Value!.Equals("X"))
						{
							min = reader.ReadAsDouble()!.Value;
						}
						else if (reader.Value!.Equals("Y"))
						{
							max = reader.ReadAsDouble()!.Value;
						}
					}
					else if (reader.TokenType == JsonToken.EndObject)
					{
						return new Range<T>((T)Convert.ChangeType(min, typeof(T)), (T)Convert.ChangeType(max, typeof(T)));
					}
				}

				throw new FormatException("Unexpected end of reader before end ofobject");
			}

			public static Range<T> operator *(Range<T> a, double b)
			{
				IFormatProvider format = CultureInfo.InvariantCulture;
				double aMin = (double)Convert.ChangeType(a.Min, typeof(double), format);
				double aMax = (double)Convert.ChangeType(a.Max, typeof(double), format);

				double rMin = aMin * b;
				double rMax = aMax * b;

				Type tType = typeof(T);
				if (tType == typeof(sbyte) ||
					tType == typeof(byte) ||
					tType == typeof(short) ||
					tType == typeof(ushort) ||
					tType == typeof(char) ||
					tType == typeof(int) ||
					tType == typeof(uint) ||
					tType == typeof(long) ||
					tType == typeof(ulong))
				{
					// ChangeType rounds integer types which is not the behavior we want, so truncate first
					rMin = Math.Truncate(rMin);
					rMax = Math.Truncate(rMax);
				}

				return new Range<T>((T)Convert.ChangeType(rMin, typeof(T), format), (T)Convert.ChangeType(rMax, typeof(T), format));
			}

			public override int GetHashCode()
			{
				int hash = 17;
				hash = hash * 23 + Min.GetHashCode();
				hash = hash * 23 + Max.GetHashCode();
				return hash;
			}

			public override bool Equals([NotNullWhen(true)] object? obj)
			{
				return obj is Range<T> other && Equals(other);
			}

			public bool Equals(Range<T> other)
			{
				return Min.Equals(other.Min) && Max.Equals(other.Max);
			}

			public override string ToString()
			{
				return Min.Equals(Max) ? Min.ToString()! : $"{Min}-{Max}";
			}

			public string ToExcelSafeString()
			{
				return $"\"=\"\"{ToString()}\"\"\"";
			}
		}
	}
}
