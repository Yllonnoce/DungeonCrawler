using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// Playing in a headset on the PC (Meta Quest Link, or any OpenXR runtime). Off unless the game is started with
/// <c>--vr</c> after the <c>--</c>, and Godot itself with <c>--xr-mode on</c> (the project leaves OpenXR off, so a
/// flat game never looks for a headset):
/// <code>godot --xr-mode on --path . -- --vr</code>
/// With no headset or no runtime the game says why and plays flat. The body's own rig is <see cref="Party.VrRig"/>.
/// </summary>
public static class Vr
{
    /// <summary>True once the headset is the game's display.</summary>
    public static bool Active { get; private set; }

    /// <summary>A flick of the right stick turns the view this far at once (a smooth turn is what makes people ill).</summary>
    public const float SnapTurnDegrees = 30f;
    /// <summary>How far a stick must be pushed to turn, and how far it must come back before it turns again.</summary>
    public const float TurnPush = 0.6f, TurnRelease = 0.3f;
    /// <summary>A trigger or a grip counts as pressed above the first and as let go below the second.</summary>
    public const float SqueezeOn = 0.6f, SqueezeOff = 0.4f;
    /// <summary>A stick inside this is at rest.</summary>
    public const float StickDeadzone = 0.15f;

    public static void Start(Viewport viewport)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--vr")) return;
        var xr = XRServer.FindInterface("OpenXR");
        if (xr == null) { GD.Print("[vr] OpenXR is not there: start Godot with --xr-mode on (before the --). Playing flat."); return; }
        if (!xr.IsInitialized() && !xr.Initialize()) { GD.Print("[vr] OpenXR did not start. Godot must be started with --xr-mode on (before the --), the headset connected with Link running, and Meta Quest Link the active OpenXR runtime. Playing flat."); return; }
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // the headset sets the pace, not the monitor
        viewport.UseXR = true;
        Active = true;
        if (xr is OpenXRInterface openXr)
        {
            MatchPhysicsTo(openXr.DisplayRefreshRate);
            openXr.SessionBegun += () => MatchPhysicsTo(openXr.DisplayRefreshRate);
        }
        GD.Print($"[vr] on: {xr.GetName()}, physics {Engine.PhysicsTicksPerSecond} ticks a second");
    }

    /// <summary>The viewport the flat interface is drawn into while in a headset (shown in the world by <see cref="Party.VrPanel"/>); null when flat.</summary>
    public static SubViewport? Ui { get; private set; }

    /// <summary>
    /// A headset shows nothing drawn flat on the screen, so the interface moves into a viewport of its own, the size
    /// the game's window is designed for. Does nothing when the game is not in a headset.
    /// </summary>
    public static void HostUi(Node owner, CanvasLayer ui)
    {
        if (!Active) return;
        var viewport = new SubViewport
        {
            Name = "VrUi", Size = new Vector2I(1920, 1080), TransparentBg = true, GuiEmbedSubwindows = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, ProcessMode = Node.ProcessModeEnum.Always,
        };
        owner.AddChild(viewport);
        ui.GetParent().RemoveChild(ui);
        viewport.AddChild(ui);
        owner.AddChild(new VrUiRelay { Name = "VrUiRelay" });
        Ui = viewport;
    }

    /// <summary>The body moves on physics ticks: one tick per frame the headset shows, or walking judders.</summary>
    private static void MatchPhysicsTo(float refreshRate)
    {
        if (refreshRate < 50f) return; // not known yet
        Engine.PhysicsTicksPerSecond = Mathf.RoundToInt(refreshRate);
    }
}
