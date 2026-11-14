using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// <c>--net-soak</c> on a joining client: keep its hero in a fight for as long as the process runs, so a traffic
/// measurement (<c>--net-stats</c>) sees combat rather than an idle party. Once in game it steps beside the nearest
/// living enemy, faces it and swings every 0.45 s, following it when it moves and picking the next one when it dies.
/// A downed hero waits. Bots called with <c>--call-bot=</c> follow it in. Not part of any test; a measuring tool.
/// </summary>
public partial class NetSoak : Node
{
    private Enemy? _target;
    private double _swing, _chase, _elapsed;

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += delta;
        var client = NetSession.Instance.Client;
        if (client == null || client.Phase != ClientHandshake.Phase.InGame || _elapsed < 5) return;
        var main = Main.Instance;
        if (main?.Builder.FloorRoot is not { } root || main.Party.Local is not { } me || me.Downed || main.Rebuilding) return;
        if (_target == null || !IsInstanceValid(_target) || !_target.IsAlive)
        {
            _target = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsBoss && e.Profile.Id != Dungeon.DungeonBuilder.ViewerOrcId)
                .OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
            if (_target == null) return;
            me.Teleport(_target.GlobalPosition + new Vector3(1.0f, 0.3f, 0), _target.GlobalPosition + Vector3.Up);
            GD.Print($"[soak] {client.PlayerName} takes on {_target.DisplayName} at {_target.GlobalPosition:0.0}");
            _swing = 0;
            return;
        }
        me.FaceToward(_target.GlobalPosition);
        _chase += delta;
        if (_chase > 0.6 && _target.GlobalPosition.DistanceTo(me.GlobalPosition) > 1.6f)
        {
            _chase = 0;
            var toward = (me.GlobalPosition - _target.GlobalPosition) with { Y = 0 };
            var stand = _target.GlobalPosition + (toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward);
            if (main.Builder.Layout!.IsWalkable(Dungeon.DungeonBuilder.WorldToCell(stand))) me.Teleport(stand with { Y = _target.GlobalPosition.Y + 0.3f }, _target.GlobalPosition + Vector3.Up);
        }
        _swing += delta;
        if (_swing > 0.45) { _swing = 0; me.Attack(); }
    }
}
