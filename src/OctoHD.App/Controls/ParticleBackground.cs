using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

namespace OctoHD.App.Controls;

public sealed class ParticleBackground : Control
{
    private const int MaximumParticleCount = 64;
    private const int OpacityBucketCount = 12;
    private const double MaximumParticleOpacity = 0.30d;
    private const double TargetFrameSeconds = 1d / 30d;
    private const double MaximumFrameGapSeconds = 0.1d;
    private static readonly ParticleBrushes[] BlueBrushes = CreateBrushes(0x7BC3E1u, 0x36 / 255d);
    private static readonly ParticleBrushes[] CyanBrushes = CreateBrushes(0xB5E8F3u, 0x30 / 255d);
    private static readonly ParticleBrushes[] GoldBrushes = CreateBrushes(0xD6A14Bu, 0x30 / 255d);
    private readonly IParticleAnimationFrameScheduler _animationScheduler;
    private ParticleSeed[] _particles = [];
    private TopLevel? _topLevel;
    private WindowBase? _hostWindow;
    private AnimationRun? _activeRun;
    private double _animationSeconds;
    private bool _isAttached;
    private bool _isHostActive;

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<ParticleBackground, int>(nameof(ParticleCount), 48);

    public static readonly StyledProperty<int> SeedProperty =
        AvaloniaProperty.Register<ParticleBackground, int>(nameof(Seed), 7927);

    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<ParticleBackground, bool>(nameof(IsAnimationEnabled), true);

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<ParticleBackground, bool>(nameof(IsPaused));

    public ParticleBackground()
        : this(AvaloniaParticleAnimationFrameScheduler.Instance)
    {
    }

    internal ParticleBackground(IParticleAnimationFrameScheduler animationScheduler)
    {
        _animationScheduler = animationScheduler ?? throw new ArgumentNullException(nameof(animationScheduler));
        Focusable = false;
        IsHitTestVisible = false;
        ClipToBounds = true;
        RebuildParticles();
    }

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public int Seed
    {
        get => GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    internal bool IsAnimationRunning => _activeRun is not null;

    internal double AnimationSeconds => _animationSeconds;

    internal IReadOnlyList<ParticleSeed> Particles => _particles;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        foreach (var particle in _particles)
        {
            var visual = ParticleField.CalculateVisual(particle, size, _animationSeconds);
            if (visual.Opacity <= 0)
            {
                continue;
            }

            var opacityBucket = Math.Clamp(
                (int)Math.Round((visual.Opacity / MaximumParticleOpacity) * OpacityBucketCount),
                0,
                OpacityBucketCount);
            if (opacityBucket == 0)
            {
                continue;
            }

            var brushes = GetBrushes(visual.Tone, opacityBucket);
            var haloRadius = visual.Radius * 3.4d;
            context.DrawEllipse(
                brushes.Halo,
                null,
                visual.Center,
                haloRadius,
                haloRadius);
            context.DrawEllipse(
                brushes.Core,
                null,
                visual.Center,
                visual.Radius,
                visual.Radius);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _topLevel = TopLevel.GetTopLevel(this);
        _hostWindow = _topLevel as WindowBase;
        _isHostActive = _animationScheduler.IsHostActive(_hostWindow);
        if (_hostWindow is not null)
        {
            _hostWindow.Activated += HostWindow_OnActivated;
            _hostWindow.Deactivated += HostWindow_OnDeactivated;
            _hostWindow.PropertyChanged += HostWindow_OnPropertyChanged;
        }

        UpdateAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _isHostActive = false;
        StopAnimation();
        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= HostWindow_OnActivated;
            _hostWindow.Deactivated -= HostWindow_OnDeactivated;
            _hostWindow.PropertyChanged -= HostWindow_OnPropertyChanged;
        }

        _hostWindow = null;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ParticleCountProperty || change.Property == SeedProperty)
        {
            RebuildParticles();
            InvalidateVisual();
        }
        else if (change.Property == IsAnimationEnabledProperty
                 || change.Property == IsPausedProperty
                 || change.Property == IsVisibleProperty)
        {
            UpdateAnimation();
        }
    }

    private static ParticleBrushes[] CreateBrushes(uint rgb, double haloOpacityRatio)
    {
        var brushes = new ParticleBrushes[OpacityBucketCount + 1];
        for (var index = 0; index < brushes.Length; index++)
        {
            var opacity = MaximumParticleOpacity * index / OpacityBucketCount;
            brushes[index] = new ParticleBrushes(
                CreateBrush(rgb, opacity * haloOpacityRatio),
                CreateBrush(rgb, opacity));
        }

        return brushes;
    }

    private static IBrush CreateBrush(uint rgb, double opacity)
    {
        var alpha = (uint)Math.Clamp((int)Math.Round(opacity * byte.MaxValue), 0, byte.MaxValue);
        return new ImmutableSolidColorBrush((alpha << 24) | rgb);
    }

    private static ParticleBrushes GetBrushes(ParticleTone tone, int opacityBucket) => tone switch
    {
        ParticleTone.PaleCyan => CyanBrushes[opacityBucket],
        ParticleTone.MutedGold => GoldBrushes[opacityBucket],
        _ => BlueBrushes[opacityBucket]
    };

    private void RebuildParticles()
    {
        var count = Math.Clamp(ParticleCount, 0, MaximumParticleCount);
        _particles = ParticleField.CreateParticles(count, Seed);
    }

    private void HostWindow_OnActivated(object? sender, EventArgs e)
    {
        _isHostActive = true;
        UpdateAnimation();
    }

    private void HostWindow_OnDeactivated(object? sender, EventArgs e)
    {
        _isHostActive = false;
        UpdateAnimation();
    }

    private void HostWindow_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        {
            UpdateAnimation();
        }
    }

    private bool ShouldAnimate() =>
        _isAttached
        && _topLevel is not null
        && IsAnimationEnabled
        && !IsPaused
        && IsEffectivelyVisible
        && (_hostWindow is null || _isHostActive)
        && _hostWindow is not Window { WindowState: WindowState.Minimized };

    private void UpdateAnimation()
    {
        if (ShouldAnimate())
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_activeRun is not null || _topLevel is null)
        {
            return;
        }

        var run = new AnimationRun(this);
        _activeRun = run;
        _animationScheduler.RequestAnimationFrame(_topLevel, run.Callback);
    }

    private void StopAnimation() => _activeRun = null;

    private void OnAnimationFrame(AnimationRun run, TimeSpan timestamp)
    {
        if (_activeRun != run || !ShouldAnimate() || _topLevel is null)
        {
            if (_activeRun == run)
            {
                StopAnimation();
            }

            return;
        }

        if (run.PreviousTimestamp is { } previousTimestamp)
        {
            var frameSeconds = Math.Clamp(
                (timestamp - previousTimestamp).TotalSeconds,
                0,
                MaximumFrameGapSeconds);
            run.AccumulatedSeconds += frameSeconds;
            if (run.AccumulatedSeconds >= TargetFrameSeconds)
            {
                _animationSeconds += run.AccumulatedSeconds;
                run.AccumulatedSeconds = 0;
                InvalidateVisual();
            }
        }

        run.PreviousTimestamp = timestamp;
        _animationScheduler.RequestAnimationFrame(_topLevel, run.Callback);
    }

    private readonly record struct ParticleBrushes(IBrush Halo, IBrush Core);

    private sealed class AnimationRun
    {
        private readonly ParticleBackground _owner;

        public AnimationRun(ParticleBackground owner)
        {
            _owner = owner;
            Callback = OnFrame;
        }

        public Action<TimeSpan> Callback { get; }

        public TimeSpan? PreviousTimestamp { get; set; }

        public double AccumulatedSeconds { get; set; }

        private void OnFrame(TimeSpan timestamp) => _owner.OnAnimationFrame(this, timestamp);
    }
}

internal interface IParticleAnimationFrameScheduler
{
    bool IsHostActive(WindowBase? hostWindow);

    void RequestAnimationFrame(TopLevel topLevel, Action<TimeSpan> callback);
}

internal sealed class AvaloniaParticleAnimationFrameScheduler : IParticleAnimationFrameScheduler
{
    public static readonly AvaloniaParticleAnimationFrameScheduler Instance = new();

    private AvaloniaParticleAnimationFrameScheduler()
    {
    }

    public bool IsHostActive(WindowBase? hostWindow) => hostWindow?.IsActive ?? true;

    public void RequestAnimationFrame(TopLevel topLevel, Action<TimeSpan> callback) =>
        topLevel.RequestAnimationFrame(callback);
}

internal static class ParticleField
{
    private const double OffscreenMargin = 28d;
    private const double Tau = Math.PI * 2d;

    public static ParticleSeed[] CreateParticles(int count, int seed)
    {
        var particles = new ParticleSeed[count];
        var random = new XorShift32(unchecked((uint)seed));
        for (var index = 0; index < particles.Length; index++)
        {
            var toneRoll = random.NextUnit();
            var tone = toneRoll switch
            {
                < 0.70d => ParticleTone.ArcaneBlue,
                < 0.92d => ParticleTone.PaleCyan,
                _ => ParticleTone.MutedGold
            };
            particles[index] = new ParticleSeed(
                random.NextUnit(),
                random.NextUnit(),
                0.8d + random.NextUnit(),
                4d + (random.NextUnit() * 7d),
                5d + (random.NextUnit() * 11d),
                9d + (random.NextUnit() * 9d),
                4d + (random.NextUnit() * 5d),
                random.NextUnit() * Tau,
                0.14d + (random.NextUnit() * 0.16d),
                tone);
        }

        return particles;
    }

    public static ParticleVisual CalculateVisual(
        ParticleSeed particle,
        Size size,
        double animationSeconds)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return new ParticleVisual(default, particle.Radius, 0, particle.Tone);
        }

        var verticalSpan = size.Height + (OffscreenMargin * 2d);
        var initialY = (particle.NormalizedY * verticalSpan) - OffscreenMargin;
        var y = PositiveModulo(
            initialY - (animationSeconds * particle.RiseSpeed) + OffscreenMargin,
            verticalSpan) - OffscreenMargin;
        var sway = Math.Sin(
            ((animationSeconds / particle.SwayPeriod) * Tau) + particle.Phase)
            * particle.SwayAmplitude;
        var x = (particle.NormalizedX * size.Width) + sway;
        var fadeDistance = Math.Max(1d, size.Height * 0.1d);
        var topFade = Math.Clamp((y + OffscreenMargin) / (fadeDistance + OffscreenMargin), 0, 1);
        var bottomFade = Math.Clamp(
            (size.Height + OffscreenMargin - y) / (fadeDistance + OffscreenMargin),
            0,
            1);
        var edgeFade = Math.Min(topFade, bottomFade);
        var twinkle = 0.72d + (0.28d * ((Math.Sin(
            ((animationSeconds / particle.TwinklePeriod) * Tau) + (particle.Phase * 1.618d)) + 1d) / 2d));
        var opacity = particle.BaseOpacity * edgeFade * twinkle;
        return new ParticleVisual(new Point(x, y), particle.Radius, opacity, particle.Tone);
    }

    private static double PositiveModulo(double value, double divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private struct XorShift32(uint seed)
    {
        private uint _state = seed == 0 ? 0xA341316Cu : seed;

        public double NextUnit()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return (value >> 8) * (1d / 16_777_216d);
        }
    }
}

internal enum ParticleTone
{
    ArcaneBlue,
    PaleCyan,
    MutedGold
}

internal readonly record struct ParticleSeed(
    double NormalizedX,
    double NormalizedY,
    double Radius,
    double RiseSpeed,
    double SwayAmplitude,
    double SwayPeriod,
    double TwinklePeriod,
    double Phase,
    double BaseOpacity,
    ParticleTone Tone);

internal readonly record struct ParticleVisual(
    Point Center,
    double Radius,
    double Opacity,
    ParticleTone Tone);
