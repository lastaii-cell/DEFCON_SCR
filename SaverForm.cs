using System.Runtime.InteropServices;

namespace DefconSaver;

public enum SaverMode
{
    /// <summary>Borderless, topmost, one per monitor; any input dismisses it.</summary>
    FullScreen,

    /// <summary>Child window inside the Windows screensaver preview pane.</summary>
    Preview,

    /// <summary>An ordinary resizable window, for trying it out.</summary>
    Window,
}

/// <summary>One window showing the globe. The host loop drives painting.</summary>
public sealed class SaverForm : Form
{
    public event EventHandler ExitRequested;

    public IDrawTarget Target { get; private set; }
    public Scene Scene { get; }
    public string BackendNote { get; private set; } = "";

    private readonly Settings _cfg;
    private readonly SaverMode _mode;
    private Point _mouseAnchor;
    private bool _haveAnchor;

    public SaverForm(Settings cfg, Rectangle bounds, float lonOffset, SaverMode mode)
    {
        _cfg = cfg;
        _mode = mode;
        Scene = new Scene(cfg, lonOffset, minimal: mode == SaverMode.Preview);

        SuspendLayout();
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        KeyPreview = true;
        Text = "DEFCON";
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        if (mode == SaverMode.Window)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(320, 240);
        }
        else
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            if (mode == SaverMode.FullScreen) TopMost = true;
        }

        Bounds = bounds;
        ResumeLayout(false);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) { /* the host loop blits directly */ }

    /// <summary>
    /// Picks a drawing backend. Direct2D where the machine can give us one, GDI+ otherwise:
    /// a screensaver has to keep working on a box with no usable display driver.
    /// </summary>
    public void CreateTarget()
    {
        bool minimal = _mode == SaverMode.Preview;
        Size cs = ClientSize;

        // DEFCON_BACKEND=gdi or =d2d forces one backend, for comparing the two.
        string forced = Environment.GetEnvironmentVariable("DEFCON_BACKEND");
        bool wantGpu = forced == null ? _cfg.UseGpu : forced.Equals("d2d", StringComparison.OrdinalIgnoreCase);

        if (wantGpu)
        {
            D2DTarget gpu = D2DTarget.TryCreate(_cfg, Handle, minimal,
                                                Math.Max(16, cs.Width), Math.Max(16, cs.Height),
                                                out string error);
            if (gpu != null) { Target = gpu; BackendNote = "Direct2D"; return; }
            BackendNote = "Direct2D unavailable (" + error + "), using GDI+";
        }
        else
        {
            BackendNote = "GDI+ (GPU disabled in settings)";
        }

        var software = new GdiTarget(_cfg, Handle, minimal);
        software.Resize(Math.Max(16, cs.Width), Math.Max(16, cs.Height));
        Target = software;
    }



    // ---- dismissal ---------------------------------------------------------

    public string LastQuitReason { get; private set; } = "none";

    private void Quit(string why)
    {
        LastQuitReason = why;
        if (_mode == SaverMode.FullScreen) ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e) { Quit($"keydown {e.KeyCode}"); base.OnKeyDown(e); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Quit($"cmdkey {keyData} msg=0x{msg.Msg:X}");
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Quit($"mousedown {e.Button}"); base.OnMouseDown(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // Windows delivers a stray move as the saver starts, so only a real nudge counts.
        Point p = Cursor.Position;
        if (!_haveAnchor) { _mouseAnchor = p; _haveAnchor = true; }
        else if (Math.Abs(p.X - _mouseAnchor.X) > 12 || Math.Abs(p.Y - _mouseAnchor.Y) > 12)
            Quit($"mousemove from {_mouseAnchor.X},{_mouseAnchor.Y} to {p.X},{p.Y}");
        base.OnMouseMove(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Target?.Dispose();
        base.Dispose(disposing);
    }

    // ---- preview-pane embedding -------------------------------------------

    private const int GwlStyle = -16;
    private const int WsChild = 0x40000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    public void AttachToPreview(IntPtr parent)
    {
        IntPtr me = Handle;                 // forces creation
        SetParent(me, parent);
        SetWindowLong(me, GwlStyle, GetWindowLong(me, GwlStyle) | WsChild);
        if (GetClientRect(parent, out NativeRect r))
            Bounds = new Rectangle(0, 0, Math.Max(8, r.Right - r.Left), Math.Max(8, r.Bottom - r.Top));
    }
}
