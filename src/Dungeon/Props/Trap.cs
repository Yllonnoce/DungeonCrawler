using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// A floor trap. Floor traps start hidden and hurt the party; traps the party places are visible and hurt enemies.
/// Once detected, a floor trap can be disarmed with E (Dexterity check) by the party's nimblest member.
/// </summary>
public partial class Trap : Area3D, IInteractable
{
    public static readonly Stats TrapStats = Stats.FromValues(new Dictionary<Stat, float> { [Stat.Accuracy] = 999, [Stat.CritChance] = 0, [Stat.CritMultiplier] = 1 });

    public TrapKind Kind { get; set; } = TrapKind.Spikes;
    public int SpawnIndex { get; set; } = -1;
    public int Floor { get; set; } = 1;
    public bool PlacedByParty { get; set; }
    public bool Detected { get; private set; }
    public bool Disarmed { get; private set; }
    public bool Triggered { get; private set; }
    public bool Armed => !Disarmed && !Triggered;

    private MeshInstance3D _mesh = null!;
    private float _flash;

    public string Prompt => Armed && Detected && !PlacedByParty ? $"Disarm {Kind.ToString().ToLower()} trap" : "";

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("Mesh");
        // once noticed, a plate is a slab of slightly wrong stone, not a beacon: the HUD prompt names it when you look at it
        _mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = SlabColor(), Roughness = 1f });
        _mesh.Scale = new Vector3(0.85f, 0.6f, 0.85f);
        if (PlacedByParty) { Detected = true; }
        UpdateVisibility();
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Floor-coloured stone with the faintest tint of the trap's kind.</summary>
    private Color SlabColor() => new Color(0.2f, 0.19f, 0.18f).Lerp(BaseColor(), 0.06f);

    private Color BaseColor() => Kind switch
    {
        TrapKind.Fire => new Color(0.9f, 0.4f, 0.1f),
        TrapKind.Gas => new Color(0.4f, 0.8f, 0.3f),
        _ => new Color(0.7f, 0.7f, 0.75f),
    };

    private void UpdateVisibility()
    {
        Visible = Detected && Armed;
        CollisionLayer = Detected && Armed && !PlacedByParty ? 8u : 0u; // only detected floor traps are interactable
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0) return;
        _flash -= (float)delta;
        if (_flash <= 0) UpdateVisibility();
    }

    public void Reveal()
    {
        if (Detected) return;
        Detected = true;
        UpdateVisibility();
        if (SpawnIndex >= 0) EventBus.Instance.RaisePropChanged(Net.NetRefs.Trap(SpawnIndex), "revealed");
    }

    public void Disarm()
    {
        Disarmed = true;
        UpdateVisibility();
        if (SpawnIndex >= 0) EventBus.Instance.RaisePropChanged(Net.NetRefs.Trap(SpawnIndex), "disarmed");
    }

    /// <summary>A client showing a trap the server sprang: the flash, then gone.</summary>
    public void ShowSprung()
    {
        Triggered = true;
        Detected = true;
        Flash();
    }

    /// <summary>Restore from a save without side effects.</summary>
    public void RestoreState(bool triggered, bool disarmed)
    {
        Triggered = triggered;
        Disarmed = disarmed;
        Detected = Detected || triggered || disarmed;
        UpdateVisibility();
    }

    private void OnBodyEntered(Node3D body)
    {
        if (Net.NetSession.IsClient) return; // the server springs traps and tells everyone
        if (!Armed) return;
        if (PlacedByParty)
        {
            if (body is Enemy { IsAlive: true } enemy) Spring(enemy);
        }
        else if (body is Adventurer adv)
        {
            Spring(adv);
        }
    }

    private void Spring(Enemy enemy)
    {
        Triggered = true;
        var r = enemy.ReceiveAttack(TrapStats, TrapRules.Attack(Kind, Floor), new HitContext { DefenderStunned = true }, Main.Instance.Party.Rng, $"{Kind} trap");
        EventBus.Instance.Log($"{enemy.DisplayName} springs the {Kind.ToString().ToLower()} trap for {r.Amount:0}{(r.Applied.Count > 0 ? $" [{string.Join(", ", r.Applied.Select(a => a.Kind))}]" : "")}.");
        Flash();
        if (SpawnIndex >= 0) EventBus.Instance.RaisePropChanged(Net.NetRefs.Trap(SpawnIndex), "sprung");
    }

    public void Spring(Adventurer victim)
    {
        if (!Armed) return;
        Triggered = true;
        Detected = true;
        EventBus.Instance.PlaySfx("trap");
        EventBus.Instance.Log($"A {Kind.ToString().ToLower()} trap springs under {victim.State.Name}!");
        victim.ApplyTrap(Kind, Floor);
        Flash();
        if (SpawnIndex >= 0) EventBus.Instance.RaisePropChanged(Net.NetRefs.Trap(SpawnIndex), "sprung");
    }

    private void Flash()
    {
        Visible = true;
        _mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = Colors.White, EmissionEnabled = true, Emission = BaseColor(), EmissionEnergyMultiplier = 3f });
        _flash = 0.5f;
        CollisionLayer = 0;
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (PlacedByParty || !Armed || !Detected) return;
        party.TryDisarm(this, actor);
    }
}
