using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Items;

/// <summary>
/// Icons for the bag and the paper dolls: each item kind's floor model, rendered once into a small transparent texture
/// by a hidden viewport, then cached for the session. Rendering is one item per frame; cells draw their old square
/// until the icon lands and are told to redraw. Headless runs have no renderer and keep the squares.
/// </summary>
public partial class ItemIcons : Node
{
    public static ItemIcons? Instance { get; private set; }
    public const int Size = 128;
    /// <summary>An icon finished rendering; cells redraw.</summary>
    public static event Action? Changed;

    private readonly Dictionary<string, Texture2D> _icons = new();
    private readonly HashSet<string> _pending = new();
    private readonly Queue<(string key, ItemInstance item)> _queue = new();
    private SubViewport _viewport = null!;
    private Node3D _holder = null!;
    private bool _busy;
    private bool _enabled;

    public override void _Ready()
    {
        Instance = this;
        _enabled = !DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase);
        if (!_enabled) return;
        _viewport = new SubViewport { Size = new Vector2I(Size, Size), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_viewport);
        var env = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.55f, 0.6f), AmbientLightEnergy = 1.2f } };
        _viewport.AddChild(env);
        var light = new DirectionalLight3D { LightEnergy = 1.6f, ShadowEnabled = false };
        light.LookAtFromPosition(new Vector3(1.5f, 2.5f, 2f), Vector3.Zero, Vector3.Up);
        _viewport.AddChild(light);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.25f, Near = 0.05f, Far = 20f };
        camera.LookAtFromPosition(new Vector3(1.6f, 1.3f, 2.2f), Vector3.Zero, Vector3.Up);
        _viewport.AddChild(camera);
        _holder = new Node3D { Name = "Holder" };
        _viewport.AddChild(_holder);
    }

    /// <summary>The icon for an item, or null while it renders (or when icons are off); the cell should draw its fallback.</summary>
    public static Texture2D? Get(ItemInstance item)
    {
        var self = Instance;
        if (self == null || !self._enabled) return null;
        var key = Key(item);
        if (self._icons.TryGetValue(key, out var tex)) return tex;
        if (self._pending.Add(key)) self._queue.Enqueue((key, item));
        return null;
    }

    /// <summary>Icons depend on the base definition (model, colour) and on the two-handed flag; affixes and rarity do not change the look.</summary>
    private static string Key(ItemInstance item) => $"{item.DefId}|{(item.TwoHanded ? "2h" : "1h")}|{item.Kind}|{item.Color}";

    public override void _Process(double delta)
    {
        if (!_enabled || _busy || _queue.Count == 0) return;
        var (key, item) = _queue.Dequeue();
        _ = RenderAsync(key, item);
    }

    private async Task RenderAsync(string key, ItemInstance item)
    {
        _busy = true;
        try
        {
            foreach (var c in _holder.GetChildren()) c.QueueFree();
            var pivot = new Node3D { Name = "Pivot" };
            _holder.AddChild(pivot);
            if (!ItemVisuals.Attach(pivot, item))
            {
                // nothing modelled: a plain block in the item's colour, so every cell still gets a picture
                var block = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(item.Color) } };
                pivot.AddChild(block);
            }
            // fill the frame: the model was normalised to hand size for the floor, so scale it up to a unit box
            var bounds = ItemVisuals.Bounds(pivot);
            var longest = Math.Max(0.02f, Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)));
            pivot.Scale = Vector3.One * (1f / longest);
            pivot.Position = -bounds.GetCenter() / longest;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = _viewport.GetTexture().GetImage();
            _icons[key] = ImageTexture.CreateFromImage(image);
            pivot.QueueFree();
            Changed?.Invoke();
        }
        catch (Exception e) { GD.PushWarning($"[ItemIcons] {key}: {e.Message}"); _icons[key] = new PlaceholderTexture2D(); }
        finally { _pending.Remove(key); _busy = false; }
    }
}
