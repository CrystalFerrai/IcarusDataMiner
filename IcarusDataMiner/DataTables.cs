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
using CUE4Parse.UE4.Objects.Core.Math;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IcarusDataMiner
{
	/// <summary>
	/// Collection of game data tables that may be used by multiple miners
	/// </summary>
	internal class DataTables
	{
		private readonly Dictionary<string, IcarusDataTable> mTableNameMap;

		public IcarusDataTable<FTalent>? TalentsTable { get; set; }

		public IcarusDataTable<FTalentTree>? TalentTreesTable { get; set; }

		public IcarusDataTable<FIcarusProspect>? ProspectsTable { get; set; }

		public IcarusDataTable<FGreatHunt>? GreatHuntsTable { get; set; }

		public IcarusDataTable<FIcarusTerrain>? TerrainsTable { get; private set; }

		public IcarusDataTable<FIcarusAtmosphere>? AtmospheresTable { get; private set; }

		public IcarusDataTable<FItemData>? ItemTemplateTable { get; private set; }

		public IcarusDataTable<FItemStaticData>? ItemStaticTable { get; private set; }

		public IcarusDataTable<FItemableData>? ItemableTable { get; private set; }

		public IcarusDataTable<FItemRewards>? ItemRewardsTable { get; private set; }

		public IcarusDataTable<FWorkshopItem>? WorkshopItemTable { get; private set; }

		public IcarusDataTable<FBreakableRockData>? BreakableRockTable { get; private set; }

		public IcarusDataTable<FAISetup>? AISetupTable { get; private set; }

		public IcarusDataTable<FAICreatureType>? AICreatureTypeTable { get; private set; }

		public IcarusDataTable<FIcarusStatDescription>? StatsTable { get; private set; }

		private DataTables()
		{
			mTableNameMap = new();
		}

		public static DataTables Load(IFileProvider provider, Logger logger)
		{
			DataTables instance = new();
			instance.TalentsTable = instance.InternalLoadDataTable<FTalent>(provider, "Talents/D_Talents.json");
			instance.TalentTreesTable = instance.InternalLoadDataTable<FTalentTree>(provider, "Talents/D_TalentTrees.json");
			instance.ProspectsTable = instance.InternalLoadDataTable<FIcarusProspect>(provider, "Prospects/D_ProspectList.json");
			instance.GreatHuntsTable = instance.InternalLoadDataTable<FGreatHunt>(provider, "GreatHunt/D_GreatHunts.json");
			instance.TerrainsTable = instance.InternalLoadDataTable<FIcarusTerrain>(provider, "Prospects/D_Terrains.json");
			instance.AtmospheresTable = instance.InternalLoadDataTable<FIcarusAtmosphere>(provider, "Prospects/D_Atmospheres.json");
			instance.ItemTemplateTable = instance.InternalLoadDataTable<FItemData>(provider, "Items/D_ItemTemplate.json");
			instance.ItemStaticTable = instance.InternalLoadDataTable<FItemStaticData>(provider, "Items/D_ItemsStatic.json");
			instance.ItemableTable = instance.InternalLoadDataTable<FItemableData>(provider, "Traits/D_Itemable.json");
			instance.ItemRewardsTable = instance.InternalLoadDataTable<FItemRewards>(provider, "Items/D_ItemRewards.json");
			instance.WorkshopItemTable = instance.InternalLoadDataTable<FWorkshopItem>(provider, "MetaWorkshop/D_WorkshopItems.json");
			instance.BreakableRockTable = instance.InternalLoadDataTable<FBreakableRockData>(provider, "World/D_BreakableRockData.json");
			instance.AISetupTable = instance.InternalLoadDataTable<FAISetup>(provider, "AI/D_AISetup.json");
			instance.AICreatureTypeTable = instance.InternalLoadDataTable<FAICreatureType>(provider, "AI/D_AICreatureType.json");
			instance.StatsTable = instance.InternalLoadDataTable<FIcarusStatDescription>(provider, "Stats/D_Stats.json");
			return instance;
		}

		public bool TryResolveHandle<T>(FRowHandle handle, [NotNullWhen(true)] out T? row) where T : IDataTableRow
		{
			if (mTableNameMap.TryGetValue(handle.DataTableName, out IcarusDataTable? table) && table is IcarusDataTable<T> tt)
			{
				row = tt[handle.RowName];
				return true;
			}
			row = default;
			return false;
		}

		public FItemableData GetItemableData(FItemData item)
		{
			if (ItemStaticTable!.TryGetValue(item.ItemStaticData.RowName, out FItemStaticData staticData) &&
				ItemableTable!.TryGetValue(staticData.Itemable.RowName, out FItemableData itemableData))
			{
				return itemableData;
			}
			return default;
		}

		public FItemableData GetItemableData(FWorkshopItem item)
		{
			if (ItemTemplateTable!.TryGetValue(item.Item.RowName, out FItemData itemTemplate))
			{
				return GetItemableData(itemTemplate);
			}
			return default;
		}

		public FItemableData GetItemableData(FItemRewardEntry item)
		{
			if (ItemTemplateTable!.TryGetValue(item.Item.RowName, out FItemData itemTemplate))
			{
				return GetItemableData(itemTemplate);
			}
			return default;
		}

		public string? GetCreatureName(FRowEnum aiSetupRow, IFileProvider locProvider)
		{
			if (aiSetupRow.Value == "None") return null;

			FAISetup aiSetup = AISetupTable![aiSetupRow.Value];
			return GetCreatureName(aiSetup, locProvider);
		}

		public string GetCreatureName(FAISetup aiSetup, IFileProvider locProvider)
		{
			FAICreatureType creatureType = AICreatureTypeTable![aiSetup.CreatureType.RowName];
			return LocalizationUtil.GetLocalizedString(locProvider, creatureType.CreatureName);
		}

		public static IcarusDataTable<T> LoadDataTable<T>(IFileProvider provider, string path) where T : IDataTableRow
		{
			GameFile file = provider.Files[path];
			string tableName = Path.GetFileNameWithoutExtension(path);
			return IcarusDataTable<T>.DeserializeTable(tableName, Encoding.UTF8.GetString(file.Read()));
		}

		private IcarusDataTable<T> InternalLoadDataTable<T>(IFileProvider provider, string path) where T : IDataTableRow
		{
			string tableName = Path.GetFileNameWithoutExtension(path);
			IcarusDataTable<T> table = LoadDataTable<T>(provider, path);
			mTableNameMap[tableName] = table;
			return table;
		}
	}

#pragma warning disable CS0649 // Field never assigned to

	internal struct FTalent : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public ETalentNodeType TalentType;
		public string DisplayName;
		public string Description;
		public ObjectPointer Icon;
		public FRowHandle ExtraData;
		public FRowHandle TalentTree;
		public FVector2D Position;
		public FVector2D Size;
		public List<FTalentReward> Rewards;
		public List<FRowHandle> RequiredTalents;
		public List<FRowHandle> RequiredFlags;
		public List<FRowHandle> ForbiddenFlags;
		public FRowHandle RequiredRank;
		public int RequiredLevel;
		public bool bDefaultUnlocked;
		public ELineDrawMethod DrawMethodOverride;
	}

	internal struct FTalentTree : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string DisplayName;
		public ObjectPointer BackgroundTexture;
		public ObjectPointer Icon;
		public FRowHandle Archetype;
		public FRowHandle FirstRank;
		public int RequiredLevel;
	}

	internal struct FIcarusProspect : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string DropName;
		public string DesignNotes;
		public string Description;
		public string FlavourText;
		public ObjectPointer ProspectImage;
		public Dictionary<EMissionDifficulty, FDifficultySetup> DifficultySetup;
		public EIcarusProspectDifficulty Difficulty;
		public FRowHandle BriefingDialogue;
		public FRowHandle LandingDialogue;
		public FRowHandle MissionCompleteDialogue;
		public int RequiredLevel;
		public EProspectRequiredTech RequiredTech;
		public List<FRowEnum> RequiredCharacterFlags;
		public List<FRowEnum> ForbiddenCharacterFlags;
		public List<FRowHandle> RequiredFlags;
		public bool bDisableWorldBosses;
		public Dictionary<FRowHandle, FVector2D> WorldBosses;
		public FRowHandle InitialForecast;
		public FRowHandle Forecast;
		public bool bDisabled;
		public FRowHandle Terrain;
		public FRowHandle FactionMission;
		public bool bIsPersistent;
		public bool bIsOpenWorld;
		public FIcarusTimeSpan TimeDuration;
		public int StartingTime;
		public ObjectPointer TimeScaleCurve;
		public List<FRowHandle> AdditionalRulesets;
		public int PlayerSpawnGroupIndex;
		public List<FMetaSpawn> MetaDepositSpawns;
		public FVector2D DefaultMetaResourceAmount;
		public int NumMetaSpawnsMin;
		public int NumMetaSpawnsMax;
		public List<FRowHandle> WorldStatList;
		public ObjectPointer BoundsOverride;
		public FRowHandle AISpawnConfigOverride;
		public bool bAbandonOnProspectExpiry;
		public EOnProspectAvailability OnProspectAvailability;
	}

	internal struct FGreatHunt : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FRowHandle Hunt;
		public FRowHandle Prospect;
		public List<FRowHandle> ForbiddenTalent;
		public EGreatHuntMissionType Type;
		public Dictionary<FRowEnum, int> WorldStats;
	}

	internal struct FIcarusTerrain : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string TerrainName;
		public ObjectPointer Level;
		public ObjectPointer TemperatureMap;
		public FVector2D TemperatureMapRange;
		public ObjectPointer BiomeMap;
		public ObjectPointer Bounds;
		public FRowHandle SpawnConfig;
		public FRowHandle FishConfig;
		public ObjectPointer AudioZoneMap;
	}

	internal struct FIcarusAtmosphere : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string AtmosphereName;
		public ObjectPointer Image_Small;
		public ObjectPointer Image_Medium;
		public ObjectPointer Image_Large;
	}

	internal struct FWorkshopItem : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FRowHandle Item;
		public List<FWorkshopCost> ResearchCost;
		public List<FWorkshopCost> ReplicationCost;
		public FRowHandle RequiredMission;
	}

	internal struct FItemData : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FRowHandle ItemStaticData;
		public List<FItemDynamicData> ItemDynamicData;
		public FCustomProperties CustomProperties;
		public string DatabaseGUID;
		public int ItemOwnerLookupId;
		public FGameplayTagContainer RuntimeTags;
	}

	internal struct FItemStaticData : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FRowHandle Meshable;
		public FRowHandle Itemable;
		public FRowHandle Interactable;
		public FRowHandle Hitable;
		public FRowHandle Equippable;
		public FRowHandle Focusable;
		public FRowHandle Highlightable;
		public FRowHandle Actionable;
		public FRowHandle Buildable;
		public FRowHandle Consumable;
		public FRowHandle Usable;
		public FRowHandle Combustible;
		public FRowHandle Deployable;
		public FRowHandle Armour;
		public FRowHandle Ballistic;
		public FRowHandle Vehicular;
		public FRowHandle Fillable;
		public FRowHandle Durable;
		public FRowHandle Floatable;
		public FRowHandle Rocketable;
		public FRowHandle Inventory;
		public FRowHandle Processing;
		public FRowHandle Thermal;
		public FRowHandle Experience;
		public FRowHandle Slotable;
		public FRowHandle Decayable;
		public FRowHandle Flammable;
		public FRowHandle Transmutable;
		public FRowHandle Generator;
		public FRowHandle Weight;
		public FRowHandle Farmable;
		public FRowHandle InventoryContainer;
		public FRowHandle Energy;
		public FRowHandle Water;
		public FRowHandle Oxygen;
		public FRowHandle Fuel;
		public FRowHandle ToolDamage;
		public FRowHandle AmmoType;
		public FRowHandle Audio;
		public FRowHandle RangedWeaponData;
		public FRowHandle FirearmData;
		public FRowHandle FLODData;
		public Dictionary<FRowEnum, int> AdditionalStats;
		public FRowHandle Attachments;
		public int CraftingExperience;
		public FGameplayTagContainer Manual_Tags;
		public FGameplayTagContainer Generated_Tags;
	}

	internal struct FItemableData : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public ObjectPointer Behaviour;
		public string DisplayName;
		public ObjectPointer Icon;
		public ObjectPointer Override_Glow_Icon;
		public string Description;
		public string FlavorText;
		public int Weight;
		public int MaxStack;
	}

	internal struct FItemRewards : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public List<FItemRewardEntry> Rewards;
	}

	internal struct FBreakableRockData : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public FRowHandle ItemReward;
		public FRowHandle PyriticCrustItemType;
		public FRowHandle Durable;
		public FGameplayTagContainer Tags;
		public ObjectPointer BreakSound;
	}

	internal struct FAISetup : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public ObjectPointer ActorClass;
		public ObjectPointer ControllerClass;
		public FRowHandle CreatureType;
		public List<FRowHandle> Descriptors;
		public FRowHandle DeadItem;
		public FRowHandle GOAPSetup;
		public ObjectPointer DefaultNavigationFilter;
		public FRowHandle Relationships;
		public List<FRowHandle> NotifiedNPCTypes;
		public bool bNotifySelfType;
		public FRowHandle AIGrowth;
		public Dictionary<EMovementState, FMovementStateData> MovementMapping;
		public FRowHandle HuntingSetup;
		public List<FCriticalHitLocation> CriticalHitBones;
		public FRowHandle Audio;
		public List<string> CollisionHitEventBones;
		public int LatentDeathDuration;
		public FRowHandle Trophy;
		public FRowHandle Loot;
		public FRowHandle Hitable;
		public bool bUseSurvivalCharacterState;
		public bool bStartWithSurvivalTickDisabled;
		public FRowHandle BestiaryGroup;
		public List<FCriticalHitLocation> BlacklistBones;
	}

	internal struct FAICreatureType : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string CreatureName;

		// NOTE: There is a lot more to this struct we are leaving out because we don't care about it
	}

	internal struct FIcarusStatDescription : IDataTableRow
	{
		public string Name { get; set; }
		public JObject? Metadata { get; set; }

		public string Title;
		public ObjectPointer Icon;
		public string PositiveTitleFormat;
		public string NegativeTitleFormat;
		public string PositiveDescription;
		public string NegativeDescription;
		public bool bIsReplicated;
		public List<FStatDisplayCalculation> DisplayOperations;
		public bool bIsWorldStat;
		public FRowHandle StatCategory;
		public bool bHideStatInUserInterface;
	}

	internal enum ETalentNodeType
	{
		Talent,
		Reroute,
		MutuallyExclusive
	}

	internal struct FTalentReward
	{
		public Dictionary<FRowEnum, int> GrantedStats;
		public List<FRowHandle> GrantedFlags;
	}

	internal enum ELineDrawMethod
	{
		Unspecified,
		NoLine,
		ShortestDistance,
		XThenY,
		YThenX,
	}

	internal struct FDifficultySetup
	{
		public List<FRowHandle> DifficultyStats;
		public FRowHandle Forecast;
	}

	internal struct FIcarusTimeSpan
	{
		public FIcarusIntRange Days;
		public FIcarusIntRange Hours;
		public FIcarusIntRange Mins;
		public FIcarusIntRange Seconds;
	}

	internal struct FMetaSpawn
	{
		public FRowEnum SpawnLocation;
		public int MinMetaAmount;
		public int MaxMetaAmount;
	}

	internal struct FIcarusIntRange
	{
		public int Min;
		public int Max;
	}

	internal enum EMissionDifficulty
	{
		None,
		Easy,
		Medium,
		Hard,
		Extreme
	}

	internal enum EIcarusProspectDifficulty
	{
		Easy,
		Normal,
		Hard,
		Extreme
	}

	internal enum EProspectRequiredTech
	{
		None,
		Tier1,
		Tier2,
		Tier3,
		Tier4
	}

	internal enum EOnProspectAvailability
	{
		None,
		Base,
		Upgrade1,
		Upgrade2,
		Upgrade3
	}

	internal enum EGreatHuntMissionType
	{
		None,
		Standard,
		Choice,
		Optional,
		Final
	}

	internal struct FWorkshopCost
	{
		public FRowHandle Meta;
		public int Amount;
	}

	internal struct FItemDynamicData
	{
		public EDynamicItemProperties PropertyType;
		public int Value;
	}

	internal struct FItemRewardEntry
	{
		public FRowHandle Item;
		public float DropChance;
		public FRowHandle DropChanceAdditiveStat;
		public FRowHandle RequiredStatToDrop;
		public int MinRandomStackCount;
		public int MaxRandomStackCount;
		public bool bRewardsScale;
		public FRowHandle StackAdditiveStat;
		public FRowHandle StackMultiplicativeStat;
	}

	internal enum EDynamicItemProperties
	{
		AssociatedItemInventoryId,
		AssociatedItemInventorySlot,
		DynamicState,
		GunCurrentMagSize,
		CurrentAmmoType,
		BuildingVariation,
		Durability,
		ItemableStack,
		MillijoulesRemaining,
		TransmutableUnits,
		Fillable_StoredUnits,
		Fillable_Type,
		Decayable_CurrentSpoilTime,
		InventoryContainer_LinkedInventoryId,
		MaxDynamicItemProperties
	}

	internal struct FCustomProperties
	{
		public List<FIcarusStatReplicated> StaticWorldStats;
		public List<FIcarusStatReplicated> StaticWorldHeldStats;
		public List<FIcarusStatReplicated> Stats;
		public List<FRowEnum> Alterations;
	}

	internal struct FIcarusStatReplicated
	{
		public FRowEnum Stat;
		public int Value;
	}

	internal struct FGameplayTagContainer
	{
		public List<FGameplayTag> GameplayTags;
		public List<FGameplayTag> ParentTags;
	}

	internal struct FGameplayTag
	{
		public string TagName; // Actual type is FName, but stored in json files as a string
	}

	internal struct FGameplayTagQuery
	{
		public int TokenStreamVersion;
		public List<FGameplayTag> TagDictionary;
		public List<byte> QueryTokenStream;
		public string UserDescription;
		public string AutoDescription;
	};

	internal enum EMovementState
	{
		Undefined,
		Stationary,
		Sneak,
		Walk,
		Jog,
		Run,
		Sprint,
		Attacking,
		Following
	}

	internal struct FMovementStateData
	{
		public float MaxWalkSpeed;
		public float GroundFriction;
		public float BrakingFriction;
		public float MaxAcceleration;
		public float BrakingDeceleration;
		public float RotationRate;
		public float MaxSwimSpeed;
	}

	internal struct FCriticalHitLocation
	{
		public string BoneName;
		public bool AffectsChildren;
	}

	internal struct FStatDisplayCalculation
	{
		public EStatDisplayOperation Operation;
		public float Value;
	}

	internal enum EStatDisplayOperation
	{
		None,
		Multiply,
		Division,
		Addition
	}

#pragma warning restore CS0649
}
