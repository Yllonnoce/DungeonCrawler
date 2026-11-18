using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber trestle carrying the party line over a stretch of about <see cref="Span"/> metres: a plank deck under the sleepers,
/// a bent of legs and a cross brace every few metres either side, a rail along each side. Dressing, built from boxes, with no
/// collision. <see cref="Collapse"/> drops it: the deck and bents go, a few timbers tumble (cosmetic rigid bodies, freed after
/// <see cref="TimberSeconds"/>). The <see cref="RailEncounterController"/> starts the collapse <see cref="SetPieceRules.TrestleGrace"/>
/// seconds after the cart's front enters it and catches a cart still on it. The server tells "collapsed" and "rebuilt"
/// (<see cref="NetRef"/>); a client's copy shows them (<see cref="ShowCollapsed"/>, its timbers tumbling too).
/// </summary>
public partial class Trestle : Node3D
{
    public const float Span = 12f;
    const float BentEvery = 3f, DeckWidth = 2.1f, LegOut = 1.15f, RailHeight = 1f;
    const float TimberSeconds = 4f;

    /// <summary>The party line's S at the trestle's middle.</summary>
    public float S { get; init; }
    public float From => S - Span / 2f;
    public float To => S + Span / 2f;
    public bool Collapsed { get; private set; }
    /// <summary>Its wire name ("trestle:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.3f, 0.2f, 0.11f), Roughness = 0.95f };
    private readonly StandardMaterial3D _dark = new() { AlbedoColor = new Color(0.17f, 0.11f, 0.06f), Roughness = 1f };
    private readonly List<MeshInstance3D> _timbers = new();

    /// <summary>Build along <paramref name="track"/> from <see cref="From"/> to <see cref="To"/>. The node itself stays at the origin.</summary>
    public void Build(TrackSampler track)
    {
        // the deck: a plank per metre across the line, just under the sleepers
        for (var s = From + 0.5f; s < To; s += 1f)
            Timber(track.Frame(s), new Vector3(DeckWidth, 0.06f, 0.92f), new Vector3(0, 0.03f, 0), _dark);
        // bents: two legs and a cross brace each side, a cap across
        for (var s = From; s <= To + 0.01f; s += BentEvery)
        {
            var frame = track.Frame(s);
            foreach (var side in new[] { -1f, 1f })
            {
                Timber(frame, new Vector3(0.2f, RailHeight + 0.1f, 0.2f), new Vector3(side * LegOut, (RailHeight + 0.1f) / 2f, 0), _wood);
                Timber(frame, new Vector3(0.08f, 1.2f, 0.1f), new Vector3(side * (LegOut + 0.12f), 0.5f, 0), _wood, new Vector3(0.75f, 0, 0));
            }
            Timber(frame, new Vector3(DeckWidth + 0.4f, 0.14f, 0.24f), new Vector3(0, 0.07f, 0), _wood);
        }
        // the side rails, a section per bent span
        for (var s = From; s < To - 0.01f; s += BentEvery)
        {
            var mid = s + BentEvery / 2f;
            var frame = track.Frame(mid);
            foreach (var side in new[] { -1f, 1f })
                Timber(frame, new Vector3(0.12f, 0.12f, BentEvery + 0.1f), new Vector3(side * LegOut, RailHeight, 0), _wood);
        }
    }

    private void Timber(Transform3D frame, Vector3 size, Vector3 local, Material material, Vector3? rotation = null)
    {
        var basis = frame.Basis.Orthonormalized();
        if (rotation is { } r) basis *= Basis.FromEuler(r);
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        AddChild(mesh);
        mesh.GlobalTransform = new Transform3D(basis, frame.Origin + frame.Basis.Orthonormalized() * local);
        _timbers.Add(mesh);
    }

    /// <summary>The trestle gives way: every timber goes; a few of them tumble down as cosmetic rigid bodies under <paramref name="root"/>.</summary>
    public void Collapse(Node3D root)
    {
        if (Collapsed) return;
        Fall(root);
        Tell("collapsed");
    }

    /// <summary>Client: falls as on the server; with <paramref name="debris"/> its timbers tumble too (a repair shows it gone, quietly).</summary>
    public void ShowCollapsed(Node3D? root, bool debris = true)
    {
        if (!Collapsed) Fall(debris ? root : null);
    }

    /// <summary>Client: standing again.</summary>
    public void ShowRebuilt()
    {
        Collapsed = false;
        foreach (var t in _timbers) t.Visible = true;
    }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    /// <summary>Every timber goes; with a <paramref name="root"/>, a few tumble down under it.</summary>
    private void Fall(Node3D? root)
    {
        Collapsed = true;
        for (var i = 0; i < _timbers.Count; i++)
        {
            var t = _timbers[i];
            t.Visible = false;
            if (root == null || i % 4 != 0 || t.Mesh is not BoxMesh box) continue; // a few of them fall where they stood
            var body = new RigidBody3D { Name = "TrestleTimber", CollisionLayer = 0, CollisionMask = 1, Mass = 40f };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size } });
            body.AddChild(new MeshInstance3D { Mesh = box, MaterialOverride = t.MaterialOverride });
            root.AddChild(body);
            body.GlobalTransform = t.GlobalTransform;
            body.LinearVelocity = new Vector3(0, -1f, 0);
            body.AngularVelocity = new Vector3((i % 3) - 1f, 0.5f, ((i / 3) % 3) - 1f) * 1.5f;
            body.GetTree().CreateTimer(TimberSeconds).Timeout += () => { if (IsInstanceValid(body)) body.QueueFree(); };
        }
    }

    /// <summary>Standing again (the ride runs again).</summary>
    public void Rebuild()
    {
        var was = Collapsed;
        Collapsed = false;
        foreach (var t in _timbers) t.Visible = true;
        if (was) Tell("rebuilt");
    }
}
