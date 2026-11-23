using DungeonCrawler.Adventure;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's lava pit (plan 15 Task 4, spec B.4; Rulings 13, 17, 18). The layout cuts one party-lane cell to lava (<see cref="Generation.RailLine.Pit"/>).
/// Every role reads it before the set pieces (<see cref="ReadPit"/>), whether or not a <c>pit</c> piece names it: the party track is cut bare
/// over it from its lip to its far edge and the <see cref="LavaPit"/> dressing stands at the lip. A <c>switch_out</c> of the pit's ride whose
/// points stand 7-40 m short of the lip is deadly (<see cref="PitRules.Deadly"/>): ride 3's machinery moved to the out lever (a human's lever,
/// layers 1|8, in the plain seated window), called twice (<see cref="TickPitCalls"/>), taken round the pit with no human aboard able to throw
/// it (R-lone, in <see cref="TakeSwitch"/>), and its plain fixed back brings the train home past the far edge. The server judges the fall
/// (<see cref="TickPit"/>): a train whose front meets the lip on its own lane runs into the lava. A ride where a correct throw could still meet
/// the lip has its fall disarmed (<see cref="GuardPit"/>), and the watchdog steps a stuck train down without carrying it past the pit
/// (<see cref="WatchdogPit"/>).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The party lane's S at the pit's lip (its near edge), or NaN on a floor without a pit.</summary>
    public float PitS { get; private set; } = float.NaN;
    /// <summary>The party lane's S at the pit's far edge, or NaN.</summary>
    public float PitFarS { get; private set; } = float.NaN;
    /// <summary>The pit's ride (1-based), or −1 without one.</summary>
    public int PitRide { get; private set; } = -1;
    /// <summary>The train falls into the pit (<see cref="TickPit"/>): false on a floor without one, or a ride where a correct throw could still meet it (<see cref="GuardPit"/>).</summary>
    public bool PitArmed { get; private set; }
    /// <summary>Trains that ran into the pit on this floor build (tests read it).</summary>
    internal int PitFalls { get; private set; }
    /// <summary>The pit's dressing (the flows' sight check reads its lamp), or null.</summary>
    internal LavaPit? PitLook { get; private set; }
    /// <summary>A <c>pit</c> piece named the layout's pit.</summary>
    private bool _pitPiece;

    /// <summary>The pit's deadly switch_out's lever (the last before the lip), or null (the flows read it).</summary>
    internal TrackSwitch? PitSwitch
    {
        get
        {
            Piece? deadly = null;
            foreach (var p in _pieces)
                if (p.PitDeadly && (deadly == null || p.S > deadly.S)) deadly = p;
            return deadly?.Switch;
        }
    }

    /// <summary>The plan id of the pit's deadly switch_out (r5_switch_out on the shipped floor), or null.</summary>
    internal string? PitSwitchId
    {
        get
        {
            foreach (var p in _pieces)
                if (p.PitDeadly && p.Switch == PitSwitch) return p.Plan.Id;
            return null;
        }
    }

    /// <summary>
    /// The layout's pit (every role, before the set pieces; Ruling 17): its lip and far edge on the party lane, the party track cut bare over
    /// [lip, far edge] and the dressing at the lip. A pit no piece names stands all the same (an error, after the pieces).
    /// </summary>
    private void ReadPit(Node3D root)
    {
        if (_layout.Rail is not { Pit: { } pit } rail || !IsInstanceValid(_rails.Party.Track.Track)) return;
        if (pit.Cell < 0 || pit.Cell >= rail.Party.Count) return;
        var cell = rail.Party[pit.Cell];
        var track = _rails.Party.Track;
        var mid = track.ClosestS(_builder.Surface(cell));
        var half = DungeonBuilder.CellSize / 2f;
        PitS = mid - half;
        PitFarS = mid + half;
        PitRide = pit.Ride;
        track.Track.CutMore(new[] { (PitS, PitFarS) }); // the party line's rails and sleepers end at the lip and start again past the far edge (its other gaps kept)
        var frame = track.Frame(PitS).Orthonormalized();
        var slagSide = _enemy.Valid && (_enemy.Point(Lanes.EnemyS(PitS)) - frame.Origin).Dot(frame.Basis.X) < 0f ? -1 : 1;
        PitLook = new LavaPit { Name = $"LavaPit_r{pit.Ride}", PitLength = PitFarS - PitS, SlagSide = slagSide };
        root.AddChild(PitLook);
        PitLook.GlobalTransform = frame; // −Z along the line: across the pit
        GD.Print($"[rails] pit on ride {pit.Ride}: cell {pit.Cell} {cell}, lip at party S={PitS:0.0}, far edge {PitFarS:0.0}; the party track bare over it");
    }

    /// <summary>
    /// A <c>pit</c> piece (Ruling 17): the plan's entry for the layout's pit, giving its order and its dressing (the piece's node). Its At more than
    /// <see cref="PitRules.AtSlack"/> from the lip, or a piece on a floor without a pit, is an error; the layout's pit is what counts.
    /// </summary>
    private Node3D? BuildPit(Node3D root, Piece piece)
    {
        if (float.IsNaN(PitS) || piece.Plan.Ride != PitRide)
        {
            GD.PushError($"[rails] ERROR: pit {piece.Plan.Id} on ride {piece.Plan.Ride} at S={piece.S:0.0}, but the layout has no pit on that ride (pit ride {PitRide})");
            var marker = new Node3D { Name = $"Pit_{piece.Plan.Id}" };
            root.AddChild(marker);
            return marker;
        }
        _pitPiece = true;
        var off = piece.S - PitS;
        GD.Print($"[rails] pit {piece.Plan.Id}: At S={piece.S:0.0} ({off:0.0} m from the lip)");
        if (Mathf.Abs(off) > PitRules.AtSlack) GD.PushError($"[rails] ERROR: pit {piece.Plan.Id}'s At (S={piece.S:0.0}) lies {Mathf.Abs(off):0.0} m from the layout's lip at S={PitS:0.0} (more than {PitRules.AtSlack:0} m): the layout's pit stands");
        return PitLook;
    }

    /// <summary>
    /// The guard (every role, after the set pieces): the pit's fall is armed only if every switch_out of its ride short of the lip stands at least
    /// <see cref="PitRules.ArmLead"/> short of it and each one's next switch back stands at or past the far edge (<see cref="PitRules.FallArmed"/>).
    /// Otherwise a correct throw could fall: an error line, and the fall is disarmed. A layout pit with no piece is an error too.
    /// </summary>
    private void GuardPit()
    {
        PitArmed = false;
        if (float.IsNaN(PitS)) return;
        if (!_pitPiece) GD.PushError($"[rails] ERROR: the layout's pit on ride {PitRide} (lip at S={PitS:0.0}) has no pit piece in the plan: it is cut and guarded all the same");
        var outs = new List<float>();
        var backs = new List<float>();
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != PitRide) continue;
            if (p.Plan.Kind == "switch_out") outs.Add(p.S);
            else if (p.Plan.Kind == "switch_back") backs.Add(p.S);
        }
        PitArmed = PitRules.FallArmed(outs, backs, PitS, PitFarS);
        var switches = $"switch_out at [{string.Join(", ", outs.Select(s => s.ToString("0.0")))}], switch_back at [{string.Join(", ", backs.Select(s => s.ToString("0.0")))}]";
        if (PitArmed) GD.Print($"[rails] pit on ride {PitRide}: its fall armed ({switches})");
        else GD.PushError($"[rails] ERROR: pit on ride {PitRide}: no switch_out at least {PitRules.ArmLead:0} m short of its lip at S={PitS:0.0} with its switch back at or past the far edge {PitFarS:0.0} ({switches}): the pit's fall is disarmed on ride {PitRide}, so a correct throw cannot fall");
    }

    /// <summary>
    /// Server, each tick after the cart stepped (beside <see cref="TickBreak"/>): the party's train, rolling, whose front met the pit's lip this step
    /// on its own lane (<see cref="RailController.LaneBlendAt"/> under 0.5 there) runs into the lava: a crevasse fall, the whole train and its wagons.
    /// </summary>
    private void TickPit()
    {
        if (float.IsNaN(PitS) || !PitArmed || _rails.State.Mode != CartMode.Rolling) return;
        var run = _rails.Party;
        var prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS);
        if (!PitRules.Falls(prevFront, run.FrontS, PitS, _rails.LaneBlendAt(PitS))) return;
        var front = run.FrontS;
        PitFalls++;
        _rails.Fall(PitRules.FallLog);
        GD.Print($"[rails] the cart runs into the pit at S={front:0.0}");
    }

    /// <summary>
    /// The pit switch's calls (server; Ruling 39): a seated hero calls it early, <see cref="PitRules.CallEarly"/> before the lever, and at the last
    /// moment, <see cref="PitRules.CallLast"/> before the points, while its lever is straight and untaken (<see cref="PitRules.Call"/>). Each once an arming.
    /// </summary>
    private void TickPitCalls(Piece piece, PartyController party)
    {
        if (!piece.PitDeadly || piece.Fired || piece.Switch is not { } sw || _rails.State.Mode != CartMode.Rolling) return;
        var front = _rails.Party.FrontS;
        var call = PitRules.Call(sw.Thrown, sw.Taken, front, sw.LeverS, piece.S, piece.Barked, piece.BarkedLast);
        if (call == BackCall.None) return;
        piece.Barked = true; // the last call stands for the early one too
        if (call == BackCall.Last) piece.BarkedLast = true;
        var by = call == BackCall.Early
            ? RailCalls.Say(party, _rails.Cart, BarkTrigger.PitSwitch, PitRules.EarlyLog)
            : RailCalls.Say(party, _rails.Cart, BarkTrigger.PitSwitchNow, PitRules.LastLog);
        GD.Print(call == BackCall.Early
            ? $"[rails] switch {piece.Plan.Id} called early at front S={front:0.0} ({sw.LeverS - front:0.0} m to the lever); bark={by ?? "none"}"
            : $"[rails] switch {piece.Plan.Id} called last at front S={front:0.0} ({piece.S - front:0.0} m to the points); bark={by ?? "none"}");
    }

    /// <summary>The pit's lip when it lies between party S <paramref name="fromS"/> and <paramref name="toS"/> (a held stop and the next: the traffic runaway halts short of it, B.3), else NaN.</summary>
    private float PitLipBefore(float fromS, float toS) => PitS > fromS && PitS < toS ? PitS : float.NaN; // NaN compares false

    /// <summary>
    /// The watchdog's view of the pit (server, <see cref="RailController"/>'s grind, all front S; Ruling 18): with the pit's fall armed on the armed
    /// ride and the train on its own lane, short of the deadly switch_out's points with its lever straight, where short of them it frees the train
    /// (<see cref="SwitchBackRules.GrindBackTo"/> over the out lever: the seated window's start or the last call's mark) and the points (taken there
    /// as the lever stands); past the points on the party lane and short of the lip, the lip (it falls there). Stuck across, nothing: it is freed to
    /// the next stop as ever. NaN for what is not there.
    /// </summary>
    internal (float Window, float Points, float Lip) WatchdogPit()
    {
        var none = (float.NaN, float.NaN, float.NaN);
        if (float.IsNaN(PitS) || !PitArmed || ArmedRide != PitRide || _rails.Crossing) return none;
        var front = _rails.Party.FrontS;
        if (!(front < PitS)) return none;
        Piece? deadly = null;
        foreach (var p in _pieces)
            if (p.PitDeadly && p.Plan.Ride == PitRide && (deadly == null || p.S > deadly.S)) deadly = p; // the last before the lip
        if (deadly?.Switch is not { } sw) return none;
        if (!deadly.Fired && front < deadly.S)
        {
            var to = SwitchBackRules.GrindBackTo(front, sw.LeverS, deadly.S, sw.Thrown);
            return (to < deadly.S ? to : float.NaN, deadly.S, float.NaN);
        }
        return _rails.LaneBlendAt(PitS) < 0.5f ? (float.NaN, float.NaN, PitS) : none;
    }
}
