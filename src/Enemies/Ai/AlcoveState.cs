using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Enemies.Ai;

/// <summary>
/// An alcove shooter's post (plan 16 Task 8), filled by <see cref="RailEncounterController"/>: the back of its pocket (the centre
/// <see cref="AlcoveRules.BackFromCentre"/> away from the lane) and its mouth (<see cref="AlcoveRules.MouthFromCentre"/> toward it), how far the
/// train's front is before the post and how far its back is past it, and the nearest seated hero in range and sight (or null).
/// </summary>
public sealed class AlcovePost
{
    public Vector3 Back;
    public Vector3 Mouth;
    public Func<float> FrontToPost = () => float.PositiveInfinity;
    public Func<float> BackPastPost = () => float.NegativeInfinity;
    public Func<Adventurer?> Target = () => null;
    /// <summary>It stepped out (the controller prints and logs it).</summary>
    public Action? SteppedOut;
    /// <summary>A bolt left at the target, aimed at the point; the seconds its wind-up waited past the cadence for a target (the controller logs it).</summary>
    public Action<Adventurer, Vector3, float>? Fired;
    /// <summary>A wind-up was lost (a stagger) or found no target at its end: no bolt (the controller logs it).</summary>
    public Action<string>? Lost;
}

/// <summary>
/// An alcove shooter's pinned state (plan 16 Task 8, spec section 4): never unaware, never leaving its pocket. Hidden, it stands at the back,
/// untargetable, and only checks the train's distance every <see cref="AlcoveRules.HiddenCheckEvery"/> s; with the front
/// <see cref="AlcoveRules.WakeAt"/> m before the post it becomes targetable and steps to the mouth over <see cref="AlcoveRules.StepOutSeconds"/>;
/// then every <see cref="AlcoveRules.ShootEvery"/> s it winds up as its kind and looses a bolt with lead aim (<see cref="LeadAim"/>) at the
/// nearest seated hero within <see cref="AlcoveRules.ShootRange"/> in sight (never at crew). Done once the train's back is
/// <see cref="AlcoveRules.GoneBehind"/> m past: the controller frees it. Server and solo.
/// </summary>
public sealed class AlcoveState : AiState
{
    private readonly AlcovePost _post;
    private float _check, _step, _cycle, _wind = -1f, _waited;

    public AlcoveState(AlcovePost post) => _post = post;

    public override string Name => "Alcove";
    public override bool Unaware => false;
    public AlcovePhase Phase { get; private set; } = AlcovePhase.Hidden;
    public AlcovePost Post => _post;

    public override void Enter(Enemy enemy)
    {
        Phase = AlcovePhase.Hidden;
        enemy.SetUntargetable(true); // hidden: seated companions cannot pick it off before it wakes (pre-flight F16)
        Hold(enemy, _post.Back);
        enemy.FaceToward(_post.Mouth);
    }

    public override void Exit(Enemy enemy) => enemy.SetUntargetable(false);

    public override void Update(Enemy enemy, float dt)
    {
        switch (Phase)
        {
            case AlcovePhase.Hidden:
                if ((_check -= dt) > 0f) return;
                _check = AlcoveRules.HiddenCheckEvery;
                Hold(enemy, _post.Back);
                Phase = AlcoveRules.Next(Phase, _post.FrontToPost(), _post.BackPastPost());
                if (Phase != AlcovePhase.StepOut) return;
                enemy.SetUntargetable(false);
                _step = 0f;
                _post.SteppedOut?.Invoke();
                return;
            case AlcovePhase.StepOut:
                if ((Phase = AlcoveRules.Next(Phase, _post.FrontToPost(), _post.BackPastPost())) == AlcovePhase.Done) return;
                _step += dt;
                Hold(enemy, _post.Back.Lerp(_post.Mouth, Mathf.Clamp(_step / AlcoveRules.StepOutSeconds, 0f, 1f)));
                if (_step < AlcoveRules.StepOutSeconds) return;
                Phase = AlcovePhase.Shooting;
                _cycle = AlcoveRules.ShootEvery - dt; // the first wind-up on the next tick (Shoot adds it)
                return;
            case AlcovePhase.Shooting:
                if ((Phase = AlcoveRules.Next(Phase, _post.FrontToPost(), _post.BackPastPost())) == AlcovePhase.Done) { _wind = -1f; return; }
                Hold(enemy, _post.Mouth);
                Shoot(enemy, dt);
                return;
        }
    }

    private void Shoot(Enemy enemy, float dt)
    {
        _cycle += dt;
        if (_wind >= 0f)
        {
            if (enemy.WindupLeft == -1f) { _wind = -1f; _post.Lost?.Invoke("staggered"); return; } // a stagger took the wind-up (and its glow): no bolt from it
            _wind += dt;
            if (_post.Target() is { } t) enemy.FaceToward(t.GlobalPosition);
            if (_wind < enemy.WindupSeconds) return;
            _wind = -1f;
            if (_post.Target() is not { } target) { _post.Lost?.Invoke("no target"); return; } // it went out of range or sight during the wind-up: no bolt
            var vel = target.Cart?.Velocity ?? target.Velocity;
            var lead = LeadAim.Point(N(enemy.Muzzle), N(target.GlobalPosition), N(vel), enemy.Profile.ProjectileSpeed);
            var aim = new Vector3(lead.X, lead.Y, lead.Z) + Vector3.Up * AlcoveRules.AimUp;
            enemy.FireAt(aim);
            _post.Fired?.Invoke(target, aim, _waited);
            return;
        }
        if (_cycle < AlcoveRules.ShootEvery || _post.Target() is not { } next) return;
        _waited = _cycle - AlcoveRules.ShootEvery;
        _cycle = 0f;
        _wind = 0f;
        enemy.FaceToward(next.GlobalPosition);
        enemy.BeginWindup(enemy.WindupSeconds); // as its kind: the clip, the hand's glow, the telegraph told
    }

    private static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Stand exactly at <paramref name="at"/>: nothing moves a pinned shooter but its state.</summary>
    private static void Hold(Enemy enemy, Vector3 at)
    {
        enemy.GlobalPosition = at;
        enemy.Velocity = Vector3.Zero;
    }
}
