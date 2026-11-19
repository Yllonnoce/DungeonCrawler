using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's cart heard where it is, on every peer (plan 6): each rendered frame the controller feeds the cart's <see cref="CartSound"/>
/// from what this peer shows (the server's and single player's own run, a client's follower, flags and plating) and a running chaser's
/// on the server and in single player, and keeps the list of heroes on foot their bells look ahead for. A client's follower snap of more
/// than a metre primes the cart's sound (it moves S in one step: no edge; <see cref="CartSound.Snapped"/>). The ride's soundscape (<see cref="RideSoundscape"/>) is a child on every role
/// but a dedicated server.
/// </summary>
public partial class RailController
{
    /// <summary>The ride's soundscape, or null (a dedicated server has none).</summary>
    public RideSoundscape? Soundscape { get; private set; }
    /// <summary>The heroes this peer sees on foot (not seated, not downed), refreshed each rendered frame before any cart is fed: the bells' look-ahead.</summary>
    internal List<Node3D> FootHeroes { get; } = new();
    private Func<bool>? _partyAhead, _chaserAhead;
    private int _partySnaps = int.MinValue;

    /// <summary>The cart flies its jump's arc now: it flew the gap and its middle is over the airborne stretch (a client's from the server's gap and jump).</summary>
    public bool Airborne => AirborneAt(State.S);

    private bool AirborneAt(float s) => _jumpV > 0f && GapSet && s >= ArcFrom && s <= ArcFrom + ArcLength;

    /// <summary>The soundscape joins the controller (not on a dedicated server: it makes no sound).</summary>
    private void AddSoundscape()
    {
        if (NetSession.IsServer || Soundscape != null) return;
        Soundscape = new RideSoundscape();
        AddChild(Soundscape);
    }

    /// <summary>The heroes on foot, this frame. No allocation.</summary>
    private void RefreshFootHeroes()
    {
        FootHeroes.Clear();
        if (Main.Instance?.Party is not { } party) return;
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked && !hero.Seated && !hero.Downed) FootHeroes.Add(hero);
    }

    /// <summary>Each rendered frame, after the pose: the party cart's sound from the state shown at <paramref name="s"/>.</summary>
    private void FeedPartySound(float s)
    {
        if (NetSession.IsServer) return; // a dedicated server does no sound work
        RefreshFootHeroes();
        if (Cart.Sound is not { } sound || !IsInstanceValid(sound)) return;
        if (NetSession.IsClient && _follow is { } f && f.Snaps != _partySnaps)
        {
            _partySnaps = f.Snaps;
            sound.Snapped(s, 0);
        }
        var input = new CartSoundInput(s, State.Mode == CartMode.Rolling ? State.V : 0f, State.Mode, State.Plating, 0, AirborneAt(s), Halted, Crossing);
        sound.Feed(input, _train.Count, _partyAhead ??= PartyAhead);
    }

    /// <summary>A hero on foot stands in the party cart's way on the lane its front is on (the bark's own geometry).</summary>
    private bool PartyAhead()
    {
        var run = JudgedRun();
        return RailWarn.AnyoneAhead(run.Track, run.State.S, State.V, FootHeroes);
    }

    /// <summary>Server and single player, each rendered frame: the running chaser's sound from its run, posed at <paramref name="s"/>.</summary>
    private void FeedChaserSound(MineCart chaser, CartRun run, float s)
    {
        if (NetSession.IsServer) return;
        if (chaser.Sound is not { } sound || !IsInstanceValid(sound)) return;
        var st = run.State;
        sound.Feed(new CartSoundInput(s, st.Mode == CartMode.Rolling ? st.V : 0f, st.Mode, st.Plating, 0, false, false, false), 0, _chaserAhead ??= ChaserAhead);
    }

    private bool ChaserAhead() => _chase is { } c && IsInstanceValid(c.Cart) && RailWarn.AnyoneAhead(c.Run.Track, c.Run.State.S, c.Run.State.V, FootHeroes);

    /// <summary>A chaser placed at <paramref name="s"/> on the party line announces its departure, and rings at once when a body on foot stands at it (<see cref="RideSound.SpawnBell"/>).</summary>
    internal void ChaserPlacedSound(MineCart chaser, TrackSampler track, float s)
    {
        if (chaser.Sound is not { } sound || !IsInstanceValid(sound)) return;
        sound.AnnounceOnStart = true;
        if (RideSound.SpawnBell(RailWarn.NearestGap(track, s, FootHeroes))) sound.RingNow();
    }
}
