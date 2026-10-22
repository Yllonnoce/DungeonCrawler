using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class NpcFlightTests
{
    // a 9x9 open floor with a wall column at x = 6
    private static bool Open(GridPos p) => p.X >= 0 && p.X < 9 && p.Y >= 0 && p.Y < 9 && p.X != 6;

    [Fact]
    public void PickFleeCell_RunsAwayFromTheThreat_ToAWalkableCell()
    {
        var npc = new GridPos(4, 4);
        var threat = new GridPos(2, 4);   // threat to the west
        var cell = NpcFlight.PickFleeCell(npc, threat, Open, NpcFlight.FleeSteps);
        Assert.NotNull(cell);
        Assert.True(Open(cell!.Value));
        Assert.True(cell.Value.X > npc.X, $"should run east, got {cell}");
        Assert.True(Distance(cell.Value, threat) > Distance(npc, threat));
    }

    [Fact]
    public void PickFleeCell_StaysOnTheFloor_WhenTheWayIsBlocked()
    {
        // the wall column at x = 6 blocks a straight run east: the pick must still be open and farther from the threat
        var npc = new GridPos(5, 4);
        var threat = new GridPos(3, 4);
        var cell = NpcFlight.PickFleeCell(npc, threat, Open, NpcFlight.FleeSteps);
        Assert.NotNull(cell);
        Assert.True(Open(cell!.Value));
        Assert.True(Distance(cell.Value, threat) > Distance(npc, threat));
    }

    [Fact]
    public void PickFleeCell_IsNull_WhenNothingIsOpen()
    {
        Assert.Null(NpcFlight.PickFleeCell(new GridPos(4, 4), new GridPos(2, 4), _ => false, NpcFlight.FleeSteps));
    }

    private static double Distance(GridPos a, GridPos b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

public class NpcFlightCrowdTests
{
    private static bool Open(GridPos p) => p.X >= 0 && p.X < 9 && p.Y >= 0 && p.Y < 9;

    [Fact]
    public void PickFleeCell_AvoidsCellsOtherNpcsAlreadyClaimed()
    {
        var npc = new GridPos(4, 4);
        var threat = new GridPos(2, 4);
        var first = NpcFlight.PickFleeCell(npc, threat, Open, NpcFlight.FleeSteps);
        Assert.NotNull(first);
        var second = NpcFlight.PickFleeCell(npc, threat, Open, NpcFlight.FleeSteps, taken: c => c == first.Value);
        Assert.NotNull(second);
        Assert.NotEqual(first.Value, second!.Value);
        Assert.True(Open(second.Value));
    }
}
