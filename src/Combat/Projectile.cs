using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Combat;

/// <summary>Arrows and spell bolts. Flies straight, resolves a hit on the first enemy or wall it touches.</summary>
public partial class Projectile : Area3D
{
    public CharacterState? Attacker { get; private set; }
    public AttackProfile Attack { get; private set; } = null!;
    public PartyController Party { get; private set; } = null!;
    public Enemy? Shooter { get; private set; }
    public bool FromEnemy => Shooter != null;
    public bool IsArrow { get; private set; }
    public Vector3 Velocity { get; private set; }
    public float Lifetime { get; set; } = 3f;

    private bool _spent;

    public Adventurer? Source { get; private set; }

    public void Setup(PartyController party, CharacterState attacker, AttackProfile attack, Vector3 velocity, bool isArrow, Color color, Adventurer? source = null)
    {
        Party = party;
        Source = source;
        Attacker = attacker;
        Attack = attack;
        Velocity = velocity;
        IsArrow = isArrow;

        var mesh = GetNode<MeshInstance3D>("Mesh");
        var mat = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = isArrow ? 0.2f : 2.5f };
        mesh.SetSurfaceOverrideMaterial(0, mat);
        if (isArrow) mesh.Scale = new Vector3(0.35f, 0.35f, 3.0f);
        var light = GetNode<OmniLight3D>("Light");
        light.LightColor = color;
        light.Visible = !isArrow;
        if (velocity.LengthSquared() > 0.001f) LookAt(GlobalPosition + velocity, Vector3.Up);
        Color = color;
        EventBus.Instance.RaiseProjectileFired(this);
    }

    public Color Color { get; private set; }
    /// <summary>A client's copy: flies and stops on contact, hurts nothing.</summary>
    public bool Cosmetic { get; private set; }
    public int SourceMember => Source?.Index ?? -1;

    public void SetupCosmetic(Vector3 velocity, bool isArrow, Color color)
    {
        Cosmetic = true;
        Velocity = velocity;
        IsArrow = isArrow;
        Color = color;
        var mesh = GetNode<MeshInstance3D>("Mesh");
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = isArrow ? 0.2f : 2.5f });
        if (isArrow) mesh.Scale = new Vector3(0.35f, 0.35f, 3.0f);
        var light = GetNode<OmniLight3D>("Light");
        light.LightColor = color;
        light.Visible = !isArrow;
        CollisionMask = 5; // walls and enemies stop it; it never touches a body's stats
        if (velocity.LengthSquared() > 0.001f) LookAt(GlobalPosition + velocity, Vector3.Up);
    }

    /// <summary>An enemy's bolt: hits the party body (layer 2) and walls.</summary>
    public void SetupEnemy(Enemy shooter, AttackProfile attack, Vector3 velocity, Color color)
    {
        Shooter = shooter;
        Attack = attack;
        Velocity = velocity;
        CollisionMask = 3;
        var mesh = GetNode<MeshInstance3D>("Mesh");
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 2f });
        GetNode<OmniLight3D>("Light").LightColor = color;
        Color = color;
        if (velocity.LengthSquared() > 0.001f) LookAt(GlobalPosition + velocity, Vector3.Up);
        EventBus.Instance.RaiseProjectileFired(this);
    }

    public override void _Ready() => BodyEntered += OnBodyEntered;

    public override void _PhysicsProcess(double delta)
    {
        if (_spent) return;
        var dt = (float)delta;
        GlobalPosition += Velocity * dt;
        Lifetime -= dt;
        if (Lifetime <= 0) QueueFree();
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_spent) return;
        if (Cosmetic) { _spent = true; QueueFree(); return; }
        if (FromEnemy)
        {
            if (body is Adventurer adv)
            {
                _spent = true;
                if (Shooter != null && IsInstanceValid(Shooter)) adv.ReceiveAttack(Shooter.Stats, Attack, Shooter.DisplayName, Shooter);
                QueueFree();
            }
            else if (body is not Enemy)
            {
                _spent = true;
                Main.Instance?.Builder.TripPlateAt(GlobalPosition, this);
                QueueFree();
            }
            return;
        }
        if (Attacker == null) return;
        switch (body)
        {
            case Enemy { IsAlive: true } enemy:
                _spent = true;
                var ctx = new HitContext { SneakAttack = enemy.IsUnaware, SneakMultiplier = Attacker.Class.SneakAttackMultiplier };
                var result = enemy.ReceiveAttack(Attacker.Stats, Attack, ctx, Party.Rng, Attacker.Name);
                Party.LogResult(Attacker.Name, enemy.DisplayName, result, heavy: false, perfect: false);
                Party.OnHitLanded(Attacker, enemy, result);
                if (Source != null && IsInstanceValid(Source)) { enemy.NoteThreat(Source, result.Amount + 5f); if (Source.IsLocal) Party.LeadTarget = enemy; }
                if (IsArrow && result.Hit) enemy.EmbeddedArrows++;
                else if (IsArrow) Party.SpawnArrowPickup(GlobalPosition, 0.5f);
                QueueFree();
                break;
            case Enemy:
                break; // corpses do not stop projectiles
            case ICombatTarget { IsAlive: true } target:
                _spent = true;
                var r = target.ReceiveAttack(Attacker.Stats, Attack, HitContext.None, Party.Rng, Attacker.Name);
                if (IsArrow) Party.SpawnArrowPickup(GlobalPosition, 0.5f);
                QueueFree();
                break;
            default:
                _spent = true;
                Main.Instance?.Builder.TripPlateAt(GlobalPosition, this); // an arrow on a plate sets the hazard off from a safe distance
                if (IsArrow) Party.SpawnArrowPickup(GlobalPosition - Velocity.Normalized() * 0.4f, 0.6f);
                QueueFree();
                break;
        }
    }
}
