namespace DungeonCrawler.Dungeon.Rails;

/// <summary>Held waits at a stop, Rolling moves along the line, Wrecked is a dead cart.</summary>
public enum CartMode : byte { Held, Rolling, Wrecked }

/// <param name="S">Distance along the line, in metres.</param>
/// <param name="V">Speed along the line, in metres per second.</param>
/// <param name="Plating">The cart's armour; damage is applied elsewhere.</param>
public readonly record struct CartState(float S, float V, CartMode Mode, float Plating);

/// <param name="Slope">Rise over run of the track at S.</param>
/// <param name="StopAt">The S of the next stop; the cart halts there.</param>
/// <param name="TargetOverride">The speed the drive aims for instead of <see cref="CartPhysics.TargetSpeed"/> (a slag cart pacing
/// the party's: pumpers and crew wagons then count for nothing), or negative (the default) for none.</param>
/// <param name="DriveAccel">The drive's acceleration at a full speed gap in place of <see cref="CartRules.DriveAccel"/> (the hijack's slow roll back,
/// <see cref="HijackRules.DriveAccelFor"/>), or negative (the default) for the cart's own.</param>
public readonly record struct CartInput(float Slope, bool Brake, int Pumpers, int CrewWagons, float StopAt, float TargetOverride = -1f, float DriveAccel = -1f);

/// <summary>Every number for the mine cart. Engine-free; the Godot nodes only read it.</summary>
public static class CartRules
{
    public const float Cruise = 6f;
    public const float MaxSpeed = 11f;
    public const float ClimbSpeed = 3f;
    public const float ClimbSlope = 0.05f;

    public const float DriveAccel = 2.5f;
    public const float BrakeDecel = 6f;
    public const float StopDecel = 3f;
    public const float Gravity = 9.81f;
    public const float Drag = 0.02f;

    public const float PumpBoost1 = 0.2f;
    public const float PumpBoost2 = 0.3f;

    public const float LoadPerWagon = 0.05f;
    /// <summary>The wagons the load counts: a longer train weighs no more, so a line of any length keeps the same pace and the same jump.</summary>
    public const int MaxLoadWagons = 3;

    public const float MaxPlating = 100f;

    public const float HalfLength = 1.1f;
    public const float HalfWidth = 0.75f;
    public const float Clearance = 1.6f;
}

/// <summary>The cart's motion along its line. Engine-free, fixed-step Euler, no allocation.</summary>
public static class CartPhysics
{
    const float SnapGap = 0.02f;
    const float ApproachSlack = 0.05f;
    const float CreepSpeed = 0.25f;

    /// <summary>The speed the drive aims for: pumpers speed it up, crew wagons weigh it down.</summary>
    public static float TargetSpeed(int pumpers, int crewWagons) => Scaled(CartRules.Cruise, pumpers, crewWagons);

    /// <summary>The speed the haul rope pulls the cart up a climb at, with the same pump and load scaling.</summary>
    public static float ClimbTarget(int pumpers, int crewWagons) => Scaled(CartRules.ClimbSpeed, pumpers, crewWagons);

    static float Scaled(float speed, int pumpers, int crewWagons)
    {
        float boost = pumpers >= 2 ? CartRules.PumpBoost2 : pumpers == 1 ? CartRules.PumpBoost1 : 0f;
        return speed * (1f + boost) / (1f + CartRules.LoadPerWagon * Math.Min(crewWagons, CartRules.MaxLoadWagons));
    }

    /// <summary>One step. Only a Rolling cart moves; it never passes StopAt and holds there. The controller must set StopAt ahead of S before switching Held to Rolling.</summary>
    public static CartState Step(CartState c, in CartInput i, float dt)
    {
        if (c.Mode != CartMode.Rolling) return c;

        float a = -CartRules.Gravity * i.Slope - CartRules.Drag * c.V * c.V;
        if (i.Brake) a -= CartRules.BrakeDecel;
        else
        {
            var commanded = i.TargetOverride >= 0f;
            float gap = (commanded ? i.TargetOverride : TargetSpeed(i.Pumpers, i.CrewWagons)) - c.V;
            a += (gap < -1f ? -1f : gap > 1f ? 1f : gap) * (i.DriveAccel >= 0f ? i.DriveAccel : CartRules.DriveAccel);
            // At or below target the drive also cancels drag, so the cart settles on the target; above it drag is real.
            if (gap >= 0f) a += CartRules.Drag * c.V * c.V;
            // A haul rope pulls the cart up a climb that the drive could not manage.
            if (i.Slope > CartRules.ClimbSlope)
            {
                var haul = ClimbTarget(i.Pumpers, i.CrewWagons);
                if (commanded && i.TargetOverride < haul) haul = i.TargetOverride; // a commanded cart is never hauled past its command
                float rope = (haul - c.V) * 2f;
                if (a < rope) a = rope;
            }
        }

        float remaining = i.StopAt - c.S;
        bool approaching = remaining <= c.V * c.V / (2f * CartRules.StopDecel) + ApproachSlack;
        if (approaching)
        {
            float need = remaining > SnapGap ? c.V * c.V / (2f * remaining) : CartRules.StopDecel;
            float decel = need > CartRules.StopDecel ? need : CartRules.StopDecel;
            if (a > -decel) a = -decel;
        }

        float s = c.S + c.V * dt;
        float v = c.V + a * dt;
        if (v > CartRules.MaxSpeed) v = CartRules.MaxSpeed;
        float floor = approaching ? CreepSpeed : 0f;
        if (v < floor) v = floor;

        if (i.StopAt - s < SnapGap) return c with { S = i.StopAt, V = 0f, Mode = CartMode.Held };
        if (s < 0f) s = 0f;
        return c with { S = s, V = v };
    }
}
