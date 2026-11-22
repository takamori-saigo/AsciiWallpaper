using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using AsciiWallpaper.Engine;
using SkiaSharp;

namespace AsciiWallpaper.Controls;

/// <summary>Контрол, который крутит AsciiEngine и рисует его напрямую через Skia (на GPU).</summary>
public sealed class AsciiCanvas : Control
{
    public AsciiEngine Engine { get; }
    readonly Settings _settings;
    readonly ConcurrentBag<Frame> _pool = new();
    readonly Stopwatch _clock = new();
    TopLevel? _top;
    double _lastTick;
    bool _running;

    public static bool Paused { get; set; }

    public AsciiCanvas(Settings settings, AsciiEngine engine)
    {
        _settings = settings;
        Engine = engine;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _running = true;
        _clock.Restart();
        _lastTick = 0;
        _top?.RequestAnimationFrame(OnFrame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        base.OnDetachedFromVisualTree(e);
    }

    void OnFrame(TimeSpan _)
    {
        if (!_running) return;

        double now = _clock.Elapsed.TotalSeconds;
        double minStep = 1.0 / Math.Clamp(_settings.Fps, 10, 120) - 0.002;
        if (now - _lastTick >= minStep)
        {
            float dt = (float)(now - _lastTick);
            _lastTick = now;
            if (!Paused)
            {
                var size = Bounds.Size;
                Engine.SetViewport((float)size.Width, (float)size.Height, (float)(_top?.RenderScaling ?? 2.0));
                Engine.Update(dt);
                InvalidateVisual();
            }
        }
        _top?.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext context)
    {
        if (!_pool.TryTake(out var frame)) frame = new Frame();
        Engine.Fill(frame);
        context.Custom(new AtlasDrawOperation(new Rect(Bounds.Size), frame, _pool));
    }

    sealed class AtlasDrawOperation : ICustomDrawOperation
    {
        readonly Frame _frame;
        readonly ConcurrentBag<Frame> _pool;

        public AtlasDrawOperation(Rect bounds, Frame frame, ConcurrentBag<Frame> pool)
        {
            Bounds = bounds;
            _frame = frame;
            _pool = pool;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => _pool.Add(_frame);

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (lease == null) return;
            using var l = lease.Lease();
            var canvas = l.SkCanvas;

            canvas.Save();
            canvas.ClipRect(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));
            canvas.Clear(SKColors.Black);

            var f = _frame;
            if (f.Atlas != null && f.Count > 0)
            {
                if (f.Glow)
                {
                    // размытая копия поверх с аддитивным смешиванием = свечение
                    using var glow = new SKPaint
                    {
                        ImageFilter = SKImageFilter.CreateBlur(f.GlowRadius, f.GlowRadius),
                        BlendMode = SKBlendMode.Plus,
                        Color = new SKColor(255, 255, 255, 200),
                    };
                    using var glowSprites = new SKPaint { FilterQuality = SKFilterQuality.Low };
                    canvas.SaveLayer(glow);
                    canvas.DrawAtlas(f.Atlas, f.Sprites, f.Xforms, f.Colors, SKBlendMode.Modulate, glowSprites);
                    canvas.Restore();
                }

                using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
                canvas.DrawAtlas(f.Atlas, f.Sprites, f.Xforms, f.Colors, SKBlendMode.Modulate, paint);
            }
            canvas.Restore();
        }
    }
}
