using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>Runs a hold-out quest's waves: spawns enemies of the wave collective near their post, aggroed, and reports each cleared wave.</summary>
public partial class WaveSpawner : Node
{
    public const float BetweenWavesSeconds = 4f;

    private QuestState _quest = null!;
    private CollectiveContents _template = null!;
    private Vector3 _from;
    private int _wave;
    private float _timer;
    private readonly List<Enemy> _alive = new();
    private bool _waiting;
    public bool Done { get; private set; }

    public void Start(QuestState quest, CollectiveContents template, Vector3 spawnAround, int completedWaves)
    {
        _quest = quest;
        _template = template;
        _from = spawnAround;
        _wave = completedWaves;
        _waiting = true;
        _timer = 1.5f;
        EventBus.Instance.Log("Something stirs in the dark. They are coming.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        if (Done || _quest.IsTerminal) { if (!Done) { Done = true; QueueFree(); } return; }
        var dt = (float)delta;
        if (_waiting)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _waiting = false;
            SpawnWave();
            return;
        }
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.IsAlive);
        if (_alive.Count > 0) return;
        _wave++;
        AdventureDirector.Instance?.Apply(new WaveCleared(_quest.Id, _wave));
        if (_wave >= _quest.Goal) { Done = true; QueueFree(); return; }
        _waiting = true;
        _timer = BetweenWavesSeconds;
        EventBus.Instance.Log($"Wave {_wave} broken. Brace for the next.");
    }

    private void SpawnWave()
    {
        var builder = Main.Instance?.Builder;
        if (builder == null) return;
        var ids = _template.MemberSpawnIndices.Count > 0 && builder.Contents != null
            ? builder.Contents.Enemies.Where(e => e.CollectiveId == _template.Id).Select(e => e.EnemyId).Distinct().ToList()
            : new List<string> { LegacyPopulator.FallbackEnemy };
        if (ids.Count == 0) ids.Add(LegacyPopulator.FallbackEnemy);
        var count = 2 + _wave;
        for (var i = 0; i < count; i++)
        {
            var angle = i * Mathf.Tau / count;
            var pos = _from + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2.5f + Vector3.Up * 0.1f;
            if (builder.SpawnEnemy(ids[i % ids.Count], pos, Game.Instance.CurrentFloor) is { } e) { e.ForceAggro(); _alive.Add(e); }
        }
        EventBus.Instance.Log($"Wave {_wave + 1} of {_quest.Goal}: {count} attackers!");
    }
}
