using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// --probe-shrine (a network client on a floor with a shrine): walk to the altar, press E on it the way a player does,
/// take the first boon when the card shows, and print the card, the screen's input state and the altar at every step.
/// </summary>
public partial class DebugTools
{
    private bool _probeShrine;
    private int _shrineStage;
    private double _shrineClock;
    private string _shrineLast = "";

    private void TickShrineProbe(double delta)
    {
        if (!_probeShrine || !Net.NetSession.IsClient || Net.NetSession.Instance.Client?.Phase != Net.ClientHandshake.Phase.InGame) return;
        if (Main.Instance?.Builder.FloorRoot is not { } root || Main.Instance.Party.Local is not { } me) return;
        var hud = Main.Instance.Hud;
        var party = Main.Instance.Party;
        _shrineClock += delta;
        var state = $"card={(hud.ShrineOpen ? $"open ({hud.Shrine.RowCount} rows: {string.Join(" | ", Enumerable.Range(0, hud.Shrine.RowCount).Select(hud.Shrine.RowText))})" : "closed")} inputLocked={party.InputLocked} mouse={Input.MouseMode} members={party.Members.Length} hp={me.State.Hp:0}/{me.State.Stats[Characters.Stat.MaxHp]:0} gold={party.Gold}";
        if (state != _shrineLast) { GD.Print($"[shrine] t={_shrineClock:0.0} {state}"); _shrineLast = state; }
        var shrine = root.GetChildren().OfType<Shrine>().FirstOrDefault();
        switch (_shrineStage)
        {
            case 0:
                if (_shrineClock < 6) return; // bots called, snapshots flowing
                if (shrine == null) { GD.Print("[shrine] no shrine on this floor"); _probeShrine = false; return; }
                var front = shrine.GlobalPosition + (shrine.GlobalTransform.Basis.Z with { Y = 0 }).Normalized() * 1.8f;
                var stand = Main.Instance.Builder.Surface(Dungeon.DungeonBuilder.WorldToCell(front));
                me.Teleport(new Vector3(front.X, stand.Y + 0.3f, front.Z), shrine.GlobalPosition);
                GD.Print($"[shrine] altar \"{shrine.Name}\" at {shrine.GlobalPosition:0.0}, used={shrine.Used}, prompt \"{shrine.Prompt}\"; standing at {me.GlobalPosition:0.0}");
                _shrineClock = 0; _shrineStage = 1;
                return;
            case 1:
                if (_shrineClock < 0.8) return;
                me.Camera.LookAt(shrine!.GlobalPosition + Vector3.Up * 0.6f, Vector3.Up);
                me.Rotation = me.Rotation with { Y = me.Camera.GlobalRotation.Y };
                me.Camera.Rotation = me.Camera.Rotation with { Y = 0, Z = 0 };
                _shrineClock = 0; _shrineStage = 2;
                return;
            case 2:
                if (_shrineClock < 0.3) return;
                GD.Print($"[shrine] looking at {(me.LookingAt == null ? "nothing" : $"\"{me.LookingAt.Prompt}\"")}; pressing E");
                if (me.LookingAt is Shrine) me.TryInteract();
                else Net.NetSession.Instance.Send(new Net.InteractIntent(Net.NetRefs.Shrine(0)));
                _shrineClock = 0; _shrineStage = 3;
                return;
            case 3:
                if (hud.ShrineOpen && _shrineClock > 1.0) { GD.Print($"[shrine] picking row 1: {hud.Shrine.Pick(0)}"); _shrineClock = 0; _shrineStage = 4; return; }
                if (_shrineClock > 6) { GD.Print("[shrine] the card never came"); _shrineClock = 0; _shrineStage = 4; }
                return;
            case 4:
                if (_shrineClock < 6) return;
                GD.Print($"[shrine] RESULT altar used={shrine?.Used} prompt \"{shrine?.Prompt}\"; {state}");
                _probeShrine = false;
                return;
        }
    }
}
