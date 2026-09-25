using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading.Tasks;

namespace DefconSaver;

/// <summary>
/// Software rendering with GDI+. Always available, never needs a usable GPU driver, and slower
/// than <see cref="D2DTarget"/>. Frames are built into an off-screen bitmap and blitted.
/// </summary>
public sealed class GdiTarget : IDrawTarget
{
    private readonly Settings _cfg;
    private readonly IntPtr _hwnd;
    private readonly bool _minimal;

    private int _w, _h;
    private float _ui = 1f;

    private Bitmap _back, _glow, _glowHalf, _glowUp, _bgFull, _scan;
    private Graphics _gBack, _gGlow, _gGlowHalf, _gGlowUp, _g;
    private Graphics _present;

    private RectangleF _glowSrc;
    private Rectangle _glowBox;
    private int[] _ux0, _ux1, _uwx;

    private Pen _pen;
    private SolidBrush _brush;
    private Font[] _fonts;
    private StringFormat _sfLeft, _sfRight, _sfCentre;

    private const float GlowScaleValue = 0.25f;

    public GdiTarget(Settings cfg, IntPtr hwnd, bool minimal)
    {
        _cfg = cfg;
        _hwnd = hwnd;
        _minimal = minimal;
    }

    public string Backend => "GDI+";
    public int Width => _w;
    public int Height => _h;
    public float Ui => _ui;
    public bool CanDrawOffThread => true;
    public bool WantsGlowLayer => Bloom > 0;
    public float GlowScale => GlowScaleValue;

    private int Bloom => _minimal ? 0 : _cfg.Bloom;

    public bool Resize(int width, int height)
    {
        if (width == _w && height == _h && _back != null) return true;
        DisposeBuffers();

        _w = Math.Max(16, width);
        _h = Math.Max(16, height);
        _ui = Math.Clamp(Math.Min(_w, _h) / 1080f, 0.42f, 2.4f);

        _back = new Bitmap(_w, _h, PixelFormat.Format32bppPArgb);
        _gBack = Graphics.FromImage(_back);
        _gBack.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _g = _gBack;

        if (Bloom > 0)
        {
            int gw = Math.Max(8, (int)(_w * GlowScaleValue));
            int gh = Math.Max(8, (int)(_h * GlowScaleValue));
            _glow = new Bitmap(gw, gh, PixelFormat.Format32bppPArgb);
            _gGlow = Graphics.FromImage(_glow);
            if (Bloom > 1)
            {
                _glowHalf = new Bitmap(Math.Max(4, gw / 2), Math.Max(4, gh / 2), PixelFormat.Format32bppPArgb);
                _gGlowHalf = Graphics.FromImage(_glowHalf);
                _gGlowHalf.InterpolationMode = InterpolationMode.Bilinear;
                _gGlowHalf.CompositingMode = CompositingMode.SourceCopy;

                _glowUp = new Bitmap(gw, gh, PixelFormat.Format32bppPArgb);
                _gGlowUp = Graphics.FromImage(_glowUp);
                _gGlowUp.InterpolationMode = InterpolationMode.Bilinear;
                _gGlowUp.PixelOffsetMode = PixelOffsetMode.Half;
            }
        }

        _pen = new Pen(Color.White, 1f) { LineJoin = LineJoin.Round };
        _brush = new SolidBrush(Color.White);
        _fonts = new[]
        {
            new Font("Consolas", 120f * _ui, FontStyle.Bold, GraphicsUnit.Pixel),
            new Font("Consolas", 21f * _ui, FontStyle.Bold, GraphicsUnit.Pixel),
            new Font("Consolas", 14f * _ui, FontStyle.Regular, GraphicsUnit.Pixel),
            new Font("Consolas", 11f * _ui, FontStyle.Regular, GraphicsUnit.Pixel),
            new Font("Consolas", 120f * _ui * GlowScaleValue, FontStyle.Bold, GraphicsUnit.Pixel),
        };
        _sfLeft ??= new StringFormat();
        _sfRight ??= new StringFormat { Alignment = StringAlignment.Far };
        _sfCentre ??= new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        _present?.Dispose();
        _present = null;
        return true;
    }

    // ---- frame -------------------------------------------------------------

    public void BeginFrame()
    {
        _g = _gBack;
        _g.SmoothingMode = SmoothingMode.AntiAlias;
        _g.CompositingQuality = CompositingQuality.HighSpeed;
    }

    public void Backdrop(float cx, float cy, float radius)
    {
        if (_bgFull == null) BuildBackdrop(cx, cy, radius);
        _gBack.CompositingMode = CompositingMode.SourceCopy;
        _gBack.DrawImageUnscaled(_bgFull, 0, 0);
        _gBack.CompositingMode = CompositingMode.SourceOver;
    }

    private void BuildBackdrop(float cx, float cy, float radius)
    {
        _bgFull = new Bitmap(_w, _h, PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(_bgFull);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.SpaceOuter);

        using (var sky = new GraphicsPath())
        {
            float sr = MathF.Max(_w, _h);
            sky.AddEllipse(cx - sr, cy - sr, sr * 2f, sr * 2f);
            using var b = new PathGradientBrush(sky)
            {
                CenterPoint = new PointF(cx, cy),
                CenterColor = Palette.SpaceInner,
                SurroundColors = new[] { Palette.SpaceOuter },
            };
            g.FillPath(b, sky);
        }

        using (var disc = new GraphicsPath())
        {
            disc.AddEllipse(cx - radius, cy - radius, radius * 2f, radius * 2f);
            using var b = new PathGradientBrush(disc)
            {
                CenterPoint = new PointF(cx - radius * 0.22f, cy - radius * 0.26f),
                CenterColor = Palette.OceanCore,
                SurroundColors = new[] { Palette.OceanEdge },
            };
            g.FillPath(b, disc);
        }
    }

    public void BeginGlowLayer()
    {
        _gGlow.Clear(Color.Transparent);
        _g = _gGlow;
        _g.SmoothingMode = SmoothingMode.None;
        _g.CompositingQuality = CompositingQuality.HighSpeed;
    }

    public void EndGlowLayer(Rectangle box)
    {
        _g = _gBack;
        _g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_glowBox != box || _ux0 == null || _ux0.Length != box.Width)
        {
            _glowBox = box;
            _glowSrc = new RectangleF(box.X * GlowScaleValue, box.Y * GlowScaleValue,
                                      box.Width * GlowScaleValue, box.Height * GlowScaleValue);
            BuildUpscaleTables(_glow.Width, _glowSrc, box.Width);
        }

        Bitmap source = _glow;
        if (Bloom > 1 && _glowHalf != null && _glowUp != null)
        {
            var glowRect = new RectangleF(0f, 0f, _glow.Width, _glow.Height);
            _gGlowHalf.DrawImage(_glow, new Rectangle(0, 0, _glowHalf.Width, _glowHalf.Height),
                                 0, 0, _glow.Width, _glow.Height, GraphicsUnit.Pixel);
            _gGlowUp.CompositingMode = CompositingMode.SourceCopy;
            _gGlowUp.DrawImage(_glowHalf, glowRect,
                               new RectangleF(0f, 0f, _glowHalf.Width, _glowHalf.Height),
                               GraphicsUnit.Pixel);
            _gGlowUp.CompositingMode = CompositingMode.SourceOver;
            _gGlowUp.DrawImage(_glow, glowRect, glowRect, GraphicsUnit.Pixel);
            source = _glowUp;
        }

        CompositeGlow(source, box);
    }

    public void Scanlines(Rectangle box)
    {
        if (_scan == null || _scan.Width != box.Width || _scan.Height != box.Height)
        {
            _scan?.Dispose();
            _scan = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppPArgb);
            using Graphics sg = Graphics.FromImage(_scan);
            sg.Clear(Color.Transparent);
            using var p = new Pen(Color.FromArgb(46, 0, 0, 0), 1f);
            for (int y = 0; y < box.Height; y += 3) sg.DrawLine(p, 0, y, box.Width, y);
        }
        _gBack.DrawImageUnscaled(_scan, box.X, box.Y);
    }

    public void Invalidate()
    {
        DisposeBuffers();
        _w = _h = 0;
    }

    public void EndFrame() { }

    public void Present()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            _present ??= Graphics.FromHwnd(_hwnd);
            _present.DrawImageUnscaled(_back, 0, 0);
        }
        catch (Exception)
        {
            _present?.Dispose();
            _present = null;
        }
    }

    /// <summary>The finished frame, for callers that want the pixels rather than a window.</summary>
    public Bitmap Frame => _back;

    // ---- primitives --------------------------------------------------------

    public void Polyline(PointF[] points, int count, Color colour, float width)
    {
        if (count < 2) return;
        _pen.Color = colour;
        _pen.Width = width;
        if (count == points.Length) _g.DrawLines(_pen, points);
        else
        {
            var exact = new PointF[count];
            Array.Copy(points, exact, count);
            _g.DrawLines(_pen, exact);
        }
    }

    public void Polygon(PointF[] points, int count, Color colour)
    {
        if (count < 3) return;
        _brush.Color = colour;
        SmoothingMode saved = _g.SmoothingMode;
        _g.SmoothingMode = _g == _gGlow ? SmoothingMode.None : SmoothingMode.HighSpeed;
        if (count == points.Length) _g.FillPolygon(_brush, points, FillMode.Winding);
        else
        {
            var exact = new PointF[count];
            Array.Copy(points, exact, count);
            _g.FillPolygon(_brush, exact, FillMode.Winding);
        }
        _g.SmoothingMode = saved;
    }

    public void Line(float x0, float y0, float x1, float y1, Color colour, float width)
    {
        _pen.Color = colour;
        _pen.Width = width;
        _g.DrawLine(_pen, x0, y0, x1, y1);
    }

    public void Ellipse(float cx, float cy, float rx, float ry, Color colour, float width)
    {
        _pen.Color = colour;
        _pen.Width = width;
        _g.DrawEllipse(_pen, cx - rx, cy - ry, rx * 2f, ry * 2f);
    }

    public void FillEllipse(float cx, float cy, float rx, float ry, Color colour)
    {
        _brush.Color = colour;
        _g.FillEllipse(_brush, cx - rx, cy - ry, rx * 2f, ry * 2f);
    }

    public void FillRect(RectangleF rect, Color colour)
    {
        _brush.Color = colour;
        _g.FillRectangle(_brush, rect);
    }

    public void DrawRect(RectangleF rect, Color colour, float width)
    {
        _pen.Color = colour;
        _pen.Width = width;
        _g.DrawRectangle(_pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public void Text(string text, FontSlot slot, Color colour, float x, float y)
    {
        _brush.Color = colour;
        _g.DrawString(text, _fonts[(int)slot], _brush, x, y, _sfLeft);
    }

    public void TextIn(string text, FontSlot slot, Color colour, RectangleF box, TextAlign align)
    {
        _brush.Color = colour;
        StringFormat sf = align switch
        {
            TextAlign.Right => _sfRight,
            TextAlign.Centre => _sfCentre,
            _ => _sfLeft,
        };
        _g.DrawString(text, _fonts[(int)slot], _brush, box, sf);
    }

    public float FontSize(FontSlot slot) => _fonts[(int)slot].Size;

    // ---- glow composite ----------------------------------------------------

    /// <summary>
    /// Blends the glow buffer up into the back buffer with bilinear filtering and premultiplied
    /// over. GDI+ will do this in one DrawImage, but its bilinear stretch costs around 7 ns per
    /// destination pixel on a single thread, which at this size is half the frame. Written out
    /// and spread across cores it is several times quicker, and the fully transparent stretches
    /// of open ocean cost almost nothing.
    /// </summary>
    private unsafe void CompositeGlow(Bitmap src, Rectangle dest)
    {
        if (dest.Width <= 0 || dest.Height <= 0 || _ux0 == null || _ux0.Length != dest.Width)
        {
            FallbackComposite(src, dest);
            return;
        }

        BitmapData sd = null, dd = null;
        try
        {
            sd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                              ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            _gBack.Flush(FlushIntention.Sync);
            dd = _back.LockBits(dest, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);

            byte* sBase = (byte*)sd.Scan0;
            byte* dBase = (byte*)dd.Scan0;
            int sStride = sd.Stride, dStride = dd.Stride;
            int sh = src.Height, dw = dest.Width, dh = dest.Height;
            int[] cx0 = _ux0, cx1 = _ux1, cwx = _uwx;
            float top = _glowSrc.Y, scaleY = _glowSrc.Height / dh;

            Parallel.For(0, dh, y =>
            {
                float v = top + (y + 0.5f) * scaleY - 0.5f;
                int y0 = (int)MathF.Floor(v);
                int wy = Math.Clamp((int)((v - y0) * 256f), 0, 256);
                int y1 = Math.Clamp(y0 + 1, 0, sh - 1);
                y0 = Math.Clamp(y0, 0, sh - 1);
                int iwy = 256 - wy;

                byte* r0 = sBase + (long)y0 * sStride;
                byte* r1 = sBase + (long)y1 * sStride;
                byte* row = dBase + (long)y * dStride;

                for (int x = 0; x < dw; x++)
                {
                    int wx = cwx[x], iwx = 256 - wx;
                    int o0 = cx0[x], o1 = cx1[x];
                    byte* a = r0 + o0; byte* b = r0 + o1;
                    byte* c = r1 + o0; byte* d = r1 + o1;

                    int alpha = ((((a[3] * iwx + b[3] * wx) >> 8) * iwy
                                + ((c[3] * iwx + d[3] * wx) >> 8) * wy) >> 8);
                    if (alpha == 0) continue;

                    int blue = ((((a[0] * iwx + b[0] * wx) >> 8) * iwy
                               + ((c[0] * iwx + d[0] * wx) >> 8) * wy) >> 8);
                    int green = ((((a[1] * iwx + b[1] * wx) >> 8) * iwy
                                + ((c[1] * iwx + d[1] * wx) >> 8) * wy) >> 8);
                    int red = ((((a[2] * iwx + b[2] * wx) >> 8) * iwy
                              + ((c[2] * iwx + d[2] * wx) >> 8) * wy) >> 8);

                    byte* p = row + x * 4;
                    int inv = 255 - alpha;
                    p[0] = Clamp8(blue + Div255(p[0] * inv));
                    p[1] = Clamp8(green + Div255(p[1] * inv));
                    p[2] = Clamp8(red + Div255(p[2] * inv));
                    p[3] = Clamp8(alpha + Div255(p[3] * inv));
                }
            });
        }
        catch (Exception)
        {
            if (dd != null) { _back.UnlockBits(dd); dd = null; }
            if (sd != null) { src.UnlockBits(sd); sd = null; }
            FallbackComposite(src, dest);
        }
        finally
        {
            if (dd != null) _back.UnlockBits(dd);
            if (sd != null) src.UnlockBits(sd);
        }
    }

    private void FallbackComposite(Bitmap src, Rectangle dest)
    {
        _gBack.CompositingQuality = CompositingQuality.HighSpeed;
        _gBack.InterpolationMode = InterpolationMode.Bilinear;
        _gBack.PixelOffsetMode = PixelOffsetMode.Half;
        _gBack.DrawImage(src, dest, _glowSrc, GraphicsUnit.Pixel);
    }

    private static int Div255(int v) => (v + 1 + (v >> 8)) >> 8;

    private static byte Clamp8(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private void BuildUpscaleTables(int srcWidth, RectangleF srcRect, int destWidth)
    {
        _ux0 = new int[destWidth];
        _ux1 = new int[destWidth];
        _uwx = new int[destWidth];

        float scale = srcRect.Width / destWidth;
        for (int x = 0; x < destWidth; x++)
        {
            float u = srcRect.X + (x + 0.5f) * scale - 0.5f;
            int x0 = (int)MathF.Floor(u);
            int w = Math.Clamp((int)((u - x0) * 256f), 0, 256);
            int x1 = Math.Clamp(x0 + 1, 0, srcWidth - 1);
            x0 = Math.Clamp(x0, 0, srcWidth - 1);
            _ux0[x] = x0 * 4;
            _ux1[x] = x1 * 4;
            _uwx[x] = w;
        }
    }

    // ---- teardown ----------------------------------------------------------

    private void DisposeBuffers()
    {
        _gBack?.Dispose(); _back?.Dispose();
        _gGlow?.Dispose(); _glow?.Dispose();
        _gGlowHalf?.Dispose(); _glowHalf?.Dispose();
        _gGlowUp?.Dispose(); _glowUp?.Dispose();
        _bgFull?.Dispose(); _scan?.Dispose();
        _pen?.Dispose(); _brush?.Dispose();
        if (_fonts != null) foreach (Font f in _fonts) f.Dispose();

        _gBack = _gGlow = _gGlowHalf = _gGlowUp = _g = null;
        _back = _glow = _glowHalf = _glowUp = _bgFull = _scan = null;
        _pen = null; _brush = null; _fonts = null;
        _ux0 = _ux1 = _uwx = null;
        _glowBox = Rectangle.Empty;
    }

    public void Dispose()
    {
        DisposeBuffers();
        _present?.Dispose();
        _sfLeft?.Dispose(); _sfRight?.Dispose(); _sfCentre?.Dispose();
    }
}
