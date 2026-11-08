namespace DungeonCrawler.Enemies;

/// <summary>How an enemy's attack announces itself: the wind-up's length under Frost or Haste, the clip's pace, the white tint, the signature decals.</summary>
public static class TelegraphRules
{
    /// <summary>A wind-up or recovery scaled by the attacker's attack-speed factor; the floor keeps a frozen giant from winding up forever.</summary>
    public static float Scaled(float seconds, float attackSpeed) => seconds / Math.Max(0.2f, attackSpeed);
    public const float DefaultHitReactSeconds = 0.6f;

    /// <summary>(tune) signature delays: was 0.8 for all, Charge 0.4.</summary>
    public const float SlamDelay = 1.1f, VolleyDelay = 1.1f, SummonDelay = 1.1f, ChargeDelay = 0.7f;
    /// <summary>The state ends Delay + Recover after it began (was 1.6 total).</summary>
    public const float SignatureRecover = 0.8f;
    /// <summary>A charge gives up this long after its wind-up (was 2.0 total).</summary>
    public const float ChargeRun = 1.6f;
    /// <summary>The body tints toward white for the last part of a wind-up.</summary>
    public const float TintSeconds = 0.2f;
    /// <summary>(tune) where the blow falls in the attack clip.</summary>
    public const float StrikeFraction = 0.55f;
    public const float SlamRadius = 4.5f, ChargeLaneLength = 9f, ChargeLaneWidth = 1.6f, PulseRadius = 1.5f;

    public static float Delay(Signature s) => s == Signature.Charge ? ChargeDelay : s == Signature.Volley ? VolleyDelay : s == Signature.Summon ? SummonDelay : SlamDelay;
    /// <summary>The clip speed that puts its strike frame at the end of the wind-up.</summary>
    public static float ClipSpeed(float clipSeconds, float windupSeconds) => clipSeconds * StrikeFraction / Math.Max(0.05f, windupSeconds);
    /// <summary>0 until the last <see cref="TintSeconds"/> of a wind-up, 1 at the blow; 0 when not winding up.</summary>
    public static float TintAmount(float windupLeft) => windupLeft < 0f ? 0f : Math.Clamp(1f - windupLeft / TintSeconds, 0f, 1f);
}
