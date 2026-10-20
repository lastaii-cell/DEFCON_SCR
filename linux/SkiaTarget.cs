using System.Drawing;
using SkiaSharp;

namespace DefconSaver;

/// <summary>
/// The drawing back end for Linux: the same IDrawTarget the Windows build implements twice,
/// over Skia instead of GDI+ or Direct2D. It renders to a CPU bitmap and lets the host decide
/// how that reaches a screen, which keeps every windowing concern out of this file.
/// </summary>
public sealed class SkiaTarget : IDrawTarget
{
    private const float GlowScaleValue = 0.25f;

    private readonly Settings _cfg;

    private int _w, _h;
    private float _ui = 1f;

    private SKBitmap _back, _glow, _backdrop;
    private SKCanvas _backCanvas, _glowCanvas, _canvas;
    private SKPaint _paint;
    private SKPaint[] _text;        // one per FontSlot; SkiaSharp 2.88 carries the font on the paint
    private float[] _fontSizes;

    public SkiaTarget(Settings cfg) => _cfg = cfg;

    public string Backend => "Skia";
    public int Width => _w;
    public int Height => _h;
    public float Ui => _ui;
    public bool CanDrawOffThread => true;
    public bool WantsGlowLayer => _cfg.Bloom > 0;
    public float GlowScale => GlowScaleValue;

    /// <summary>The finished frame, for the host to blit or save.</summary>
    public SKBitmap Frame => _back;

    // ---- lifecycle ---------------------------------------------------------

    public bool Resize(int width, int height)
    {
        if (width == _w && height == _h && _back != null) return true;
        DisposeBuffers();

        _w = Math.Max(16, width);
        _h = Math.Max(16, height);
        _ui = Math.Clamp(Math.Min(_w, _h) / 1080f, 0.42f, 2.4f);

        var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _back = new SKBitmap(info);
        _backCanvas = new SKCanvas(_back);
        _canvas = _backCanvas;

        if (_cfg.Bloom > 0)
        {
            int gw = Math.Max(8, (int)(_w * GlowScaleValue));
            int gh = Math.Max(8, (int)(_h * GlowScaleValue));
            _glow = new SKBitmap(new SKImageInfo(gw, gh, SKColorType.Bgra8888, SKAlphaType.Premul));
            _glowCanvas = new SKCanvas(_glow);
        }

        _paint = new SKPaint
        {
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        SKTypeface mono = FindMono();
        _fontSizes = new[] { 120f * _ui, 21f * _ui, 14f * _ui, 11f * _ui, 120f * _ui * GlowScaleValue };
        _text = new SKPaint[_fontSizes.Length];
        for (int i = 0; i < _text.Length; i++)
            _text[i] = new SKPaint
            {
                Typeface = mono,
                TextSize = _fontSizes[i],
                IsAntialias = true,
                SubpixelText = true,
                Style = SKPaintStyle.Fill,
            };

        return true;
    }

    /// <summary>
    /// Consolas does not exist on a Linux box, and the readout is laid out from font metrics,
    /// so the substitute has to be monospaced or the columns stop lining up. Asks the font
    /// manager for each by name and takes the first that is actually installed, rather than
    /// trusting a generic alias to resolve to something fixed-width.
    /// </summary>
    private static SKTypeface FindMono()
    {
        string[] names =
        {
            "Consolas", "CaskaydiaMono Nerd Font", "JetBrains Mono", "DejaVu Sans Mono",
            "Liberation Mono", "Noto Sans Mono", "Ubuntu Mono", "monospace",
        };
        foreach (string n in names)
        {
            SKTypeface t = SKFontManager.Default.MatchFamily(n, SKFontStyle.Bold);
            if (t != null) return t;
        }
        return SKTypeface.Default;
    }

    public void BeginFrame() => _canvas = _backCanvas;

    public void EndFrame() { }

    public void Present() { }

    public void Invalidate()
    {
        _backdrop?.Dispose();
        _backdrop = null;
        DisposeBuffers();
    }

    // ---- layers ------------------------------------------------------------

    public void Backdrop(float cx, float cy, float radius)
    {
        if (_backdrop == null) BuildBackdrop(cx, cy, radius);
        _backCanvas.DrawBitmap(_backdrop, 0, 0);
    }

    private void BuildBackdrop(float cx, float cy, float radius)
    {
        _backdrop = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(_backdrop);
        c.Clear(Sk(Palette.SpaceOuter));

        float sr = Math.Max(_w, _h);
        using (var sky = new SKPaint { IsAntialias = true })
        {
            sky.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), sr,
                new[] { Sk(Palette.SpaceInner), Sk(Palette.SpaceOuter) },
                null, SKShaderTileMode.Clamp);
            c.DrawRect(SKRect.Create(0, 0, _w, _h), sky);
        }

        using (var disc = new SKPaint { IsAntialias = true })
        {
            disc.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx - radius * 0.22f, cy - radius * 0.26f), radius * 1.45f,
                new[] { Sk(Palette.OceanCore), Sk(Palette.OceanEdge) },
                null, SKShaderTileMode.Clamp);
            c.DrawCircle(cx, cy, radius, disc);
        }
    }

    public void BeginGlowLayer()
    {
        _glowCanvas.Clear(SKColors.Transparent);
        _canvas = _glowCanvas;
    }

    public void EndGlowLayer(Rectangle box)
    {
        _canvas = _backCanvas;
        if (_glow == null) return;

        // Skia has a real gaussian, so the glow does not need the downscale-then-upscale
        // trick the GDI+ path uses to fake one.
        float sigma = (_cfg.Bloom > 1 ? 7f : 3.5f) * _ui;
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };

        var src = SKRect.Create(box.X * GlowScaleValue, box.Y * GlowScaleValue,
                                box.Width * GlowScaleValue, box.Height * GlowScaleValue);
        var dst = SKRect.Create(box.X, box.Y, box.Width, box.Height);
        _backCanvas.DrawBitmap(_glow, src, dst, paint);
    }

    public void Scanlines(Rectangle box)
    {
        using var p = new SKPaint { Color = new SKColor(0, 0, 0, 46), StrokeWidth = 1f, IsAntialias = false };
        for (int y = box.Y; y < box.Y + box.Height; y += 3)
            _backCanvas.DrawLine(box.X, y + 0.5f, box.X + box.Width, y + 0.5f, p);
    }

    // ---- primitives --------------------------------------------------------

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

    private SKPaint Stroked(Color c, float width)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.Color = Sk(c);
        _paint.StrokeWidth = width;
        return _paint;
    }

    private SKPaint Filled(Color c)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Sk(c);
        return _paint;
    }

    public void Polyline(PointF[] points, int count, Color colour, float width)
    {
        if (count < 2) return;
        using var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < count; i++) path.LineTo(points[i].X, points[i].Y);
        _canvas.DrawPath(path, Stroked(colour, width));
    }

    public void Polygon(PointF[] points, int count, Color colour)
    {
        if (count < 3) return;
        using var path = new SKPath { FillType = SKPathFillType.Winding };
        path.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < count; i++) path.LineTo(points[i].X, points[i].Y);
        path.Close();
        _canvas.DrawPath(path, Filled(colour));
    }

    public void Line(float x0, float y0, float x1, float y1, Color colour, float width)
        => _canvas.DrawLine(x0, y0, x1, y1, Stroked(colour, width));

    public void Ellipse(float cx, float cy, float rx, float ry, Color colour, float width)
        => _canvas.DrawOval(cx, cy, rx, ry, Stroked(colour, width));

    public void FillEllipse(float cx, float cy, float rx, float ry, Color colour)
        => _canvas.DrawOval(cx, cy, rx, ry, Filled(colour));

    public void FillRect(RectangleF rect, Color colour)
        => _canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Filled(colour));

    public void DrawRect(RectangleF rect, Color colour, float width)
        => _canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Stroked(colour, width));

    // ---- text --------------------------------------------------------------

    public float FontSize(FontSlot slot) => _fontSizes[(int)slot];

    public void Text(string text, FontSlot slot, Color colour, float x, float y)
    {
        if (string.IsNullOrEmpty(text)) return;
        SKPaint p = _text[(int)slot];
        p.Color = Sk(colour);
        _canvas.DrawText(text, x, y - p.FontMetrics.Ascent, p);
    }

    public void TextIn(string text, FontSlot slot, Color colour, RectangleF box, TextAlign align)
    {
        if (string.IsNullOrEmpty(text)) return;
        SKPaint p = _text[(int)slot];
        p.Color = Sk(colour);
        SKFontMetrics m = p.FontMetrics;

        float x = align switch
        {
            TextAlign.Right => box.Right - p.MeasureText(text),
            TextAlign.Centre => box.Left + (box.Width - p.MeasureText(text)) * 0.5f,
            _ => box.Left,
        };

        // Matches both Windows targets: centred text is centred in its box, anything else
        // sits at the top of it.
        float baseline = align == TextAlign.Centre
            ? box.Top + (box.Height - (m.Descent - m.Ascent)) * 0.5f - m.Ascent
            : box.Top - m.Ascent;

        _canvas.DrawText(text, x, baseline, p);
    }

    // ---- teardown ----------------------------------------------------------

    private void DisposeBuffers()
    {
        _backCanvas?.Dispose(); _backCanvas = null;
        _glowCanvas?.Dispose(); _glowCanvas = null;
        _back?.Dispose(); _back = null;
        _glow?.Dispose(); _glow = null;
        _paint?.Dispose(); _paint = null;

        if (_text != null) foreach (SKPaint t in _text) t?.Dispose();
        _text = null;
    }

    public void Dispose()
    {
        DisposeBuffers();
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
