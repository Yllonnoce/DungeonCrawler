using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The scripted multiplayer check (<c>scripts/net_test.sh</c>): a headless server and a headless client prove the
/// join, the identical floor, movement echoed back, chat, and a clean leave. Each side prints one RESULT line.
/// </summary>
public partial class NetTestFlow : Node
{
    public bool ServerSide { get; set; }
    private int _step;
    private double _clock, _elapsed;
    private Vector3 _start;
    private bool _done;

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        _elapsed += delta;
        if (ServerSide) ServerTick(); else ClientTick(delta);
    }

    private void Finish(string tag, bool ok, string detail)
    {
        _done = true;
        GD.Print($"{tag} RESULT {(ok ? "ok" : "FAIL")}: {detail}");
        GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(ok ? 0 : 1);
    }

    private void ServerTick()
    {
        var world = NetSession.Instance.Server;
        if (world == null) return;
        var resumed = NetSession.Instance.Resumed;
        if ((resumed && world.Trace.Contains("empty")) || world.Trace.Count(t => t == "empty") >= 2 || (world.Trace.Contains("empty") && _elapsed > 90))
        {
            var pressOnFloor = Main.Instance?.Builder.Hazards.OfType<Dungeon.Props.Press>().Any() == true;
            var want = resumed ? new[] { "rejoin:", "ready:", "left:" }
                : pressOnFloor ? new[] { "join:", "ready:", "moved:", "chat", "kill", "floor:2", "hazard:", "left:", "rejoin:" }
                : new[] { "join:", "ready:", "moved:", "chat", "kill", "floor:2", "left:", "rejoin:" };
            var missing = want.Where(w => !world.Trace.Any(t => t.StartsWith(w))).ToList();
            var ordered = true;
            var last = -1;
            foreach (var w in want)
            {
                var i = world.Trace.FindIndex(t => t.StartsWith(w));
                if (i < last) ordered = false;
                last = Math.Max(last, i);
            }
            Finish("[net-server]", missing.Count == 0 && ordered, $"trace=[{string.Join(", ", world.Trace)}]" + (missing.Count > 0 ? $" missing=[{string.Join(", ", missing)}]" : "") + (ordered ? "" : " out of order"));
            return;
        }
        if (_elapsed > 110) Finish("[net-server]", false, $"timed out, trace=[{string.Join(", ", world.Trace)}]");
    }

    private void ClientTick(double delta)
    {
        var net = NetSession.Instance;
        var client = net.Client;
        if (client == null)
        {
            if (_step == 9 && _clock > 1.5) { _clock = 0; _step = 10; net.Connect(net.JoinUrl!, net.JoinName == "" ? "Player" : net.JoinName, net.JoinClass); Trace("reconnecting"); return; }
            if (_step == 9) { _clock += delta; return; }
            if (_elapsed > 30) Finish("[net]", false, $"no client ({net.Status})");
            return;
        }
        if (client.Trace.Contains("failed") || client.Trace.Contains("rejected") || client.Trace.Contains("dropped")) { Finish("[net]", false, $"connection: {net.Status}"); return; }
        var main = Main.Instance;
        _clock += delta;
        switch (_step)
        {
            case 0:
                if (!client.Trace.Contains("ready") || main.Party?.Local is not { } local) { if (_elapsed > 40) Finish("[net]", false, $"never ready ({net.Status})"); return; }
                GD.Print($"[net] in game as member {client.MemberIndex}, floor {Game.Instance.CurrentFloor}, layout hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies");
                if (net.TestRejoinOnly)
                {
                    if (client.SnapshotsSeen < 3) return; // snapshots only flow once the server has our Ready
                    // a resumed server: the character, its bag and its floor must all have come back from the save
                    var bag = main.Party.Bag.Items.Count();
                    var okBack = client.MemberIndex == 0 && bag >= 1 && Game.Instance.CurrentFloor == 2 && main.LayoutHashMatched && client.Welcome?.Players.Count >= 1;
                    _done = true;
                    GD.Print($"[net] RESULT {(okBack ? "ok" : "FAIL")}: resumed server gave back member {client.MemberIndex} with {bag} bag item(s) on floor {Game.Instance.CurrentFloor} ({client.Welcome?.Players.Count} known players)");
                    net.Disconnect("test finished");
                    GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(okBack ? 0 : 1);
                    return;
                }
                _start = local.GlobalPosition;
                _clock = 0; _step = 1;
                return;
            case 1:
                if (_clock < 0.5) return; // let ForcePose land first
                main.Party.ForcedMoveInput = new Vector2(0, -1);
                if (_clock > 2.5) { main.Party.ForcedMoveInput = null; _start = _startAfterPose ?? _start; _clock = 0; _step = 2; }
                return;
            case 2:
                var moved = main.Party.Local!.GlobalPosition.DistanceTo(_startAfterPose ?? _start);
                if (client.Trace.Contains("echo") && moved > 0.8f) { GD.Print($"[net] moved {moved:0.0} m and saw the echo after {client.SnapshotsSeen} snapshots"); net.Send(new ChatIntent("hello from the test client")); _clock = 0; _step = 3; }
                else if (_clock > 15) Finish("[net]", false, $"no echo (moved {moved:0.0} m, snapshots {client.SnapshotsSeen}, echo {client.Trace.Contains("echo")})");
                return;
            case 3:
                if (client.Trace.Contains("chat")) { _clock = 0; _step = 4; }
                else if (_clock > 10) Finish("[net]", false, "chat never came back");
                return;
            case 4:
            {
                // fight: stand in front of the nearest living enemy and swing until the server reports the kill
                var me = main.Party.Local!;
                if (_target == null || !IsInstanceValid(_target) || !_target.IsAlive)
                {
                    if (client.Trace.Contains("kill")) { GD.Print($"[net] kill confirmed after {_swings} swings"); _clock = 0; _step = 5; return; }
                    // a loner, not a camp or a nest: one level-1 warrior against a band would be a different test
                    // the viewer's 280 hp orc by the entrance is a showpiece, not a level-1 fight
                    var candidates = main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsAlive && !e.IsBoss && !e.IsHorde && e.Kind == Enemies.EnemyKind.Melee && e.Profile.Id != Dungeon.DungeonBuilder.ViewerOrcId).ToList() ?? new List<Enemies.Enemy>();
                    _target = candidates.Where(e => e.Group == null).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault()
                        ?? candidates.OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                    if (_target == null) { Finish("[net]", false, "no enemy to fight"); return; }
                    _placed = false;
                }
                if (!_placed)
                {
                    // step in front of it (the server accepts poses on walkable cells)
                    // beside it, on its level (it may stand in a cellar or on a ledge)
                    me.Teleport(_target.GlobalPosition + new Vector3(1.0f, 0.3f, 0), _target.GlobalPosition + Vector3.Up * 1.0f);
                    _placed = true; _swingClock = 0;
                    return;
                }
                me.FaceToward(_target.GlobalPosition);
                _swingClock += delta;
                _chaseClock += delta;
                // it moves; stay on its heels (the server sees our poses, so the swing lands where we stand)
                if (_chaseClock > 0.6 && _target.GlobalPosition.DistanceTo(me.GlobalPosition) > 1.4f)
                {
                    _chaseClock = 0;
                    var toward = (me.GlobalPosition - _target.GlobalPosition) with { Y = 0 };
                    var stand = _target.GlobalPosition + (toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward) * 1.0f;
                    if (main.Builder.Layout!.IsWalkable(Dungeon.DungeonBuilder.WorldToCell(stand))) me.Teleport(stand with { Y = _target.GlobalPosition.Y + 0.3f }, _target.GlobalPosition + Vector3.Up * 1.0f);
                }
                if (_swingClock > 0.45) { _swingClock = 0; _swings++; me.Attack(); }
                if (_clock > 40) Finish("[net]", false, $"could not kill {_target.DisplayName} (hp {_target.Hp:0}, {_target.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m away, it at {_target.GlobalPosition:0.0}, we at {me.GlobalPosition:0.0}) in {_swings} swings; we have {me.State.Hp:0} hp, trace=[{string.Join(", ", client.Trace)}]");
                return;
            }
            case 5:
            {
                // loot: walk over to whatever the kill dropped and pick it up through the server
                var me = main.Party.Local!;
                var drop = main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().OrderBy(d => d.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                if (drop == null) { if (_clock > 3) { GD.Print("[net] nothing dropped; skipping the pickup"); _clock = 0; _step = 6; } return; }
                if (!_placed2) { me.Teleport(drop.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), drop.GlobalPosition); _placed2 = true; _bagBefore = main.Party.Bag.Items.Count(); return; }
                if (_clock > 0.8 && !_askedPickup) { _askedPickup = true; net.Send(new InteractIntent(NetRefs.Loot(drop.Item.Id))); }
                if (client.Trace.Contains("bag") && main.Party.Bag.Items.Count() > _bagBefore) { GD.Print($"[net] picked up {main.Party.Bag.Items.Count() - _bagBefore} item(s), bag now {main.Party.Bag.Items.Count()}"); _clock = 0; _step = 6; }
                else if (_clock > 8) Finish("[net]", false, $"pickup never landed (bag {main.Party.Bag.Items.Count()} vs {_bagBefore}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            }
            case 6:
            {
                // chest: open the nearest unlocked one; the server spills loot and everyone sees it
                var me = main.Party.Local!;
                _chest ??= main.Builder.FloorRoot?.GetChildren().OfType<Dungeon.Props.Chest>().Where(c => !c.Opened && !c.Locked).OrderBy(c => c.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                var chest = _chest;
                if (chest == null) { GD.Print("[net] no unlocked chest on this floor; skipping"); _clock = 0; _step = 7; return; }
                if (!_placed3)
                {
                    // a step back from the chest on the walkable side, at its level
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, Dungeon.DungeonBuilder.WorldToCell(chest.GlobalPosition));
                    var away = (main.Builder.Surface(cell) - chest.GlobalPosition) with { Y = 0 };
                    var stand = chest.GlobalPosition + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward) * 1.3f;
                    me.Teleport(stand with { Y = chest.GlobalPosition.Y + 0.3f }, chest.GlobalPosition);
                    _placed3 = true; _lootBefore = client.Trace.Count(t => t == "loot");
                    return;
                }
                if (_clock > 0.8 && !_askedChest) { _askedChest = true; net.Send(new InteractIntent(NetRefs.Chest(chest.SpawnIndex))); }
                if (client.Trace.Contains("chest") && chest.Opened) { GD.Print($"[net] chest opened, {client.Trace.Count(t => t == "loot") - _lootBefore} item(s) spilled"); _clock = 0; _step = 7; }
                else if (_clock > 8) Finish("[net]", false, $"chest never opened (trace=[{string.Join(", ", client.Trace)}])");
                return;
            }
            case 7:
                // stairs: any player, immediately; the whole party arrives on floor 2 built from the same plan
                if (!_placed4 && main.Builder.Layout is { } lay1)
                {
                    var down = main.Builder.Surface(lay1.DownStairs);
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(lay1, lay1.DownStairs);
                    var away = (main.Builder.Surface(cell) - down) with { Y = 0 };
                    var stand = down + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward) * 1.3f;
                    main.Party.Local!.Teleport(stand with { Y = down.Y + 0.3f }, down);
                    _placed4 = true; _clock = 0; return;
                }
                if (!_askedStairs && _clock > 0.8) { _askedStairs = true; net.Send(new InteractIntent(NetRefs.StairsDown)); _clock = 0; return; }
                if (client.Trace.Contains("floor:2") && Game.Instance.CurrentFloor == 2 && main.Builder.Layout != null)
                {
                    GD.Print($"[net] on floor 2: {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies, hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}");
                    _clock = 0; _step = 11;
                }
                else if (_clock > 20) Finish("[net]", false, $"floor 2 never came (floor {Game.Instance.CurrentFloor}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            case 11:
            {
                // the forced Press: stand on its plate, see the server's Warning phase arrive, step off before it closes
                if (client.SnapshotsSeen < 10 && _clock < 10) return;
                var press = main.Builder.Hazards.OfType<Dungeon.Props.Press>().FirstOrDefault();
                if (press == null) { GD.Print("[net] no Press on this floor; skipping the hazard step"); _clock = 0; _step = 8; return; }
                if (_hazardReturn == null)
                {
                    _hazardReturn = main.Party.Local!.GlobalPosition;
                    main.Party.Local.Teleport(main.Builder.Surface(press.Placement.Plate) + Vector3.Up * 0.3f);
                    GD.Print($"[net] stepping onto the Press plate at {main.Party.Local.GlobalPosition:0.0} (plate node {press.Plate.GlobalPosition:0.0}, cell {press.Placement.Plate}); body was at {_hazardReturn:0.0}");
                    _clock = 0; return;
                }
                if (client.Trace.Contains("hazard:Warning"))
                {
                    main.Party.Local!.Teleport(_hazardReturn.Value);
                    GD.Print($"[net] press warned after {_clock:0.0} s (phase {press.Phase}, {press.PhaseTime:0.00} s in); stepped clear");
                    _clock = 0; _step = 8;
                }
                else if (_clock > 10) Finish("[net]", false, $"the Press never warned (phase {press.Phase}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 8:
                if (client.SnapshotsSeen < 10 && _clock < 10) return;
                // leave, then come back under the same name: same member, same bag, same floor
                _firstRun = $"hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, snapshots {client.SnapshotsSeen}, rtt {client.Rtt * 1000:0} ms, trace=[{string.Join(", ", client.Trace.Distinct())}]";
                _bagAtLeave = main.Party.Bag.Items.Count();
                _memberAtLeave = client.MemberIndex;
                GD.Print($"[net] leaving with {_bagAtLeave} bag item(s) as member {_memberAtLeave}; rejoining shortly");
                net.Disconnect("test: leaving to rejoin");
                _clock = 0; _step = 9;
                return;
            case 10:
                if (!client.Trace.Contains("ready")) { if (_clock > 40) Finish("[net]", false, $"rejoin never became ready ({net.Status})"); return; }
                if (client.SnapshotsSeen < 5) return;
                var back = main.Party.Bag.Items.Count();
                var okRejoin = client.MemberIndex == _memberAtLeave && back >= _bagAtLeave && Game.Instance.CurrentFloor == 2 && main.LayoutHashMatched;
                var ok = okRejoin && _firstRun.Contains("hash match");
                _done = true;
                GD.Print($"[net] rejoined as member {client.MemberIndex} (was {_memberAtLeave}), bag {back} (was {_bagAtLeave}), floor {Game.Instance.CurrentFloor}");
                GD.Print($"[net] RESULT {(ok ? "ok" : "FAIL")}: first run {_firstRun}; rejoin {(okRejoin ? "ok" : "FAIL")}");
                net.Disconnect("test finished");
                GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(ok ? 0 : 1);
                return;
        }
    }

    private Enemies.Enemy? _target;
    private Dungeon.Props.Chest? _chest;
    private bool _placed, _placed2, _placed3, _placed4, _askedPickup, _askedChest, _askedStairs;
    private double _swingClock, _chaseClock;
    private int _swings, _bagBefore, _lootBefore;
    private string _firstRun = "";
    private int _bagAtLeave, _memberAtLeave;
    private void Trace(string what) => GD.Print($"[net] {what}");
    private Vector3? _startAfterPose, _hazardReturn;
    public void NotePlaced(Vector3 at) => _startAfterPose = at;
}
