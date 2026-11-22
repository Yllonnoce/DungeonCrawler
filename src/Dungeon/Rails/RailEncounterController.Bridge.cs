using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 13's collapsing bridge on ride 4 (a set piece "bridge", Rulings 2-8): the cavern's party-lane deck cells (<see cref="CrevasseRules.CavernDecks"/>),
/// built from the layout, not from the piece's At, which must lie within <see cref="BridgeRules.AtSlack"/> of the span's start on a flat cavern
/// (<see cref="LeapRules.Flat"/>); otherwise it is dead, a plain deck. Live, the party track is cut bare over the span and a <see cref="CavernBridge"/>
/// lays each cell's rails. The server (and single player) runs it while its ride is armed: the cart's front coming onto it groans; a grace
/// (<see cref="BridgeRules.Grace"/>) after the train's rear came onto it, it starts to go from its start at <see cref="BridgeRules.Pace"/>, called by a
/// seated hero; each cell goes as the front passes its far edge. Unbraked, the front holds <see cref="BridgeRules.HoldShort"/> behind the train's rear;
/// with a seated player's brake in the last <see cref="BridgeRules.BrakeMemory"/> it runs on and takes the train (<see cref="RailController.Fall"/>),
/// "Don't stop" called once as a brake comes with it near. A wreck or a fall stops it where it stands; an arrival does not (it runs on to the end);
/// ride 4's next arming, a restore and a spare's takeover stand it whole again. While the train rolls on it at 1 m/s or more nobody gets out
/// (<see cref="HoldsRiders"/>). The ride's runaway whose front is on the gone span goes down with it, freed plainly (neither rammed nor plunged).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The live bridge's party S span (its first cell's near edge to its last cell's far edge), NaN with no live bridge.</summary>
    public float BridgeFromS { get; private set; } = float.NaN;
    public float BridgeToS { get; private set; } = float.NaN;
    /// <summary>The collapse's front (party S), NaN while the bridge stands.</summary>
    public float CollapseS { get; private set; } = float.NaN;
    /// <summary>The live bridge's deck cells.</summary>
    public int BridgeCells { get; private set; }
    /// <summary>The live bridge's cells gone now.</summary>
    public int BridgeGone => _bridgePiece?.Bridge is { } b && IsInstanceValid(b) ? b.Gone : 0;
    /// <summary>Trains the bridge took on this floor build (tests read it).</summary>
    internal int BridgeTakes { get; private set; }
    /// <summary>The ride runaway the bridge took last: its flags as it went and its front's S (tests read it), or null.</summary>
    internal (bool Rammed, bool Plunged, float FrontS)? BridgeRunawayTaken { get; private set; }
    /// <summary>When the first call was made this arming (<c>NetSession.Instance.Now</c>), or NaN (tests read it).</summary>
    internal double BridgeCalledAt => _bridgeCalledAt;
    /// <summary>The stall guard held the front short of the train's rear this arming (tests read it).</summary>
    internal bool BridgeHeldByStall => _bridgeHeldLogged;

    private Piece? _bridgePiece;
    private double _bridgeRearOnAt = double.NaN;
    /// <summary>When the first call was made (<c>NetSession.Instance.Now</c>, the brakes' clock), or NaN: a brake counts only when pressed a reaction time after it (<see cref="BridgeRules.Counts"/>).</summary>
    private double _bridgeCalledAt = double.NaN;
    private float _bridgePace;
    /// <summary>A wreck or a fall stopped it where it stands, until it stands again.</summary>
    private bool _bridgeStopped;
    private bool _bridgeGroaned, _bridgeHeldLogged, _bridgeBrakeCalled;
    private int _bridgeLogged;
    /// <summary>The ride whose bridge a test holds (<see cref="DebugHoldBridge"/>), or −1.</summary>
    private int _heldBridgeRide = -1;

    /// <summary>
    /// The bridge piece (every role): the party half of the cavern's decks, each cell's span from its centre's party S (cells meet at the
    /// midpoints between centres); live when its At lies within <see cref="BridgeRules.AtSlack"/> of the span's start and the cavern is flat,
    /// with its span cut bare from the party track (one <see cref="RailTrack.CutMore"/>) and a <see cref="CavernBridge"/> laying the cells' rails;
    /// otherwise a marker, said dead in the log.
    /// </summary>
    private Node3D? BuildBridge(Node3D root, Piece piece)
    {
        var id = piece.Plan.Id;
        var track = _rails.Party.Track;
        var cells = PartyDeckCells(track);
        float from = cells.Count > 0 ? cells[0].FromS : float.NaN, to = cells.Count > 0 ? cells[^1].ToS : float.NaN;
        var over = cells.Count > 0 && _rails.OverCrevasse(from, to);
        string? dead = null;
        if (cells.Count == 0) dead = _layout.Rail?.Cavern == null ? "no cavern on this floor" : "no deck cell on the cavern's party lane";
        else if (!(Mathf.Abs(piece.S - from) <= BridgeRules.AtSlack)) dead = $"its At lies at S={piece.S:0.0}, {Mathf.Abs(piece.S - from):0.0} m from the span's start (over {BridgeRules.AtSlack:0} m)";
        else if (!LeapRules.Flat(_layout, _layout.Rail?.Cavern)) dead = "the cavern is not flat";
        else if (!_cellsOrdered) dead = "its cells do not run along the line in order"; // (the floor's ChasmDressing may enter the tree after this: CavernBridge reads it as the cells go)
        GD.Print($"[rails] bridge {id}: party S={from:0.0}..{to:0.0}, {cells.Count} deck cell(s), over the crevasse {over} ({(dead == null ? "live" : $"dead: {dead}")})");
        if (dead != null)
        {
            var marker = new Node3D { Name = $"Bridge_{id}" };
            root.AddChild(marker);
            if (cells.Count > 0) marker.GlobalPosition = track.Point(from);
            return marker;
        }
        track.Track.CutMore(new[] { (from, to) }); // the piece's one cut: the track is laid again once, bare over the span (its other gaps kept)
        var bridge = new CavernBridge { Name = $"Bridge_{id}", NetRef = NetRefs.Bridge(piece.Index), Layout = _layout };
        root.AddChild(bridge); // in the tree first: LaySpan places in its frame
        bridge.Build(track, cells);
        piece.Bridge = bridge;
        _bridgePiece = piece;
        BridgeFromS = from;
        BridgeToS = to;
        BridgeCells = cells.Count;
        return bridge;
    }

    /// <summary>The cavern's party-lane deck cells in line order, each with its party S span: the near edge of the first and the far edge of the last a half cell out, the joins at the midpoints.</summary>
    private List<(GridPos Cell, float FromS, float ToS)> PartyDeckCells(TrackSampler track)
    {
        var cells = new List<(GridPos, float, float)>();
        var decks = CrevasseRules.CavernDecks(_layout).Where(_layout.IsPartyRail).ToList();
        if (decks.Count == 0 || !track.Valid) return cells;
        var mids = decks.Select(c => track.ClosestS(_builder.Surface(c))).ToList();
        _cellsOrdered = true;
        for (var k = 1; k < mids.Count; k++) if (!(mids[k] > mids[k - 1])) _cellsOrdered = false; // the centres ascend along the line (midpoint joins would hide a swap)
        var half = DungeonBuilder.CellSize / 2f;
        for (var k = 0; k < decks.Count; k++)
        {
            var near = k == 0 ? mids[0] - half : (mids[k - 1] + mids[k]) / 2f;
            var far = k == decks.Count - 1 ? mids[k] + half : (mids[k] + mids[k + 1]) / 2f;
            cells.Add((decks[k], near, far));
        }
        return cells;
    }

    /// <summary>The last <see cref="PartyDeckCells"/> found the cells' centres ascending along the line.</summary>
    private bool _cellsOrdered;

    // ---- arming and standing again ---------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: its bridge stands whole again (Ruling 5).</summary>
    private void ArmBridge(int ride)
    {
        if (_bridgePiece is { } p && p.Plan.Ride == ride) StandBridge(p, "the ride arms");
    }

    /// <summary>The bridge stands whole again (an arming, a restore, a spare's takeover, a test's hold): every cell back, its collapse and calls reset.</summary>
    private void StandBridge(Piece p, string why)
    {
        if (p.Bridge is not { } bridge || !IsInstanceValid(bridge)) return;
        var was = bridge.Gone;
        var going = !float.IsNaN(CollapseS);
        bridge.Stand();
        CollapseS = float.NaN;
        _bridgeRearOnAt = double.NaN;
        _bridgeCalledAt = double.NaN;
        _bridgeStopped = false;
        _bridgeGroaned = _bridgeHeldLogged = _bridgeBrakeCalled = false;
        _bridgeLogged = 0;
        if (was > 0 || going) GD.Print($"[rails] t={_now:0.00} bridge {p.Plan.Id} stands whole again ({why}; {was} cell(s) were gone)");
    }

    /// <summary>The party cart's Wrecked event (a fall too): the collapse stops where it stands, or, with a spare already taken over, the bridge stands again.</summary>
    private void BridgeOnWreck()
    {
        if (_bridgePiece is not { } p) return;
        if (_rails.State.Mode != CartMode.Wrecked) { StandBridge(p, "a spare takes over"); return; } // the spare moved the cart off it in the same call
        if (_bridgeStopped) return;
        _bridgeStopped = true;
        if (!float.IsNaN(CollapseS)) GD.Print($"[rails] t={_now:0.00} bridge {p.Plan.Id} stops where it stands (the train is wrecked): front S={CollapseS:0.0}, {BridgeGone}/{BridgeCells} gone");
    }

    /// <summary>The cart is restored (a wipe's rise or a load): the bridge stands whole again.</summary>
    private void BridgeOnRestore()
    {
        if (_bridgePiece is { } p) StandBridge(p, "the cart is restored");
    }

    /// <summary>Server test hook (Ruling 21, as <see cref="DebugHoldRunaway"/>): ride <paramref name="ride"/>'s bridge never starts to go on this floor build (one going now stands again).</summary>
    internal void DebugHoldBridge(int ride)
    {
        if (_heldBridgeRide == ride) return;
        _heldBridgeRide = ride;
        if (_bridgePiece is not { } p || p.Plan.Ride != ride) return;
        GD.Print($"[rails] bridge {p.Plan.Id}: held by the test");
        if (p.Bridge is { Gone: > 0 } || !float.IsNaN(CollapseS)) StandBridge(p, "held by the test");
    }

    // ---- the collapse ----------------------------------------------------------------------------

    /// <summary>
    /// Server and single player, each tick: while the ride is armed, the groan, the rear's stamp and the start a grace after it; then, armed or
    /// not (an arrival lets it run on), the front advanced (held short of the rear unless braked), the cells given way, the brake's call and the take.
    /// </summary>
    private void TickBridge(Piece piece, PartyController party, float dt)
    {
        if (piece.Bridge is not { } bridge || !IsInstanceValid(bridge) || _bridgeStopped) return;
        if (_rails.State.Mode == CartMode.Wrecked) return; // the wreck's event stops it (BridgeOnWreck)
        var id = piece.Plan.Id;
        var armed = ArmedRide == piece.Plan.Ride;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        var rear = TrainRearS;
        if (float.IsNaN(CollapseS))
        {
            if (!armed || !rolling) return;
            var front = _rails.Party.FrontS;
            if (!_bridgeGroaned && front >= BridgeFromS && front < BridgeToS)
            {
                _bridgeGroaned = true;
                EventBus.Instance.Log("The bridge groans under the cart.");
                RideAudio.At("trestle_creak", bridge.Entry); // a client's from its own cart's front (ClientPresent)
                GD.Print($"[rails] t={_now:0.00} the cart's front comes onto bridge {id} at S={front:0.0} (v {_rails.State.V:0.0})");
            }
            if (_heldBridgeRide == piece.Plan.Ride) return;
            if (double.IsNaN(_bridgeRearOnAt) && rear >= BridgeFromS && rear < BridgeToS) _bridgeRearOnAt = _now;
            if (!BridgeRules.Running(_bridgeRearOnAt, _now)) return;
            CollapseS = BridgeFromS;
            _bridgePace = BridgeRules.Pace(_rails.Train.Count);
            _bridgeCalledAt = NetSession.Instance.Now;
            var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.BridgeGoing, "The bridge is going behind us — keep her rolling!");
            GD.Print($"[rails] t={_now:0.00} bridge {id} goes from S={BridgeFromS:0.0} at {_bridgePace:0.00} m/s ({_rails.Train.Count} wagon(s)); called by {by ?? "none (logged)"}");
            return;
        }
        // a brake counts only when pressed a reaction time after the call (Task 3 review I1): until then the front holds short whatever the brake
        var braked = BridgeRules.Counts(_rails.BrakePressedAt, _rails.LastBrakeAt, _bridgeCalledAt, NetSession.Instance.Now);
        var free = Mathf.Min(BridgeToS, CollapseS + _bridgePace * dt);
        CollapseS = BridgeRules.Advance(CollapseS, BridgeToS, _bridgePace, dt, rear, braked);
        if (!_bridgeHeldLogged && CollapseS < free - 1e-4f)
        {
            _bridgeHeldLogged = true;
            GD.Print($"[rails] t={_now:0.00} bridge {id} held by the stall guard at S={CollapseS:0.0} (rear S={rear:0.0})");
        }
        var gone = BridgeRules.GoneCells(CollapseS, bridge.FarS);
        if (gone > bridge.Gone)
        {
            bridge.GiveWay(gone, GetParent() as Node3D);
            if (gone / 4 > _bridgeLogged / 4 || gone == bridge.Count)
            {
                _bridgeLogged = gone;
                GD.Print($"[rails] t={_now:0.00} bridge {id}: {gone}/{bridge.Count} gone, front S={CollapseS:0.0}, rear S={rear:0.0}");
            }
        }
        if (armed && !_bridgeBrakeCalled && BridgeRules.CallsBrake(_rails.Braking, CollapseS, rear))
        {
            _bridgeBrakeCalled = true;
            var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.BridgeBrake, "Don't stop — it's going under us!");
            GD.Print($"[rails] t={_now:0.00} bridge {id}: a brake with the front {rear - CollapseS:0.0} m behind the rear; called by {by ?? "none (logged)"}");
        }
        if (!BridgeRules.Catches(rolling, braked, CollapseS, rear)) return;
        BridgeTakes++;
        GD.Print($"[rails] t={_now:0.00} bridge {id} takes the train at S={rear:0.0} (braked {NetSession.Instance.Now - _rails.LastBrakeAt:0.0} s ago, pressed {_rails.BrakePressedAt - _bridgeCalledAt:0.0} s after the call)");
        var under = 0; // the cells under the train go with it (up to the cart's front)
        while (under < bridge.Count && bridge.Cells[under].FromS < _rails.Party.FrontS) under++;
        bridge.GiveWay(under, GetParent() as Node3D);
        _rails.Fall("The bridge goes out from under the train!", seen: true); // seen dropping through the gone cells (BridgeRules.FallSeen); its Wrecked event stops the collapse here
    }

    /// <summary>
    /// The ride's runaway whose front is on the gone span (<see cref="BridgeRules.TakesRunaway"/>) goes down with the bridge, quietly (Ruling 8):
    /// the thud and the crash where it goes, "The runaway goes down with the bridge!" only with a hero within <see cref="SwitchBackRules.PlungeHeard"/>,
    /// and it is freed plainly, neither rammed nor plunged, so a client's copy just goes. True when it went.
    /// </summary>
    private bool BridgeTakesRunaway(RideRunaway w)
    {
        if (_bridgePiece is not { Bridge: not null } p || w.Ride != p.Plan.Ride || w.OnSpur) return false;
        var front = w.Run.FrontS;
        if (!BridgeRules.TakesRunaway(front, BridgeFromS, CollapseS)) return false;
        var at = w.Body.GlobalPosition;
        var nearest = NearestHeroTo(at);
        var told = SwitchBackRules.PlungeTold(nearest);
        if (told) EventBus.Instance.Log("The runaway goes down with the bridge!");
        if (GetParent() is Node3D root) Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        RideAudio.At("cart_crash", at); // positional: heard only near the drop
        BridgeRunawayTaken = (w.Body.Rammed, w.Body.Plunged, front);
        GD.Print($"[rails] t={_now:0.00} runaway {w.Id} goes down with the bridge at S={front:0.0} (the bridge's front S={CollapseS:0.0}); nearest hero {nearest:0.0} m{(told ? ", told" : ", unseen")}");
        FreeRideRunaway("it went down with the bridge");
        return true;
    }

    /// <summary>
    /// Nobody gets out (Ruling 6): the party's train, its front past the live bridge's start and its rear short of its end, rolls at 1 m/s or
    /// more. Every role, from this peer's own cart.
    /// </summary>
    public bool HoldsRiders()
    {
        if (float.IsNaN(BridgeFromS) || !IsInstanceValid(_rails)) return false;
        var on = _rails.State.Mode == CartMode.Rolling && _rails.Party.FrontS > BridgeFromS && TrainRearS < BridgeToS;
        return BridgeRules.HoldsRiders(on, _rails.State.V);
    }
}
