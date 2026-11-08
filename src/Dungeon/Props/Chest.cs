using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>A chest: interact once to spill its loot onto the floor. A mimic (fortune) springs instead and yields its roll when the brute dies.</summary>
public partial class Chest : Area3D, IInteractable
{
    [Export] public string TableId { get; set; } = "table.chest";
    public int SpawnIndex { get; set; } = -1;
    public bool Locked { get; set; }
    public bool Opened { get; private set; }
    /// <summary>A mouth, not a chest: opening it spawns the tier's brute (Hulking, twice the XP); the roll waits for its death.</summary>
    public bool IsMimic { get; set; }
    public bool Sprung { get; private set; }
    public Enemy? Mimic { get; private set; }
    /// <summary>Iron-banded with a purple seam (fortune): a vault roll, and sixty seconds Hunted for whoever opens it.</summary>
    public bool Bound { get; set; }
    /// <summary>Failed picks so far; the third jams the lock (fortune).</summary>
    public int LockFails { get; private set; }
    /// <summary>The pick snapped off: only a key turns it now.</summary>
    public bool Jammed { get; private set; }
    private bool _hidden;

    public void MarkOpened()
    {
        Opened = true;
        if (_hidden) ShowBody();
        Tint(new Color(0.35f, 0.3f, 0.25f));
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Chest(SpawnIndex), "opened");
    }

    public string Prompt => Opened ? "Empty chest"
        : Bound ? FortuneRules.BoundPrompt
        : Sprung ? "The chest is elsewhere"
        : IsMimic && Noticed() ? $"Open chest ({MimicRules.Tell})"
        : Locked && Jammed ? "Jammed lock: only a key opens it"
        : Locked ? "Locked chest: use a key or pick the lock" : "Open chest";

    /// <summary>A living member whose Perception reaches the chest (the secret-wall rule) sees the lock breathe.</summary>
    private bool Noticed()
    {
        if (Main.Instance is not { } main || !main.InGame) return false;
        foreach (var a in main.Party.Active)
            if (!a.Downed && a.GlobalPosition.DistanceTo(GlobalPosition) <= TrapRules.DetectRadiusCells(a.State.Stats[Characters.Stat.Perception]) * DungeonBuilder.CellSize + 1f) return true;
        return false;
    }

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        if (ResourceLoader.Exists("res://assets/models/treasure_chest/treasure_chest_1k.gltf"))
        {
            mesh.Visible = false;
            var model = GD.Load<PackedScene>("res://assets/models/treasure_chest/treasure_chest_1k.gltf").Instantiate<Node3D>();
            model.Name = "Model";
            AddChild(model);
            model.Scale = Vector3.One * 1.6f;
            model.RotateY(Mathf.Pi);
        }
        if (Locked) Tint(new Color(0.75f, 0.7f, 0.55f));
        if (Bound) BindSeam();
        if (_hidden) HideBody();
    }

    /// <summary>The bound chest's look: a thin emissive purple band around the lid.</summary>
    private void BindSeam()
    {
        var seam = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.15f, 0.8f), EmissionEnabled = true, Emission = new Color(0.55f, 0.15f, 0.8f), EmissionEnergyMultiplier = 1.4f, Roughness = 0.4f };
        var model = GetNodeOrNull<Node3D>("Model");
        var y = model != null ? 0.62f : 0.5f;
        AddChild(new MeshInstance3D { Name = "Seam", Mesh = new BoxMesh { Size = new Vector3(1.18f, 0.05f, 0.78f) }, MaterialOverride = seam, Position = new Vector3(0, y, 0) });
        AddChild(new OmniLight3D { LightColor = new Color(0.6f, 0.2f, 0.9f), LightEnergy = 0.6f, OmniRange = 3f, ShadowEnabled = false, Position = new Vector3(0, 0.9f, 0) });
    }

    private void Tint(Color color)
    {
        if (GetNodeOrNull<Node3D>("Model") is { } model)
            foreach (var mi in FindMeshes(model)) { var mat = (mi.GetActiveMaterial(0)?.Duplicate() as StandardMaterial3D) ?? new StandardMaterial3D(); mat.AlbedoColor = color; mi.SetSurfaceOverrideMaterial(0, mat); }
        else GetNode<MeshInstance3D>("Mesh").SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color });
    }

    private static IEnumerable<MeshInstance3D> FindMeshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in FindMeshes(c)) yield return m;
    }

    /// <summary>The lid as a lure, gone: no model, no solid body while the brute stands.</summary>
    private void HideBody()
    {
        _hidden = true;
        if (!IsNodeReady()) return;
        if (GetNodeOrNull<Node3D>("Model") is { } model) model.Visible = false;
        GetNode<MeshInstance3D>("Mesh").Visible = false;
        if (GetNodeOrNull<StaticBody3D>("Body") is { } body) body.CollisionLayer = 0;
    }

    private void ShowBody()
    {
        _hidden = false;
        if (!IsNodeReady()) return;
        if (GetNodeOrNull<Node3D>("Model") is { } model) model.Visible = true;
        else GetNode<MeshInstance3D>("Mesh").Visible = true;
        if (GetNodeOrNull<StaticBody3D>("Body") is { } body) body.CollisionLayer = 1;
    }

    private void Jam()
    {
        Jammed = true;
        EventBus.Instance.Log("The pick snaps off in the lock.");
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Chest(SpawnIndex), "jammed");
    }

    /// <summary>A client's or a reload's view of a jammed lock.</summary>
    public void ShowJammed() => Jammed = true;

    /// <summary>A client's view of a sprung mimic: the chest gone, the brute arriving by EnemySpawned.</summary>
    public void ShowSprung() { Sprung = true; HideBody(); }

    /// <summary>A reload mid-spring (authority): the chest hidden and the brute back, idle, so the roll is neither lost nor free.</summary>
    public void RestoreSprung()
    {
        Sprung = true;
        HideBody();
        if (Net.NetSession.IsClient || Main.Instance?.Builder is not { } builder) return;
        var floor = Game.Instance.CurrentFloor;
        if (MimicRules.BruteFor(builder.RegularEnemies(floor), builder.EnemyProfile) is { } id)
            Mimic = FortuneSpawns.Spawn(builder, id, GlobalPosition + Vector3.Up * 0.1f, floor, MimicRules.EliteName, MimicRules.Affixes, aggro: false, xpFactor: MimicRules.XpFactor);
    }

    private void Spring(PartyController party)
    {
        Sprung = true;
        HideBody();
        if (Main.Instance?.Builder is { } builder)
        {
            var floor = Game.Instance.CurrentFloor;
            if (MimicRules.BruteFor(builder.RegularEnemies(floor), builder.EnemyProfile) is { } id)
                Mimic = FortuneSpawns.Spawn(builder, id, GlobalPosition + Vector3.Up * 0.1f, floor, MimicRules.EliteName, MimicRules.Affixes, aggro: true, xpFactor: MimicRules.XpFactor);
        }
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Chest(SpawnIndex), "mimic");
        EventBus.Instance.RaiseMimicSprung(GlobalPosition);
        EventBus.Instance.PlaySfx("chest");
        EventBus.Instance.Log("The chest opens its mouth.");
    }

    /// <summary>The brute is dead: the chest is back where it stood, open, with its roll on the floor.</summary>
    public void Yield(PartyController party)
    {
        if (Opened) return;
        var toward = Mimic != null && IsInstanceValid(Mimic) ? Mimic.GlobalPosition - GlobalPosition : Vector3.Forward;
        Mimic = null;
        MarkOpened();
        EventBus.Instance.PlaySfx("chest");
        EventBus.Instance.RaiseChestOpened(GlobalPosition);
        Spill(party, toward);
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Opened || Sprung) return;
        if (IsMimic) { Spring(party); return; } // a lock on a mouth is part of the lure
        if (Locked)
        {
            if (Jammed && party.Bag.Count("item.iron_key") == 0) { EventBus.Instance.Log("The lock is jammed. Only a key will turn it."); return; }
            if (!party.TryUnlock("the chest", out var failed, GlobalPosition))
            {
                if (failed && ++LockFails >= LockRules.JamAfterFails && !Jammed) Jam();
                return;
            }
            Locked = false;
        }
        MarkOpened();
        EventBus.Instance.PlaySfx("chest");
        EventBus.Instance.RaiseChestOpened(GlobalPosition);
        Spill(party, actor.GlobalPosition - GlobalPosition);
        if (Bound) FortuneDirector.Instance?.StartHunt(party, GlobalPosition);
    }

    /// <summary>The roll (the chest's own seed, so the same on every peer and reload), spilled toward <paramref name="toward"/>, and the coins.</summary>
    private void Spill(PartyController party, Vector3 toward)
    {
        var rng = new Random(Rng.ActorSeed(Game.Instance.GameSeed, Game.Instance.CurrentFloor, 5000 + SpawnIndex));
        var cat = Game.Instance.Catalog;
        var table = cat.Table(TableId);
        var level = TierRules.ItemLevel(Game.Instance.CurrentFloor);
        var items = LootRoller.RollMany(cat, table, level, rng);
        if (ChestRules.GuaranteesConsumable(TableId) && LootRoller.Roll(cat, table with { Entries = new[] { new LootEntry("kind:Consumable", 1) }, Rolls = 1, DropChance = 1f }, level, rng) is { } extra) items.Add(extra); // a secret's chest always holds a draught
        toward.Y = 0;
        toward = toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward;
        var side = toward.Cross(Vector3.Up);
        for (var i = 0; i < items.Count; i++)
            party.SpawnLoot(items[i], GlobalPosition + toward * 1.0f + side * (i - (items.Count - 1) / 2f) * 0.7f + Vector3.Up * 0.4f);
        var coins = CoinRules.ChestCoins(Game.Instance.CurrentFloor, rng);
        if (coins > 0) party.SpawnCoins(coins, GlobalPosition + toward * 1.5f + Vector3.Up * 0.4f);
        var held = items.Select(i => i.ToString()).ToList();
        if (coins > 0) held.Add($"{coins} gold");
        EventBus.Instance.Log(held.Count == 0 ? "The chest is empty." : items.Count == 0 ? $"The chest holds {coins} gold." : coins > 0 ? $"The chest holds {string.Join(", ", held.Take(held.Count - 1))} and {coins} gold." : $"The chest holds {string.Join(", ", held)}.");
    }
}
