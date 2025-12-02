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

namespace IcarusDataMiner
{
	/// <summary>
	/// Utlity for obtaining information about prospects
	/// </summary>
	internal class ProspectDataUtil
	{
		private List<ProspectData>? mAvailableProspects;
		private Dictionary<string, IList<ProspectData>>? mProspectsByTree;

		/// <summary>
		/// All prospects available to the player
		/// </summary>
		public IReadOnlyList<ProspectData> AvailableProspects => mAvailableProspects ?? throw new InvalidOperationException("ProspectDataUtil not initialized");

		/// <summary>
		/// Maps talent tree names to prospects within each tree
		/// </summary>
		public IReadOnlyDictionary<string, IList<ProspectData>> ProspectsByTree => mProspectsByTree ?? throw new InvalidOperationException("ProspectDataUtil not initialized");

		private ProspectDataUtil()
		{
		}

		public static ProspectDataUtil Create(IFileProvider dataProvider, DataTables dataTables, Logger logger)
		{
			ProspectDataUtil instance = new();
			instance.Initialize(dataProvider, dataTables, logger);
			return instance;
		}

		private void Initialize(IFileProvider dataProvider, DataTables dataTables, Logger logger)
		{
			HashSet<string> treeNames = new()
			{
				"Prospect_Olympus",
				"Prospect_Styx",
				"Prospect_Prometheus",
				"GreatHunt_IceMammoth",
				"GreatHunt_Ape",
				"GreatHunt_RockGolem"
			};

			mAvailableProspects = new();
			mProspectsByTree = treeNames.ToDictionary(t => t, t => (IList<ProspectData>)new List<ProspectData>());

			foreach (var pair in dataTables.TalentsTable!)
			{
				if (!treeNames.Contains(pair.Value.TalentTree.RowName))
				{
					continue;
				}

				if (pair.Value.ExtraData.IsNone)
				{
					continue;
				}

				if (!pair.Value.ExtraData.TryResolve(dataTables, out FIcarusProspect prospect))
				{
					if (!pair.Value.ExtraData.TryResolve(dataTables, out FGreatHunt greatHunt))
					{
						logger.Log(LogLevel.Debug, $"[ProspectDataUtil] Unable to resolve row handle {pair.Value.ExtraData}");
						continue;
					}
					if (!greatHunt.Prospect.TryResolve(dataTables, out prospect))
					{
						logger.Log(LogLevel.Debug, $"[ProspectDataUtil] Unable to resolve row handle {greatHunt.Prospect}");
						continue;
					}					
				}

				ProspectData prospectData = new()
				{
					Prospect = prospect,
					Talent = new()
					{
						DataTableName = dataTables.TalentsTable.Name!,
						RowName = pair.Key
					}
				};

				mAvailableProspects.Add(prospectData);
				mProspectsByTree[pair.Value.TalentTree.RowName].Add(prospectData);
			}
		}
	}

	internal struct ProspectData
	{
		public FIcarusProspect Prospect;
		public FRowHandle Talent;
	}
}
