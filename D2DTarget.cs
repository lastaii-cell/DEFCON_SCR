using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

using Color = System.Drawing.Color;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using DwFactory = Vortice.DirectWrite.IDWriteFactory;

namespace DefconSaver;

/// <summary>
/// Hardware rendering through Direct2D. Same picture as <see cref="GdiTarget"/>, drawn by the
/// GPU: the vector work is tessellated and anti-aliased on the card, and the glow upscale that
/// dominated the software renderer becomes a single filtered blit.
/// </summary>
public sealed class D2DTarget : IDrawTarget
{
    private readonly Settings _cfg;
    private readonly IntPtr _hwnd;
    private readonly bool _minimal;

    private ID2D1Factory1 _factory;
    private DwFactory _dwrite;
    private ID2D1HwndRenderTarget _rt;
    private ID2D1BitmapRenderTarget _glowRt;
    private ID2D1Bitmap _glowBitmap;
    private ID2D1RenderTarget _current;
    private ID2D1SolidColorBrush _brush;
    private IDWriteTextFormat[] _formats;
    private float[] _fontSizes;

    private ID2D1RadialGradientBrush _sky, _ocean;
    private float _bgCx = float.NaN, _bgCy, _bgRadius;

    private int _w, _h;
    private float _ui = 1f;
    private bool _lost;

    // One pending stroke batch, so a run of same-coloured polylines becomes one geometry
    // instead of one per call. Flushed whenever the colour, the width or the primitive changes,
    // which keeps everything in the order the scene asked for.
    private Vector2[] _batchPoints = new Vector2[8192];
    private readonly List<int> _batchRuns = new();
    private int _batchN;
    private Color _batchColour;
    private float _batchWidth;
    private bool _batchOpen;

    private const float GlowScaleValue = 0.25f;

    private D2DTarget(Settings cfg, IntPtr hwnd, bool minimal)
    {
        _cfg = cfg;
        _hwnd = hwnd;
        _minimal = minimal;
    }

    public string Backend => "Direct2D";
    public int Width => _w;
    public int Height => _h;
    public float Ui => _ui;
    public bool CanDrawOffThread => false;
    public bool WantsGlowLayer => Bloom > 0 && _glowRt != null;
    public float GlowScale => GlowScaleValue;

    private int Bloom => _minimal ? 0 : _cfg.Bloom;

    /// <summary>Builds a Direct2D target, or returns null if the machine cannot provide one.</summary>
    public static D2DTarget TryCreate(Settings cfg, IntPtr hwnd, bool minimal, int width, int height,
                                      out string error)
    {
        error = null;
        var target = new D2DTarget(cfg, hwnd, minimal);
        try
        {
            target._factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            target._dwrite = DWrite.DWriteCreateFactory<DwFactory>();
            if (!target.Resize(width, height)) throw new InvalidOperationException("render target not created");
            return target;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            target.Dispose();
            return null;
        }
    }

    public bool Resize(int width, int height)
    {
        int nw = Math.Max(16, width), nh = Math.Max(16, height);
        if (!_lost && _rt != null && nw == _w && nh == _h) return true;

        _w = Math.Max(16, width);
        _h = Math.Max(16, height);
        _ui = Math.Clamp(Math.Min(_w, _h) / 1080f, 0.42f, 2.4f);

        ReleaseSizedResources();

        // 96 dpi explicitly: the scene works in pixels, and letting Direct2D inherit the
        // desktop scaling would quietly stretch every coordinate.
        var rtProps = new RenderTargetProperties(
            RenderTargetType.Default,
            new D2DPixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore),
            96f, 96f, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);

        var hwndProps = new HwndRenderTargetProperties
        {
            Hwnd = _hwnd,
            PixelSize = new SizeI(_w, _h),
            PresentOptions = PresentOptions.None,
        };

        _rt = _factory.CreateHwndRenderTarget(rtProps, hwndProps);
        _rt.AntialiasMode = AntialiasMode.PerPrimitive;
        _current = _rt;
        _brush = _rt.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));

        if (Bloom > 0)
        {
            int gw = Math.Max(8, (int)(_w * GlowScaleValue));
            int gh = Math.Max(8, (int)(_h * GlowScaleValue));
            _glowRt = _rt.CreateCompatibleRenderTarget(
                new SizeI(gw, gh),
                new SizeI(gw, gh),
                new D2DPixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied));
            _glowRt.AntialiasMode = AntialiasMode.PerPrimitive;
            _glowBitmap = _glowRt.Bitmap;
        }

        _fontSizes = new[]
        {
            120f * _ui, 21f * _ui, 14f * _ui, 11f * _ui, 120f * _ui * GlowScaleValue,
        };
        _formats = new IDWriteTextFormat[_fontSizes.Length];
        for (int i = 0; i < _fontSizes.Length; i++)
        {
            _formats[i] = _dwrite.CreateTextFormat(
                "Consolas", null,
                i is 0 or 1 or 4 ? Vortice.DirectWrite.FontWeight.Bold
                                 : Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                _fontSizes[i]);
            _formats[i].WordWrapping = WordWrapping.NoWrap;
        }

        _lost = false;
        _bgCx = float.NaN;
        return true;
    }

    // ---- frame -------------------------------------------------------------

    public void BeginFrame()
    {
        if (_lost) Resize(_w, _h);
        _current = _rt;
        _rt.BeginDraw();
    }

    public void Backdrop(float cx, float cy, float radius)
    {
        if (float.IsNaN(_bgCx) || cx != _bgCx || cy != _bgCy || radius != _bgRadius)
        {
            _bgCx = cx; _bgCy = cy; _bgRadius = radius;
            BuildBackdropBrushes(cx, cy, radius);
        }

        _rt.Clear(ToColor4(Palette.SpaceOuter));
        _rt.FillRectangle(new Rect(0f, 0f, _w, _h), _sky);
        _rt.FillEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), _ocean);
    }

    private void BuildBackdropBrushes(float cx, float cy, float radius)
    {
        _sky?.Dispose();
        _ocean?.Dispose();

        using (ID2D1GradientStopCollection stops = _rt.CreateGradientStopCollection(new[]
        {
            new GradientStop { Position = 0f, Color = ToColor4(Palette.SpaceInner) },
            new GradientStop { Position = 1f, Color = ToColor4(Palette.SpaceOuter) },
        }))
        {
            float sr = MathF.Max(_w, _h);
            _sky = _rt.CreateRadialGradientBrush(
                new RadialGradientBrushProperties
                {
                    Center = new Vector2(cx, cy),
                    GradientOriginOffset = Vector2.Zero,
                    RadiusX = sr,
                    RadiusY = sr,
                }, stops);
        }

        using (ID2D1GradientStopCollection stops = _rt.CreateGradientStopCollection(new[]
        {
            new GradientStop { Position = 0f, Color = ToColor4(Palette.OceanCore) },
            new GradientStop { Position = 1f, Color = ToColor4(Palette.OceanEdge) },
        }))
        {
            // The offset origin is what gives the disc its lit-from-above-left look.
            _ocean = _rt.CreateRadialGradientBrush(
                new RadialGradientBrushProperties
                {
                    Center = new Vector2(cx, cy),
                    GradientOriginOffset = new Vector2(-radius * 0.22f, -radius * 0.26f),
                    RadiusX = radius,
                    RadiusY = radius,
                }, stops);
        }
    }

    public void BeginGlowLayer()
    {
        Flush();
        _glowRt.BeginDraw();
        _glowRt.Clear(new Color4(0f, 0f, 0f, 0f));
        _current = _glowRt;
    }

    public void EndGlowLayer(Rectangle box)
    {
        Flush();
        _glowRt.EndDraw();
        _current = _rt;

        var dest = new Rect(box.X, box.Y, box.Width, box.Height);
        var src = new Rect(box.X * GlowScaleValue, box.Y * GlowScaleValue,
                           box.Width * GlowScaleValue, box.Height * GlowScaleValue);
        _rt.DrawBitmap(_glowBitmap, dest, 1f, BitmapInterpolationMode.Linear, src);
    }

    public void Scanlines(Rectangle box)
    {
        Flush();
        _brush.Color = new Color4(0f, 0f, 0f, 46f / 255f);
        for (int y = box.Y; y < box.Bottom; y += 3)
            _rt.FillRectangle(new Rect(box.X, y, box.Width, 1f), _brush);
    }

    public void Invalidate() => _lost = true;

    public void Present() { /* EndDraw already presented */ }

    public void EndFrame()
    {
        Flush();
        try
        {
            _rt.EndDraw();
        }
        catch (Exception)
        {
            // Device lost, most often a display mode change. Rebuild on the next frame.
            _lost = true;
        }
    }

    // ---- primitives --------------------------------------------------------

    public void Polyline(PointF[] points, int count, Color colour, float width)
    {
        if (count < 2) return;
        if (_batchOpen && (_batchColour != colour || _batchWidth != width)) Flush();

        if (_batchN + count > _batchPoints.Length)
            Array.Resize(ref _batchPoints, Math.Max(_batchN + count, _batchPoints.Length * 2));

        for (int i = 0; i < count; i++)
            _batchPoints[_batchN + i] = new Vector2(points[i].X, points[i].Y);

        _batchN += count;
        _batchRuns.Add(count);
        _batchColour = colour;
        _batchWidth = width;
        _batchOpen = true;
    }

    private void Flush()
    {
        if (!_batchOpen || _batchRuns.Count == 0)
        {
            _batchOpen = false;
            _batchN = 0;
            _batchRuns.Clear();
            return;
        }

        using ID2D1PathGeometry geometry = _factory.CreatePathGeometry();
        using (ID2D1GeometrySink sink = geometry.Open())
        {
            int offset = 0;
            foreach (int run in _batchRuns)
            {
                // AddLines wants an exactly sized array, so lengths are cached and reused
                // rather than allocated per run.
                Vector2[] tail = LineBuf(run - 1);
                Array.Copy(_batchPoints, offset + 1, tail, 0, run - 1);
                sink.BeginFigure(_batchPoints[offset], FigureBegin.Hollow);
                sink.AddLines(tail);
                sink.EndFigure(FigureEnd.Open);
                offset += run;
            }
            sink.Close();
        }

        _brush.Color = ToColor4(_batchColour);
        _current.DrawGeometry(geometry, _brush, _batchWidth);

        _batchOpen = false;
        _batchN = 0;
        _batchRuns.Clear();
    }

    public void Polygon(PointF[] points, int count, Color colour)
    {
        if (count < 3) return;
        Flush();

        using ID2D1PathGeometry geometry = _factory.CreatePathGeometry();
        using (ID2D1GeometrySink sink = geometry.Open())
        {
            sink.SetFillMode(FillMode.Winding);
            Vector2[] buf = LineBuf(count - 1);
            for (int i = 1; i < count; i++) buf[i - 1] = new Vector2(points[i].X, points[i].Y);
            sink.BeginFigure(new Vector2(points[0].X, points[0].Y), FigureBegin.Filled);
            sink.AddLines(buf);
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }

        _brush.Color = ToColor4(colour);
        _current.FillGeometry(geometry, _brush);
    }

    public void Line(float x0, float y0, float x1, float y1, Color colour, float width)
    {
        Flush();
        _brush.Color = ToColor4(colour);
        _current.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), _brush, width);
    }

    public void Ellipse(float cx, float cy, float rx, float ry, Color colour, float width)
    {
        Flush();
        _brush.Color = ToColor4(colour);
        _current.DrawEllipse(new Ellipse(new Vector2(cx, cy), rx, ry), _brush, width);
    }

    public void FillEllipse(float cx, float cy, float rx, float ry, Color colour)
    {
        Flush();
        _brush.Color = ToColor4(colour);
        _current.FillEllipse(new Ellipse(new Vector2(cx, cy), rx, ry), _brush);
    }

    public void FillRect(RectangleF rect, Color colour)
    {
        Flush();
        _brush.Color = ToColor4(colour);
        _current.FillRectangle(ToRect(rect), _brush);
    }

    public void DrawRect(RectangleF rect, Color colour, float width)
    {
        Flush();
        _brush.Color = ToColor4(colour);
        _current.DrawRectangle(ToRect(rect), _brush, width);
    }

    public void Text(string text, FontSlot slot, Color colour, float x, float y)
        => TextIn(text, slot, colour, new RectangleF(x, y, _w, _fontSizes[(int)slot] * 2f), TextAlign.Left);

    public void TextIn(string text, FontSlot slot, Color colour, RectangleF box, TextAlign align)
    {
        if (string.IsNullOrEmpty(text)) return;
        Flush();

        IDWriteTextFormat format = _formats[(int)slot];
        format.TextAlignment = align switch
        {
            TextAlign.Right => Vortice.DirectWrite.TextAlignment.Trailing,
            TextAlign.Centre => Vortice.DirectWrite.TextAlignment.Center,
            _ => Vortice.DirectWrite.TextAlignment.Leading,
        };
        format.ParagraphAlignment = align == TextAlign.Centre
            ? ParagraphAlignment.Center
            : ParagraphAlignment.Near;

        _brush.Color = ToColor4(colour);
        _current.DrawText(text, format, ToRect(box), _brush);
    }

    public float FontSize(FontSlot slot) => _fontSizes[(int)slot];

    // ---- conversions -------------------------------------------------------

    private readonly Vector2[][] _lineCache = new Vector2[8192][];

    private Vector2[] LineBuf(int n)
        => n < _lineCache.Length ? _lineCache[n] ??= new Vector2[n] : new Vector2[n];

    private static Color4 ToColor4(Color c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private static Rect ToRect(RectangleF r) => new(r.X, r.Y, r.Width, r.Height);

    // ---- teardown ----------------------------------------------------------

    private void ReleaseSizedResources()
    {
        _sky?.Dispose(); _sky = null;
        _ocean?.Dispose(); _ocean = null;
        if (_formats != null) foreach (IDWriteTextFormat f in _formats) f?.Dispose();
        _formats = null;
        _glowBitmap?.Dispose(); _glowBitmap = null;
        _glowRt?.Dispose(); _glowRt = null;
        _brush?.Dispose(); _brush = null;
        _rt?.Dispose(); _rt = null;
        _current = null;
    }

    public void Dispose()
    {
        ReleaseSizedResources();
        _dwrite?.Dispose(); _dwrite = null;
        _factory?.Dispose(); _factory = null;
    }
}
