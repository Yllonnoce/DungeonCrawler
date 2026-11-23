using DungeonCrawler.Adventure;
using DungeonCrawler.Combat;
using DungeonCrawler.Combat.Fx;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The last ride, the kiln run. A "golem" bursts out of the kiln wall onto the enemy line as the cart reaches its At: an elite forge golem (never a
/// boss) carried by an unseen runner that holds pace beside the party's cart, slamming it every <see cref="GolemEvery"/> s after a
/// <see cref="GolemWindup"/> s glow, until it dies or a thrown "spur_switch" sends it off the line into the wall. "hammers" drop on
/// the party line on the floor's metronome; a "pour" of slag runs on its clock until its quench valve is broken; a "trestle" falls
/// <see cref="SetPieceRules.TrestleGrace"/> s after the cart's front enters it; a "jump" is a gap in the rails the cart must leap
/// (<see cref="RailController.SetGap"/>); a "buffer" is smashed by the cart arriving. Every hazard warns first. Judged on the server.
/// </summary>
public partial class RailEncounterController
{
    public const float GolemEvery = 4f;
    public const float GolemWindup = TelegraphRules.RailSlamWindup;
    public const float GolemDent = 12f;
    /// <summary>The golem's fist reaches the cart from this far (flat): the lanes stand 4 m apart.</summary>
    public const float GolemReach = 6.5f;
    public const float HammerDent = 15f;
    public const float PourDent = 20f;
    /// <summary>A walker this close (flat) to the line's centre is under a hammer or the pour.</summary>
    public const float UnderReach = 1f;
    /// <summary>A body mangled by a last-ride hazard (a hammer) carries this as MangledBy: no floor hazard has it, so it always counts as armed.</summary>
    public const int KilnHazardIndex = 1003;
    const float PourBurnEvery = 0.8f;
    const string GolemName = "Kilnheart the Hulking";

    private sealed class Golem
    {
        /// <summary>Its runner's id on the wire.</summary>
        public int CartId;
        public int Ride;
        public Enemy Body = null!;
        public Node3D Runner = null!;
        public CartRun Run = null!;
        public string EncounterId = "";
        public float Clock;
        public bool WindingUp;
        /// <summary>The runner has passed the spur's points (taken them or not).</summary>
        public bool PastSpur;
        /// <summary>On the spur: running into the stop.</summary>
        public bool OnSpur;
        /// <summary>Off the line (diverted, dropped at the line's end, or dead): the runner is gone and nothing more is scripted.</summary>
        public bool Done;
        public bool DeathTold;
        public int Slams;
        /// <summary>The spur it was sent down (while <see cref="OnSpur"/>).</summary>
        public SpurSwitch? Spur;
        /// <summary>Its runner's sound: a track change only.</summary>
        public CartSound? Sound;
    }

    private Golem? _golem;
    private Func<Transform3D>? _golemSpot;
    private Func<Vector3>? _golemDrop;
    private readonly List<Node3D> _under = new();

    /// <summary>
    /// The golem has died on this floor: it never bursts out again (an elite pays loot even as a reinforcement, so a re-armed ride
    /// must not send a fresh one). Server state; the save keeps it (Task 9).
    /// </summary>
    public bool GolemDefeated { get; set; }

    /// <summary>The ride's golem now (alive or a corpse), or null (tests and the smoke read it).</summary>
    public Enemy? GolemBody => _golem?.Body is { } b && IsInstanceValid(b) ? b : null;
    /// <summary>The golem's runner on the enemy line now, or null once it is off the line.</summary>
    public CartRun? GolemRun => _golem is { Done: false } g ? g.Run : null;
    /// <summary>Slams the golem has landed on the cart this arming.</summary>
    public int GolemSlams => _golem?.Slams ?? 0;
    /// <summary>Hammer blows that hit the cart / the cart's passes through a pour (tests and the smoke read them).</summary>
    public int HammerHits { get; private set; }
    public int PourHits { get; private set; }

    // ---- building --------------------------------------------------------------------------------

    private Node3D? BuildHammers(Node3D root, Piece piece)
    {
        var holder = new Node3D { Name = $"Hammers_{piece.Plan.Id}" };
        root.AddChild(holder);
        piece.Hammers = new DropHammer[3];
        for (var i = 0; i < 3; i++)
        {
            var s = piece.S + (i - 1) * 3f;
            var hammer = new DropHammer { Name = $"Hammer{i}", S = s, Index = i };
            holder.AddChild(hammer);
            hammer.Place(_rails.Party.Track.Frame(s));
            hammer.CueRange = CueRangeAt(hammer.GlobalPosition);
            piece.Hammers[i] = hammer;
        }
        GD.Print($"[rails] hammers {piece.Plan.Id} at party S={piece.S - 3f:0.0}/{piece.S:0.0}/{piece.S + 3f:0.0}");
        return holder;
    }

    private Node3D? BuildPour(Node3D root, Piece piece)
    {
        var track = _rails.Party.Track;
        var frame = track.Frame(piece.S);
        var valveS = Mathf.Max(0f, piece.S - MoltenPour.ValveLead);
        var valveFrame = track.Frame(valveS);
        // the valve stands on the side away from the enemy lane: clear of the golem's line and its lever
        var x = valveFrame.Basis.X.Normalized();
        var side = (_enemy.Point(Lanes.EnemyS(valveS)) - valveFrame.Origin).Dot(x) >= 0f ? -1f : 1f;
        var pour = new MoltenPour { Name = $"Pour_{piece.Plan.Id}", S = piece.S, NetRef = NetRefs.Valve(piece.Index) };
        root.AddChild(pour);
        pour.Place(frame, valveFrame, side);
        pour.CueRange = CueRangeAt(pour.GlobalPosition);
        piece.Pour = pour;
        GD.Print($"[rails] pour {piece.Plan.Id} at party S={piece.S:0.0}, quench valve at S={valveS:0.0} side {side:+0;-0}");
        return pour;
    }

    private Node3D? BuildTrestle(Node3D root, Piece piece)
    {
        var trestle = new Trestle { Name = $"Trestle_{piece.Plan.Id}", S = piece.S, NetRef = NetRefs.Trestle(piece.Index) };
        root.AddChild(trestle);
        trestle.Build(_rails.Party.Track);
        piece.Trestle = trestle;
        GD.Print($"[rails] trestle {piece.Plan.Id} from party S={trestle.From:0.0} to {trestle.To:0.0}");
        GD.Print($"[rails] {piece.Plan.Id} over a crevasse: {_rails.OverCrevasse(trestle.From, trestle.To)}"); // its collapse under the cart is a fall (TickTrestle)
        return trestle;
    }

    private Node3D? BuildGap(Node3D root, Piece piece, List<(float from, float to)> gaps)
    {
        var gap = new RailGap { Name = $"Gap_{piece.Plan.Id}", S = piece.S };
        root.AddChild(gap);
        gap.Build(_rails.Party.Track);
        piece.Gap = gap;
        gaps.Add((gap.From, gap.To));
        GD.Print($"[rails] gap {piece.Plan.Id} from party S={gap.From:0.0} to {gap.To:0.0}");
        GD.Print($"[rails] {piece.Plan.Id} over a crevasse: {_rails.OverCrevasse(gap.From, gap.To)}"); // a missed jump is a fall (RailController.DropIn)
        return gap;
    }

    private Node3D? BuildSpur(Node3D root, Piece piece)
    {
        var spur = new SpurSwitch { Name = $"Spur_{piece.Plan.Id}", PointsS = piece.S, LeverRef = NetRefs.Spur(piece.Index), LeverPastPoints = OnExitBridge(piece.S - SpurSwitch.LeverLead) };
        root.AddChild(spur);
        var path = spur.Build(_rails.Party.Track, _enemy, Lanes, _steel ??= DungeonBuilder.RailSteel(),
            _sleeper ??= DungeonBuilder.RailSleeper(), w => _layout[DungeonBuilder.WorldToCell(w)] is Cell.Wall or Cell.Secret);
        ClearTimber(root, path);
        piece.Spur = spur;
        piece.Switch = spur.Lever; // armed and reset with the other levers
        return spur;
    }

    /// <summary>The party line's S <paramref name="s"/> lies over an exit bridge cell (Ruling 43): the spur's lever cannot stand there.</summary>
    private bool OnExitBridge(float s)
    {
        if (_layout.Rail is not { Island: { } island } rail || !_rails.Party.Track.Valid) return false;
        var cell = DungeonBuilder.WorldToCell(_rails.Party.Track.Point(s));
        for (var i = island.Bridge.From; i <= island.Bridge.To; i++) if (rail.Party[i] == cell) return true;
        return false;
    }

    private Node3D? BuildBuffer(Node3D root, Piece piece)
    {
        var buffer = new BufferStop { Name = $"Buffer_{piece.Plan.Id}", S = piece.S, NetRef = NetRefs.Buffer(piece.Index) };
        root.AddChild(buffer);
        buffer.GlobalTransform = _rails.Party.Track.Frame(piece.S).Orthonormalized();
        piece.Buffer = buffer;
        return buffer;
    }

    /// <summary>A hazard's cue range at <paramref name="at"/>, by its distance to the nearest of every stop (<see cref="SetPieceRules.CueRangeNearest"/>; 30 m on a line without one). Every role builds the same.</summary>
    private float CueRangeAt(Vector3 at)
        => SetPieceRules.CueRangeNearest(_rails.StopS.Select(s => at.DistanceTo(_rails.Party.Track.Point(s))));

    /// <summary>The party line is laid bare over the floor's gaps (the curve and every S stay as they were).</summary>
    private void CutGaps(List<(float from, float to)> gaps)
    {
        if (gaps.Count == 0 || !IsInstanceValid(_rails.Party.Track.Track)) return;
        _rails.Party.Track.Track.Cut(gaps);
    }

    // ---- arming and clearing ---------------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: the gap waits for the cart, the trestle and the buffer stand, the hammers start their beat; a broken valve stays broken.</summary>
    private void ArmKiln(int ride)
    {
        var tick = _builder.FloorTick;
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ride) continue;
            p.Timer = -1f;
            p.Phase = 0;
            if (p.Gap is { } gap) _rails.SetGap(gap.From, gap.To);
            p.Trestle?.Rebuild();
            p.Buffer?.Restore();
            if (p.Pour is { } pour)
            {
                p.PourCount = p.PourHit = 0;
                p.BurnClock = 0f;
                p.Phase = (int)SetPieceRules.Pour(tick, out _);
            }
            if (p.Hammers != null)
                foreach (var h in p.Hammers) h.Judged = SetPieceRules.Phase(tick, h.Index, out _);
        }
    }

    /// <summary>
    /// The ride is over or undone: the golem goes (its corpse stays), the gap is closed to judging. With <paramref name="payOff"/>
    /// (not a load) a living golem whose spur lever is thrown and that has not hit the stop yet goes off the spur first.
    /// </summary>
    private void ClearKiln(bool payOff = true)
    {
        if (payOff) PayOffSpur();
        ClearGolem();
        _rails.ClearGap();
    }

    /// <summary>The cart's state changed: a cart restored at or past a buffer's ride stands past it.</summary>
    private void ResetKiln(CartEventKind kind)
    {
        foreach (var p in _pieces)
        {
            if (kind == CartEventKind.Restored && p.Buffer is { } buffer) buffer.Restore(smashed: _rails.StopIndex >= p.Plan.Ride);
        }
    }

    // ---- save ------------------------------------------------------------------------------------

    /// <summary>The save keeps what the floor remembers of the rides: the golem's death, the broken valve and the throwers sent once. Server only.</summary>
    public void Capture(FloorStateDto dto)
    {
        if (NetSession.IsClient) return;
        dto.GolemDefeated = GolemDefeated;
        dto.ValveBroken = _pieces.Any(p => p.Pour is { Quenched: true });
        dto.ThrowersSent = _throwersSent.Capture();
    }

    /// <summary>
    /// Back from a save: no ride is live (the Restored event cleared them and the pieces rebuild at the next arming), the golem
    /// stays dead if it was killed, a broken valve stays broken, and a thrower sent before comes again as a reinforcement. Server
    /// only; older saves carry nulls, read as false (and no thrower sent).
    /// </summary>
    public void Restore(FloorStateDto dto)
    {
        if (NetSession.IsClient) return;
        ClearRide(loaded: true); // idempotent: nothing live stands over a load
        ClearThrowers();
        _rails.ClearGap();
        _rails.ClearLane();
        GolemDefeated = dto.GolemDefeated ?? false;
        _throwersSent.Restore(dto.ThrowersSent);
        var broken = dto.ValveBroken ?? false;
        foreach (var p in _pieces)
            if (p.Pour is { } pour)
            {
                if (broken) pour.Quench();
                else if (pour.Quenched) pour.Rebuild();
            }
    }

    // ---- the golem -------------------------------------------------------------------------------

    /// <summary>
    /// The golem bursts out of the wall onto the enemy line beside the party's cart: an elite forge golem (Hulking, never a boss),
    /// carried by an unseen runner paced like a slag cart at the encounter's offset, awake.
    /// </summary>
    private void SpawnGolem(int index, RailEncounterPlan enc)
    {
        if (GetParent() is not Node3D root || !_enemy.Valid || enc.Riders.Count == 0) return;
        ClearGolem();
        if (GolemDefeated) { GD.Print($"[rails] golem {enc.Id}: already defeated on this floor, it does not come again"); return; }
        var partyS = _rails.State.S;
        var s = Mathf.Clamp(PaceRules.TargetS(Lanes.EnemyS(partyS), enc.Offset), 0f, Mathf.Max(0f, _enemy.Length - LineEnd - 1f));
        var runner = new Node3D { Name = $"GolemRunner_{enc.Id}" };
        root.AddChild(runner);
        var sound = new CartSound { Wheels = false };
        runner.AddChild(sound);
        var run = new CartRun(_enemy, runner, new CartState(s, _rails.State.V * Lanes.Stretch(partyS), CartMode.Rolling, CartRules.MaxPlating)) { Limit = _enemy.Length };
        run.PoseAt(s);
        var elite = new EliteRoll(new[] { EliteAffix.Hulking }, GolemName);
        if (_builder.SpawnEnemy(enc.Riders[0].EnemyId, runner.GlobalPosition + Vector3.Up * 0.05f, _builder.CurrentFloor, elite: elite) is not { } golem) { runner.QueueFree(); return; }
        if (golem.IsBoss) GD.PushError($"[rails] the golem {enc.Id} ({golem.Profile.Name}) spawned as a boss: its death would unlock the stairs"); // an authored id must never be a boss (the validator drops them)
        golem.IsReinforcement = true; // it came with the ride (an elite still pays its loot: it never comes again once dead, GolemDefeated)
        var g = new Golem { Body = golem, Runner = runner, Run = run, EncounterId = enc.Id, Ride = enc.Ride, CartId = NextCartId(), Sound = sound };
        _golem = g;
        _golemSpot ??= () => _golem is { } x && IsInstanceValid(x.Runner) ? new Transform3D(x.Runner.GlobalBasis, x.Runner.GlobalPosition + Vector3.Up * 0.05f) : Transform3D.Identity;
        _golemDrop ??= () => _golem is { } x ? x.Run.Track.Point(x.Run.State.S) + Vector3.Up * 0.05f : Vector3.Zero; // it falls between the rails
        golem.Carry(runner, _golemSpot);
        golem.DeathDrop = _golemDrop;
        golem.ForceAggro();
        // the kiln wall bursts: a thud, the ground shakes, dust where it breaks out
        var at = runner.GlobalPosition;
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, 0f);
        Audio.Synth.PlayAt(root, Audio.Synth.Rumble(), at, -3f);
        Tremor.ShakeOnly(0.07f, 1f);
        var outward = at - _rails.Party.Track.Point(partyS);
        outward.Y = 0f;
        Dust(root, at + Vector3.Up * 1f, outward.LengthSquared() > 0.01f ? -outward.Normalized() : Vector3.Up, 60);
        GD.Print($"[rails] golem {enc.Id} ({golem.DisplayName}, {golem.Profile.Name}, boss={golem.IsBoss}, elite=[{string.Join(",", golem.Profile.Affixes ?? Array.Empty<EliteAffix>())}], hp {golem.Hp:0}/{golem.MaxHp:0}, scale {golem.Profile.Scale:0.00}) bursts onto the enemy line at S={s:0.0} (party S={partyS:0.0})");
        ShowLine(index, enc);
        AdventureDirector.Instance?.RailTaunt(Adventure.Objectives.TauntTrigger.GolemEntrance, urgent: true); // held for the wall line (GolemHoldSeconds)
        SlagLog.Add((enc.Id, "bursts out", golem.Name, partyS, _rails.Party.FrontS, 0f, _now));
        GolemClearsTheSlagLine(enc.Ride, runner.GlobalPosition);
    }

    /// <summary>
    /// The golem's burst (plan 14 Task 6, Ruling 19): every other live slag cart of its ride is freed quietly (a hero on a deck is hauled back
    /// first); its log gives the cart's distance from the golem's runner at <paramref name="runnerAt"/>.
    /// </summary>
    private void GolemClearsTheSlagLine(int ride, Vector3 runnerAt)
    {
        for (var i = _carts.Count - 1; i >= 0; i--)
        {
            var cart = _carts[i];
            if (!IsInstanceValid(cart) || cart.Derailed || RideOf(cart) != ride) continue;
            FreeQuietly(cart, "the golem bursts out", cart.GlobalPosition.DistanceTo(runnerAt));
            _carts.RemoveAt(i);
        }
    }

    /// <summary>The ride of the encounter <paramref name="cart"/> came from, or −1 (traffic).</summary>
    private int RideOf(SlagCart cart)
    {
        foreach (var e in _encounters) if (e.Id == cart.EncounterId) return e.Ride;
        return -1;
    }

    /// <summary>A one-shot dust burst (as a creature breaking cover).</summary>
    private void Dust(Node3D root, Vector3 at, Vector3 toward, int amount)
    {
        var dust = new GpuParticles3D
        {
            Name = "KilnDust", Amount = amount, Lifetime = 1.4, OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial { Direction = toward + Vector3.Up * 0.4f, Spread = 55f, InitialVelocityMin = 1.5f, InitialVelocityMax = 4.5f, Gravity = new Vector3(0, -1.8f, 0), ScaleMin = 0.4f, ScaleMax = 1.3f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.8f, 1f, 0.8f) },
            DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.36f, 0.3f, 0.22f), AlbedoTexture = Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        root.AddChild(dust);
        dust.GlobalPosition = at;
        GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(dust)) dust.QueueFree(); };
    }

    /// <summary>The golem goes silently with its runner, alive or on its own feet; a corpse stays where it fell.</summary>
    private void ClearGolem()
    {
        if (_golem == null) return;
        var keep = _golem;
        _golem = null;
        if (IsInstanceValid(keep.Runner) && !keep.Runner.IsQueuedForDeletion()) keep.Runner.QueueFree();
        if (!IsInstanceValid(keep.Body)) return;
        keep.Body.DeathDrop = null;
        if (!keep.Body.IsAlive) GolemDefeated = true; // it died the tick the ride ended, before its tick saw it
        if (keep.Body.IsAlive && !keep.Body.IsQueuedForDeletion()) keep.Body.QueueFree();
    }

    /// <summary>The golem's tick: its death told once, its runner paced (or on the spur), the points judged, the slam on its clock.</summary>
    private void TickGolem(float dt)
    {
        if (_golem is not { } g) return;
        if (!IsInstanceValid(g.Body)) { if (IsInstanceValid(g.Runner)) g.Runner.QueueFree(); _golem = null; return; }
        if (!g.Body.IsAlive)
        {
            if (!g.DeathTold)
            {
                g.DeathTold = true;
                GolemDefeated = true;
                if (!g.Done) EventBus.Instance.Log("The golem falls between the rails."); // on its runner (not after it was set down)
                g.Done = true;
                GD.Print($"[rails] golem {g.EncounterId} died at runner S={g.Run.State.S:0.0} after {g.Slams} slams; it will not come again");
                if (IsInstanceValid(g.Runner)) g.Runner.QueueFree();
            }
            return;
        }
        if (g.Done) return;
        if (g.OnSpur) { TickSpurRun(g, dt); return; }

        PaceRun(g.Run, 0f, dt);
        if (g.Run.State.S >= _enemy.Length - LineEnd) { LetGo(g, "runs off the end of the line"); return; }
        if (!g.PastSpur) JudgeSpur(g);
        if (g.OnSpur) return;

        // the slam: a glow for the wind-up, then the fist on the plating, every few seconds while it keeps pace
        var rolling = _rails.State.Mode == CartMode.Rolling;
        var alongside = rolling && (PaceRules.Alongside(g.Run.State.S, PaceRules.TargetS(Lanes.EnemyS(_rails.State.S), 0f)) || Flat(g.Body.GlobalPosition, _rails.Cart.GlobalPosition) <= GolemReach); // round a bend the lanes' S part ways: near enough is beside
        g.Clock += dt;
        if (!g.WindingUp)
        {
            if (g.Clock < GolemEvery - GolemWindup) return;
            if (!alongside || g.Body.Effects.IsStunned) { g.Clock = GolemEvery - GolemWindup; return; } // it waits for the cart beside it
            g.WindingUp = true;
            g.Clock = GolemEvery - GolemWindup;
            TelegraphFx.HandGlow(g.Body, GolemWindup);
            EventBus.Instance.RaiseEnemyTelegraph(g.Body, TelegraphRules.RailSlam); // clients glow its fist too (EnemyStateMsg) and hear its wind-up there (Enemy.ClientTelegraph)
            RideAudio.At("golem_windup", g.Body.GlobalPosition);
            return;
        }
        if (g.Clock < GolemEvery) return;
        g.WindingUp = false;
        g.Clock = 0f;
        if (!alongside || g.Body.Effects.IsStunned) return; // the cart got away from it (or it was stunned): the fist falls on nothing
        g.Slams++;
        EventBus.Instance.Log("The golem slams the cart!"); // every peer hears the slam once: the party cart's Hit edge, the golem beside it (CartSound)
        foreach (var rider in _rails.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.08f, 0.4f);
        GD.Print($"[rails] t={_now:0.00} golem slam {g.Slams} at party S={_rails.State.S:0.0} (golem S={g.Run.State.S:0.0}): plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - GolemDent):0}");
        _rails.DentPlating(GolemDent);
    }

    /// <summary>The runner reached the spur's points: thrown, the golem goes down the spur; else it runs on and the lever has nothing left to do this ride.</summary>
    private void JudgeSpur(Golem g)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ArmedRide || p.Spur is not { } spur || g.Run.State.S < spur.EnemyPointsS) continue;
            g.PastSpur = true;
            spur.Lever.Taken = true;
            if (!spur.Lever.Thrown) { GD.Print($"[rails] golem passes the spur points straight at S={g.Run.State.S:0.0}"); return; }
            g.OnSpur = true;
            g.WindingUp = false;
            var v = Mathf.Max(g.Run.State.V, 2f);
            g.Run = new CartRun(spur.Sampler, g.Runner, new CartState(0f, v, CartMode.Rolling, CartRules.MaxPlating)) { Limit = spur.Sampler.Length + 100f }; // no braking for the stop: it runs into it
            g.Run.PoseAt(0f); // the points' clunk is the runner's sound's track change (enemy line to the spur)
            GD.Print($"[rails] golem takes the spur at enemy S={spur.EnemyPointsS:0.0} (v {v:0.0}); the stop is {spur.CrashS:0.0} m on");
            g.Spur = spur;
            return;
        }
    }

    /// <summary>On the spur: it coasts into the stop and is thrown off its runner, badly hurt; then it is a creature on its own feet.</summary>
    private void TickSpurRun(Golem g, float dt)
    {
        var run = g.Run;
        run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, run.State.V), dt);
        run.PoseAt(run.State.S);
        g.Body.FollowCarrier();
        if (g.Spur == null || run.State.S < g.Spur.CrashS) return;
        SpurCrash(g, $"at spur S={run.State.S:0.0}");
    }

    /// <summary>
    /// The ride ends (the cart arrived or was wrecked) with the golem alive and its spur lever thrown, but before it hit the stop
    /// (on the spur, or a runner lagging short of the points): it goes off the spur now, so a thrown lever always pays off.
    /// </summary>
    private void PayOffSpur()
    {
        if (_golem is not { Done: false } g || !IsInstanceValid(g.Body) || !g.Body.IsAlive) return;
        if (!g.OnSpur && g.PastSpur) return; // it passed the points straight: the lever was not thrown in time
        var spur = g.Spur;
        if (spur == null)
            foreach (var p in _pieces)
                if (p.Plan.Ride == g.Ride && p.Spur is { Lever.Thrown: true } s) { spur = s; break; }
        if (spur == null) return;
        g.Spur = spur;
        SpurCrash(g, g.OnSpur ? $"as the ride ends, at spur S={g.Run.State.S:0.0}" : $"as the ride ends, the runner {spur.EnemyPointsS - g.Run.State.S:0.0} m short of the points");
    }

    /// <summary>The golem hits the spur's stop: thrown off its runner, badly hurt (three big run-overs' worth), then a creature on its own feet.</summary>
    private void SpurCrash(Golem g, string where)
    {
        var at = g.Body.GlobalPosition;
        EventBus.Instance.Log("The golem goes off the spur!");
        if (GetParent() is Node3D root)
        {
            Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, 0f);
            Dust(root, at + Vector3.Up * 0.8f, Vector3.Up, 40);
        }
        Tremor.ShakeOnly(0.05f, 0.6f);
        var big = RunOverRules.Outcome(VictimKind.Enemy, true, TierRules.Lethality);
        var hp = g.Body.Hp;
        if (g.Spur != null) g.Spur.Lever.Taken = true;
        LetGo(g, "goes off the spur");
        g.Body.ReceiveHazard(HazardDamage.Crush, big with { HpFraction = big.HpFraction * 3f }, "The spur's stop", KilnHazardIndex); // three big run-overs' worth
        GD.Print($"[rails] golem off the spur {where}: hp {hp:0} -> {g.Body.Hp:0} of {g.Body.MaxHp:0}, alive={g.Body.IsAlive}, now at {g.Body.GlobalPosition}");
    }

    /// <summary>The golem leaves its runner for good: set down beside the enemy line, hunting the party on its own feet.</summary>
    private void LetGo(Golem g, string why)
    {
        g.Done = true;
        g.OnSpur = false;
        g.WindingUp = false;
        var body = g.Body;
        body.DeathDrop = null;
        if (body.Carried) body.Drop(_rails.SetAsideBesideEnemyLane(body.GlobalPosition) + Vector3.Up * 0.1f);
        body.ForceAggro();
        if (IsInstanceValid(g.Runner)) g.Runner.QueueFree();
        GD.Print($"[rails] golem {g.EncounterId} {why}: on its own feet at {body.GlobalPosition}");
    }

    private void PoseGolem(float frac)
    {
        if (_golem is not { Done: false } g || !IsInstanceValid(g.Runner) || !IsInstanceValid(g.Body)) return;
        g.Run.Pose(frac);
        g.Body.FollowCarrier();
        if (!NetSession.IsServer && g.Sound != null && IsInstanceValid(g.Sound)) g.Sound.FeedTrack(GolemTrack(g));
    }

    /// <summary>The golem runner's track as the wire numbers it, on every role: 1 the enemy line, the spur's 2+k once it is on it.</summary>
    private byte GolemTrack(Golem g) => g.OnSpur ? SpurTrackNumber(g.Spur) : (byte)1;

    // ---- the set pieces --------------------------------------------------------------------------

    /// <summary>
    /// The hammers' beat (each hammer cues its own wind and fall, positional: <see cref="DropHammer"/>): at the drop, a walker under a
    /// hammer is crushed (any time), and with <paramref name="judgeCart"/> (their ride is armed) a cart under it loses <see cref="HammerDent"/> plating and
    /// its riders are shaken (never mangled).
    /// </summary>
    private void TickHammers(Piece piece, bool judgeCart)
    {
        if (piece.Hammers == null) return;
        var tick = _builder.FloorTick;
        foreach (var hammer in piece.Hammers)
        {
            var phase = SetPieceRules.Phase(tick, hammer.Index, out _);
            var was = hammer.Judged;
            hammer.Judged = phase;
            if (phase == was) continue;
            if (phase != HammerPhase.Fall) continue; // the wind and the fall are heard from the hammer itself (DropHammer)
            if (judgeCart && _rails.State.Mode != CartMode.Wrecked && !_rails.OnEnemyLane(hammer.S) && Mathf.Abs(_rails.State.S - hammer.S) <= CartRules.HalfLength + DropHammer.HalfLength + 0.05f)
            {
                HammerHits++;
                EventBus.Instance.Log("A drop hammer slams into the cart!");
                foreach (var rider in _rails.Cart.Riders.Values)
                    if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.1f, 0.45f);
                GD.Print($"[rails] t={_now:0.00} hammer {hammer.Index} at S={hammer.S:0.0} hits the cart at S={_rails.State.S:0.0}: plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - HammerDent):0}");
                _rails.DentPlating(HammerDent);
            }
            CrushUnder(hammer.S, DropHammer.HalfLength, gated: !judgeCart);
        }
    }

    /// <summary>Walkers under a falling hammer at party S <paramref name="s"/> are crushed (by the lethality, as any hazard).</summary>
    private void CrushUnder(float s, float halfLength, bool gated)
    {
        if (!Under(s, halfLength, 2.6f, gated)) return;
        foreach (var body in _under)
        {
            if (body is not IHazardVictim victim) continue;
            GD.Print($"[rails] a drop hammer at S={s:0.0} crushes {body.Name}");
            victim.ReceiveHazard(HazardDamage.Crush, HazardRules.Outcome(victim.HazardVictimKind, TierRules.Lethality), "The drop hammer", KilnHazardIndex);
        }
    }

    /// <summary>An unarmed piece's scan (gated, in <see cref="Under"/>) looks only at bodies this close to it. Nothing the scan reaches lies that far: it changes nothing.</summary>
    public const float UnarmedScan = 30f;

    /// <summary>
    /// Fill <see cref="_under"/> with the walkers on the party lane within <paramref name="halfLength"/> (plus their radius) of S <paramref name="s"/>.
    /// True when there are any. <paramref name="gated"/> (a piece whose ride is not armed): only bodies within <see cref="UnarmedScan"/>.
    /// </summary>
    private bool Under(float s, float halfLength, float height, bool gated = false)
    {
        _under.Clear();
        var party = Main.Instance.Party;
        GatherBodies(party);
        var track = _rails.Party.Track;
        var at = track.Point(s);
        foreach (var body in _bodies)
        {
            if (!IsInstanceValid(body) || body is Adventurer { Seated: true }) continue;
            var pos = body.GlobalPosition;
            var d2 = pos.DistanceSquaredTo(at);
            if (gated && d2 > UnarmedScan * UnarmedScan) continue;
            if (d2 > 25f) continue;
            if (!track.Measure(pos, s, 3f, out var b)) continue;
            var r = RunOverSweep.RadiusOf(body);
            if (Mathf.Abs(b.S - s) > halfLength + r || b.Lateral > UnderReach || b.Height < -0.5f || b.Height > height) continue;
            _under.Add(body);
        }
        return _under.Count > 0;
    }

    /// <summary>
    /// The pour's clock (the pour cues its own hiss and stream, positional: <see cref="MoltenPour"/>): while it pours, a walker in it
    /// burns every <see cref="PourBurnEvery"/> s (any
    /// time), and with <paramref name="judgeCart"/> (its ride is armed) a cart through the stream loses <see cref="PourDent"/> plating
    /// and each seated hero is burned twice, once per pour.
    /// </summary>
    private void TickPour(Piece piece, float dt, bool judgeCart)
    {
        if (piece.Pour is not { } pour) return;
        var phase = pour.Quenched ? PourPhase.Dry : SetPieceRules.Pour(_builder.FloorTick, out _);
        var was = (PourPhase)piece.Phase;
        piece.Phase = (int)phase;
        if (phase != was)
        {
            if (phase == PourPhase.Pouring) { piece.PourCount++; piece.BurnClock = 0f; }
        }
        if (phase != PourPhase.Pouring) return;
        if (judgeCart && piece.PourHit != piece.PourCount && _rails.State.Mode != CartMode.Wrecked && !_rails.OnEnemyLane(piece.S)
            && Mathf.Abs(_rails.State.S - piece.S) <= CartRules.HalfLength + MoltenPour.Zone)
        {
            piece.PourHit = piece.PourCount;
            PourHits++;
            EventBus.Instance.Log("Molten slag pours over the cart!");
            _burnt.Clear();
            foreach (var rider in _rails.Cart.Riders.Values) _burnt.Add(rider); // a burn can unseat: read the book first
            var attack = TrapRules.FloorFire(_builder.CurrentFloor);
            var burnt = 0;
            foreach (var rider in _burnt)
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero))
                {
                    hero.ReceiveAttack(Trap.TrapStats, attack, "The molten slag");
                    if (!hero.Downed) hero.ReceiveAttack(Trap.TrapStats, attack, "The molten slag");
                    burnt++;
                }
            GD.Print($"[rails] t={_now:0.00} pour {piece.Plan.Id} at S={piece.S:0.0} hits the cart at S={_rails.State.S:0.0}: plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - PourDent):0}, {burnt} riders burnt twice");
            _rails.DentPlating(PourDent);
        }
        if ((piece.BurnClock -= dt) > 0f) return;
        piece.BurnClock = PourBurnEvery;
        if (!Under(piece.S, MoltenPour.Zone, 2.5f, gated: !judgeCart)) return;
        var rng = Main.Instance.Party.Rng;
        var lick = TrapRules.FloorFire(_builder.CurrentFloor);
        foreach (var body in _under)
        {
            if (body is Adventurer { Downed: false } hero) hero.ReceiveAttack(Trap.TrapStats, lick, "The molten slag");
            else if (body is Enemy { IsAlive: true } enemy) enemy.ReceiveAttack(Trap.TrapStats, lick, new HitContext(), rng, "the molten slag"); // NPCs are spared, as the floor's fires spare them
        }
    }

    /// <summary>
    /// The cart's front on the trestle starts its grace; at the end of it the trestle falls, and catches a cart still on it: over a
    /// crevasse (<see cref="RailController.OverCrevasse"/>) the train falls with it, anywhere else it is wrecked. Over a crevasse a seated
    /// hero calls the drop on entry (<see cref="RailCalls"/>, <see cref="BarkTrigger.Crevasse"/>): a fall from it kills, so it is said.
    /// </summary>
    private void TickTrestle(Piece piece, PartyController party, bool rolling, float dt)
    {
        if (piece.Trestle is not { Collapsed: false } trestle) return;
        if (piece.Timer < 0f)
        {
            if (!rolling || _rails.Party.FrontS < trestle.From) return;
            piece.Timer = SetPieceRules.TrestleGrace;
            EventBus.Instance.Log("The trestle groans under the cart.");
            RideAudio.At("trestle_creak", trestle.Entry); // a client's from its own cart's front (ClientPresent)
            var over = _rails.OverCrevasse(trestle.From, trestle.To);
            var by = over ? RailCalls.Say(party, _rails.Cart, BarkTrigger.Crevasse, CrevasseWatch.WarnLog) : null;
            GD.Print($"[rails] t={_now:0.00} the cart's front enters the trestle at S={_rails.State.S:0.0} (v {_rails.State.V:0.0}): it falls in {SetPieceRules.TrestleGrace:0.0} s{(over ? $"; over the crevasse, called by {by ?? "none (logged)"}" : "")}");
            return;
        }
        if ((piece.Timer -= dt) > 0f) return;
        var on = _rails.State.Mode != CartMode.Wrecked && _rails.Party.BackS < trestle.To && _rails.Party.FrontS > trestle.From;
        if (GetParent() is Node3D root) trestle.Collapse(root); // with its cue
        Tremor.ShakeOnly(0.04f, 0.8f);
        GD.Print($"[rails] t={_now:0.00} the trestle falls with the cart at S={_rails.State.S:0.0} (back {_rails.Party.BackS:0.0}, trestle to {trestle.To:0.0}): {(on ? "caught" : "clear")}");
        if (on && _rails.OverCrevasse(trestle.From, trestle.To)) _rails.Fall("The trestle gives way into the crevasse!");
        else if (on) _rails.Catch("The trestle gives way under the cart!");
        else EventBus.Instance.Log("The trestle collapses behind the cart.");
    }

    /// <summary>A seated hero calls the jump once (<see cref="RailCalls"/>), at the cart's 3 s look-ahead to the lip (the companions are already pumping).</summary>
    private void TickJump(Piece piece, PartyController party, bool rolling)
    {
        if (piece.Barked || !rolling || piece.Gap is not { } gap || _rails.Jumped) return;
        var d = gap.From - _rails.Party.FrontS;
        if (d < 0f || d > Mathf.Max(_rails.State.V, RunOverRules.WarnMinSpeed) * WarnSeconds) return;
        piece.Barked = true;
        var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.JumpCall, "Pump it — we have to jump!");
        GD.Print($"[rails] jump {piece.Plan.Id} called at front S={_rails.Party.FrontS:0.0} ({d:0.0} m to the lip, v {_rails.State.V:0.0}); bark={by ?? "none"}");
    }

    /// <summary>The cart's front meets the buffer: it smashes, no harm done; the cart rolls on into the stop.</summary>
    private void TickBuffer(Piece piece, bool rolling)
    {
        if (!rolling || piece.Buffer is not { Smashed: false } buffer || _rails.Party.FrontS < buffer.FaceS || GetParent() is not Node3D root) return;
        buffer.Smash(root, _rails.Cart.Velocity);
        EventBus.Instance.Log("The cart smashes through the buffer!");
        var taunt = AdventureDirector.Instance?.RailTaunt(Adventure.Objectives.TauntTrigger.BufferSmashed, urgent: true) == true; // the line's last word: held for a line being said (RideHoldSeconds), never lost to the cooldown after the golem's
        foreach (var rider in _rails.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.06f, 0.3f);
        GD.Print($"[rails] buffer {piece.Plan.Id} smashed at S={_rails.State.S:0.0} (v {_rails.State.V:0.0}); taunt said or held={taunt}");
    }
}
