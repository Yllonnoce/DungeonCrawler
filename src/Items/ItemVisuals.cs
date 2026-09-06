using Godot;

namespace DungeonCrawler.Items;

/// <summary>
/// What an item looks like lying on the floor: a KayKit model where the pack has one (keys, bottles, blades, shields,
/// the spellbook, arrows), a small built mesh otherwise (armour, jewellery, scrolls, bows, spears, maces, bones), all
/// normalised to hand size and centred so they can spin on a drop.
/// </summary>
public static class ItemVisuals
{
    private const string Dungeon = "res://assets/models/kaykit_dungeon/";
    private const string Gear = "res://assets/models/kaykit_items/";

    /// <summary>Builds the visual under <paramref name="parent"/>; returns false when nothing suited the item (caller keeps its cube).</summary>
    public static bool Attach(Node3D parent, ItemInstance item)
    {
        var node = Build(item, out var size);
        if (node == null) return false;
        node.Name = "ItemModel";
        parent.AddChild(node);
        // normalise: longest side to the wanted size, centred on the origin
        var aabb = Bounds(node);
        var longest = Math.Max(0.02f, Math.Max(aabb.Size.X, Math.Max(aabb.Size.Y, aabb.Size.Z)));
        var scale = size / longest;
        node.Scale = Vector3.One * scale;
        node.Position = -aabb.GetCenter() * scale;
        return true;
    }

    private static Node3D? Build(ItemInstance item, out float size)
    {
        size = 0.45f;
        bool Has(string tag) => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        var color = new Color(item.Color);
        var iron = item.DefId.Contains("iron") || item.DefId.Contains("plate") || item.DefId.Contains("chain") || item.DefId.Contains("gauntlet");
        Material Skin(bool metal, float alpha = 1f) => new StandardMaterial3D { AlbedoColor = color with { A = alpha }, Metallic = metal ? 0.8f : 0.05f, Roughness = metal ? 0.35f : 0.75f, Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled };

        if (Has("key")) { size = 0.5f; return Model(Dungeon + "key.gltf.glb"); }
        if (Has("potion")) { size = 0.42f; return Recolored(Model(Dungeon + (item.DefId.Contains("antidote") ? "bottle_A_green.gltf.glb" : "bottle_B_brown.gltf.glb")), Skin(false, 0.85f), keepDark: true); }
        if (Has("tome")) { size = 0.42f; return Model(Gear + "spellbook_closed.gltf"); }
        if (Has("scroll")) return Scroll(color);
        if (Has("ammo")) { size = 0.6f; return Model(Gear + "arrow_bundle.gltf"); }
        if (Has("trapkit")) { size = 0.4f; return Model(Dungeon + "box_small_decorated.gltf.glb"); }
        if (item.DefId == "item.quest_relic") { size = 0.5f; return Crystal(new Color(1f, 0.85f, 0.4f)); }
        if (item.BaseWeapon is { } weapon)
        {
            size = 0.95f;
            switch (weapon.Kind)
            {
                case Combat.WeaponKind.Sword: return Model(Gear + (item.TwoHanded ? "sword_2handed.gltf" : "sword_1handed.gltf"));
                case Combat.WeaponKind.Axe: return Model(Gear + (item.TwoHanded ? "axe_2handed.gltf" : "axe_1handed.gltf"));
                case Combat.WeaponKind.Dagger: size = 0.6f; return Model(Gear + "dagger.gltf");
                case Combat.WeaponKind.Staff: size = 1.2f; return Model(Gear + "staff.gltf");
                case Combat.WeaponKind.Bow: size = 1.1f; return Bow();
                case Combat.WeaponKind.Polearm: size = 1.5f; return Spear();
                case Combat.WeaponKind.Mace: size = 0.8f; return Mace();
            }
            return null;
        }
        switch (item.Kind)
        {
            case SlotKind.Offhand when item.Offhand == OffhandKind.Shield: size = 0.7f; return Model(Gear + (iron ? "shield_square.gltf" : "shield_round.gltf"));
            case SlotKind.Offhand when item.Offhand == OffhandKind.Quiver: size = 0.6f; return Model(Gear + "quiver.gltf");
            case SlotKind.Offhand: size = 0.4f; return Crystal(color);
            case SlotKind.Head: size = 0.45f; return Helm(Skin(iron));
            case SlotKind.Body: size = 0.6f; return Cuirass(Skin(iron), item.ArmorClass == ArmorClass.Cloth);
            case SlotKind.Hands: size = 0.4f; return Pair(new Vector3(0.12f, 0.22f, 0.06f), Skin(iron), 0.16f);
            case SlotKind.Feet: size = 0.45f; return Boots(Skin(iron));
            case SlotKind.Ring: size = 0.28f; return Ring(color, iron);
            case SlotKind.Amulet: size = 0.4f; return Amulet(color);
            case SlotKind.Junk: size = 0.45f; return Bone();
        }
        return null;
    }

    // ---- packs -------------------------------------------------------------------------------

    private static Node3D? Model(string path) => ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path).Instantiate<Node3D>() : null;

    /// <summary>Replace every material with one colour; <paramref name="keepDark"/> keeps the model's dark parts (a cork, a label) by mixing.</summary>
    private static Node3D? Recolored(Node3D? node, Material material, bool keepDark)
    {
        if (node == null) return null;
        foreach (var mi in Core.AnimatedModel.Meshes(node))
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++) mi.SetSurfaceOverrideMaterial(i, material);
        return node;
    }

    // ---- built meshes ------------------------------------------------------------------------

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3? rotation = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        if (rotation is { } r) mi.Rotation = r;
        parent.AddChild(mi);
        return mi;
    }

    private static Node3D Scroll(Color ribbon)
    {
        var n = new Node3D();
        var paper = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.84f, 0.66f), Roughness = 0.95f };
        Part(n, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.42f }, paper, Vector3.Zero, new Vector3(0, 0, Mathf.Pi / 2f));
        Part(n, new TorusMesh { InnerRadius = 0.055f, OuterRadius = 0.08f }, new StandardMaterial3D { AlbedoColor = ribbon }, Vector3.Zero, new Vector3(0, 0, Mathf.Pi / 2f));
        return n;
    }

    private static Node3D Crystal(Color color)
    {
        var n = new Node3D();
        var mat = new StandardMaterial3D { AlbedoColor = color with { A = 0.85f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 1.2f, Roughness = 0.2f };
        Part(n, new PrismMesh { Size = new Vector3(0.18f, 0.26f, 0.18f) }, mat, new Vector3(0, 0.13f, 0));
        Part(n, new PrismMesh { Size = new Vector3(0.18f, 0.26f, 0.18f) }, mat, new Vector3(0, -0.13f, 0), new Vector3(Mathf.Pi, 0, 0));
        return n;
    }

    private static Node3D Bow()
    {
        var n = new Node3D();
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.16f), Roughness = 0.8f };
        const int segments = 8;
        const float radius = 0.55f;
        var arc = Mathf.Pi * 0.75f;
        for (var i = 0; i < segments; i++)
        {
            var a0 = -arc / 2f + arc * i / segments;
            var a1 = -arc / 2f + arc * (i + 1) / segments;
            var p0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0);
            var p1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
            var mid = (p0 + p1) / 2f;
            var seg = Part(n, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = p0.DistanceTo(p1) + 0.01f }, wood, mid);
            seg.Rotation = new Vector3(0, 0, Mathf.Atan2(p1.Y - p0.Y, p1.X - p0.X) - Mathf.Pi / 2f);
        }
        var top = new Vector3(Mathf.Cos(arc / 2f) * radius, Mathf.Sin(arc / 2f) * radius, 0);
        var bottom = top with { Y = -top.Y };
        Part(n, new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.006f, Height = top.DistanceTo(bottom) }, new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.82f, 0.7f) }, (top + bottom) / 2f);
        return n;
    }

    private static Node3D Spear()
    {
        var n = new Node3D();
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.28f, 0.15f), Roughness = 0.85f };
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.76f, 0.78f), Metallic = 0.9f, Roughness = 0.3f };
        Part(n, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 1.7f }, wood, Vector3.Zero);
        Part(n, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.06f, Height = 0.32f }, steel, new Vector3(0, 1.0f, 0));
        return n;
    }

    private static Node3D Mace()
    {
        var n = new Node3D();
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.22f, 0.12f), Roughness = 0.85f };
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f), Metallic = 0.9f, Roughness = 0.4f };
        Part(n, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.035f, Height = 0.7f }, wood, Vector3.Zero);
        Part(n, new SphereMesh { Radius = 0.11f, Height = 0.22f }, steel, new Vector3(0, 0.4f, 0));
        for (var i = 0; i < 6; i++)
        {
            var a = i * Mathf.Tau / 6f;
            var spike = Part(n, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.035f, Height = 0.12f }, steel, new Vector3(Mathf.Cos(a) * 0.13f, 0.4f, Mathf.Sin(a) * 0.13f));
            spike.Rotation = new Vector3(Mathf.Sin(a) * Mathf.Pi / 2f, 0, -Mathf.Cos(a) * Mathf.Pi / 2f);
        }
        return n;
    }

    private static Node3D Helm(Material skin)
    {
        var n = new Node3D();
        Part(n, new SphereMesh { Radius = 0.16f, Height = 0.16f, IsHemisphere = true }, skin, Vector3.Zero);
        Part(n, new TorusMesh { InnerRadius = 0.15f, OuterRadius = 0.18f }, skin, new Vector3(0, 0.005f, 0));
        return n;
    }

    private static Node3D Cuirass(Material skin, bool cloth)
    {
        var n = new Node3D();
        Part(n, new BoxMesh { Size = new Vector3(0.34f, 0.4f, cloth ? 0.16f : 0.2f) }, skin, Vector3.Zero);
        Part(n, new SphereMesh { Radius = 0.09f, Height = 0.18f }, skin, new Vector3(-0.2f, 0.16f, 0));
        Part(n, new SphereMesh { Radius = 0.09f, Height = 0.18f }, skin, new Vector3(0.2f, 0.16f, 0));
        return n;
    }

    private static Node3D Pair(Vector3 size, Material skin, float gap)
    {
        var n = new Node3D();
        Part(n, new BoxMesh { Size = size }, skin, new Vector3(-gap / 2f - size.X / 2f, 0, 0));
        Part(n, new BoxMesh { Size = size }, skin, new Vector3(gap / 2f + size.X / 2f, 0, 0));
        return n;
    }

    private static Node3D Boots(Material skin)
    {
        var n = Pair(new Vector3(0.12f, 0.26f, 0.14f), skin, 0.08f);
        Part(n, new BoxMesh { Size = new Vector3(0.12f, 0.08f, 0.12f) }, skin, new Vector3(-0.1f, -0.09f, 0.12f));
        Part(n, new BoxMesh { Size = new Vector3(0.12f, 0.08f, 0.12f) }, skin, new Vector3(0.1f, -0.09f, 0.12f));
        return n;
    }

    private static Node3D Ring(Color color, bool iron)
    {
        var n = new Node3D();
        var metal = new StandardMaterial3D { AlbedoColor = iron ? new Color(0.6f, 0.6f, 0.62f) : new Color(0.95f, 0.78f, 0.3f), Metallic = 0.95f, Roughness = 0.25f };
        Part(n, new TorusMesh { InnerRadius = 0.09f, OuterRadius = 0.13f }, metal, Vector3.Zero, new Vector3(Mathf.Pi / 2f, 0, 0));
        Part(n, new SphereMesh { Radius = 0.04f, Height = 0.08f }, new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.6f }, new Vector3(0, 0.13f, 0));
        return n;
    }

    private static Node3D Amulet(Color color)
    {
        var n = new Node3D();
        var chain = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.35f), Metallic = 0.9f, Roughness = 0.3f };
        Part(n, new TorusMesh { InnerRadius = 0.17f, OuterRadius = 0.19f }, chain, Vector3.Zero);
        Part(n, new PrismMesh { Size = new Vector3(0.12f, 0.14f, 0.05f) }, new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.5f }, new Vector3(0, -0.2f, 0), new Vector3(Mathf.Pi, 0, 0));
        return n;
    }

    private static Node3D Bone()
    {
        var n = new Node3D();
        var bone = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.82f, 0.72f), Roughness = 0.9f };
        Part(n, new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.36f }, bone, Vector3.Zero, new Vector3(0, 0, Mathf.Pi / 2f));
        Part(n, new SphereMesh { Radius = 0.06f, Height = 0.12f }, bone, new Vector3(0.19f, 0, 0));
        Part(n, new SphereMesh { Radius = 0.05f, Height = 0.1f }, bone, new Vector3(-0.19f, 0.03f, 0));
        return n;
    }

    public static Aabb Bounds(Node3D root)
    {
        var aabb = new Aabb();
        var first = true;
        foreach (var mi in Core.AnimatedModel.Meshes(root))
        {
            var box = mi.GetAabb();
            var xf = root.GlobalTransform.AffineInverse() * mi.GlobalTransform;
            var local = xf * box;
            aabb = first ? local : aabb.Merge(local);
            first = false;
        }
        return aabb.Abs();
    }
}
