using DungeonCrawler.Adventure;
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
        if (Main.Instance?.Party?.Adventurers.FirstOrDefault(a => a != null && IsInstanceValid(a)) is { } leadNow) _lastLead = leadNow.GlobalPosition;
        if (!_pileLeft && !resumed && Game.Instance.CurrentFloor != 0 && _lastLead is { } lastLead && world.Trace.Any(t => t.StartsWith("left:")) && Main.Instance?.Party is { } sp)
        {
            // leave a coin pile on the floor while the client is away: it must come back through the floor state with an id the server knows
            _pileLeft = true;
            var leftPile = sp.SpawnCoins(37, lastLead + new Vector3(1.5f, 0.4f, 0));
            if (leftPile != null) leftPile.DroppedByParty = true; // companions leave it alone: only the rejoined client takes it
            GD.Print($"[net-server] left a 37 gold pile at {lastLead:0.0}: {leftPile != null}");
        }
        if (!_goldGranted && !resumed && world.Trace.Contains("floor:2") && Main.Instance?.Party is { } rich)
        {
            // the client buys at the merchant on floor 2: enough gold for any of his wares
            _goldGranted = true;
            rich.AddGold(10_000);
            GD.Print($"[net-server] granted 10000 gold for the shop: purse {rich.Gold}");
        }
        if (((resumed || Game.Instance.CurrentFloor == 0) && world.Trace.Contains("empty")) || world.Trace.Count(t => t == "empty") >= 2 || (world.Trace.Contains("empty") && _elapsed > 90))
        {
            var pressOnFloor = Main.Instance?.Builder.Hazards.OfType<Dungeon.Props.Press>().Any() == true;
            var want = resumed ? new[] { "rejoin:", "ready:", "left:" }
                : Game.Instance.CurrentFloor == 0 ? new[] { "join:", "ready:", "node:deal", "left:" } // --floor=0: the client joins the tavern, strikes the deal and leaves
                : pressOnFloor ? new[] { "join:", "ready:", "moved:", "chat", "ping", "kill", "stairs:immediate", "floor:2", "hazard:", "shop", "cheer", "left:", "rejoin:" }
                : new[] { "join:", "ready:", "moved:", "chat", "ping", "kill", "stairs:immediate", "floor:2", "shop", "cheer", "left:", "rejoin:" };
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
                if (!net.TestRejoinOnly && client.SnapshotsSeen >= 3 && !client.Trace.Contains("shop")) { Finish("[net]", false, "no shop sync arrived on joining"); return; }
                GD.Print($"[net] in game as member {client.MemberIndex}, floor {Game.Instance.CurrentFloor}, layout hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies");
                // join floor: the client built the floor the Welcome named, and a tavern is a tavern with its merchant in it
                if (client.Welcome is not { } welcome || Game.Instance.CurrentFloor != welcome.CurrentFloor || !main.LayoutHashMatched)
                { Finish("[net]", false, $"join floor: client on floor {Game.Instance.CurrentFloor}, welcome said {client.Welcome?.CurrentFloor}, layout hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}"); return; }
                if (welcome.CurrentFloor == 0)
                {
                    var kind = main.Builder.Layout?.Kind;
                    var merchant = main.Builder.FloorRoot?.GetNodeOrNull<Npc>("Npc_merchant");
                    if (kind != Dungeon.Generation.LayoutKind.Tavern || merchant == null) { Finish("[net]", false, $"tavern join: layout {kind}, merchant {(merchant == null ? "missing" : "present")}"); return; }
                    if (!_tavernJoinSeen) GD.Print($"[net-client] tavern join OK: layout {kind}, merchant {merchant.NpcName} present");
                    _tavernJoinSeen = true; // step 0 runs again while the join-only check waits for snapshots
                }
                if (net.TestJoinOnly)
                {
                    if (welcome.CurrentFloor != 0) { Finish("[net]", false, $"--net-test-join-only expects a server in the tavern, got floor {welcome.CurrentFloor}"); return; }
                    if (client.SnapshotsSeen < 3) return; // the server has our Ready, so it handles intents from us
                    // the briefing is local to this screen: its deal node goes to the server, which pays the shared purse once
                    _goldBefore = main.Party.Gold;
                    net.Send(new DialogueNodeIntent("merchant", "deal"));
                    _clock = 0; _step = 20;
                    return;
                }
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
                var orcs = main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count(e => e.Profile.Id == Dungeon.DungeonBuilder.ViewerOrcId) ?? 0;
                GD.Print($"[net] viewer orcs on this floor: {orcs}");
                if (orcs > 1) { Finish("[net]", false, $"{orcs} copies of the viewer orc: the client spawned its own beside the server's"); return; }
                _start = local.GlobalPosition;
                _clock = 0; _step = 1;
                return;
            case 20:
            {
                var gained = main.Party.Gold - _goldBefore;
                if (gained == TavernRules.SeedGold)
                {
                    if (!_dealSentTwice) { _dealSentTwice = true; net.Send(new DialogueNodeIntent("merchant", "deal")); _clock = 0; return; } // a second deal must not pay again
                    if (_clock < 2) return;
                    Finish("[net]", true, $"tavern join: the deal reached the server and the purse went {_goldBefore} -> {main.Party.Gold} once");
                    net.Disconnect("test finished");
                    return;
                }
                if (gained != 0) { Finish("[net]", false, $"the deal paid {gained} gold, expected {TavernRules.SeedGold} once"); return; }
                if (_clock > 8) Finish("[net]", false, $"the deal never paid (gold {main.Party.Gold}, before {_goldBefore})");
                return;
            }
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
                if (client.Trace.Contains("chat"))
                {
                    // a ping: where this body looks (floor 1's walls and floor are within the aim ray), up to the server and back to every screen with our name
                    var pinged = main.Hud.Ping();
                    if (!pinged) { Finish("[net]", false, "Hud.Ping() refused (down, cooling down, or looking at nothing)"); return; }
                    _clock = 0; _step = 30;
                }
                else if (_clock > 10) Finish("[net]", false, "chat never came back");
                return;
            case 30:
            {
                var mark = UI.Hud.PingMarkers.Instance?.Board.Active.FirstOrDefault(m => m.Name == net.JoinName);
                if (client.Trace.Contains("ping") && mark is { } m)
                {
                    GD.Print($"[net] ping relayed: \"{m.Name}\" at {m.X:0.0},{m.Y:0.0},{m.Z:0.0}");
                    _clock = 0; _step = 4;
                }
                else if (_clock > 6) Finish("[net]", false, $"the ping never came back (trace ping {client.Trace.Contains("ping")}, marks {UI.Hud.PingMarkers.Instance?.Board.Active.Count ?? -1})");
                return;
            }
            case 4:
            {
                // fight: stand in front of the nearest living enemy and swing until the server reports the kill
                var me = main.Party.Local!;
                if (!_fightHeard) { _fightHeard = true; _killsBefore = client.Trace.Count(t => t == "kill"); _hitFxBefore = client.Trace.Count(t => t == "hitfx"); EventBus.Instance.Sfx += FightCue; }
                if (_target == null || !IsInstanceValid(_target) || !_target.IsAlive)
                {
                    if (client.Trace.Contains("kill"))
                    {
                        // a second for the EnemyDied messages to land (a killing blow's EnemyHurt shows the death a tick earlier):
                        // then every death since the fight began was heard exactly once, and the blows were heard as grunts
                        _settle += delta;
                        if (_settle < 1.0) return;
                        EventBus.Instance.Sfx -= FightCue;
                        var kills = client.Trace.Count(t => t == "kill") - _killsBefore;
                        GD.Print($"[net] kill confirmed after {_swings} swings; heard {_deathCues} death cue(s) for {kills} kill(s), {_hurtCues} hurt cue(s)");
                        if (_deathCues != kills) { Finish("[net]", false, $"heard {_deathCues} death cue(s) for {kills} kill(s)"); return; }
                        if (_hurtCues < 1) { Finish("[net]", false, "heard no hurt cue while fighting"); return; }
                        // our sword blows bleed the enemy on the server, which relays each spatter as a HitFxEvt
                        var hitFx = client.Trace.Count(t => t == "hitfx") - _hitFxBefore;
                        GD.Print($"[net] hit fx: {hitFx} events");
                        if (hitFx < 1) { Finish("[net]", false, "no hit fx (blood) reached this client while fighting"); return; }
                        _clock = 0; _step = 5; return;
                    }
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
                if (chest == null) { GD.Print("[net] no unlocked chest on this floor; skipping"); _clock = 0; _step = 12; return; }
                if (!_placed3)
                {
                    // a step back from the chest on the walkable side, at its level
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, Dungeon.DungeonBuilder.WorldToCell(chest.GlobalPosition));
                    var away = (main.Builder.Surface(cell) - chest.GlobalPosition) with { Y = 0 };
                    var stand = chest.GlobalPosition + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward) * 1.3f;
                    me.Teleport(stand with { Y = chest.GlobalPosition.Y + 0.3f }, chest.GlobalPosition);
                    _placed3 = true; _goldBefore = main.Party.Gold; _lootBefore = client.Trace.Count(t => t == "loot");
                    return;
                }
                if (_clock > 0.8 && !_askedChest) { _askedChest = true; net.Send(new InteractIntent(NetRefs.Chest(chest.SpawnIndex))); }
                // the chest's coin pile reached the client as a pile; take it through the server like any pickup
                if (chest.Opened && !_askedCoins && main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().Where(d => d.Coins > 0).OrderBy(d => d.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault() is { } pile)
                {
                    _askedCoins = true;
                    me.Teleport(pile.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), pile.GlobalPosition);
                    net.Send(new InteractIntent(NetRefs.Loot(pile.Item.Id)));
                }
                if (client.Trace.Contains("chest") && chest.Opened && client.Trace.Contains("gold") && main.Party.Gold > _goldBefore)
                {
                    GD.Print($"[net] chest opened, {client.Trace.Count(t => t == "loot") - _lootBefore} item(s) spilled");
                    GD.Print($"[net] gold synced: {main.Party.Gold}");
                    _clock = 0; _step = 12;
                }
                else if (_clock > 8) Finish("[net]", false, $"chest never opened or its gold never synced (gold {main.Party.Gold} vs {_goldBefore}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            }
            case 12:
            {
                // a fighter on this floor: ask it to join through the dialogue; the server decides and its answer picks the line we see
                var me = main.Party.Local!;
                var fighter = main.Builder.FloorRoot?.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Mode == NpcMode.Fighter && !n.Dead);
                if (fighter == null) { GD.Print("[net] no fighter on this floor; skipping the join"); _clock = 0; _step = 7; return; }
                var dialogue = main.Hud.Dialogue;
                if (!_placedJoin)
                {
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, Dungeon.DungeonBuilder.WorldToCell(fighter.GlobalPosition));
                    me.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f, fighter.GlobalPosition);
                    main.Hud.OpenDialogue(fighter);
                    var join = dialogue.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.Join);
                    if (join < 0) { Finish("[net]", false, $"{fighter.NpcName}'s dialogue offers no Join choice"); return; }
                    dialogue.Choose(join);
                    _placedJoin = true; _clock = 0; return;
                }
                var answered = client.Trace.Contains("joined") || client.Trace.Contains("refused");
                if (answered && _clock > 1.5)
                {
                    var yes = client.Trace.Contains("joined");
                    var shown = dialogue.Node?.Id;
                    var agree = shown == (yes ? "yes" : "no") && fighter.Escorting == yes;
                    GD.Print($"[net] asked {fighter.NpcName} to join: server said {(yes ? "yes" : "no")}, dialogue shows '{shown}', escorting={fighter.Escorting}");
                    dialogue.Close();
                    if (!agree) { Finish("[net]", false, $"join answer and screen disagree (server {(yes ? "yes" : "no")}, node {shown}, escorting {fighter.Escorting})"); return; }
                    _clock = 0; _step = 7;
                }
                else if (_clock > 8) Finish("[net]", false, $"no answer to the join question (dialogue node {dialogue.Node?.Id}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
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
                if (!_askedStairs)
                {
                    // walk onto the landing: the stairs must ask before we answer (the question is what a player sees)
                    var stairs = main.Builder.FloorRoot?.GetChildren().OfType<Dungeon.Stairs>().FirstOrDefault(s => s.Direction > 0);
                    if (stairs == null) { Finish("[net]", false, "no down stairs on this floor"); return; }
                    if (!main.Hud.StairsPromptOpen)
                    {
                        if (_clock > 0.5) { main.Party.Local!.FaceToward(stairs.GlobalPosition); main.Party.ForcedMoveInput = new Vector2(0, -1); }
                        if (_clock > 6) { main.Party.ForcedMoveInput = null; Finish("[net]", false, $"the stairs never asked (we stand {main.Party.Local!.GlobalPosition.DistanceTo(stairs.GlobalPosition):0.0} m from them, prompt open {main.Hud.StairsPromptOpen})"); }
                        return;
                    }
                    main.Party.ForcedMoveInput = null;
                    GD.Print($"[net] the stairs asked after {_clock:0.0} s: \"{stairs.Question}\"; answering yes");
                    _askedStairs = true;
                    stairs.Confirm(main.Party, main.Party.Local!); // the Yes button's path: a client sends the intent
                    _clock = 0; return;
                }
                if (client.Trace.Contains("floor:2") && Game.Instance.CurrentFloor == 2 && main.Builder.Layout != null)
                {
                    GD.Print($"[net] on floor 2: {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies, hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, floor card {(client.Trace.Contains("floorcard") ? "received" : "MISSING")}: {main.Hud?.LastFloorCard}");
                    if (!client.Trace.Contains("floorcard")) { Finish("[net]", false, "the floor card for floor 1 never reached the client (run-structure's FloorCardEvt)"); return; }
                    _placementsAtFloor2 = _placements; _clock = 0; _step = 11;
                }
                else if (_clock > 20) Finish("[net]", false, $"floor 2 never came (floor {Game.Instance.CurrentFloor}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            case 11:
            {
                // the forced Press: stand on its plate, see the server's Warning phase arrive, step off before it closes
                // the server answers our Ready with a ForcePose to the arrival spot; stepping onto the plate before it lands would be undone by it
                if (_hazardReturn == null && _placements == _placementsAtFloor2 && _clock < 5) return;
                if (_placements != _placementsAtFloor2 && _hazardReturn == null && _clock < 0.3) return; // let the placement settle
                var press = main.Builder.Hazards.OfType<Dungeon.Props.Press>().FirstOrDefault();
                if (press == null) { GD.Print("[net] no Press on this floor; skipping the hazard step"); _clock = 0; _step = 14; return; }
                if (_hazardReturn == null)
                {
                    _hazardReturn = main.Party.Local!.GlobalPosition;
                    main.Party.Local.Teleport(main.Builder.Surface(press.Placement.Plate) + Vector3.Up * 0.3f);
                    GD.Print($"[net] stepping onto the Press plate at {main.Party.Local.GlobalPosition:0.0} (plate node {press.Plate.GlobalPosition:0.0}, cell {press.Placement.Plate}); body was at {_hazardReturn:0.0}; placements {_placements} (floor 2 arrival {(_placements > _placementsAtFloor2 ? "seen" : "NOT seen")})");
                    _placementsAtFloor2 = _placements; _clock = 0; return;
                }
                if (_placements != _placementsAtFloor2 && main.Party.Local!.GlobalPosition.DistanceTo(main.Builder.Surface(press.Placement.Plate)) > 1.5f)
                {
                    // a late placement pulled us off the plate: step back on
                    _placementsAtFloor2 = _placements;
                    main.Party.Local.Teleport(main.Builder.Surface(press.Placement.Plate) + Vector3.Up * 0.3f);
                    GD.Print($"[net] a late placement moved us; back onto the plate at {main.Party.Local.GlobalPosition:0.0}");
                    return;
                }
                if (client.Trace.Contains("hazard:Warning"))
                {
                    main.Party.Local!.Teleport(_hazardReturn.Value);
                    GD.Print($"[net] press warned after {_clock:0.0} s (phase {press.Phase}, {press.PhaseTime:0.00} s in); stepped clear");
                    _clock = 0; _step = 14;
                }
                else if (_clock > 10) Finish("[net]", false, $"the Press never warned (phase {press.Phase}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 14:
            {
                // the safe room: step inside; the server sees our body there and its objective line, which ends with "Safe room", reaches our HUD
                if (main.Builder.Layout is not { SafeRoom: >= 0 } safeLayout) { GD.Print("[net] no safe room on this floor; skipping"); _clock = 0; _step = 8; return; }
                var me = main.Party.Local!;
                if (_safeReturn == null)
                {
                    if (_clock < 0.5) return; // let the step off the plate land first
                    _safeReturn = me.GlobalPosition;
                    if (main.Hud.ObjectiveText.EndsWith("Safe room")) { Finish("[net]", false, $"safe room: the HUD says so before we stepped in (\"{main.Hud.ObjectiveText}\")"); return; }
                    me.Teleport(main.Builder.Surface(SafeRoomRules.MerchantCell(safeLayout)) + Vector3.Up * 0.3f, main.Builder.Surface(safeLayout.SafeRoomDoor));
                    _clock = 0; return;
                }
                var line = main.Hud.ObjectiveText;
                if (line.EndsWith("Safe room"))
                {
                    if (!client.Trace.Contains("shop")) { Finish("[net]", false, "the server never sent the shop's sold slots"); return; }
                    GD.Print($"[net] safe room: HUD shows \"{line}\" {_clock:0.0} s after stepping inside");
                    _clock = 0; _step = 15; // still inside: trade with the merchant before leaving the room
                }
                else if (_clock > 6) Finish("[net]", false, $"safe room: the HUD never showed it (line \"{line}\", we stand at {Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition)}, inside={safeLayout.IsInSafeRoom(Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition))})");
                return;
            }
            case 15:
            {
                // the merchant: buy his first ware and sell it back, from inside the safe room (the server refuses trades away from him)
                var me = main.Party.Local!;
                var bag = main.Party.Bag;
                var shops = client.Trace.Count(t => t == "shop");
                if (_shopPhase == 0)
                {
                    var stock = main.Party.ShopStockFor(Game.Instance.CurrentFloor);
                    if (stock.Items.Count == 0) { Finish("[net]", false, "the merchant has no stock"); return; }
                    var price = Items.Prices.BuyPrice(stock.Items[0]);
                    if (main.Party.Gold < price) { if (_clock > 8) Finish("[net]", false, $"never had the gold for the shop ({main.Party.Gold} < {price})"); return; }
                    _shopHeld = Enumerable.Range(0, bag.Capacity).Where(i => bag[i] != null).ToHashSet();
                    _bagBefore = bag.Items.Count(); _shopsBefore = shops; _goldBefore = main.Party.Gold;
                    main.Party.ShopBuy(0); // a client sends ShopBuyIntent
                    _shopPhase = 1; _clock = 0; return;
                }
                if (_shopPhase == 1)
                {
                    var bought = Enumerable.Range(0, bag.Capacity).FirstOrDefault(i => bag[i] != null && !_shopHeld.Contains(i), -1);
                    if (shops > _shopsBefore && bag.Items.Count() == _bagBefore + 1 && bought >= 0 && main.Party.Gold < _goldBefore)
                    {
                        GD.Print($"[net] bought {bag[bought]} into cell {bought}: gold {_goldBefore} -> {main.Party.Gold}, sold [{string.Join(",", main.Party.ShopSoldOnFloor)}]");
                        _bagBefore = bag.Items.Count(); _shopsBefore = shops; _goldBefore = main.Party.Gold;
                        main.Party.ShopSell(Party.BagSlot.Bag(bought)); // a client sends ShopSellIntent
                        _shopPhase = 2; _clock = 0;
                    }
                    else if (_clock > 8) Finish("[net]", false, $"the purchase never landed (bag {bag.Items.Count()} vs {_bagBefore}, gold {main.Party.Gold} vs {_goldBefore}, shop syncs {shops - _shopsBefore}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                    return;
                }
                if (shops > _shopsBefore && bag.Items.Count() == _bagBefore - 1 && main.Party.Gold > _goldBefore)
                {
                    GD.Print($"[net] sold it back: gold {_goldBefore} -> {main.Party.Gold}, bag {_bagBefore} -> {bag.Items.Count()}");
                    GD.Print("[net] bought and sold at the merchant");
                    me.Teleport(_safeReturn!.Value);
                    _clock = 0; _step = 16;
                }
                else if (_clock > 8) Finish("[net]", false, $"the sale never landed (bag {bag.Items.Count()} vs {_bagBefore}, gold {main.Party.Gold} vs {_goldBefore}, shop syncs {shops - _shopsBefore})");
                return;
            }
            case 16:
            {
                // cheer: V goes to the server as a CheerIntent; the server picks our hero's line and it comes back as a BarkMsg for our member
                if (!_cheerSent) { _cheerSent = true; _barksBefore = client.Trace.Count(t => t == $"bark:{client.MemberIndex}"); net.Send(new CheerIntent()); _clock = 0; return; }
                if (client.Trace.Count(t => t == $"bark:{client.MemberIndex}") > _barksBefore) { GD.Print($"[net] cheered: our hero's line came back from the server"); _clock = 0; _step = 8; }
                else if (_clock > 3) Finish("[net]", false, $"the cheer never came back as our bark (trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 8:
                if (client.SnapshotsSeen < 10 && _clock < 10) return;
                // leave, then come back under the same name: same member, same bag, same floor
                _firstRun = $"hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, snapshots {client.SnapshotsSeen}, rtt {client.Rtt * 1000:0} ms, trace=[{string.Join(", ", client.Trace.Distinct())}]";
                _bagAtLeave = main.Party.Bag.Items.Count();
                _memberAtLeave = client.MemberIndex;
                _goldAtLeave = main.Party.Gold;
                GD.Print($"[net] leaving with {_bagAtLeave} bag item(s) as member {_memberAtLeave}; rejoining shortly");
                net.Disconnect("test: leaving to rejoin");
                _clock = 0; _step = 9;
                return;
            case 10:
                if (!client.Trace.Contains("ready")) { if (_clock > 40) Finish("[net]", false, $"rejoin never became ready ({net.Status})"); return; }
                if (client.SnapshotsSeen < 5) return;
                var back = main.Party.Bag.Items.Count();
                var okRejoin = client.MemberIndex == _memberAtLeave && back >= _bagAtLeave && Game.Instance.CurrentFloor == 2 && main.LayoutHashMatched && main.Party.Gold == _goldAtLeave;
                _okRejoin = okRejoin;
                GD.Print($"[net] rejoined as member {client.MemberIndex} (was {_memberAtLeave}), bag {back} (was {_bagAtLeave}), gold {main.Party.Gold} (was {_goldAtLeave}), floor {Game.Instance.CurrentFloor}");
                _clock = 0; _step = 13;
                return;
            case 13:
            {
                // the pile the server left while we were away: pick it up through the server, which only works if the client rebuilt it with the server's id
                var me = main.Party.Local!;
                var pile = main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().FirstOrDefault(d => d.Coins == 37);
                if (pile == null && !_askedRejoinPile) { if (_clock > 5) Finish("[net]", false, $"rejoin pile pickup: FAIL, the 37 gold pile never reached the rejoined client welcome carried {client.Welcome?.FloorState.Items.Count} item(s)"); return; }
                if (!_askedRejoinPile && pile != null)
                {
                    _askedRejoinPile = true; _goldBefore = main.Party.Gold;
                    me.Teleport(pile.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), pile.GlobalPosition);
                    net.Send(new InteractIntent(NetRefs.Loot(pile.Item.Id)));
                    _clock = 0;
                    return;
                }
                var pileOk = main.Party.Gold == _goldBefore + 37;
                if (!pileOk && _clock < 8) return;
                GD.Print($"[net] rejoin pile pickup: {(pileOk ? "OK" : "FAIL")} (gold {_goldBefore} -> {main.Party.Gold})");
                var ok = _okRejoin && pileOk && _firstRun.Contains("hash match");
                _done = true;
                GD.Print($"[net] RESULT {(ok ? "ok" : "FAIL")}: first run {_firstRun}; rejoin {(_okRejoin ? "ok" : "FAIL")}");
                net.Disconnect("test finished");
                GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(ok ? 0 : 1);
                return;
            }
        }
    }

    private Enemies.Enemy? _target;
    private Dungeon.Props.Chest? _chest;
    private bool _placed, _placed2, _placed3, _placed4, _askedPickup, _askedChest, _askedStairs;
    private double _swingClock, _chaseClock;
    private int _swings, _bagBefore, _lootBefore;
    private bool _fightHeard;
    private bool _cheerSent;
    private int _barksBefore;
    private int _killsBefore, _deathCues, _hurtCues, _hitFxBefore;
    private double _settle;

    /// <summary>The fight's cues on the client: enemy death and hurt sounds, grunts or the generic thuds.</summary>
    private void FightCue(string cue)
    {
        if (cue.StartsWith("grunt_death_") || cue == "enemy_die") _deathCues++;
        else if (cue.StartsWith("grunt_hurt_") || cue == "enemy_hurt") _hurtCues++;
    }
    private string _firstRun = "";
    private int _bagAtLeave, _memberAtLeave, _goldBefore, _goldAtLeave;
    private Vector3? _lastLead;
    private bool _askedCoins, _pileLeft, _okRejoin, _askedRejoinPile;
    private void Trace(string what) => GD.Print($"[net] {what}");
    private Vector3? _startAfterPose, _hazardReturn, _safeReturn;
    private int _placements, _placementsAtFloor2;
    private bool _placedJoin;
    private bool _goldGranted, _dealSentTwice, _tavernJoinSeen;
    private int _shopPhase, _shopsBefore;
    private HashSet<int> _shopHeld = new();
    /// <summary>The server placed our body (a ForcePose): the start position on floor 1, and the arrival spot after a floor change.</summary>
    public void NotePlaced(Vector3 at) { _startAfterPose = at; _placements++; }
}
