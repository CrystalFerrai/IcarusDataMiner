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
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Extracts information about prospects
	/// </summary>
	internal class ProspectsMiner : IDataMiner
	{
		public string Name => "Prospects";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ISet<string> activeProspects = GetActiveProspects(providerManager);
			ExportProspectList(providerManager, activeProspects, config, logger);
			return true;
		}

		private ISet<string> GetActiveProspects(IProviderManager providerManager)
		{
			// For the sake of performance, this function uses a forward-only stream reader rather than
			// fully loading the Json and using random access.

			HashSet<string> treeNames = new HashSet<string>()
			{
				"Prospect_Olympus",
				"Prospect_Styx"
			};

			HashSet<string> activeProspects = new HashSet<string>();

			GameFile file = providerManager.DataProvider.Files["Talents/D_Talents.json"];
			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				TalentParseState state = TalentParseState.SearchingForRows;
				int objectDepth = 0;
				string? prospectName = null, treeName = null;

				while (state != TalentParseState.Done && reader.Read())
				{
					if (prospectName != null && treeName != null)
					{
						if (treeNames.Contains(treeName))
						{
							activeProspects.Add(prospectName.ToLowerInvariant());
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
								if (reader.Value!.Equals("ExtraData"))
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

		private void ExportProspectList(IProviderManager providerManager, ISet<string> activeProspects, Config config, Logger logger)
		{
			// For the sake of performance, this function uses a forward-only stream reader rather than
			// fully loading the Jsona nd using random access.

			string outputPath = Path.Combine(config.OutputDirectory, $"{Name}.csv");

			GameFile file = providerManager.DataProvider.Files["Prospects/D_ProspectList.json"];

			Dictionary<string, List<string>> prospectMap = new Dictionary<string, List<string>>();

			using (FArchive archive = file.CreateReader())
			using (StreamReader stream = new StreamReader(archive))
			using (JsonReader reader = new JsonTextReader(stream))
			{
				ProspectData prospectData = new();

				ProspectParseState state = ProspectParseState.SearchingForRows;
				int objectDepth = 0;

				while (state != ProspectParseState.Done && reader.Read())
				{
					switch (state)
					{
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

									if (!activeProspects.Contains(prospectData.ID!.ToLowerInvariant()))
									{
										prospectData.Reset();
										state = ProspectParseState.ExitObject;
									}
								}
								else if (reader.Value!.Equals("DropName"))
								{
									prospectData.Name = LocalizationUtil.GetLocalizedString(providerManager.DataProvider, reader.ReadAsString()!);
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

									List<string>? list;
									if (!prospectMap.TryGetValue(tier, out list))
									{
										list = new List<string>();
										prospectMap.Add(tier, list);
									}
									list!.Add(prospectData.Serialize());
								}
								prospectData.Reset();
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
								if(unit != null)
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
						case ProspectParseState.ExitObject:
							if (reader.Depth < objectDepth)
							{
								prospectData.Reset();
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

			List<string> tiers = new List<string>(prospectMap.Keys);
			tiers.Sort();

			using (FileStream outStream = IOUtil.CreateFile(outputPath, logger))
			using (StreamWriter writer = new StreamWriter(outStream))
			{
				writer.WriteLine("ID,Name,Duration,NodeCount,NodeIds,NodeAmount");

				foreach (string tier in tiers)
				{
					foreach (string output in prospectMap[tier])
					{
						writer.WriteLine(output);
					}
				}
			}
		}

		private enum ProspectParseState
		{
			SearchingForRows,
			InRows,
			InObject,
			InDuration,
			InMetaDeposits,
			ExitObject,
			Done
		}

		private class ProspectData
		{
			public string? ID { get; set; }

			public string? Name { get; set; }

			public string? Duration { get; set; }

			public List<string> Nodes { get; }

			public int NodeCountMin { get; set; }

			public int NodeCountMax { get; set; }

			public int NodeAmountMin { get; set; }

			public int NodeAmountMax { get; set; }

			public ProspectData()
			{
				Nodes = new List<string>();
				Reset();
			}

			public void Reset()
			{
				ID = null;
				Name = null;
				Duration = null;
				Nodes.Clear();
				NodeCountMin = 1;
				NodeCountMax = 1;
				NodeAmountMin = int.MaxValue;
				NodeAmountMax = int.MinValue;
			}

			public bool IsValid()
			{
				return ID != null && Name != null && Duration != null;
			}

			public string Serialize()
			{
				int countMin = Math.Min(NodeCountMin, Nodes.Count);
				int countMax = Math.Min(NodeCountMax, Nodes.Count);
				int amountMin = NodeAmountMin == int.MaxValue ? 0 : NodeAmountMin;
				int amountMax = NodeAmountMax == int.MinValue ? 0 : NodeAmountMax;

				// Weird format so that Excel won't interpret the field as a date
				string nodeCount = countMin == countMax ? countMin.ToString() : $"\"=\"\"{countMin}-{countMax}\"\"\"";
				string nodeAmount = amountMin == amountMax ? amountMin.ToString() : $"\"=\"\"{amountMin}-{amountMax}\"\"\"";
				string nodeList = $"\"{string.Join(", ", Nodes)}\"";

				string value = $"{ID},{Name},{Duration},{nodeCount},{nodeList},{nodeAmount}";

				return value;
			}
		}
	}
}
