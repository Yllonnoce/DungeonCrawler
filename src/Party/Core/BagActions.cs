using DungeonCrawler.Characters;
using DungeonCrawler.Items;

namespace DungeonCrawler.Party;

/// <summary>Addresses one bag cell, one member's equipment slot, or the drop zone. Engine-free so it can travel over the wire.</summary>
public readonly record struct BagSlot(int Member, EquipSlot Slot, int BagIndex, bool IsDrop = false, bool IsDestroy = false)
{
    public bool IsBag => Member < 0 && !IsDrop && !IsDestroy;
    public bool IsEquip => Member >= 0;

    public static BagSlot Bag(int index) => new(-1, EquipSlot.Weapon, index);
    public static BagSlot Equip(int member, EquipSlot slot) => new(member, slot, -1);
    /// <summary>The floor: the item lands at the party's feet (and companions leave it there).</summary>
    public static BagSlot Drop => new(-1, EquipSlot.Weapon, -1, true);
    /// <summary>Gone for good.</summary>
    public static BagSlot Destroy => new(-1, EquipSlot.Weapon, -1, false, true);
}

/// <summary>What a bag action did, for whoever has to show it: log lines, a sound, members whose stats changed, side effects to carry out in the world.</summary>
public sealed class BagResult
{
    public List<string> Log { get; } = new();
    public string? Sfx { get; set; }
    public HashSet<int> ChangedMembers { get; } = new();
    /// <summary>An item that left the bag for the floor: the caller spawns the loot drop.</summary>
    public ItemInstance? Dropped { get; set; }
    /// <summary>A trap kind name to place in front of the actor.</summary>
    public string PlaceTrap { get; set; } = "";
    public bool Changed { get; set; }

    public BagResult Say(string text) { Log.Add(text); return this; }
}

/// <summary>
/// Moving, equipping, unequipping, dropping and using items: the rules behind the inventory screen, with no Godot in them,
/// so the same code runs on a server for another player's bag intent.
/// </summary>
public static class BagActions
{
    public static ItemInstance? Get(Inventory bag, CharacterState[] members, BagSlot r)
        => r.IsBag ? bag[r.BagIndex] : r.IsEquip && r.Member < members.Length ? members[r.Member].Equipment[r.Slot] : null;

    /// <summary>Which slot a drop on <paramref name="to"/> should really use: the cell itself if the kind matches, else the member's preferred slot.</summary>
    private static EquipSlot? ResolveSlot(CharacterState m, BagSlot to, ItemInstance item)
    {
        if (SlotRules.KindOf(to.Slot) == item.Kind) return to.Slot;
        return m.Equipment.PreferredSlot(m, item) ?? SlotRules.SlotsFor(item.Kind).FirstOrDefault();
    }

    /// <summary>
    /// Drops are accepted generously so the player always gets a message: any equippable item may be dropped anywhere on a
    /// character; <see cref="Move"/> routes it to the right slot or says why it cannot go there.
    /// </summary>
    public static bool CanMove(Inventory bag, CharacterState[] members, BagSlot from, BagSlot to)
    {
        if (from == to) return false;
        if (Get(bag, members, from) == null) return false;
        if (to.IsDrop || to.IsDestroy) return from.IsBag;
        return true;
    }

    public static BagResult Move(Inventory bag, CharacterState[] members, BagSlot from, BagSlot to)
    {
        var r = new BagResult();
        var item = Get(bag, members, from);
        if (item == null) return r;
        if (to.IsEquip && to.Member >= members.Length || from.IsEquip && from.Member >= members.Length) return r;

        if (to.IsDestroy)
        {
            if (!from.IsBag) return r;
            if (item.DefId == "item.quest_relic") return r.Say($"{item.DisplayName} is not yours to destroy; someone wants it.");
            bag.RemoveAt(from.BagIndex);
            r.Changed = true;
            r.Sfx = "barrel";
            r.Say($"Destroyed {item}.");
        }
        else if (to.IsDrop)
        {
            if (!from.IsBag) return r;
            bag.RemoveAt(from.BagIndex);
            r.Dropped = item;
            r.Changed = true;
            r.Say($"Dropped {item}.");
        }
        else if (to.IsBag && from.IsBag)
        {
            bag.Swap(from.BagIndex, to.BagIndex);
            r.Changed = true;
        }
        else if (to.IsBag)
        {
            // unequip into a bag cell; if that cell holds something, it must be able to take the vacated slot
            var m = members[from.Member];
            var target = bag[to.BagIndex];
            if (target != null && Equipment.WhyCannotEquip(m, target, from.Slot) is { } why)
            {
                if (bag.FreeSlots == 0) return r.Say(why + ".");
                m.Equipment.Unequip(from.Slot);
                bag.TryAdd(item);
            }
            else
            {
                m.Equipment.Unequip(from.Slot);
                bag.Set(to.BagIndex, item);
                if (target != null) m.Equipment.Equip(from.Slot, target);
            }
            m.Recalculate();
            r.Changed = true;
            r.Say($"{m.Name} unequips {item.DisplayName}.");
            r.ChangedMembers.Add(from.Member);
        }
        else
        {
            var m = members[to.Member];
            if (!item.IsEquippable)
                return r.Say(item.Consumable != null ? $"{item.DisplayName} is used by double-clicking it in the bag." : $"{item.DisplayName} cannot be equipped.");
            if (ResolveSlot(m, to, item) is not { } slot) return r.Say($"{m.Name} has no slot for {item.DisplayName}.");

            var why = Equipment.WhyCannotEquip(m, item, slot);
            if (why != null && item.TwoHanded && m.Equipment[EquipSlot.Offhand] is { } offhand && bag.FreeSlots > 0)
            {
                m.Equipment.Unequip(EquipSlot.Offhand);
                bag.TryAdd(offhand);
                r.Say($"{m.Name} stows {offhand.DisplayName} to free a hand.");
                r.Changed = true;
                r.ChangedMembers.Add(to.Member);
                why = Equipment.WhyCannotEquip(m, item, slot);
            }
            if (why != null) { m.Recalculate(); return r.Say(why + "."); }

            // whatever gets displaced must fit where the dragged item came from
            var displaced = m.Equipment[slot];
            if (displaced != null && from.IsEquip && Equipment.WhyCannotEquip(members[from.Member], displaced, from.Slot) is { } backWhy)
            {
                if (bag.FreeSlots == 0) { m.Recalculate(); return r.Say(backWhy + ", and the bag is full."); }
                m.Equipment.Unequip(slot);
                bag.TryAdd(displaced);
                r.Say($"{displaced.DisplayName} goes to the bag ({backWhy.ToLower()}).");
                displaced = null;
            }

            m.Equipment.Equip(slot, item);
            if (from.IsBag) bag.Set(from.BagIndex, displaced);
            else
            {
                var fm = members[from.Member];
                fm.Equipment.Unequip(from.Slot);
                if (displaced != null) fm.Equipment.Equip(from.Slot, displaced);
                fm.Recalculate();
                r.ChangedMembers.Add(from.Member);
            }
            m.Recalculate();
            r.Changed = true;
            r.Sfx = "equip";
            r.Say($"{m.Name} equips {item.DisplayName}.");
            r.ChangedMembers.Add(to.Member);
        }
        return r;
    }

    /// <summary>Double-click / right-click: equip on the focused member, use a consumable, or unequip to the bag.</summary>
    public static BagResult QuickAction(Inventory bag, CharacterState[] members, BagSlot slotRef, int focused, SkillCatalog skills)
    {
        var r = new BagResult();
        var item = Get(bag, members, slotRef);
        if (item == null || focused < 0 || focused >= members.Length) return r;
        if (slotRef.IsBag)
        {
            if (item.Consumable != null) return UseConsumable(bag, members, item, focused, skills);
            if (!item.IsEquippable) return r.Say($"{item.DisplayName} cannot be equipped.");
            var m = members[focused];
            var slot = m.Equipment.PreferredSlot(m, item);
            if (slot == null)
            {
                var first = SlotRules.SlotsFor(item.Kind).First();
                return r.Say((Equipment.WhyCannotEquip(m, item, first) ?? $"{m.Name} cannot use {item.DisplayName}") + ".");
            }
            return Move(bag, members, slotRef, BagSlot.Equip(focused, slot.Value));
        }
        if (slotRef.IsEquip)
        {
            if (bag.FreeSlots == 0) return r.Say("The bag is full.");
            var m = members[slotRef.Member];
            m.Equipment.Unequip(slotRef.Slot);
            bag.TryAdd(item);
            m.Recalculate();
            r.Changed = true;
            r.Say($"{m.Name} unequips {item.DisplayName}.");
            r.ChangedMembers.Add(slotRef.Member);
        }
        return r;
    }

    public static BagResult UseConsumable(Inventory bag, CharacterState[] members, ItemInstance item, int focused, SkillCatalog skills)
    {
        var r = new BagResult();
        if (item.Consumable is not { } c || bag.IndexOf(item) < 0) return r;
        if (c.Respec)
        {
            var target = members[focused];
            var refunded = SkillRules.Respec(target, skills);
            bag.TakeOne(item);
            r.Changed = true;
            r.ChangedMembers.Add(focused);
            return r.Say($"{target.Name} forgets everything: {refunded} skill point{(refunded == 1 ? "" : "s")} refunded.");
        }
        if (c.PlaceTrap != "")
        {
            bag.TakeOne(item);
            r.Changed = true;
            r.PlaceTrap = c.PlaceTrap;
            return r;
        }
        var targets = c.TargetsParty ? members.Where(x => x.IsAlive).ToList() : new List<CharacterState> { members[focused] };
        var text = ConsumableRules.Use(c, targets);
        bag.TakeOne(item);
        r.Changed = true;
        r.Sfx = "potion";
        r.Say($"{item.DisplayName}: {text}");
        for (var i = 0; i < members.Length; i++) r.ChangedMembers.Add(i);
        return r;
    }
}
