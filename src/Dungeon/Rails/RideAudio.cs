using DungeonCrawler.Audio;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The ride's one-shots at a spot in the world (<see cref="AudioManager.PlayAt"/>): a prop's or a set piece's cue where it happens, on
/// every peer. On the server and in single player each plays in the prop's noisy method; a client hears a prop's from the live
/// <see cref="PropEvt"/> (<see cref="OnProp"/>, from <c>ClientWorld.OnProp</c>), never from a prop state's catch-up, and it is never
/// subscribed to <see cref="EventBus.PropChanged"/>: the restore paths raise that too. A looping cue never plays here.
/// </summary>
public static class RideAudio
{
    /// <summary>
    /// Play <paramref name="cue"/> once at <paramref name="at"/>, heard within <paramref name="range"/> metres. One that starts beyond its range of
    /// the active camera is not played at all (a short one-shot never reaches a listener who walks up later); with no camera (a dedicated
    /// server) it goes on to <see cref="AudioManager.PlayAt"/>, which only raises the test hook there.
    /// </summary>
    public static void At(string cue, Vector3 at, float db = 0f, float range = 30f, bool ambient = false)
    {
        if (AudioManager.Instance is not { } audio) return;
        if (audio.GetViewport()?.GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(at) > range * range) return;
        audio.PlayAt(cue, at, db, range, ambient);
    }

    /// <summary>
    /// Client: a rail prop's live change (<paramref name="netRef"/> "kind:id", <paramref name="what"/>) makes its sound at the prop, after the
    /// copy has shown it. A quiet change (a reset, the points taken or freed, the quiet tells) makes none.
    /// </summary>
    public static void OnProp(string netRef, string what)
    {
        if (!NetSession.IsClient || RideSound.PropCueOf(netRef, what) is not { } cue) return;
        if (Main.Instance?.Builder.FloorRoot is not { } root || NetRefResolver.Resolve(root, netRef) is not Node3D node) return;
        At(cue, SoundAt(node));
    }

    /// <summary>Where a prop's sound comes from: its node, or a trestle's middle (its node stays at the origin).</summary>
    public static Vector3 SoundAt(Node3D node) => node is Trestle t ? t.Middle : node.GlobalPosition;
}
