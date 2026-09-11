using DungeonCrawler.Adventure.Objectives;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class JailbreakTests
{
    private static RoomView Room(int i, int enemies, int fromMe, int fromThreat, bool jail = false) => new(i, enemies, fromMe, fromThreat, jail);
    private static readonly Random Rng = new(1);

    [Fact]
    public void Flee_PicksTheEmptyRoomFarthestFromTheThreat_NeverTheJail()
    {
        var rooms = new[] { Room(0, 0, 0, 1), Room(1, 2, 1, 0), Room(2, 0, 1, 3), Room(3, 0, 2, 5, jail: true), Room(4, 0, 2, 4) };
        var d = PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Wandering, 0, 3f, rooms, 0f), Rng);
        var flee = Assert.IsType<Flee>(d);
        Assert.Equal(4, flee.Room);
    }

    [Fact]
    public void Flee_SettlesForTheLeastCrowdedRoomWhenNoneIsEmpty()
    {
        var rooms = new[] { Room(0, 1, 0, 0), Room(1, 3, 1, 1), Room(2, 1, 1, 2) };
        var flee = Assert.IsType<Flee>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Wandering, 0, 2f, rooms, 0f), Rng));
        Assert.Equal(2, flee.Room);
    }

    [Fact]
    public void Quiet_WaitsThenWandersNearby()
    {
        var rooms = new[] { Room(0, 0, 0, 9), Room(1, 0, 1, 9), Room(2, 0, 5, 9), Room(3, 0, 2, 9, jail: true) };
        Assert.IsType<Wait>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Wandering, 0, 50f, rooms, 2f), Rng));
        var w = Assert.IsType<WanderTo>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Wandering, 0, 50f, rooms, PrisonerBrain.WanderEvery), Rng));
        Assert.Equal(1, w.Room);
    }

    [Fact]
    public void OtherStates_DoWhatTheyMust()
    {
        var rooms = new[] { Room(0, 0, 0, 9) };
        Assert.IsType<FollowJailer>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Captured, 0, 1f, rooms, 0f), Rng));
        Assert.IsType<Wait>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Jailed, 0, 1f, rooms, 0f), Rng));
        Assert.IsType<Wait>(PrisonerBrain.Decide(new PrisonerContext(PrisonerState.Freed, 0, 1f, rooms, 0f), Rng));
    }

    [Fact]
    public void Rules_TransitionsAndStickiness()
    {
        Assert.Equal(PrisonerState.Captured, JailbreakRules.Apply(PrisonerState.Wandering, new PrisonerCaught()));
        Assert.Equal(PrisonerState.Jailed, JailbreakRules.Apply(PrisonerState.Captured, new PrisonerDelivered()));
        Assert.Equal(PrisonerState.Wandering, JailbreakRules.Apply(PrisonerState.Captured, new JailerLost()));
        Assert.Equal(PrisonerState.Freed, JailbreakRules.Apply(PrisonerState.Jailed, new PrisonerFreed()));
        Assert.Equal(PrisonerState.Freed, JailbreakRules.Apply(PrisonerState.Wandering, new PrisonerFreed()));
        Assert.Equal(PrisonerState.Freed, JailbreakRules.Apply(PrisonerState.Freed, new PrisonerCaught()));
        Assert.Equal(PrisonerState.Jailed, JailbreakRules.Apply(PrisonerState.Jailed, new PrisonerCaught()));
        Assert.Equal(PrisonerState.Wandering, JailbreakRules.Apply(PrisonerState.Wandering, new PrisonerDelivered()));
    }

    [Fact]
    public void Completion_AndHud()
    {
        Assert.False(JailbreakRules.Completed(Array.Empty<PrisonerState>()));
        Assert.False(JailbreakRules.Completed(new[] { PrisonerState.Freed, PrisonerState.Jailed }));
        Assert.True(JailbreakRules.Completed(new[] { PrisonerState.Freed, PrisonerState.Freed }));
        Assert.Equal("Prisoners freed 1/4; 2 in the jail; 1 being taken.", JailbreakRules.HudText(new[] { PrisonerState.Freed, PrisonerState.Jailed, PrisonerState.Jailed, PrisonerState.Captured }));
        Assert.Equal("Prisoners freed 0/2.", JailbreakRules.HudText(new[] { PrisonerState.Wandering, PrisonerState.Wandering }));
        Assert.True(JailbreakRules.XpForFreeing(10) > JailbreakRules.XpForFreeing(1));
    }
}
