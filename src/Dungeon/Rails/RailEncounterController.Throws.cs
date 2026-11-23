using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Combat.Fx;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A boarder alone with the prisoners throws them off (plan 14 Task 5, spec A.4, Rulings 16-17), server and solo, while a ride is armed. Each
/// coupled wagon has its <see cref="ThrowClock"/>, stepped each physics tick (after <see cref="TickAnswers"/>) with the wagon alone
/// (<see cref="BoarderRules.Alone"/>: its boarder aboard, crew seated in it, no hero up in it, the train rolling) and paused while the train rolls
/// and moves are refused for everyone (<see cref="RailController.MovesRefused"/>: a seated lever window, the live bridge).
/// <list type="bullet">
/// <item>An alone stretch begins: the seated up hero of the train nearest the wagon calls it (<see cref="RailCalls.SayNearest"/>,
/// <see cref="BarkTrigger.BoarderThrow"/>); with nobody to speak the log reads "It's at the prisoners — wagon {k}!".</item>
/// <item><see cref="BoarderRules.ThrowGlow"/> before each throw the hound crouches over the crew with the hazard warning's glow
/// (<see cref="TelegraphFx.HandGlow"/>, as the leap's crouch).</item>
/// <item>At <see cref="BoarderRules.ThrowFirst"/>, and each <see cref="BoarderRules.ThrowEvery"/> after, it throws one crew member off, the
/// rear bench seat first (<see cref="BoarderRules.ThrowSeat"/>), through <see cref="CrewRescueController.ThrowCrew"/>.</item>
/// </list>
/// The clocks go with the ride (<see cref="ClearThrows"/>).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>Each wagon's throw clock, by wagon number (made on first need).</summary>
    private readonly Dictionary<int, ThrowClock> _throwClocks = new();

    /// <summary>Prisoners thrown off on this floor build (tests read it).</summary>
    internal int Throws { get; private set; }

    /// <summary>Every call, glow and throw on this floor build (tests read them): the wagon, the step, the physics frame, the seconds counted and paused, and who (the caller, or the crew member thrown).</summary>
    internal readonly List<(int Wagon, ThrowStep Step, ulong Frame, float Counted, float Paused, string Who)> ThrowLog = new();

    /// <summary>Wagon <paramref name="wagon"/>'s throw clock now (tests read it), or null before its first tick.</summary>
    internal ThrowClock? ThrowClockOf(int wagon) => _throwClocks.GetValueOrDefault(wagon);

    /// <summary>Test hook: told after each wagon's clock steps (the wagon, its clock), in the physics tick.</summary>
    internal Action<int, ThrowClock>? DebugThrowTick { get; set; }

    /// <summary>Each physics tick while a ride is armed (server and solo): every wagon's clock steps, and its call, glow or throw happens.</summary>
    private void TickThrows(float dt)
    {
        if (ArmedRide < 1 || !IsInstanceValid(_rails.Cart)) return;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        var paused = rolling && _rails.MovesRefused;
        foreach (var wagon in _rails.Wagons)
        {
            if (!IsInstanceValid(wagon)) continue;
            if (!_throwClocks.TryGetValue(wagon.Number, out var clock)) _throwClocks[wagon.Number] = clock = new ThrowClock();
            var coupled = wagon.Coupled && _rails.WagonAt(wagon.Number) == wagon;
            var boarder = coupled ? BoarderOn(wagon) : null;
            var crew = coupled ? CrewIn(wagon) : 0;
            var step = clock.Step(dt, BoarderRules.Alone(boarder != null, crew, coupled ? UpHeroesIn(wagon) : 0, rolling), paused);
            if (step != ThrowStep.None && boarder != null) OnThrowStep(wagon, boarder, clock, step, crew);
            DebugThrowTick?.Invoke(wagon.Number, clock);
        }
    }

    private void OnThrowStep(TrainWagon wagon, Enemy hound, ThrowClock clock, ThrowStep step, int crew)
    {
        var k = wagon.Number;
        switch (step)
        {
            case ThrowStep.Call:
            {
                var who = RailCalls.SayNearest(Main.Instance.Party, wagon, BarkTrigger.BoarderThrow, $"It's at the prisoners — wagon {k}!");
                ThrowLog.Add((k, step, Engine.GetPhysicsFrames(), clock.Running, clock.Paused, who ?? "the log"));
                GD.Print($"[rails] t={_now:0.00} wagon {k}: {hound.Name} alone with {crew} crew; called by {who ?? "the log"}");
                break;
            }
            case ThrowStep.Glow:
                TelegraphFx.HandGlow(hound, BoarderRules.ThrowGlow); // the crouch over the crew: the hazard warning's glow, as the leap's
                ThrowLog.Add((k, step, Engine.GetPhysicsFrames(), clock.Running, clock.Paused, hound.Name));
                GD.Print($"[rails] t={_now:0.00} {hound.Name} crouches over the crew of wagon {k} ({clock.Running:0.0} s alone)");
                break;
            case ThrowStep.Throw:
            {
                if (ThrowTarget(wagon) is not var (npc, seat))
                {
                    GD.Print($"[rails] t={_now:0.00} {hound.Name} on wagon {k}: no crew member in a bench seat to throw");
                    break;
                }
                if (ObjectiveController.Instance is not CrewRescueController crewCtrl || !crewCtrl.ThrowCrew(npc, wagon)) break;
                Throws++;
                hound.PlayAnim("Punch", force: true);
                ThrowLog.Add((k, step, Engine.GetPhysicsFrames(), clock.Running, clock.Paused, npc.NpcName));
                GD.Print($"[rails] t={_now:0.00} {hound.Name} throws {npc.NpcName} off wagon {k} (seat {seat}) after {clock.Running:0.0} s alone ({clock.Paused:0.0} s paused)");
                break;
            }
        }
    }

    /// <summary>The crew member a throw takes off <paramref name="wagon"/> (<see cref="BoarderRules.ThrowSeat"/>: seat 5's, then 4's), or null.</summary>
    private static (Npc Npc, int Seat)? ThrowTarget(TrainWagon wagon)
    {
        Npc? at4 = null, at5 = null;
        foreach (var rider in wagon.Riders.Values)
        {
            if (rider is not Npc { Crew: true, Dead: false } npc || !IsInstanceValid(npc)) continue;
            if (npc.Seat == TrainRules.CrewSeats[0]) at4 = npc;
            else if (npc.Seat == TrainRules.CrewSeats[1]) at5 = npc;
        }
        return BoarderRules.ThrowSeat(at4 != null, at5 != null) switch
        {
            5 => (at5!, 5),
            4 => (at4!, 4),
            _ => null,
        };
    }

    /// <summary>The ride is over (or undone): every wagon's throw clock goes with it.</summary>
    private void ClearThrows() => _throwClocks.Clear();
}
