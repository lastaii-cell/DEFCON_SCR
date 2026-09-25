namespace DefconSaver;

public enum FontSlot
{
    Huge,
    Big,
    Medium,
    Small,

    /// <summary>Huge, sized for the quarter-scale glow layer so the banner can bloom.</summary>
    HugeGlow,
}

public enum TextAlign { Left, Right, Centre }

/// <summary>
/// The drawing operations the scene needs, and nothing else. Two backends implement it: GDI+
/// software rendering, which works anywhere, and Direct2D, which puts the same picture on the
/// GPU. Keeping the scene written against this rather than against either one is what stops
/// the globe having to be drawn twice in two dialects.
/// </summary>
public interface IDrawTarget : IDisposable
{
    string Backend { get; }

    int Width { get; }
    int Height { get; }

    /// <summary>Scale factor for anything sized in pixels, 1.0 at 1080p.</summary>
    float Ui { get; }

    /// <summary>True when frames may be built off the thread that owns the window.</summary>
    bool CanDrawOffThread { get; }

    /// <summary>Prepares buffers for the given client size. Returns false if the target is unusable.</summary>
    bool Resize(int width, int height);

    void BeginFrame();

    /// <summary>Deep space and the lit ocean disc, centred on the globe.</summary>
    void Backdrop(float cx, float cy, float radius);

    /// <summary>Switches drawing to the glow layer, which is blurred and blended back later.</summary>
    void BeginGlowLayer();

    /// <summary>Finishes the glow layer and blends it over the frame within the given box.</summary>
    void EndGlowLayer(Rectangle box);

    /// <summary>Whether a glow layer is wanted at all this frame.</summary>
    bool WantsGlowLayer { get; }

    /// <summary>Scale applied to coordinates while the glow layer is active.</summary>
    float GlowScale { get; }

    void Scanlines(Rectangle box);

    void EndFrame();

    /// <summary>Puts the finished frame on screen. Must run on the thread owning the window.</summary>
    void Present();

    /// <summary>Forces buffers to be rebuilt on the next Resize, after a quality change.</summary>
    void Invalidate();

    // ---- primitives --------------------------------------------------------

    void Polyline(PointF[] points, int count, Color colour, float width);

    /// <summary>Filled with the winding rule; the scene relies on that for limb-closed shapes.</summary>
    void Polygon(PointF[] points, int count, Color colour);

    void Line(float x0, float y0, float x1, float y1, Color colour, float width);

    void Ellipse(float cx, float cy, float rx, float ry, Color colour, float width);

    void FillEllipse(float cx, float cy, float rx, float ry, Color colour);

    void FillRect(RectangleF rect, Color colour);

    void DrawRect(RectangleF rect, Color colour, float width);

    void Text(string text, FontSlot slot, Color colour, float x, float y);

    void TextIn(string text, FontSlot slot, Color colour, RectangleF box, TextAlign align);

    float FontSize(FontSlot slot);
}
