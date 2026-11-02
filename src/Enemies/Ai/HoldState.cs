using Godot;

namespace DungeonCrawler.Enemies.Ai;

/// <summary>Stands at its post, awake (no sneak attacks), and gives chase the moment it sees the party.</summary>
public sealed class HoldState : AiState
{
    public override string Name => "Hold";
    public override bool Unaware => false;
    /// <summary>Farther than this (m, flat) from its post, a holder walks back; it stops again within <see cref="ArriveDistance"/>.</summary>
    public const float StrayDistance = 1.5f;
    public const float ArriveDistance = 0.5f;
    private bool _returning;

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.CanSeeParty()) { enemy.ChangeState(new ChaseState()); return; }
        var home = enemy.HomePosition;
        var away = new Vector2(enemy.GlobalPosition.X - home.X, enemy.GlobalPosition.Z - home.Z).Length();
        if (away > StrayDistance) _returning = true;
        else if (away <= ArriveDistance) _returning = false;
        // back to the post after a chase that lost the party, then stand there awake
        if (_returning) enemy.MoveAlongPathToward(home, dt);
        else enemy.MoveToward(enemy.GlobalPosition, dt); // keep gravity active
    }
}
