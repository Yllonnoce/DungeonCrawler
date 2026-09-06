using DungeonCrawler.Items;
using Xunit;

namespace DungeonCrawler.Tests.Items;

public class InventoryTests
{
    private static ItemInstance Arrows(int n) => new() { DefId = "item.arrow", BaseName = "Arrow", Kind = SlotKind.Ammo, Stackable = true, StackCount = n, MaxStack = 50 };

    [Fact]
    public void Stackables_Merge()
    {
        var bag = new Inventory();
        bag.TryAdd(Arrows(10));
        bag.TryAdd(Arrows(5));
        Assert.Single(bag.Items);
        Assert.Equal(15, bag.Count("item.arrow"));
    }

    [Fact]
    public void Consume_TakesAcrossStacksAndRefusesWhenShort()
    {
        var bag = new Inventory();
        bag.TryAdd(new ItemInstance { DefId = "item.arrow", BaseName = "Arrow", Stackable = false, StackCount = 3 });
        bag.TryAdd(Arrows(4));
        Assert.Equal(7, bag.Count("item.arrow"));
        Assert.False(bag.Consume("item.arrow", 8));
        Assert.Equal(7, bag.Count("item.arrow"));
        Assert.True(bag.Consume("item.arrow", 6));
        Assert.Equal(1, bag.Count("item.arrow"));
        Assert.Single(bag.Items);
    }

    [Fact]
    public void Stacks_RespectMaxStackAndSpill()
    {
        var bag = new Inventory(3);
        Assert.True(bag.TryAdd(Arrows(45)));
        Assert.True(bag.TryAdd(Arrows(10)));  // 5 merge, 5 spill to a new cell
        Assert.Equal(55, bag.Count("item.arrow"));
        Assert.Equal(2, bag.Items.Count());
        Assert.Equal(50, bag[0]!.StackCount);
        Assert.Equal(5, bag[1]!.StackCount);
    }

    [Fact]
    public void Swap_MovesAndMerges()
    {
        var bag = new Inventory(4);
        bag.TryAdd(Arrows(10));
        bag.TryAdd(new ItemInstance { DefId = "item.sword", BaseName = "Sword", Kind = SlotKind.Weapon });
        bag.Swap(0, 3);
        Assert.Null(bag[0]);
        Assert.Equal("Arrow", bag[3]!.BaseName);
        bag.Set(2, Arrows(5));
        bag.Swap(2, 3);
        Assert.Null(bag[2]);
        Assert.Equal(15, bag[3]!.StackCount);
    }

    [Fact]
    public void TakeOne_DecrementsOrRemoves()
    {
        var bag = new Inventory(2);
        var arrows = Arrows(2);
        bag.TryAdd(arrows);
        bag.TakeOne(arrows);
        Assert.Equal(1, arrows.StackCount);
        bag.TakeOne(arrows);
        Assert.Empty(bag.Items);
    }

    [Fact]
    public void Capacity_BlocksNewSlotsButNotMerges()
    {
        var bag = new Inventory(1);
        Assert.True(bag.TryAdd(Arrows(1)));
        Assert.False(bag.TryAdd(new ItemInstance { DefId = "item.sword", BaseName = "Sword" }));
        Assert.True(bag.TryAdd(Arrows(1)));
        Assert.Equal(2, bag.Count("item.arrow"));
    }
}
