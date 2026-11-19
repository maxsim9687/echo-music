using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Echo.UI;

// Soft spots that drift, push away from the pointer and swell while music plays (web fx.js). One look per theme (CFG).
public sealed class Spots : Grid
{
    sealed record Cfg(int N, float Min, float Max, float A, bool Hard = false, bool Stretch = false, bool Ring = false, bool Orbit = false);
    static readonly Dictionary<int, Cfg> CFG = new()
    {
        [1] = new(8, 160, 360, 0.17f), [2] = new(7, 180, 400, 0.11f), [3] = new(11, 50, 150, 0.95f, Hard: true),
        [4] = new(9, 140, 320, 0.15f), [5] = new(26, 24, 90, 0.24f), [6] = new(9, 120, 280, 0.18f, Stretch: true),
        [7] = new(42, 10, 46, 0.38f), [8] = new(7, 120, 300, 0.5f, Ring: true), [9] = new(9, 140, 300, 0.15f, Orbit: true),
        [10] = new(9, 200, 420, 0.24f),
    };

    sealed class Pt { public float hx, hy, x, y, vx, vy, r, ph, sp, amp, ang, rad; }

    readonly CanvasAnimatedControl? anim;
    readonly CanvasControl? still;
    readonly Random rnd = new();
    readonly object gate = new();
    List<Pt> pts = new();
    Cfg cfg = CFG[10];
    Color col = Colors.White;
    float t, en = 0.25f;
    volatile float target = 0.25f, mx = -9999, my = -9999, cx = -1, cy = -1, W = 1300, H = 800;
    float fx = -9999, fy = -9999;
    volatile bool reseed = true;
    CanvasRadialGradientBrush? brush;
    Color brushColor;
    public bool ReduceMotion { get; }

    public Spots()
    {
        IsHitTestVisible = false;
        try { ReduceMotion = !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; } catch { }
        if (ReduceMotion)
        {
            still = new CanvasControl { ClearColor = Colors.Transparent };
            still.Draw += (s, e) => { lock (gate) Render(e.DrawingSession, 0); };
            Children.Add(still);
        }
        else
        {
            anim = new CanvasAnimatedControl { ClearColor = Colors.Transparent };
            anim.Draw += (s, e) =>
            {
                var dt = (float)Math.Min(0.05, e.Timing.ElapsedTime.TotalSeconds);
                lock (gate) Render(e.DrawingSession, dt);
            };
            Children.Add(anim);
        }
        SizeChanged += (s, e) => { W = (float)Math.Max(1, e.NewSize.Width); H = (float)Math.Max(1, e.NewSize.Height); reseed = true; still?.Invalidate(); };
    }

    public void SetTheme(int v)
    {
        lock (gate) { cfg = CFG.TryGetValue(v, out var c) ? c : CFG[10]; col = Theme.Particle; reseed = true; }
        still?.Invalidate();
    }

    public void Energy(bool playing) { target = playing ? 1f : 0.25f; still?.Invalidate(); }
    public void Pointer(float x, float y) { mx = x; my = y; }
    public void PointerGone() { mx = -9999; my = -9999; }
    public void Center(float x, float y) { cx = x; cy = y; }

    public void Shutdown()
    {
        try { anim?.RemoveFromVisualTree(); still?.RemoveFromVisualTree(); } catch { }
    }

    float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    void Seed()
    {
        var k = Math.Max(0.55f, Math.Min(1.2f, W / 1300f));
        var list = new List<Pt>();
        for (var i = 0; i < cfg.N; i++)
        {
            float hx = R(0, W), hy = R(0, H);
            list.Add(new Pt { hx = hx, hy = hy, x = hx, y = hy, r = R(cfg.Min, cfg.Max) * k, ph = R(0, 6.283f), sp = R(0.15f, 0.45f), amp = R(30, 120), ang = R(0, 6.283f), rad = R(120, Math.Max(W, H) * 0.55f) });
        }
        pts = list;
    }

    Color A(float a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), col.R, col.G, col.B);

    void Blob(CanvasDrawingSession ds, float px, float py, float r, float a, float sx = 1)
    {
        if (cfg.Hard) { ds.FillEllipse(px, py, r * 0.5f * sx, r * 0.5f, A(a)); return; }
        if (cfg.Ring)
        {
            ds.FillEllipse(px, py, r * 0.5f * sx, r * 0.5f, A(a * 0.08f));
            ds.DrawEllipse(px, py, r * 0.5f * sx, r * 0.5f, A(a * 0.35f), 1.5f);
            return;
        }
        if (brush == null || brushColor != col)
        {
            brush?.Dispose();
            var stops = new[]
            {
                new CanvasGradientStop { Position = 0, Color = A(1) },
                new CanvasGradientStop { Position = 0.45f, Color = A(0.45f) },
                new CanvasGradientStop { Position = 1, Color = A(0) },
            };
            brush = new CanvasRadialGradientBrush(ds, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied);
            brushColor = col;
        }
        brush.Center = new Vector2(px, py);
        brush.RadiusX = r * sx; brush.RadiusY = r;
        brush.Opacity = Math.Clamp(a, 0, 1);
        ds.FillEllipse(px, py, r * sx, r, brush);
    }

    void Render(CanvasDrawingSession ds, float dt)
    {
        if (reseed) { reseed = false; Seed(); }
        t += dt; en += (target - en) * (dt > 0 ? 0.04f : 1f);
        var beat = en * (0.6f + 0.4f * MathF.Abs(MathF.Sin(t * 2.2f)));
        float ocx = cx >= 0 ? cx : W / 2, ocy = cy >= 0 ? cy : H * 0.4f;
        float pmx = mx, pmy = my;
        foreach (var p in pts)
        {
            float tx, ty;
            if (cfg.Orbit) { p.ang += dt * 0.08f * (1 + beat * 2); tx = ocx + MathF.Cos(p.ang) * p.rad; ty = ocy + MathF.Sin(p.ang) * p.rad * 0.45f; }
            else { tx = p.hx + MathF.Sin(t * p.sp + p.ph) * p.amp; ty = p.hy + MathF.Cos(t * p.sp * 0.8f + p.ph) * p.amp; }
            if (dt > 0)
            {
                float dx = p.x - pmx, dy = p.y - pmy, d = MathF.Sqrt(dx * dx + dy * dy), Rr = p.r * 1.1f + 140;
                if (d < Rr && d > 0) { var f = (1 - d / Rr) * 9; p.vx += dx / d * f; p.vy += dy / d * f; }
                p.vx += (tx - p.x) * 0.012f; p.vy += (ty - p.y) * 0.012f; p.vx *= 0.88f; p.vy *= 0.88f; p.x += p.vx; p.y += p.vy;
            }
            var sw = 1 + beat * 0.28f * MathF.Sin(t * 3 + p.ph);
            Blob(ds, p.x, p.y, p.r * sw, cfg.A * (0.75f + beat * 0.5f), cfg.Stretch ? 1.6f + beat * 0.8f : 1);
        }
        if (pmx > -999 && dt > 0)
        {
            if (fx < -999) { fx = pmx; fy = pmy; }
            fx += (pmx - fx) * 0.08f; fy += (pmy - fy) * 0.08f;
            if (!cfg.Hard && !cfg.Ring) Blob(ds, fx, fy, 180, cfg.A * 0.9f);
        }
    }
}
