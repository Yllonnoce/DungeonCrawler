namespace DungeonCrawler.Enemies.Ai;

/// <summary>Stands at its post, awake (no sneak attacks), and gives chase the moment it sees the party.</summary>
public sealed class HoldState : AiState
{
    public override string Name => "Hold";
    public override bool Unaware => false;
    public override void Update(Enemy enemy, float dt)
    {
        enemy.MoveToward(enemy.GlobalPosition, dt); // keep gravity active
        if (enemy.CanSeeParty()) enemy.ChangeState(new ChaseState());
    }
}
