using Godot;

namespace DungeonCrawler.Combat.Fx;

/// <summary>
/// A single burst of particles that frees itself: <see cref="Ttl"/> counts down and the node goes at 0. An optional
/// <see cref="Flash"/> light child fades to dark over <see cref="FlashSeconds"/>. Built by <see cref="HitFx"/>; it lives
/// under the floor root, so a floor change frees it with everything else and it never reaches outside itself.
/// </summary>
public partial class OneShotFx : GpuParticles3D
{
    public float Ttl { get; set; } = 1.0f;
    public OmniLight3D? Flash { get; set; }
    public float FlashSeconds { get; set; } = 0.25f;

    private float _age;
    private float _flashEnergy = -1f;

    public override void _Process(double delta)
    {
        if (IsQueuedForDeletion()) return;
        var dt = (float)delta;
        _age += dt;
        if (Flash != null)
        {
            if (!IsInstanceValid(Flash)) Flash = null;
            else
            {
                if (_flashEnergy < 0f) _flashEnergy = Flash.LightEnergy;
                var k = FlashSeconds > 0f ? Mathf.Clamp(1f - _age / FlashSeconds, 0f, 1f) : 0f;
                Flash.LightEnergy = _flashEnergy * k;
                if (k <= 0f) Flash.Visible = false;
            }
        }
        Ttl -= dt;
        if (Ttl <= 0f) QueueFree();
    }
}
