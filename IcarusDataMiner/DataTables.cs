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
using CUE4Parse.UE4.Objects.Core.i18N;
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
		public IcarusDataTable<FItemData>? ItemTemplateTable { get; private set; }

		public IcarusDataTable<FItemStaticData>? ItemStaticTable { get; private set; }

		public IcarusDataTable<FItemableData>? ItemableTable { get; private set; }

		public IcarusDataTable<FItemRewards>? ItemRewardsTable { get; private set; }

		public IcarusDataTable<FWorkshopItem>? WorkshopItemTable { get; private set; }

		public IcarusDataTable<FBreakableRockData>? BreakableRockTable { get; private set; }

		private DataTables()
		{
		}

		[MemberNotNull(nameof(WorkshopItemTable), nameof(ItemTemplateTable), nameof(ItemStaticTable), nameof(ItemableTable))]
		public static DataTables Load(IFileProvider provider, Logger logger)
		{
			return new()
			{
				ItemTemplateTable = LoadDataTable<FItemData>(provider, "Items/D_ItemTemplate.json"),
				ItemStaticTable = LoadDataTable<FItemStaticData>(provider, "Items/D_ItemsStatic.json"),
				ItemableTable = LoadDataTable<FItemableData>(provider, "Traits/D_Itemable.json"),
				ItemRewardsTable = LoadDataTable<FItemRewards>(provider, "Items/D_ItemRewards.json"),
				WorkshopItemTable = LoadDataTable<FWorkshopItem>(provider, "MetaWorkshop/D_WorkshopItems.json"),
				BreakableRockTable = LoadDataTable<FBreakableRockData>(provider, "World/D_BreakableRockData.json")
			};
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

		public static IcarusDataTable<T> LoadDataTable<T>(IFileProvider provider, string path) where T : IDataTableRow
		{
			GameFile file = provider.Files[path];
			string tableName = Path.GetFileNameWithoutExtension(path);
			return IcarusDataTable<T>.DeserializeTable(tableName, Encoding.UTF8.GetString(file.Read()));
		}
	}

#pragma warning disable CS0649 // Field never assigned to

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

#pragma warning restore CS0649
}
