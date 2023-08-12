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

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;

namespace IcarusDataMiner
{
	/// <summary>
	/// Utlity for obtaining information about prospects
	/// </summary>
	internal class ProspectDataUtil
	{
		private IReadOnlyDictionary<string, string>? mActiveProspects;

		private IReadOnlyDictionary<string, IList<ProspectData>>? mProspectsByTier;

		/// <summary>
		/// Gets a map of names to talent names for all prospects which are currently playable in the game
		/// </summary>
		/// <remarks>
		/// Keys are all forced lower case to allow fast case-insensitive lookups.
		/// </remarks>
		public IReadOnlyDictionary<string, string> ActiveProspects => mActiveProspects ?? throw new InvalidOperationException("Util not initialized");

		/// <summary>
		/// Gets a map of prospect tiers to prospect data
		/// </summary>
		public IReadOnlyDictionary<string, IList<ProspectData>> ProspectsByTier => mProspectsByTier ?? throw new InvalidOperationException("Util not initialized");

		private ProspectDataUtil()
		{
		}

		public static ProspectDataUtil Create(IFileProvider dataProvider, Logger logger)
		{
			ProspectDataUtil instance = new();
			instance.Initialize(dataProvider, logger);
			return instance;
		}

		private void Initialize(IFileProvider dataProvider, Logger logger)
		{
			mActiveProspects = GetActiveProspects(dataProvider);
			mProspectsByTier = LoadProspects(mActiveProspects, dataProvider, logger);
		}

		private static IReadOnlyDictionary<string, string> GetActiveProspects(IFileProvider dataProvider)
		{
			// For the sake of performance, this function uses a forward-only stream reader rather than
			// fully loading the Json and using random access.

			HashSet<string> treeNames = new HashSet<string>()
			{
				"Prospect_Olympus",
				"Prospect_Styx"
			};

			Dictionary<string, string> activeProspects = new();

			GameFile file = dataProvider.Files["Talents/D_Talents.json"];
			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				TalentParseState state = TalentParseState.SearchingForRows;
				int objectDepth = 0;
				string? prospectName = null, treeName = null, talentName = null;

				while (state != TalentParseState.Done && reader.Read())
				{
					if (prospectName != null && treeName != null && talentName != null)
					{
						if (treeNames.Contains(treeName))
						{
							activeProspects.Add(prospectName.ToLowerInvariant(), talentName);
						}
						prospectName = null;
						treeName = null;
						state = TalentParseState.ExitObject;
					}

					switch (state)
					{
						case TalentParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = TalentParseState.InRows;
							break;
						case TalentParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = TalentParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = TalentParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case TalentParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									talentName = reader.ReadAsString();
								}
								else if (reader.Value!.Equals("ExtraData"))
								{
									state = TalentParseState.InExtraData;
								}
								else if (reader.Value!.Equals("TalentTree"))
								{
									state = TalentParseState.InTalentTree;
								}
								else
								{
									reader.Skip();
								}
							}
							if (reader.Depth < objectDepth)
							{
								prospectName = null;
								treeName = null;
								state = TalentParseState.InRows;
							}
							break;
						case TalentParseState.InExtraData:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									prospectName = reader.ReadAsString();
								}
							}
							if (reader.TokenType == JsonToken.EndObject)
							{
								state = TalentParseState.InObject;
							}
							break;
						case TalentParseState.InTalentTree:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									treeName = reader.ReadAsString();
								}
							}
							if (reader.TokenType == JsonToken.EndObject)
							{
								state = TalentParseState.InObject;
							}
							break;
						case TalentParseState.ExitObject:
							if (reader.Depth < objectDepth)
							{
								state = TalentParseState.InRows;
							}
							else
							{
								reader.Skip();
							}
							break;
					}
				}
			}

			return activeProspects;
		}

		private enum TalentParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InExtraData,
			InTalentTree,
			ExitObject,
			Done
		}

		private static IReadOnlyDictionary<string, IList<ProspectData>> LoadProspects(IReadOnlyDictionary<string, string> activeProspects, IFileProvider dataProvider, Logger logger)
		{
			// For the sake of performance, this function uses a forward-only stream reader rather than
			// fully loading the Jsona nd using random access.

			GameFile file = dataProvider.Files["Prospects/D_ProspectList.json"];

			Dictionary<string, IList<ProspectData>> prospectMap = new();

			string defaultForecast = string.Empty;

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				ProspectData prospectData = new();

				ProspectParseState state = ProspectParseState.SearchingForDefaults;
				int outerDepth = 0, objectDepth = 0, difficultyArrayDepth = 0;
				int currentDifficulty = 0;

				while (state != ProspectParseState.Done && reader.Read())
				{
					switch (state)
					{
						case ProspectParseState.SearchingForDefaults:

							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Defaults"))
							{
								reader.Skip();
								break;
							}

							outerDepth = reader.Depth;

							reader.Read();
							state = ProspectParseState.InDefaults;
							break;
						case ProspectParseState.InDefaults:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Forecast"))
								{
									state = ProspectParseState.InDefaultForecast;
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == outerDepth)
							{
								state = ProspectParseState.SearchingForRows;
							}
							break;
						case ProspectParseState.InDefaultForecast:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									defaultForecast = reader.ReadAsString() ?? string.Empty;
								}
							}
							if (reader.TokenType == JsonToken.EndObject)
							{
								state = ProspectParseState.InDefaults;
							}
							break;
						case ProspectParseState.SearchingForRows:
							if (reader.TokenType != JsonToken.PropertyName) break;

							if (!reader.Value!.Equals("Rows"))
							{
								reader.Skip();
								break;
							}

							reader.Read();
							state = ProspectParseState.InRows;
							break;
						case ProspectParseState.InRows:
							if (reader.TokenType == JsonToken.EndArray)
							{
								state = ProspectParseState.Done;
							}
							else if (reader.TokenType == JsonToken.StartObject)
							{
								state = ProspectParseState.InObject;
								objectDepth = reader.Depth + 1;
							}
							break;
						case ProspectParseState.InObject:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Name"))
								{
									prospectData.ID = reader.ReadAsString();

									if (activeProspects.TryGetValue(prospectData.ID!.ToLowerInvariant(), out string? talentName))
									{
										prospectData.Talent = talentName;
									}
									else
									{
										prospectData = new ProspectData();
										state = ProspectParseState.ExitObject;
									}
								}
								else if (reader.Value!.Equals("DropName"))
								{
									prospectData.Name = LocalizationUtil.GetLocalizedString(dataProvider, reader.ReadAsString()!);
								}
								else if (reader.Value!.Equals("TimeDuration"))
								{
									state = ProspectParseState.InDuration;
								}
								else if (reader.Value!.Equals("MetaDepositSpawns"))
								{
									state = ProspectParseState.InMetaDeposits;
								}
								else if (reader.Value!.Equals("NumMetaSpawnsMin"))
								{
									prospectData.NodeCountMin = reader.ReadAsInt32()!.Value;
								}
								else if (reader.Value!.Equals("NumMetaSpawnsMax"))
								{
									prospectData.NodeCountMax = reader.ReadAsInt32()!.Value;
								}
								else if (reader.Value!.Equals("Forecast"))
								{
									state = ProspectParseState.InForecast;
								}
								else if (reader.Value!.Equals("AISpawnConfigOverride"))
								{
									difficultyArrayDepth = reader.Depth + 1;
									state = ProspectParseState.InSpawnConfigOverride;
								}
								else if (reader.Value!.Equals("DifficultySetup"))
								{
									difficultyArrayDepth = reader.Depth + 1;
									state = ProspectParseState.InDifficultyArray;
								}
								else
								{
									reader.Skip();
								}
							}
							if (reader.Depth < objectDepth)
							{
								if (prospectData.IsValid())
								{
									string tier = prospectData.ID![..prospectData.ID!.IndexOf('_')];

									if (string.IsNullOrEmpty(prospectData.Forecast) || prospectData.Forecast.Equals("None"))
									{
										prospectData.Forecast = defaultForecast;
									}

									IList<ProspectData>? list;
									if (!prospectMap.TryGetValue(tier, out list))
									{
										list = new List<ProspectData>();
										prospectMap.Add(tier, list);
									}
									list!.Add(prospectData);
								}
								prospectData = new ProspectData();
								state = ProspectParseState.InRows;
							}
							break;
						case ProspectParseState.InDuration:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								string? unit = null;
								if (reader.Value!.Equals("Days"))
								{
									unit = "Days";
								}
								else if (reader.Value!.Equals("Hours"))
								{
									unit = "Hours";
								}
								else if (reader.Value!.Equals("Mins"))
								{
									unit = "Minutes";
								}
								else if (reader.Value!.Equals("Seconds"))
								{
									unit = "Seconds";
								}
								if (unit != null)
								{
									reader.Read();

									int depth = reader.Depth;
									int? durationMin = null, durationMax = null;
									while (reader.Read() && reader.Depth > depth)
									{
										if (reader.TokenType != JsonToken.PropertyName) continue;

										if (reader.Value!.Equals("Min"))
										{
											durationMin = reader.ReadAsInt32();
										}
										else if (reader.Value!.Equals("Max"))
										{
											durationMax = reader.ReadAsInt32();
										}

										if (durationMin.HasValue && durationMax.HasValue)
										{
											if (durationMin == durationMax)
											{
												prospectData.Duration = $"{durationMin.Value} {unit}";
											}
											else
											{
												prospectData.Duration = $"{durationMin.Value}-{durationMax.Value} {unit}";
											}
										}
									}
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == objectDepth)
							{
								state = ProspectParseState.InObject;
							}
							break;
						case ProspectParseState.InMetaDeposits:
							if (reader.TokenType == JsonToken.StartObject)
							{
								while (reader.Read() && reader.Depth > objectDepth + 1)
								{
									if (reader.TokenType == JsonToken.PropertyName)
									{
										if (reader.Value!.Equals("SpawnLocation"))
										{
											reader.Read();
											reader.Read();
											if (reader.TokenType == JsonToken.PropertyName && reader.Value!.Equals("Value"))
											{
												string nodeId = reader.ReadAsString()!;
												nodeId = nodeId[(nodeId.IndexOf('_') + 1)..];
												prospectData.Nodes.Add(nodeId);
											}
										}
										else if (reader.Value!.Equals("MinMetaAmount"))
										{
											int amount = reader.ReadAsInt32()!.Value;
											if (amount < prospectData.NodeAmountMin) prospectData.NodeAmountMin = amount;
										}
										else if (reader.Value!.Equals("MaxMetaAmount"))
										{
											int amount = reader.ReadAsInt32()!.Value;
											if (amount > prospectData.NodeAmountMax) prospectData.NodeAmountMax = amount;
										}
									}
								}
							}
							if (reader.TokenType == JsonToken.EndArray && reader.Depth == objectDepth)
							{
								state = ProspectParseState.InObject;
							}
							break;
						case ProspectParseState.InForecast:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									prospectData.Forecast = reader.ReadAsString();
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == objectDepth)
							{
								state = ProspectParseState.InObject;
							}
							break;
						case ProspectParseState.InSpawnConfigOverride:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									prospectData.AISpawnConfigOverride = reader.ReadAsString();
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == objectDepth)
							{
								state = ProspectParseState.InObject;
							}
							break;
						case ProspectParseState.InDifficultyArray:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Easy"))
								{
									currentDifficulty = 0;
									state = ProspectParseState.InDifficulty;
								}
								else if (reader.Value!.Equals("Medium"))
								{
									currentDifficulty = 1;
									state = ProspectParseState.InDifficulty;
								}
								else if (reader.Value!.Equals("Hard"))
								{
									currentDifficulty = 2;
									state = ProspectParseState.InDifficulty;
								}
								else if (reader.Value!.Equals("Extreme"))
								{
									currentDifficulty = 3;
									state = ProspectParseState.InDifficulty;
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == objectDepth)
							{
								state = ProspectParseState.InObject;
							}
							break;
						case ProspectParseState.InDifficulty:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("Forecast"))
								{
									state = ProspectParseState.InDifficultyForecast;
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == difficultyArrayDepth)
							{
								state = ProspectParseState.InDifficultyArray;
							}
							break;
						case ProspectParseState.InDifficultyForecast:
							if (reader.TokenType == JsonToken.PropertyName)
							{
								if (reader.Value!.Equals("RowName"))
								{
									prospectData.ForecastOverrides[currentDifficulty] = reader.ReadAsString();
								}
							}
							if (reader.TokenType == JsonToken.EndObject && reader.Depth == difficultyArrayDepth + 1)
							{
								state = ProspectParseState.InDifficulty;
							}
							break;
						case ProspectParseState.ExitObject:
							if (reader.Depth < objectDepth)
							{
								prospectData = new ProspectData();
								state = ProspectParseState.InRows;
							}
							else
							{
								reader.Skip();
							}
							break;
					}
				}
			}

			return prospectMap;
		}

		private enum ProspectParseState
		{
			SearchingForDefaults,
			InDefaults,
			InDefaultForecast,
			SearchingForRows,
			InRows,
			InObject,
			InDuration,
			InMetaDeposits,
			InForecast,
			InSpawnConfigOverride,
			InDifficultyArray,
			InDifficulty,
			InDifficultyForecast,
			ExitObject,
			Done
		}

	}

	internal class ProspectData
	{
		public string? ID { get; set; }

		public string? Talent { get; set; }

		public string? Name { get; set; }

		public string? Duration { get; set; }

		public List<string> Nodes { get; }

		public int NodeCountMin { get; set; }

		public int NodeCountMax { get; set; }

		public int NodeAmountMin { get; set; }

		public int NodeAmountMax { get; set; }

		public string? Forecast { get; set; }

		public string?[] ForecastOverrides { get; }

		public string? AISpawnConfigOverride { get; set; }

		public ProspectData()
		{
			Nodes = new List<string>();
			ForecastOverrides = new string?[4];
			ID = null;
			Name = null;
			Talent = null;
			Duration = null;
			Nodes.Clear();
			NodeCountMin = 1;
			NodeCountMax = 1;
			NodeAmountMin = int.MaxValue;
			NodeAmountMax = int.MinValue;
			Forecast = null;
			for (int i = 0; i < ForecastOverrides.Length; ++i)
			{
				ForecastOverrides[i] = null;
			}
		}

		public bool IsValid()
		{
			return ID != null && Name != null && Duration != null;
		}
	}
}
