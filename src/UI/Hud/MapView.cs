using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>
/// Draws the explored part of the current floor. In corner mode it is a fixed-scale, north-up window centred on
/// the party; in full mode it fits the whole floor to the control. Markers: party arrow, discovered stairs,
/// enemies that currently have line of sight to the party, loot in explored cells.
/// </summary>
public partial class MapView : Control
{
    [Export] public bool FullMap { get; set; }
    [Export] public float CornerCellPixels { get; set; } = 7f;

    private static readonly Color Bg = new(0.05f, 0.05f, 0.06f, 0.75f);
    private static readonly Color FloorColor = new(0.42f, 0.40f, 0.36f);
    private static readonly Color WallColor = new(0.72f, 0.70f, 0.66f);
    private static readonly Color DoorColor = new(0.6f, 0.5f, 0.3f);
    private static readonly Color UpColor = new(0.9f, 0.85f, 0.5f);
    private static readonly Color DownColor = new(0.35f, 0.5f, 1f);
    private static readonly Color EnemyColor = new(1f, 0.25f, 0.2f);
    private static readonly Color LootColor = new(1f, 0.8f, 0.2f);
    private static readonly Color PartyColor = new(0.4f, 1f, 0.5f);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Exploration is not { } explored) return;

        DrawRect(new Rect2(Vector2.Zero, Size), Bg);

        var party = main.Party;
        var partyCell = DungeonBuilder.WorldToCell(party.GlobalPosition);

        // cell -> pixel mapping
        float cell;
        Vector2 origin; // pixel position of grid cell (0,0)
        if (FullMap)
        {
            cell = Mathf.Floor(Mathf.Min(Size.X / layout.Width, Size.Y / layout.Height));
            origin = (Size - new Vector2(layout.Width, layout.Height) * cell) / 2f;
        }
        else
        {
            cell = CornerCellPixels;
            var partyPixel = (party.GlobalPosition / DungeonBuilder.CellSize) is var w ? new Vector2(w.X, w.Z) * cell : Vector2.Zero;
            origin = Size / 2f - partyPixel;
        }

        Vector2 CellTopLeft(GridPos p) => origin + new Vector2(p.X, p.Y) * cell;
        Vector2 CellCentre(GridPos p) => CellTopLeft(p) + Vector2.One * (cell / 2f);

        // visible window (corner mode) so we do not iterate the whole grid every frame
        int minX = 0, minY = 0, maxX = layout.Width - 1, maxY = layout.Height - 1;
        if (!FullMap)
        {
            var half = (int)Mathf.Ceil(Size.X / cell / 2f) + 1;
            minX = Math.Max(0, partyCell.X - half); maxX = Math.Min(maxX, partyCell.X + half);
            minY = Math.Max(0, partyCell.Y - half); maxY = Math.Min(maxY, partyCell.Y + half);
        }

        var inset = cell > 4 ? 1f : 0f;
        var partyUnder = main.Builder.IsUnderAt(party.GlobalPosition);
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var p = new GridPos(x, y);
                Color color;
                if (partyUnder)
                {
                    if (!layout.IsUnder(p)) continue;
                    color = new Color(0.3f, 0.28f, 0.32f);
                }
                else
                {
                    if (!explored.IsSeen(p)) continue;
                    color = layout[p] switch
                    {
                        Cell.Floor => layout.LevelOf(p) == 1 ? FloorColor.Lightened(0.25f) : FloorColor,
                        Cell.Door => DoorColor,
                        Cell.Wall or Cell.Secret => WallColor,
                        Cell.Ramp => FloorColor.Lightened(0.12f),
                        Cell.Pit => new Color(0.05f, 0.05f, 0.08f),
                        _ => Colors.Transparent,
                    };
                }
                if (color.A == 0) continue;
                DrawRect(new Rect2(CellTopLeft(p) + Vector2.One * inset * 0.5f, Vector2.One * (cell - inset)), color);
            }

        // stairs, once their cell has been seen
        if (explored.IsSeen(layout.UpStairs)) DrawMarker(CellCentre(layout.UpStairs), cell, UpColor, square: true);
        if (explored.IsSeen(layout.DownStairs)) DrawMarker(CellCentre(layout.DownStairs), cell, DownColor, square: true);

        if (main.Builder.FloorRoot is { } root)
        {
            foreach (var child in root.GetChildren())
            {
                switch (child)
                {
                    case LootDrop loot when explored.IsSeen(DungeonBuilder.WorldToCell(loot.GlobalPosition)):
                        DrawMarker(CellCentre(DungeonBuilder.WorldToCell(loot.GlobalPosition)), cell, LootColor, square: false);
                        break;
                    case Trap { Detected: true, Armed: true } trap:
                        DrawMarker(CellCentre(DungeonBuilder.WorldToCell(trap.GlobalPosition)), cell, trap.PlacedByParty ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.3f, 0.3f), square: true);
                        break;
                    case SecretWall { Detected: true, IsOpen: false } secret:
                        DrawMarker(CellCentre(secret.Cell), cell, new Color(0.9f, 0.9f, 0.4f), square: true);
                        break;
                    case Door { IsOpen: false } door when explored.IsSeen(door.Cell):
                        DrawMarker(CellCentre(door.Cell), cell, new Color(0.6f, 0.4f, 0.2f), square: true);
                        break;
                    case Lever { Pulled: false } lever when explored.IsSeen(DungeonBuilder.WorldToCell(lever.GlobalPosition)):
                        DrawMarker(CellCentre(DungeonBuilder.WorldToCell(lever.GlobalPosition)), cell, new Color(0.3f, 0.9f, 0.9f), square: false);
                        break;
                    case Enemy { IsAlive: true } enemy when enemy.CanSeeParty():
                        var ep = enemy.GlobalPosition / DungeonBuilder.CellSize;
                        DrawCircle(origin + new Vector2(ep.X, ep.Z) * cell, Mathf.Max(2f, cell * 0.35f), EnemyColor);
                        break;
                }
            }
        }

        // companions
        foreach (var adv in main.Party.Adventurers)
        {
            if (adv.IsLocal || adv.Downed) continue;
            var ap = adv.GlobalPosition / DungeonBuilder.CellSize;
            DrawCircle(origin + new Vector2(ap.X, ap.Z) * cell, Mathf.Max(2f, cell * 0.3f), PartyColor.Darkened(0.3f));
        }

        // party arrow: Godot forward is -Z; map +y is world +z
        var forward = -party.GlobalTransform.Basis.Z;
        var dir = new Vector2(forward.X, forward.Z).Normalized();
        var pp = party.GlobalPosition / DungeonBuilder.CellSize;
        var centre = origin + new Vector2(pp.X, pp.Z) * cell;
        var size = Mathf.Max(5f, cell * 0.9f);
        var side = new Vector2(-dir.Y, dir.X);
        var tri = new[] { centre + dir * size, centre - dir * size * 0.6f + side * size * 0.55f, centre - dir * size * 0.6f - side * size * 0.55f };
        DrawColoredPolygon(tri, PartyColor);

        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.25f), filled: false, width: 1f);
    }

    private void DrawMarker(Vector2 centre, float cell, Color color, bool square)
    {
        var r = Mathf.Max(2.5f, cell * 0.4f);
        if (square) DrawRect(new Rect2(centre - Vector2.One * r, Vector2.One * r * 2f), color);
        else DrawCircle(centre, r * 0.7f, color);
    }
}
