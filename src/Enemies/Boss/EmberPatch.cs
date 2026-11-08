using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>A patch of burning floor that crawls around the arena's centre (the Colossus's phase two). Everyone moves it; the server's FloorFire burns.</summary>
public partial class EmberPatch : Node3D
{
    public int Floor { get; set; } = 1;
    public Vector3 Centre { get; set; }
    public float Phase { get; set; }

    public override void _Ready()
    {
        AddChild(new FloorFire { Floor = Floor, Name = "Fire" });
        Place();
    }

    public override void _PhysicsProcess(double delta)
    {
        Phase += (float)delta * BossRules.EmberSpeed;
        Place();
    }

    private void Place() => GlobalPosition = Centre + new Vector3(Mathf.Cos(Phase) * BossRules.EmberOrbit, 0, Mathf.Sin(Phase) * BossRules.EmberOrbit);
}
