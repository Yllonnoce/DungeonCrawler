using DungeonCrawler.Characters;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.Tests.Characters;
using DungeonCrawler.Tests.Items;
using Xunit;

namespace DungeonCrawler.Tests.Party;

public class BagActionsTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    private readonly SkillCatalog _skills = new();

    private (Inventory bag, CharacterState[] members) Party()
    {
        var warrior = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, _cat.Create("item.longsword"));
        var mage = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        warrior.Recalculate(); mage.Recalculate();
        warrior.FullRestore(); mage.FullRestore();
        return (new Inventory(10), new[] { warrior, mage });
    }

    [Fact]
    public void BagToEquip_EquipsAndReportsMember()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.plate"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(0, EquipSlot.Body));
        Assert.True(r.Changed);
        Assert.Equal("equip", r.Sfx);
        Assert.Contains(0, r.ChangedMembers);
        Assert.Equal("Plate Armor", members[0].Equipment[EquipSlot.Body]!.DisplayName);
        Assert.Null(bag[0]);
    }

    [Fact]
    public void BagToWrongSlot_RoutesToPreferredSlot()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.ring"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(0, EquipSlot.Head));
        Assert.True(r.Changed);
        Assert.NotNull(members[0].Equipment[EquipSlot.Ring1] ?? members[0].Equipment[EquipSlot.Ring2]);
    }

    [Fact]
    public void ClassRestriction_RefusesWithReason()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.plate"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(1, EquipSlot.Body));
        Assert.False(r.Changed);
        Assert.Contains(r.Log, l => l.Contains("cannot wear"));
        Assert.NotNull(bag[0]);
    }

    [Fact]
    public void Swap_EquipReplacesAndOldGoesToBagCell()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.spear"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(0, EquipSlot.Weapon));
        Assert.True(r.Changed);
        Assert.Equal("Spear", members[0].Equipment[EquipSlot.Weapon]!.DisplayName);
        Assert.Equal("Longsword", bag[0]!.DisplayName);
    }

    [Fact]
    public void TwoHanded_StowsOffhandWhenBagHasRoom()
    {
        var (bag, members) = Party();
        members[0].Equipment.Equip(EquipSlot.Offhand, _cat.Create("item.shield"));
        bag.TryAdd(_cat.Create("item.spear"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(0, EquipSlot.Weapon));
        Assert.True(r.Changed);
        Assert.Null(members[0].Equipment[EquipSlot.Offhand]);
        Assert.Contains(bag.Items, i => i.DisplayName == "Shield");
        Assert.Contains(r.Log, l => l.Contains("stows"));
    }

    [Fact]
    public void Drop_LeavesBagAndReportsItem()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.potion"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Drop);
        Assert.NotNull(r.Dropped);
        Assert.Null(bag[0]);
        Assert.False(BagActions.CanMove(bag, members, BagSlot.Equip(0, EquipSlot.Weapon), BagSlot.Drop));
    }

    [Fact]
    public void UnequipToBag_ThenBagSwap()
    {
        var (bag, members) = Party();
        var r = BagActions.Move(bag, members, BagSlot.Equip(0, EquipSlot.Weapon), BagSlot.Bag(3));
        Assert.True(r.Changed);
        Assert.Equal("Longsword", bag[3]!.DisplayName);
        Assert.Null(members[0].Equipment[EquipSlot.Weapon]);
        r = BagActions.Move(bag, members, BagSlot.Bag(3), BagSlot.Bag(0));
        Assert.True(r.Changed);
        Assert.Equal("Longsword", bag[0]!.DisplayName);
    }

    [Fact]
    public void QuickAction_DrinksPotionOnFocusedMember()
    {
        var (bag, members) = Party();
        members[1].Hp = 1;
        bag.TryAdd(_cat.Create("item.potion", 2));
        var r = BagActions.QuickAction(bag, members, BagSlot.Bag(0), 1, _skills);
        Assert.True(r.Changed);
        Assert.Equal("potion", r.Sfx);
        Assert.True(members[1].Hp > 1);
        Assert.Equal(1, bag.Count("item.potion"));
    }

    [Fact]
    public void QuickAction_PartyScroll_TouchesEveryone()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.scroll_haste"));
        var r = BagActions.QuickAction(bag, members, BagSlot.Bag(0), 0, _skills);
        Assert.True(r.Changed);
        Assert.Equal(members.Length, r.ChangedMembers.Count);
        Assert.All(members, m => Assert.True(m.Effects.Has(DungeonCrawler.Combat.StatusKind.Haste)));
    }

    [Fact]
    public void QuickAction_EquipOnFocused_AndUnequipBack()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.robe"));
        var r = BagActions.QuickAction(bag, members, BagSlot.Bag(0), 1, _skills);
        Assert.True(r.Changed);
        Assert.Equal("Robe", members[1].Equipment[EquipSlot.Body]!.DisplayName);
        r = BagActions.QuickAction(bag, members, BagSlot.Equip(1, EquipSlot.Body), 1, _skills);
        Assert.True(r.Changed);
        Assert.Null(members[1].Equipment[EquipSlot.Body]);
        Assert.Contains(bag.Items, i => i.DisplayName == "Robe");
    }

    [Fact]
    public void OutOfRangeMember_IsIgnored()
    {
        var (bag, members) = Party();
        bag.TryAdd(_cat.Create("item.robe"));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Equip(7, EquipSlot.Body));
        Assert.False(r.Changed);
        Assert.NotNull(bag[0]);
    }
}

public class BagDestroyTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();

    [Fact]
    public void Destroy_RemovesTheItemForGood_ButNotQuestRelics()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var members = new[] { w };
        var bag = new Inventory(6);
        bag.TryAdd(_cat.Create("item.potion", 3));
        bag.TryAdd(ItemInstance.Simple("item.quest_relic", "Tally-stick"));
        Assert.True(BagActions.CanMove(bag, members, BagSlot.Bag(0), BagSlot.Destroy));
        Assert.False(BagActions.CanMove(bag, members, BagSlot.Equip(0, EquipSlot.Weapon), BagSlot.Destroy));
        var r = BagActions.Move(bag, members, BagSlot.Bag(0), BagSlot.Destroy);
        Assert.True(r.Changed);
        Assert.Null(r.Dropped);
        Assert.Null(bag[0]);
        Assert.Contains(r.Log, l => l.StartsWith("Destroyed"));
        var refused = BagActions.Move(bag, members, BagSlot.Bag(1), BagSlot.Destroy);
        Assert.False(refused.Changed);
        Assert.NotNull(bag[1]);
        Assert.False(BagSlot.Destroy.IsBag);
        Assert.False(BagSlot.Drop.IsDestroy);
    }
}
