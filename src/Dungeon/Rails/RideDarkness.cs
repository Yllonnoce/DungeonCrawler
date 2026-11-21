using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The dark stretch on a peer that draws (Ruling 16), added by <see cref="RailEncounterController"/> under itself: each frame this view is
/// darkened by its camera's place in the span (<see cref="Main.ApplyDarkness"/>) and each hero's torch by the hero's own, to
/// <see cref="DarkRules.TorchKept"/> of its energy. Local only, nothing on the wire: every co-op peer darkens by its own view. A torch's base
/// is the energy it had before the dark touched it, taken again whenever something else sets it (a view handed over). Leaving the tree
/// gives the view and the torches back their light.
/// </summary>
public partial class RideDarkness : Node
{
    private readonly RailEncounterController _rails;
    /// <summary>Each torch's energy before the dark, and what the dark last set it to.</summary>
    private readonly System.Collections.Generic.Dictionary<Adventurer, (float Base, float Set)> _torches = new();

    public RideDarkness(RailEncounterController rails)
    {
        _rails = rails;
        Name = "RideDarkness";
    }

    public override void _Process(double delta)
    {
        if (Main.Instance is not { } main) return;
        if (GetViewport()?.GetCamera3D() is { } camera) main.ApplyDarkness(_rails.DarknessAt(camera.GlobalPosition));
        foreach (var hero in main.Party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Torch is not { } torch) continue;
            var now = torch.LightEnergy;
            if (!_torches.TryGetValue(hero, out var t) || !Mathf.IsEqualApprox(now, t.Set)) t = (now, now); // not yet seen, or set by someone else: its new base
            var e = t.Base * DarkRules.Kept(_rails.DarknessAt(hero.GlobalPosition), DarkRules.TorchKept);
            if (!Mathf.IsEqualApprox(e, now)) torch.LightEnergy = e;
            _torches[hero] = (t.Base, e);
        }
    }

    public override void _ExitTree()
    {
        foreach (var (hero, t) in _torches)
            if (IsInstanceValid(hero) && hero.Torch is { } torch && IsInstanceValid(torch) && Mathf.IsEqualApprox(torch.LightEnergy, t.Set)) torch.LightEnergy = t.Base;
        _torches.Clear();
        if (Main.Instance is { } main && IsInstanceValid(main)) main.ApplyDarkness(0f);
    }
}
