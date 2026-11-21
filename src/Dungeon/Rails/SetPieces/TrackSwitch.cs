using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A switch lever on a post beside the party line, with a lamp on top: green while the points lie straight, red when the
/// lever is thrown. E (layer 8) throws it over or back; any hit (layer 4: a blade from the seat, an arrow from afar) only
/// throws it, never back, so a stray friendly shot cannot undo a thrown lever. The post is solid (layer 1). A <see cref="Fixed"/> switch is always thrown and takes nothing (the return switch).
/// A <see cref="Deadly"/> one (a live switch back, plan 11) takes no hits (layers 1|8) and moves only for a human's E
/// (<see cref="SwitchBackRules.MayToggle"/>): a companion's arrow or blade, or a hero bot, never decides it. Built by
/// the <see cref="RailEncounterController"/>, which reads <see cref="Thrown"/> as the cart's front reaches the points. The server
/// judges it and tells every change (<see cref="NetRef"/>: "thrown", "straight", "taken", "free", "reset"); a client's copy only shows
/// what it is told (<see cref="ShowThrown"/>, <see cref="ShowTaken"/>, <see cref="ShowReset"/>). Ride 4's split lever (plan 12, <see cref="SplitSwitch"/>)
/// is deadly too, its seated window from <see cref="WindowStartS"/> to its taking (<see cref="WindowEndS"/>), its prompt the split's, and a throw
/// just short of its points held to them (<see cref="HoldThrow"/>).
/// </summary>
public partial class TrackSwitch : StaticBody3D, IInteractable, ICombatTarget, ISoundPoint
{
    /// <summary>Where the lever's clunk is heard, on every role: at its pivot.</summary>
    public Vector3 SoundPoint => GlobalPosition + Vector3.Up * 0.9f;

    public const float PostHeight = 1.5f;
    const float Swing = 0.6f; // the handle's lean either way, radians
    /// <summary>The mine pack's Lever stands this tall on the pivot, its foot this far below it (its top where the box rod's knob was).</summary>
    const float LeverTall = 1.1f, LeverFoot = 0.3f;

    /// <summary>The party line's S at the points (where the cart takes the switch), not the lever's own S.</summary>
    public float PointsS { get; init; }
    /// <summary>The party line's S at the lever, set by its builder on every peer (the seated window opens from it: <see cref="SeatedSwitchRules"/>).</summary>
    public float LeverS { get; init; }
    /// <summary>
    /// What the seated window reads of this lever (<see cref="SeatedSwitchRules.InWindow"/>): it opens <see cref="SeatedSwitchRules.WindowAhead"/>
    /// before <see cref="LeverS"/>, or at <see cref="WindowStartS"/> when set, and ends at <see cref="PointsS"/>, or <see cref="WindowEndS"/> when set.
    /// </summary>
    public SeatedLever SeatedView => new(float.IsNaN(WindowStartS) ? LeverS : WindowStartS + SeatedSwitchRules.WindowAhead,
        float.IsNaN(WindowEndS) ? PointsS : WindowEndS, Fixed, Taken);
    /// <summary>
    /// Where the seated window ends instead of the points (NaN: at the points). Ride 4's split sets +∞ (plan 12, Ruling 5): its window runs on
    /// past the points until it is <see cref="Taken"/> (the runaway reached them), and a press is late only once taken.
    /// </summary>
    public float WindowEndS { get; init; } = float.NaN;
    /// <summary>Where the seated window opens instead of <see cref="SeatedSwitchRules.WindowAhead"/> before the lever (NaN: there). The split's opens at its points less the straight before them (ruling R-bend).</summary>
    public float WindowStartS { get; init; } = float.NaN;
    /// <summary>
    /// A throw (straight to thrown) by this actor now is held, not applied (the split's, Ruling 7: pressed just short of its points). It is
    /// recorded (<see cref="HeldBy"/>) and applied by its builder after the cart's branch is decided (<see cref="ThrowHeld"/>). Server and solo.
    /// </summary>
    public Func<Adventurer, bool>? HoldThrow { get; init; }
    /// <summary>Who pressed the throw being held to the points (<see cref="HoldThrow"/>), or null.</summary>
    public string? HeldBy { get; private set; }
    /// <summary>When a held throw was last applied (<c>NetSession.Instance.Now</c>; −∞ never): a set-back is ignored for <see cref="SplitRules.HeldGuardSeconds"/> after.</summary>
    public double HeldAppliedAt { get; private set; } = double.NegativeInfinity;
    /// <summary>Ride 4's split (plan 12): the cart's front is past its points now. Set, the prompt is the split's (<see cref="PropPromptRules.SplitPrompt"/>).</summary>
    public Func<bool>? SplitPast { get; init; }
    /// <summary>Server and solo: when it was last toggled by a use or a hit (<c>NetSession.Instance.Now</c>; never: −∞), and by whom (a hero's or attacker's name), for the seated set-back guard (<see cref="SeatedSwitchRules.RefuseSetBack"/>).</summary>
    public double LastToggledAt { get; private set; } = double.NegativeInfinity;
    /// <inheritdoc cref="LastToggledAt"/>
    public string? LastToggledBy { get; private set; }
    /// <summary>Always thrown and not to be touched: the return switch brings the cart home before the ride ends.</summary>
    public bool Fixed { get; init; }
    /// <summary>
    /// A live switch back (plan 11, Ruling 15): left straight, the train runs on to the broken bridge. It takes no hits and only a human moves it.
    /// Never true with <see cref="Fixed"/>.
    /// </summary>
    public bool Deadly { get => _deadly && !Fixed; init => _deadly = value; }
    private readonly bool _deadly;
    /// <summary>
    /// A deadly lever is offered only while this holds (its builder's: the cart across with its return open, <see cref="SwitchBackRules.Offered"/>),
    /// on every peer: otherwise it has no prompt (neither the seated window nor the look ray offers it) and E does nothing.
    /// </summary>
    public Func<bool>? OfferedNow { get; init; }
    private bool Withheld => Deadly && OfferedNow?.Invoke() == false;
    /// <summary>
    /// A press is taken now, a little wider than the offer (the split's <see cref="SplitRules.Accepts"/>: a co-op client's press up to
    /// <see cref="SeatedSwitchRules.AcceptLead"/> before the server's window opens is held, not dropped). Null: as <see cref="OfferedNow"/>.
    /// </summary>
    public Func<bool>? AcceptsNow { get; init; }
    private bool PressWithheld => Deadly && (AcceptsNow ?? OfferedNow)?.Invoke() == false;
    public bool Thrown { get; private set; }
    /// <summary>
    /// The cart's front has passed its points this ride: the lever offers nothing until the ride is over. Taking it is told ("taken"),
    /// and so is clearing it outside a <see cref="Reset"/> ("free": a ride's arming), so a client's prompt never stays blank.
    /// </summary>
    public bool Taken
    {
        get => _taken;
        set
        {
            if (_taken == value) return;
            _taken = value;
            Tell(value ? "taken" : "free");
        }
    }
    private bool _taken;
    /// <summary>Its wire name ("switch:{piece}" or "spur:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.7f, Roughness = 0.5f };
    private readonly StandardMaterial3D _handle = new() { AlbedoColor = new Color(0.55f, 0.12f, 0.08f), Roughness = 0.6f };
    private readonly StandardMaterial3D _lamp = new() { EmissionEnabled = true, EmissionEnergyMultiplier = 3f, Roughness = 0.4f };
    private Node3D _pivot = null!;
    private OmniLight3D _light = null!;

    public TrackSwitch()
    {
        CollisionMask = 0;
    }

    public override void _Ready()
    {
        CollisionLayer = Fixed ? 1u : Deadly ? 1u | 8u : 1u | 4u | 8u; // solid; and, unless fixed, something to use and (unless deadly) to hit
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.5f, PostHeight, 0.5f) }, Position = new Vector3(0, PostHeight / 2f, 0) }); // a little bigger than it looks: easier to reach from a moving cart
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, PostHeight, 0.14f) }, MaterialOverride = _iron, Position = new Vector3(0, PostHeight / 2f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.12f, 0.4f) }, MaterialOverride = _iron, Position = new Vector3(0, 0.06f, 0) });
        // the lever: a rod on a pivot at the post's waist, leaning toward the points when straight, back when thrown
        _pivot = new Node3D { Name = "Pivot", Position = new Vector3(0, 0.9f, 0) };
        AddChild(_pivot);
        if (MinePack.Piece("Lever", new Vector3(0, LeverTall, 0)) is { } lever)
        {
            _pivot.AddChild(lever); // swung by the pivot as the rod was
            lever.Position = new Vector3(0, -LeverFoot, 0);
        }
        else
        {
            _pivot.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.7f, RadialSegments = 8 }, MaterialOverride = _iron, Position = new Vector3(0, 0.35f, 0) });
            _pivot.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 10, Rings = 5 }, MaterialOverride = _handle, Position = new Vector3(0, 0.72f, 0) });
        }
        // the lamp on top
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 12, Rings = 6 }, MaterialOverride = _lamp, Position = new Vector3(0, PostHeight + 0.08f, 0) });
        _light = new OmniLight3D { Name = "Lamp", OmniRange = 4f, LightEnergy = 1.4f, ShadowEnabled = false, Position = new Vector3(0, PostHeight + 0.2f, 0) };
        AddChild(_light);
        SetThrown(Fixed);
    }

    /// <summary>The same rule on every peer: a client's copy reads the Fixed, Taken and Thrown the server told it.</summary>
    public string Prompt => Withheld ? "" : SplitPast is { } past ? PropPromptRules.SplitPrompt(Taken, past(), Thrown) : PropPromptRules.SwitchPrompt(Fixed, Taken, Thrown);

    /// <summary>
    /// E on the lever: the server's (or solo) use by any hero up on their feet, a remote player's through its InteractIntent (the server checked the
    /// reach). A deadly lever does nothing for a companion's or a parked body's use, nor while it is not offered (<see cref="OfferedNow"/>).
    /// </summary>
    public void Interact(PartyController party, Adventurer actor)
    {
        if (actor.Downed || !SwitchBackRules.MayToggle(Deadly, actor.Control) || PressWithheld) return;
        if (!Thrown && !NetSession.IsClient && HoldThrow?.Invoke(actor) == true) // the hold is the server's: a client's copy never asks it (it would write the hold's state there)
        {
            if (Fixed || Taken) return;
            var again = HeldBy != null;
            HeldBy = actor.State.Name; // applied after the cart's branch is decided (ThrowHeld): a press just short of the points never sends it down the spur
            if (!again) EventBus.Instance.Log(SplitRules.HeldLog); // the hold is seen: the lever stands straight until the train passes the points
            GD.Print($"[rails] {GetParent()?.Name}/{Name}: {actor.State.Name}'s throw held to the points{(again ? " (again)" : "")}");
            return;
        }
        if (SplitRules.HeldGuards(Thrown, NetSession.Instance?.Now ?? 0, HeldAppliedAt))
        {
            GD.Print($"[rails] {GetParent()?.Name}/{Name}: {actor.State.Name}'s set-back {(NetSession.Instance?.Now ?? 0) - HeldAppliedAt:0.00} s after the held throw went in, ignored");
            return;
        }
        Toggle(actor.State.Name);
    }

    /// <summary>Server and solo: the held throw (<see cref="HeldBy"/>) applied as an ordinary toggle by <paramref name="by"/>. True when it threw the lever.</summary>
    public bool ThrowHeld(string by)
    {
        HeldBy = null;
        if (Thrown || !Toggle(by)) return false;
        HeldAppliedAt = NetSession.Instance?.Now ?? 0;
        return true;
    }

    /// <summary>Server and solo: a held throw is dropped unapplied (the watchdog set the lever back, or the ride ended).</summary>
    public void DropHeld() => HeldBy = null;

    public string DisplayName => "switch";
    public bool IsAlive => true;
    public bool IsUnaware => false;
    public bool IsCreature => false;
    public void ApplyStatus(StatusApplication status) { }

    /// <summary>Any hit throws it over; a thrown lever takes nothing (only E sets it back). A deadly lever takes no hits at all.</summary>
    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (Deadly || Thrown || !Toggle(attackerName)) return DamageResult.Miss(attack.Type);
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    private bool Toggle(string who)
    {
        if (NetSession.IsClient || Fixed || Taken) return false;
        SetThrown(!Thrown);
        LastToggledAt = NetSession.Instance?.Now ?? 0;
        LastToggledBy = who;
        RideAudio.At("switch_clunk", SoundPoint); // a client's comes from its "thrown" or "straight" (RideAudio.OnProp), at the same point
        var what = SplitPast != null ? "split" : "switch";
        EventBus.Instance.Log(Thrown ? $"{who} throws the {what}." : $"{who} sets the {what} back.");
        Tell(Thrown ? "thrown" : "straight");
        return true;
    }

    /// <summary>Back to straight without a word (the ride is over or undone). A fixed switch stays thrown.</summary>
    public void Reset()
    {
        var changed = _taken || (Thrown && !Fixed);
        _taken = false;
        HeldBy = null;
        HeldAppliedAt = double.NegativeInfinity;
        if (!Fixed) SetThrown(false);
        LastToggledAt = double.NegativeInfinity;
        LastToggledBy = null;
        if (changed) Tell("reset");
    }

    /// <summary>Server and solo: set straight without a hand on it (the watchdog's, plan 12 Ruling 15), any held throw dropped. True when it was thrown.</summary>
    public bool SetBack()
    {
        HeldBy = null;
        if (NetSession.IsClient || Fixed || !Thrown) return false;
        SetThrown(false);
        RideAudio.At("switch_clunk", SoundPoint);
        Tell("straight");
        return true;
    }

    /// <summary>Server test hook: throw it.</summary>
    internal void DebugThrow() { if (!Thrown && !Fixed) { SetThrown(true); Tell("thrown"); } }

    /// <summary>The server tells its peers of a change (a client, or a switch with no wire name, tells nobody).</summary>
    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    // ---- a client's copy: shown as the server tells it, silently --------------------------------

    /// <summary>Client: the lever stands thrown (or straight) as on the server. A fixed switch stays thrown.</summary>
    public void ShowThrown(bool thrown) { if (!Fixed) SetThrown(thrown); }
    /// <summary>Client: its points are behind the cart this ride ("taken"), or no longer ("free").</summary>
    public void ShowTaken(bool taken = true) => _taken = taken;
    /// <summary>Client: the end-of-ride reset, without a sound.</summary>
    public void ShowReset()
    {
        _taken = false;
        if (!Fixed) SetThrown(false);
    }

    private void SetThrown(bool thrown)
    {
        Thrown = thrown;
        if (_pivot == null) return;
        _pivot.Rotation = new Vector3(thrown ? Swing : -Swing, 0, 0); // −Z (toward the points) when straight, +Z when thrown
        var c = thrown ? new Color(1f, 0.12f, 0.08f) : new Color(0.15f, 1f, 0.25f);
        _lamp.AlbedoColor = c;
        _lamp.Emission = c;
        _light.LightColor = c;
    }
}
