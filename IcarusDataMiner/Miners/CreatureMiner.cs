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
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine.Curves;
using Newtonsoft.Json.Linq;
using System.Text;

namespace IcarusDataMiner.Miners
{
	/// <summary>
	/// Mines data about creature stats and scaling
	/// </summary>
	internal class CreatureMiner : IDataMiner
	{
		public string Name => "Creatures";

		public bool Run(IProviderManager providerManager, Config config, Logger logger)
		{
			ExportGrowthData(providerManager, config, logger);
			return true;
		}

		private void ExportGrowthData(IProviderManager providerManager, Config config, Logger logger)
		{
			IcarusDataTable<FAISetup> setupTable = providerManager.DataTables.AISetupTable!;

			GameFile aiGrowthFile = providerManager.DataProvider.Files["AI/D_AIGrowth.json"];
			IcarusDataTable<FAIGrowth> aiGrowthTable = IcarusDataTable<FAIGrowth>.DeserializeTable("D_AIGrowth", Encoding.UTF8.GetString(aiGrowthFile.Read()));

			string getCreatureName(FAISetup aiSetup)
			{
				return providerManager.DataTables.GetCreatureName(aiSetup, providerManager.AssetProvider);
			}

			FRichCurve? loadCurve(ObjectPointer obj)
			{
				string assetPath = obj.GetAssetPath(true)!;
				if (assetPath.Equals("None", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}

				GameFile curveAsset = providerManager.AssetProvider.Files[assetPath];
				Package curvePackage = (Package)providerManager.AssetProvider.LoadPackage(curveAsset);

				UObject curveObject = curvePackage.ExportMap[0].ExportObject.Value;
				FStructFallback curveStruct = PropertyUtil.Get<FStructFallback>(curveObject, "FloatCurve");

				return new(curveStruct);
			}

			FRowEnum baseHealthEnum = new() { Value = "BaseMaximumHealth_+" };
			FRowEnum baseMeleeDamageEnum = new() { Value = "BaseMeleeDamage_+" };
			FRowEnum baseExplosiveDamageEnum = new() { Value = "BaseExplosiveDamage_+" };

			List<CreatureGrowthData> creatures = new();

			foreach (FAISetup aiSetup in setupTable.Values)
			{
				if (aiSetup.AIGrowth.RowName.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;

				FAIGrowth growth = aiGrowthTable[aiSetup.AIGrowth.RowName];

				string name = getCreatureName(aiSetup);

				int baseHealth;
				if (!growth.Base.TryGetValue(baseHealthEnum, out baseHealth))
				{
					baseHealth = 0;
				}

				int baseMeleeDamage;
				if (!growth.Base.TryGetValue(baseMeleeDamageEnum, out baseMeleeDamage))
				{
					baseMeleeDamage = 0;
				}

				int baseExplosiveDamage;
				if (!growth.Base.TryGetValue(baseExplosiveDamageEnum, out baseExplosiveDamage))
				{
					baseExplosiveDamage = 0;
				}

				FRichCurve? healthCurve = loadCurve(growth.Health);
				FRichCurve? damageCurve = loadCurve(growth.MeleeDamage);

				creatures.Add(new(aiSetup.Name, name, baseHealth, baseMeleeDamage, baseExplosiveDamage, healthCurve, damageCurve));
			}

			creatures.Sort();

			string outPath = Path.Combine(config.OutputDirectory, Name, $"Creatures.csv");
			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(file))
			{
				writer.WriteLine("Creature,Level,Health,Damage");

				foreach (CreatureGrowthData creature in creatures)
				{
					if (creature.HealthCurve is null && creature.DamageCurve is null)
					{
						writer.WriteLine($"{creature},,{creature.BaseHealth:0.},{(creature.BaseMeleeDamage != 0 ? creature.BaseMeleeDamage : creature.BaseExplosiveDamage):0.}");
					}
					else
					{
						writer.WriteLine($"{creature},0,{creature.GetHealth(0):0.},{creature.GetDamage(0):0.}");
						for (int i = 40; i <= 120; i += 40)
						{
							writer.WriteLine($",{i},{creature.GetHealth(i):0.},{creature.GetDamage(i):0.}");
						}
					}
				}
			}
		}

		private class CreatureGrowthData : IComparable<CreatureGrowthData>
		{
			public string RowName { get; }

			public string Name { get; }

			public int BaseHealth { get; }

			public int BaseMeleeDamage { get; }

			public int BaseExplosiveDamage { get; }

			public FRichCurve? HealthCurve { get; }

			public FRichCurve? DamageCurve { get; }

			public CreatureGrowthData(string rowName, string name, int baseHealth, int baseMeleeDamage, int baseExplosiveDamage, FRichCurve? healthCurve, FRichCurve? damageCurve)
			{
				RowName = rowName;
				Name = name;
				BaseHealth = baseHealth;
				BaseMeleeDamage = baseMeleeDamage;
				BaseExplosiveDamage = baseExplosiveDamage;
				HealthCurve = healthCurve;
				DamageCurve = damageCurve;
			}

			public float GetHealth(float level)
			{
				if (HealthCurve is null) return BaseHealth;
				return HealthCurve.Eval(level);
			}

			public float GetDamage(float level)
			{
				if (DamageCurve is null) return BaseMeleeDamage > 0 ? BaseMeleeDamage : BaseExplosiveDamage;
				return DamageCurve.Eval(level);
			}

			public override string ToString()
			{
				return $"{Name} ({RowName})";
			}

			public int CompareTo(CreatureGrowthData? other)
			{
				if (other is null) return 1;
				return Name.CompareTo(other.Name);
			}
		}

#pragma warning disable CS0649 // Field never assigned to

		private struct FAIGrowth : IDataTableRow
		{
			public string Name { get; set; }
			public JObject? Metadata { get; set; }

			public Dictionary<FRowEnum, int> Base;
			public ObjectPointer Health;
			public ObjectPointer MeleeDamage;
			public ObjectPointer MovementSpeed;
			public List<FCustomScaledStat> CustomStats;
			public ObjectPointer ExperienceMultiplier;
			public ObjectPointer ProtectiveThreatOverDistance;
		}

		private struct FCustomScaledStat
		{
			public FRowEnum Stat;
			public ObjectPointer Curve;
		}

#pragma warning restore CS0649
	}
}
