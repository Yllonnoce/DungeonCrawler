using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The exit bridge's look (plan 15, spec B.2): an open trestle under each track's span over the lava. Two bents per 4 m cell, each two
/// posts from under the sleepers down into the molten surface with a cap across and a diagonal brace, and a stringer under each rail the
/// sleepers lie on. No deck, no planking, no handrail: the lava shows between the sleepers (the track's gravel is omitted over the span).
/// Procedural boxes on <c>weathered_planks</c>. Look only: no collision, built only where the look is drawn (never headless). The node stays
/// at the origin.
/// </summary>
public partial class ExitBridge : Node3D
{
    /// <summary>A stringer's section (across, up) and a post's side, in metres.</summary>
    const float StringerWide = 0.22f, StringerDeep = 0.28f, Post = 0.26f;
    /// <summary>The bents stand this far out from the track's middle (just outside the sleepers' ends).</summary>
    const float PostOut = 0.8f;
    /// <summary>The posts go this far into the lava, so no foot shows.</summary>
    const float IntoLava = 0.6f;

    private Material _wood = null!;

    /// <summary>Lay the trestle under the party track over <paramref name="partySpan"/> and the enemy track over <paramref name="enemySpan"/>, its posts down to <paramref name="lavaY"/>.</summary>
    public void Build(TrackSampler party, TrackSampler enemy, (float From, float To) partySpan, (float From, float To) enemySpan, float lavaY)
    {
        _wood = DungeonBuilder.Material("weathered_planks", new Color(0.42f, 0.3f, 0.19f), 1.6f);
        Trestle(party, partySpan, lavaY);
        Trestle(enemy, enemySpan, lavaY);
    }

    private void Trestle(TrackSampler track, (float From, float To) span, float lavaY)
    {
        var length = span.To - span.From;
        if (!(length > 0.5f)) return;
        // the stringers: one under each rail, a box per metre or so along the (straight) span
        var pieces = Mathf.Max(1, Mathf.RoundToInt(length));
        var step = length / pieces;
        for (var k = 0; k < pieces; k++)
        {
            var frame = track.Frame(span.From + step * (k + 0.5f));
            var b = frame.Basis.Orthonormalized();
            foreach (var side in new[] { -1f, 1f })
                Box(new Vector3(StringerWide, StringerDeep, step + 0.02f), new Transform3D(b, frame.Origin + b.X * (side * Props.RailTrack.Gauge / 2f) - b.Y * (StringerDeep / 2f)));
        }
        // two bents per cell, a cell's quarter in from each of its ends
        var cells = Mathf.Max(1, Mathf.RoundToInt(length / DungeonBuilder.CellSize));
        var cell = length / cells;
        for (var c = 0; c < cells; c++)
            foreach (var at in new[] { 0.25f, 0.75f })
                Bent(track.Frame(span.From + cell * (c + at)), lavaY);
    }

    /// <summary>One bent at <paramref name="frame"/>: a cap under the stringers, two posts down into the lava, a brace from one post's top to the other's foot.</summary>
    private void Bent(Transform3D frame, float lavaY)
    {
        var b = frame.Basis.Orthonormalized();
        var capY = frame.Origin.Y - StringerDeep - 0.15f;
        var cap = frame.Origin - b.Y * (StringerDeep + 0.15f);
        Box(new Vector3(PostOut * 2f + Post, 0.3f, 0.3f), new Transform3D(b, cap));
        var bottom = lavaY - IntoLava;
        var height = capY - 0.15f - bottom;
        if (height <= 0.1f) return;
        var flatX = b.X with { Y = 0f };
        var flatZ = b.Z with { Y = 0f };
        var flat = new Basis(flatX.Normalized(), Vector3.Up, flatZ.Normalized());
        foreach (var side in new[] { -1f, 1f })
        {
            var top = cap + b.X * (side * PostOut);
            Box(new Vector3(Post, height, Post), new Transform3D(flat, top with { Y = bottom + height / 2f }));
        }
        var from = (cap - b.X * PostOut) with { Y = capY - 0.3f };
        var to = (cap + b.X * PostOut) with { Y = Mathf.Max(bottom + 0.3f, lavaY + 0.2f) };
        var x = (to - from).Normalized();
        Box(new Vector3(from.DistanceTo(to), 0.18f, 0.18f), new Transform3D(new Basis(x, flat.Z.Cross(x), flat.Z), (from + to) / 2f));
    }

    private void Box(Vector3 size, Transform3D at)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = _wood };
        AddChild(mesh);
        mesh.GlobalTransform = at;
    }
}
