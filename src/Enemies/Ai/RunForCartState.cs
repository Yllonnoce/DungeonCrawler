using Godot;

namespace DungeonCrawler.Enemies.Ai;

/// <summary>
/// A hijacker running for the party's train (plan 16 Task 6, spec section 3): it walks the corridors to <c>goal()</c> (the cart's side, or a
/// wagon's, read each tick: the train may be rolling back) and nothing else; its owner (<see cref="Dungeon.Rails.StopTrapController"/>) judges
/// the climb, a hit's fight back and its release. Never unaware: it is a creature on the attack, so no sneak attack lands on it.
/// </summary>
public sealed class RunForCartState : AiState
{
    private readonly Func<Vector3> _goal;

    public RunForCartState(Func<Vector3> goal) => _goal = goal;

    public override string Name => "RunForCart";
    public override bool Unaware => false;

    public override void Update(Enemy enemy, float dt) => enemy.MoveAlongPathToward(_goal(), dt);
}
