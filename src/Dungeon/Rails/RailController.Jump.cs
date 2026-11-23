using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A gap in the party line (<see cref="SetGap"/>, a jump on ride 4): when the cart's front reaches its lip the cart either flies
/// (<see cref="SetPieceRules.JumpMade"/>: S runs on and the pose rises by <see cref="SetPieceRules.ArcHeight"/> over the gap, read
/// per S so each coupled wagon flies at its own S) or drops in and is wrecked; over a crevasse (<see cref="OverCrevasse"/>) the
/// train falls instead (<see cref="Fall"/>). Companions pump while the lip is within
/// <see cref="PumpForJump"/> metres ahead. The pose composes with a lane blend (<see cref="LaneFrame"/>) through one
/// <see cref="CartRun.FrameAt"/>. Server only.
/// </summary>
public partial class RailController
{
    /// <summary>Seated companions pump while a gap's lip is this close ahead of the front.</summary>
    public const float PumpForJump = 30f;

    private float _gapFrom = float.NaN, _gapTo = float.NaN;
    /// <summary>The front has reached the lip this ride (the jump was judged).</summary>
    private bool _jumpJudged;
    /// <summary>The speed the cart left the lip at, or 0 while it has not flown.</summary>
    private float _jumpV;
    private bool _landed;
    /// <summary>The gap set this ride lies over a crevasse (<see cref="OverCrevasse"/>): a drop in is a fall, not a wreck.</summary>
    private bool _gapOverCrevasse;
    private Func<float, Transform3D>? _partyFrame;

    /// <summary>The party line's S where the gap starts (the lip), or NaN when no gap waits this ride.</summary>
    public float GapFrom => _gapFrom;
    public float GapTo => _gapTo;
    /// <summary>The cart flew the gap this ride.</summary>
    public bool Jumped => _jumpV > 0f;
    /// <summary>The speed the cart left the lip at (0 while it has not flown).</summary>
    public float JumpSpeed => _jumpV;
    private bool GapSet => !float.IsNaN(_gapFrom);

    /// <summary>A gap from <paramref name="from"/> to <paramref name="to"/> (party S) waits ahead this ride (the encounter controller sets it as the ride arms). Server only.</summary>
    public void SetGap(float from, float to)
    {
        if (NetSession.IsClient || to <= from) return;
        _gapFrom = from;
        _gapTo = to;
        _jumpJudged = _landed = false;
        _jumpV = 0f;
        _gapOverCrevasse = OverCrevasse(from, to);
        RefreshFrameAt();
    }

    /// <summary>A party-line S is sampled this often by <see cref="OverCrevasse"/>.</summary>
    const float OverCrevasseEvery = 1f;

    /// <summary>
    /// Some party-lane cell under the line from S <paramref name="from"/> to <paramref name="to"/> (sampled every metre, both ends
    /// included) is over a crevasse (<see cref="CrevasseRules.OverCrevasse"/>, ruling 7): a missed jump or a fallen trestle there is a
    /// fall. False on a floor without chasms.
    /// </summary>
    public bool OverCrevasse(float from, float to)
    {
        if (_layout == null || _layout.ChasmCount == 0 || !Party.Track.Valid || !float.IsFinite(from) || !float.IsFinite(to)) return false;
        for (var s = from; ; s += OverCrevasseEvery)
        {
            var at = Mathf.Min(s, to);
            if (CrevasseRules.OverCrevasse(_layout, DungeonBuilder.WorldToCell(Party.Track.Point(at)))) return true;
            if (at >= to) return false;
        }
    }

    /// <summary>The cart dropped into the gap (<see cref="JudgeJump"/>): over a crevasse the train falls, anywhere else it is wrecked.</summary>
    private void DropIn()
    {
        if (_gapOverCrevasse) FallNow("The cart drops into the crevasse!");
        else Wreck();
    }

    /// <summary>
    /// Client: the gap and the jump the server's cart state names (NaN for none; <paramref name="jumpV"/> the speed it left the lip
    /// at, NaN before it flew). Sets no server state: the arc poses the cart and its train at once.
    /// </summary>
    internal void ApplyGapNet(float from, float to, float jumpV)
    {
        if (!NetSession.IsClient) return;
        var v = float.IsNaN(jumpV) || jumpV <= 0f ? 0f : jumpV;
        if (Same(_gapFrom, from) && Same(_gapTo, to) && _jumpV == v) return;
        _gapFrom = from;
        _gapTo = to;
        _jumpV = v;
        _jumpJudged = v > 0f;
        RefreshFrameAt();
    }

    /// <summary>No gap waits (the ride is over or undone).</summary>
    public void ClearGap()
    {
        _gapFrom = _gapTo = float.NaN;
        _jumpJudged = _landed = false;
        _jumpV = 0f;
        _gapOverCrevasse = false;
        RefreshFrameAt();
    }

    /// <summary>The lip of an unjudged gap lies ahead of the front within <paramref name="reach"/> metres.</summary>
    public bool GapAhead(float reach)
    {
        if (!GapSet || _jumpJudged) return false;
        var d = _gapFrom - Party.FrontS;
        return d >= 0f && d <= reach;
    }

    /// <summary>The run poses through <see cref="PartyFrame"/> while a switch is taken or a gap waits; else on the track.</summary>
    private void RefreshFrameAt() => Party.FrameAt = Crossing || GapSet ? (_partyFrame ??= PartyFrame) : null;

    /// <summary>The airborne stretch: the whole body clears the gap, from a half length short of the lip to a half length past the far end.</summary>
    private float ArcFrom => _gapFrom - CartRules.HalfLength;
    private float ArcLength => _gapTo - _gapFrom + 2f * CartRules.HalfLength;

    /// <summary>The frame at party S: the lane blend's (or the track's), raised over the gap by the arc when the cart flew it and pitched along it.</summary>
    private Transform3D PartyFrame(float s)
    {
        var frame = Crossing ? LaneFrame(s) : Party.Track.Frame(s);
        if (_jumpV <= 0f) return frame;
        var u = s - ArcFrom;
        if (u <= 0f || u >= ArcLength) return frame;
        var h = SetPieceRules.ArcHeight(u, ArcLength, _jumpV);
        const float d = 0.25f;
        var slope = (SetPieceRules.ArcHeight(u + d, ArcLength, _jumpV) - SetPieceRules.ArcHeight(u - d, ArcLength, _jumpV)) / (2f * d);
        var basis = frame.Basis.Orthonormalized();
        var forward = (-basis.Z + Vector3.Up * slope).Normalized(); // nose up on the way up, down on the way down
        var x = basis.X;
        var z = -forward;
        var y = z.Cross(x).Normalized();
        x = y.Cross(z).Normalized();
        return new Transform3D(new Basis(x, y, z), frame.Origin + Vector3.Up * h);
    }

    /// <summary>
    /// Right after a step: the front reached the lip on its own lane (<see cref="SetPieceRules.JudgesJump"/>: met across, it is passed). Fast enough, the cart flies (the arc poses it); too slow, it drops in and
    /// the plating goes (the caller drops it in: <see cref="DropIn"/>, a wreck or, over a crevasse, a fall). The landing shakes the riders. True when it dropped in. The cart's sound hears the leap and the landing (<see cref="Airborne"/>); a drop in ends in the wreck's cue.
    /// </summary>
    private bool JudgeJump()
    {
        if (!GapSet || State.Mode != CartMode.Rolling) return false;
        if (_jumpJudged)
        {
            if (!_landed && Jumped && State.S >= ArcFrom + ArcLength)
            {
                _landed = true;
                foreach (var rider in Cart.Riders.Values)
                    if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.08f, 0.35f);
                GD.Print($"[rails] the cart lands past the gap at S={State.S:0.0} (v {State.V:0.0})");
            }
            return false;
        }
        if (Party.FrontS < _gapFrom) return false;
        if (!SetPieceRules.JudgesJump(GapSet, _jumpJudged, Party.FrontS, _gapFrom, LaneBlendAt(_gapFrom)))
        {
            _jumpJudged = true; // met across (plan 15, B.4's lane check): passed on the other lane, never judged, and never judged later at a lip behind the cart
            GD.Print($"[rails] the cart passes the gap {_gapFrom:0.0}-{_gapTo:0.0} across on the other lane (blend {LaneBlendAt(_gapFrom):0.00} at its lip): not judged");
            return false;
        }
        _jumpJudged = true;
        if (SetPieceRules.JumpMade(State.V))
        {
            _jumpV = State.V;
            EventBus.Instance.Log("The cart leaps the gap!");
            GD.Print($"[rails] the cart leaps the gap {_gapFrom:0.0}-{_gapTo:0.0} at v {State.V:0.00} (front {Party.FrontS:0.0}); apex {SetPieceRules.ArcHeight(ArcLength / 2f, ArcLength, _jumpV):0.00} m over {ArcLength:0.0} m");
            return false;
        }
        GD.Print($"[rails] the cart is too slow for the gap {_gapFrom:0.0}-{_gapTo:0.0}: v {State.V:0.00} < {SetPieceRules.JumpSpeed:0.0} (front {Party.FrontS:0.0})");
        if (!_gapOverCrevasse) EventBus.Instance.Log("The cart drops into the gap!"); // over a crevasse the fall says why
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.14f, 0.8f);
        Props.Tremor.ShakeOnly(0.05f, 0.6f);
        Party.State = State with { Plating = 0f };
        return true;
    }

    /// <summary>Something on the line catches the cart (a trestle falling under it): the plating goes and, rolling, the cart is wrecked. Server only.</summary>
    public void Catch(string why)
    {
        if (NetSession.IsClient || State.Mode == CartMode.Wrecked) return;
        EventBus.Instance.Log(why);
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.14f, 0.8f);
        DentPlating(State.Plating);
    }
}
