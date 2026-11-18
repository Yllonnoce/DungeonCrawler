using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber beam across the party line, built from boxes: two posts outside the lane and a crossbeam between them. Its
/// underside clears the head of the tallest seated rider by <see cref="Clearance"/>, so the cart passes under it without a
/// touch; the beam has no collision (a hero on foot walks under it) and the posts are solid. Dressing, except that
/// the <see cref="RailEncounterController"/> ducks the seated desktop cameras as the cart's front passes.
/// </summary>
public partial class LowBeam : Node3D
{
    /// <summary>The rear bench is the tallest seat: the tub floor, its deck and the seated eye above that.</summary>
    public const float TallestEye = MineCart.InsideFloor + MineCart.BenchDeck + Adventurer.SeatedEyeHeight;
    /// <summary>From the eye to the top of the head.</summary>
    public const float HeadAboveEye = 0.15f;
    /// <summary>The beam's underside is this far above the tallest seated head.</summary>
    public const float Clearance = 0.1f;
    /// <summary>The crossbeam's underside above the track point (the cart's origin height): 2.19 m.</summary>
    public const float Underside = TallestEye + HeadAboveEye + Clearance;
    const float Thickness = 0.26f, PostSize = 0.3f, PostOut = 1.35f;

    /// <summary>The line's S the beam stands at.</summary>
    public float S { get; set; }

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.27f, 0.18f, 0.1f), Roughness = 0.9f };

    /// <summary>Stand across the line at <paramref name="frame"/> (the track's frame there: −Z along the line, Y up from the track).</summary>
    public void Place(Transform3D frame)
    {
        GlobalTransform = frame;
        var height = Underside + Thickness;
        foreach (var side in new[] { -1f, 1f })
        {
            var post = new StaticBody3D { Name = side < 0 ? "PostL" : "PostR", CollisionLayer = 1, CollisionMask = 0, Position = new Vector3(side * PostOut, height / 2f, 0) };
            post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(PostSize, height, PostSize) } });
            post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(PostSize, height, PostSize) }, MaterialOverride = _wood });
            AddChild(post);
        }
        AddChild(new MeshInstance3D
        {
            Name = "Crossbeam",
            Mesh = new BoxMesh { Size = new Vector3(PostOut * 2f + PostSize + 0.5f, Thickness, 0.3f) },
            MaterialOverride = _wood,
            Position = new Vector3(0, Underside + Thickness / 2f, 0),
        });
        foreach (var side in new[] { -1f, 1f }) // a knee brace each side
            AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "BraceL" : "BraceR",
                Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.6f, 0.1f) },
                MaterialOverride = _wood,
                Position = new Vector3(side * (PostOut - 0.25f), Underside - 0.2f, 0),
                RotationDegrees = new Vector3(0, 0, side * 45f),
            });
    }
}
