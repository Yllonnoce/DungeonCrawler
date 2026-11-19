using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Every cart's head lamp (the spec: every cart carries one): a spot aimed down the track, tilted a little to the floor, a small glow
/// round it, and an emissive lens facing forward so the lamp reads from ahead. The party's cart, its spares and chasers burn warm
/// white; an enemy cart (a slag cart, a runaway wagon) burns a hot orange. All three are children of the cart node itself, never of
/// its "Body": a tumble duplicates the Body, and the lights must not ride along into it.
/// </summary>
public static class CartLamp
{
    public const float LensDiameter = 0.16f;

    /// <summary>Hang the lamp on <paramref name="cart"/> at <paramref name="at"/> (the cart's space; forward is −Z).</summary>
    public static void Attach(Node3D cart, Vector3 at, bool enemy)
    {
        var colour = enemy ? new Color(1f, 0.55f, 0.3f) : new Color(1f, 0.93f, 0.78f);
        cart.AddChild(new SpotLight3D
        {
            Name = "Lamp",
            Position = at,
            RotationDegrees = new Vector3(-8f, 0, 0),
            SpotRange = 26f,
            SpotAngle = 30f,
            SpotAttenuation = 0.7f,
            LightEnergy = enemy ? 4f : 6f,
            LightColor = colour,
            ShadowEnabled = false,
        });
        cart.AddChild(new OmniLight3D
        {
            Name = "LampGlow",
            Position = at,
            OmniRange = 3.5f,
            LightEnergy = 1.2f,
            LightColor = colour,
            ShadowEnabled = false,
        });
        // the lens: a thin disc across the line of travel, its face forward (a cylinder's axis is Y: turned onto Z)
        cart.AddChild(new MeshInstance3D
        {
            Name = "LampLens",
            Mesh = new CylinderMesh { TopRadius = LensDiameter / 2f, BottomRadius = LensDiameter / 2f, Height = 0.02f, RadialSegments = 16, Rings = 1 },
            // once per lamp, not static: a static resource outlives the scene tree and is reported at exit
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                EmissionEnabled = true,
                Emission = colour,
                EmissionEnergyMultiplier = 3f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), at),
        });
    }
}
