using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OctoHD.App.Controls;

namespace OctoHD.App.Tests;

public sealed class ParticleBackgroundTests
{
    [AvaloniaFact]
    public void Animation_loop_ignores_paused_and_detached_frame_callbacks()
    {
        var scheduler = new ManualAnimationFrameScheduler();
        var particleLayer = new ParticleBackground(scheduler);
        var window = new Window
        {
            Width = 320,
            Height = 240,
            Content = particleLayer
        };

        try
        {
            window.Show();
            Assert.True(particleLayer.IsAnimationRunning);
            Assert.Equal(1, scheduler.PendingFrameCount);

            scheduler.RunNext(TimeSpan.FromSeconds(1));
            scheduler.RunNext(TimeSpan.FromSeconds(1.02));
            scheduler.RunNext(TimeSpan.FromSeconds(1.04));
            Assert.Equal(0.04d, particleLayer.AnimationSeconds, 6);

            particleLayer.IsPaused = true;
            scheduler.RunNext(TimeSpan.FromSeconds(5));
            Assert.False(particleLayer.IsAnimationRunning);
            Assert.Equal(0.04d, particleLayer.AnimationSeconds, 6);
            Assert.Equal(0, scheduler.PendingFrameCount);

            particleLayer.IsPaused = false;
            scheduler.RunNext(TimeSpan.FromSeconds(10));
            scheduler.RunNext(TimeSpan.FromSeconds(10.04));
            Assert.True(particleLayer.IsAnimationRunning);
            Assert.Equal(0.08d, particleLayer.AnimationSeconds, 6);

            window.Close();
            scheduler.RunNext(TimeSpan.FromSeconds(20));
            Assert.False(particleLayer.IsAnimationRunning);
            Assert.Equal(0.08d, particleLayer.AnimationSeconds, 6);
            Assert.Equal(0, scheduler.PendingFrameCount);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Particle_field_is_deterministic_for_the_same_seed()
    {
        var first = ParticleField.CreateParticles(36, 7927);
        var second = ParticleField.CreateParticles(36, 7927);
        var different = ParticleField.CreateParticles(36, 7928);

        Assert.True(first.SequenceEqual(second));
        Assert.False(first.SequenceEqual(different));
        Assert.Contains(first, particle => particle.Tone == ParticleTone.ArcaneBlue);
        Assert.Contains(first, particle => particle.Tone == ParticleTone.PaleCyan);
        Assert.Contains(first, particle => particle.Tone == ParticleTone.MutedGold);
    }

    [Fact]
    public void Particle_field_creates_bounded_visual_parameters()
    {
        var particles = ParticleField.CreateParticles(64, 7927);

        Assert.Equal(64, particles.Length);
        Assert.All(particles, particle =>
        {
            Assert.InRange(particle.NormalizedX, 0, 1);
            Assert.InRange(particle.NormalizedY, 0, 1);
            Assert.InRange(particle.Radius, 0.8, 1.8);
            Assert.InRange(particle.RiseSpeed, 4, 11);
            Assert.InRange(particle.SwayAmplitude, 5, 16);
            Assert.InRange(particle.SwayPeriod, 9, 18);
            Assert.InRange(particle.TwinklePeriod, 4, 9);
            Assert.InRange(particle.BaseOpacity, 0.14, 0.30);
        });
    }

    [Fact]
    public void Particle_visuals_stay_within_the_wrapped_logical_field()
    {
        var size = new Size(1240, 760);
        var particles = ParticleField.CreateParticles(36, 7927);

        foreach (var seconds in new[] { 0d, 1d, 17.5d, 180d, 10_000d })
        {
            Assert.All(particles, particle =>
            {
                var visual = ParticleField.CalculateVisual(particle, size, seconds);

                Assert.InRange(visual.Center.X, -16, size.Width + 16);
                Assert.InRange(visual.Center.Y, -28, size.Height + 28);
                Assert.Equal(particle.Radius, visual.Radius);
                Assert.InRange(visual.Opacity, 0, particle.BaseOpacity);
                Assert.Equal(particle.Tone, visual.Tone);
            });
        }
    }

    [Fact]
    public void Particle_field_handles_zero_sized_layouts()
    {
        var particle = Assert.Single(ParticleField.CreateParticles(1, 7927));

        var visual = ParticleField.CalculateVisual(particle, default, 10);

        Assert.Equal(default, visual.Center);
        Assert.Equal(0, visual.Opacity);
    }

    private sealed class ManualAnimationFrameScheduler : IParticleAnimationFrameScheduler
    {
        private readonly Queue<Action<TimeSpan>> _callbacks = new();

        public int PendingFrameCount => _callbacks.Count;

        public bool IsHostActive(WindowBase? hostWindow) => true;

        public void RequestAnimationFrame(TopLevel topLevel, Action<TimeSpan> callback) =>
            _callbacks.Enqueue(callback);

        public void RunNext(TimeSpan timestamp)
        {
            if (!_callbacks.TryDequeue(out var callback))
            {
                throw new InvalidOperationException("No animation frame is pending.");
            }

            callback(timestamp);
        }
    }
}
