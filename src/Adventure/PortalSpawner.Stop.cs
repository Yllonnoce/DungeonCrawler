using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The stop mode (plan 16 Task 3, spec section 1): while the train waits at a cage stop, portals tear open around the platform in
/// waves (<see cref="StopSiegeRules"/>) until it leaves. <see cref="Dungeon.Rails.StopTrapController"/> switches it on and off from
/// the cart's events. One portal system, two modes: the siege's creatures join the same alive list (Ruling 4: the HUD counts them;
/// while a siege runs only its own cap applies, floor-wide leftovers counted against it), and the floor-wide pressure pauses while
/// it runs. Every silence of <see cref="RefreshSilence"/> but an empty roster silences it too (Ruling 5), and silencing it mid-siege
/// drops its portals still pending. Its creatures left alive once it ends are stragglers: freed quietly once far and unseen.
/// Nothing of the siege is saved. Server and solo only (the tick returns on a client; clients see the portals by PortalOpenedEvt
/// and the creatures by EnemySpawned).
/// </summary>
public partial class PortalSpawner
{
    /// <summary>The stop under siege, or −1.</summary>
    public int SiegeStop { get; private set; } = -1;
    /// <summary>Waves the current (or last) siege has opened.</summary>
    public int SiegeWaves { get; private set; }

    /// <summary>A siege creature came through (creature, stop, wave), on the server or in solo: T6 tags its hijackers from these.</summary>
    public event Action<Enemy, int, int>? SiegeSpawned;

    // ---- test hooks (static: a flow keeps hearing them across a load's or a wipe's rebuild) ------------------------------------
    /// <summary>A siege started (stop, first wave in seconds, humans).</summary>
    internal static event Action<int, float, int>? DebugSiegeStarted;
    /// <summary>A wave opened (stop, wave, sites, creatures per site, alive before it with those still to come through, cap).</summary>
    internal static event Action<int, int, IReadOnlyList<GridPos>, IReadOnlyList<int>, int, int>? DebugWaveOpened;
    /// <summary>A wave could not open (stop, wave, why: "alive a/cap" or "no site").</summary>
    internal static event Action<int, int, string>? DebugWaveWaited;
    /// <summary>A siege ended (stop, why, waves).</summary>
    internal static event Action<int, string, int>? DebugSiegeEnded;
    /// <summary>A straggler was freed (creature, metres from the nearest hero, seconds unseen).</summary>
    internal static event Action<Enemy, float, float>? DebugStragglerFreed;

    private SiegeWave _siegeRow;
    private int _siegeHumans = 1;
    /// <summary>Seconds since the siege started or its last wave opened.</summary>
    private float _siegeClock;
    /// <summary>When (on <see cref="_siegeClock"/>) the next wave is tried; NaN: the row's gap at the current intensity (a change of setting counts at once).</summary>
    private float _siegeNextAt = float.NaN;
    /// <summary>The wave (of the current siege) that has had its cinder brute, or 0.</summary>
    private int _bruteWave;
    /// <summary>The current siege's creatures.</summary>
    private readonly List<Enemy> _siegeLive = new();
    /// <summary>Every siege creature still tracked: the current siege's and the stragglers.</summary>
    private readonly HashSet<Enemy> _siegeAll = new();

    /// <summary>A creature of a siege that has ended (Ruling 32), and how long no hero has been in its sight.</summary>
    private sealed class Straggler
    {
        public readonly Enemy Enemy;
        public float Unseen;
        public Straggler(Enemy enemy) => Enemy = enemy;
    }
    private readonly List<Straggler> _stragglers = new();
    private float _stragglerClock;
    private PhysicsRayQueryParameters3D? _stragglerRay;
    private readonly List<SiegeHero> _siegeHeroes = new();
    private readonly List<int> _waveCounts = new();

    /// <summary>A siege creature, of the current siege or a straggler of an ended one (the flows' cull spares them; T6 reads it).</summary>
    internal bool IsSiegeCreature(Enemy e) => _siegeAll.Contains(e);

    /// <summary>
    /// The train is held at sieged stop <paramref name="stop"/>: the siege starts, its first wave <paramref name="firstDelay"/> seconds on
    /// (the row's first-wave delay after an arrival, the restart's grace after a load or a rise). <paramref name="humans"/> is the heroes a
    /// human drives (solo 1). A siege already running ends first; a silenced one does not start.
    /// </summary>
    public void StartSiege(int stop, float firstDelay, int humans)
    {
        if (Net.NetSession.IsClient) return;
        if (SiegeStop >= 1) EndSiege($"a siege starts at stop {stop}");
        RefreshSilence(); // the setting as it stands now
        var (silent, why) = SiegeSilence();
        if (silent) { GD.Print($"[portal] siege at stop {stop}: silenced ({why})"); return; }
        if (_layout.Rail is not { } rail || stop < 1 || stop >= rail.Stops.Count) return;
        SiegeStop = stop;
        SiegeWaves = 0;
        _siegeRow = StopSiegeRules.For(stop);
        _siegeHumans = Math.Max(1, humans);
        _siegeClock = 0f;
        _siegeNextAt = firstDelay;
        _bruteWave = 0;
        _siegeLive.Clear();
        GD.Print($"[portal] siege at stop {stop}: on (first wave in {firstDelay:0.0} s, {_siegeHumans} human(s))");
        DebugSiegeStarted?.Invoke(stop, firstDelay, _siegeHumans);
    }

    /// <summary>The siege ends (the train left, a wreck, a load, a silence): its portals still pending close, its creatures left alive become stragglers.</summary>
    public void EndSiege(string why)
    {
        if (SiegeStop < 1) return;
        var stop = SiegeStop;
        _pending.RemoveAll(p => p.Stop >= 1);
        foreach (var e in _siegeLive)
            if (IsInstanceValid(e) && e.IsAlive) _stragglers.Add(new Straggler(e));
        _siegeLive.Clear();
        SiegeStop = -1;
        GD.Print($"[portal] siege at stop {stop}: off ({why}) after {SiegeWaves} wave(s)");
        DebugSiegeEnded?.Invoke(stop, why, SiegeWaves);
    }

    /// <summary>Ruling 5: the siege keeps every silence of <see cref="RefreshSilence"/> but an empty roster (it has its own).</summary>
    private (bool Silent, string Why) SiegeSilence()
    {
        if (!PortalRules.Enabled(Intensity)) return (true, "reinforcements are off in Settings");
        if (DebugTools.Instance?.NoPortals == true) return (true, "--no-portals");
        var bossFloor = PortalRules.BossFloorFor(_floor);
        if ((Game.Instance?.Floors.TryGetValue(bossFloor, out var s) == true && s.BossDefeated) || (bossFloor == _floor && Main.Instance?.BossDefeatedThisFloor == true))
            return (true, $"the boss of floor {bossFloor} is dead");
        if (Objectives.ObjectiveController.Instance is Objectives.RitualController { Active: true }) return (true, "the ritual's breaches are enough");
        return (false, "");
    }

    /// <summary>With the floor-wide silence re-read (every 2 s): a siege a silence now covers ends at once.</summary>
    private void CheckSiegeSilence()
    {
        if (SiegeStop < 1) return;
        var (silent, why) = SiegeSilence();
        if (silent) EndSiege(why);
    }

    private void TickSiege(float dt, Main main)
    {
        TickStragglers(dt, main);
        if (SiegeStop < 1) return;
        _siegeClock += dt;
        var due = float.IsNaN(_siegeNextAt) ? StopSiegeRules.Gap(_siegeRow, Intensity) : _siegeNextAt;
        if (_siegeClock < due) return;
        OpenWave(main);
    }

    /// <summary>
    /// The next wave, under the hard cap (Ruling 36): only as many creatures as the cap leaves room for, over the fewest portals that hold
    /// them (at most the row's), at sites around the stop. No room or no site: it waits and tries again in <see cref="StopSiegeRules.RetrySeconds"/>.
    /// </summary>
    private void OpenWave(Main main)
    {
        var stop = SiegeStop;
        var wave = SiegeWaves + 1;
        var cap = StopSiegeRules.AliveCap(_siegeRow, _siegeHumans);
        var pending = PendingCount;
        var room = StopSiegeRules.Room(cap, _alive.Count, pending);
        var alive = _alive.Count + pending; // as the log and the hooks count it: those still coming through are as good as here
        if (room <= 0) { Waits(stop, wave, $"alive {alive}/{cap}"); return; }
        var per = StopSiegeRules.PerPortal(_siegeRow, _siegeHumans, _rng.NextDouble());
        var sent = Math.Min(_siegeRow.Portals * per, room);
        var portals = Math.Min(_siegeRow.Portals, (sent + per - 1) / per);
        var rail = _layout.Rail!;
        var railStop = rail.Stops[stop];
        GridPos? cage = null;
        if (Objectives.ObjectiveController.Instance is Objectives.CrewRescueController crew && IsInstanceValid(crew))
            foreach (var c in crew.Cages) if (c.Stop == stop) { cage = c.Cell; break; }
        _siegeHeroes.Clear();
        foreach (var a in main.Party.Active)
        {
            if (a.Downed) continue;
            var fwd = -a.GlobalBasis.Z;
            _siegeHeroes.Add(new SiegeHero(DungeonBuilder.WorldToCell(a.GlobalPosition), fwd.X, fwd.Z));
        }
        var builder = main.Builder;
        var sites = StopSiegeRules.Sites(_layout, _layout.Rooms[railStop.Room], railStop, cage, _siegeHeroes, portals, _rng, builder.IsCellBlockedForEnemies);
        if (sites.Count == 0) { Waits(stop, wave, "no site"); return; }
        sent = Math.Min(sent, sites.Count * per);
        portals = (sent + per - 1) / per;
        if (sites.Count > portals) sites.RemoveRange(portals, sites.Count - portals);
        _waveCounts.Clear();
        for (var i = 0; i < portals; i++) _waveCounts.Add(sent / portals + (i < sent % portals ? 1 : 0)); // spread evenly
        SiegeWaves = wave;
        _siegeClock = 0f;
        _siegeNextAt = float.NaN;
        EventBus.Instance.Log(wave == 1 ? Voice.StopTrapLines.SiegeFirstLog : Voice.StopTrapLines.SiegeLaterLog);
        for (var i = 0; i < portals; i++)
        {
            var at = builder.Surface(sites[i]) + Vector3.Up * 0.05f;
            _pending.Add(new PendingPortal { At = at, Count = _waveCounts[i], Timer = 1.1f, Stop = stop, Wave = wave });
            EventBus.Instance.RaisePortalOpened(at);
            ShowPortal(at);
        }
        var each = _waveCounts.All(n => n == _waveCounts[0]) ? _waveCounts[0].ToString() : string.Join("/", _waveCounts);
        GD.Print($"[portal] siege stop {stop} wave {wave}: {portals} portal(s) x {each} at [{string.Join(" ", sites)}] (alive {alive}/{cap})");
        DebugWaveOpened?.Invoke(stop, wave, sites, _waveCounts.ToList(), alive, cap);
    }

    private void Waits(int stop, int wave, string why)
    {
        _siegeNextAt = _siegeClock + StopSiegeRules.RetrySeconds;
        GD.Print($"[portal] siege stop {stop} wave {wave} waits ({why})");
        DebugWaveWaited?.Invoke(stop, wave, why);
    }

    /// <summary>A siege portal's next creature: the roster's roll, at most one cinder brute per wave.</summary>
    private string SiegePick(PendingPortal p)
    {
        var id = StopSiegeRules.Pick(p.Stop, _bruteWave == p.Wave, _rng.NextDouble());
        if (id == StopSiegeRules.Brute) _bruteWave = p.Wave;
        return id;
    }

    /// <summary>A siege creature came through (after the floor-wide path's IsReinforcement, RunBoost, ForceAggro and the alive list).</summary>
    private void SiegeCreature(Enemy e, PendingPortal p)
    {
        if (p.Stop == SiegeStop) _siegeLive.Add(e);
        else _stragglers.Add(new Straggler(e)); // a portal of a siege that has ended meanwhile (not reached: EndSiege drops them)
        _siegeAll.Add(e);
        SiegeSpawned?.Invoke(e, p.Stop, p.Wave);
    }

    /// <summary>
    /// Once a second: each straggler (a creature of an ended siege, never one carried or leaping: Ruling 32) looks for the nearest hero
    /// with one sight ray; farther than <see cref="StopSiegeRules.StragglerFar"/> from every hero and unseen for
    /// <see cref="StopSiegeRules.StragglerUnseen"/>, it is freed quietly.
    /// </summary>
    private void TickStragglers(float dt, Main main)
    {
        _stragglerClock += dt;
        if (_stragglerClock < 1f) return;
        var step = _stragglerClock;
        _stragglerClock = 0f;
        _siegeLive.RemoveAll(e => !IsInstanceValid(e) || !e.IsAlive);
        _siegeAll.RemoveWhere(e => !IsInstanceValid(e) || !e.IsAlive);
        for (var i = _stragglers.Count - 1; i >= 0; i--)
        {
            var s = _stragglers[i];
            var e = s.Enemy;
            if (!IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion()) { _stragglers.RemoveAt(i); continue; }
            if (e.Carried || e.Leaping) continue;
            Adventurer? nearest = null;
            var d = float.MaxValue;
            foreach (var a in main.Party.Adventurers)
            {
                if (!IsInstanceValid(a) || a.Parked) continue;
                var ad = e.GlobalPosition.DistanceTo(a.GlobalPosition);
                if (ad < d) { d = ad; nearest = a; }
            }
            if (nearest == null) continue;
            s.Unseen = Sees(e, nearest.GlobalPosition) ? 0f : s.Unseen + step;
            if (!StopSiegeRules.Culls(d, s.Unseen)) continue;
            GD.Print($"[portal] straggler {e.Name} freed ({d:0} m from every hero, unseen {s.Unseen:0} s)");
            DebugStragglerFreed?.Invoke(e, d, s.Unseen);
            _stragglers.RemoveAt(i);
            Free(e);
        }
    }

    private bool Sees(Enemy e, Vector3 hero)
    {
        _stragglerRay ??= PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Up, collisionMask: 1);
        _stragglerRay.From = e.GlobalPosition + Vector3.Up * 1.2f;
        _stragglerRay.To = hero + Vector3.Up * 1.2f;
        return e.GetWorld3D().DirectSpaceState.IntersectRay(_stragglerRay).Count == 0;
    }

    /// <summary>A siege creature goes quietly: off every list, freed.</summary>
    private void Free(Enemy e)
    {
        _alive.Remove(e);
        _siegeAll.Remove(e);
        _siegeLive.Remove(e);
        if (IsInstanceValid(e) && !e.IsQueuedForDeletion()) e.QueueFree();
    }

    /// <summary>Test hook: the next wave is due in half a second.</summary>
    internal void DebugSiegeNow() { if (SiegeStop >= 1) _siegeNextAt = _siegeClock + 0.5f; }

    /// <summary>Test hook (Ruling 24): every live siege creature (the current siege's and the stragglers) rooted where it stands, or let go.</summary>
    internal void DebugHoldSiegeCreatures(bool hold)
    {
        foreach (var e in _siegeAll) if (IsInstanceValid(e) && e.IsAlive) e.DebugRooted = hold;
    }

    /// <summary>Test hook: every siege creature (the current siege's and the stragglers) freed quietly.</summary>
    internal void DebugClearAlive()
    {
        foreach (var e in _siegeAll.ToList()) Free(e);
        _stragglers.Clear();
        _siegeLive.Clear();
    }

    /// <summary>Test hook: the live creatures of the current siege and the stragglers.</summary>
    internal (int Current, int Stragglers) DebugSiegeCounts()
    {
        var current = 0;
        foreach (var e in _siegeLive) if (IsInstanceValid(e) && e.IsAlive && !e.IsQueuedForDeletion()) current++;
        var stragglers = 0;
        foreach (var s in _stragglers) if (IsInstanceValid(s.Enemy) && s.Enemy.IsAlive && !s.Enemy.IsQueuedForDeletion()) stragglers++;
        return (current, stragglers);
    }
}
