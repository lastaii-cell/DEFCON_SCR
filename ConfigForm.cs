using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DefconSaver;

/// <summary>The /c settings dialog, dressed to match the saver.</summary>
public sealed class ConfigForm : Form
{
    private static readonly Color Back = Color.FromArgb(6, 11, 24);
    private static readonly Color Panel = Color.FromArgb(10, 19, 40);
    private static readonly Color Ink = Color.FromArgb(158, 196, 242);
    private static readonly Color InkDim = Color.FromArgb(104, 138, 184);
    private static readonly Color Accent = Color.FromArgb(53, 198, 255);

    private readonly Settings _cfg;

    private DarkSlider _speed, _globe, _glowStroke;
    private ComboBox _bloom;
    private NumericUpDown _fps;
    private CheckBox _hud, _names, _grid, _scan, _perMonitor, _gpu, _population;
    private Label _speedValue, _globeValue, _glowStrokeValue;

    public ConfigForm(Settings cfg)
    {
        _cfg = cfg;
        Build();
        Pull();
    }

    private void Build()
    {
        Text = "DEFCON Globe Screensaver";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 586);
        BackColor = Back;
        ForeColor = Ink;
        Font = new Font("Consolas", 9.75f);

        int y = 16;
        Add(new Label
        {
            Text = "DEFCON GLOBE",
            Font = new Font("Consolas", 17f, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(18, y),
        });
        y += 32;
        Add(new Label
        {
            Text = "A wireframe globe playing itself to pieces.",
            ForeColor = InkDim,
            AutoSize = true,
            Location = new Point(18, y),
        });
        y += 30;

        _speedValue = Section("SIMULATION SPEED", ref y);
        _speed = Slider(25, 300, ref y);
        _speed.ValueChanged += (_, _) => _speedValue.Text = $"{_speed.Value / 100f:0.00}x";

        _globeValue = Section("GLOBE SIZE", ref y);
        _globe = Slider(60, 125, ref y);
        _globe.ValueChanged += (_, _) => _globeValue.Text = $"{_globe.Value}%";

        Section("GLOW", ref y);
        _bloom = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, y),
            Width = 180,
            BackColor = Panel,
            ForeColor = Ink,
            FlatStyle = FlatStyle.Flat,
        };
        _bloom.Items.AddRange(new object[] { "Off (fastest)", "Soft", "Full bloom" });
        Add(_bloom);

        Add(new Label { Text = "FRAME CAP", ForeColor = InkDim, AutoSize = true, Location = new Point(250, y + 3) });
        _fps = new NumericUpDown
        {
            Location = new Point(348, y),
            Width = 90,
            Minimum = 20,
            Maximum = 120,
            Increment = 5,
            BackColor = Panel,
            ForeColor = Ink,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Add(_fps);
        y += 40;

        _glowStrokeValue = Section("GLOW SPREAD", ref y);
        _glowStroke = Slider(90, 300, ref y);
        _glowStroke.ValueChanged += (_, _) => _glowStrokeValue.Text = $"{_glowStroke.Value / 100f:0.00}x";

        _hud = Check("Show the DEFCON readout, tallies and log", ref y);
        _names = Check("Label major cities", ref y);
        _grid = Check("Draw the latitude / longitude grid", ref y);
        _population = Check("Show population as a haze that thins as people die", ref y);
        _scan = Check("CRT scanlines", ref y);
        _perMonitor = Check("Show a different longitude on each monitor", ref y);
        _gpu = Check("Draw with the GPU (Direct2D) when available", ref y);

        y += 14;
        Button preview = MakeButton("PREVIEW", 20, y, 130);
        preview.Click += (_, _) =>
        {
            Push();
            _cfg.Save();
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "/s")
                {
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DEFCON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        Button ok = MakeButton("SAVE", 190, y, 120);
        ok.Click += (_, _) => { Push(); _cfg.Save(); DialogResult = DialogResult.OK; Close(); };

        Button cancel = MakeButton("CANCEL", 320, y, 120);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Asks the desktop compositor for a dark title bar so the frame matches the dialog.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1;
        try
        {
            if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }
        catch { /* pre-Windows-10: light title bar it is */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- little builders ---------------------------------------------------

    private void Add(Control c) => Controls.Add(c);

    private Label Section(string title, ref int y)
    {
        Add(new Label { Text = title, ForeColor = InkDim, AutoSize = true, Location = new Point(18, y) });
        var value = new Label
        {
            ForeColor = Accent,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(140, 16),
            Location = new Point(310, y),
            Text = "",
        };
        Add(value);
        y += 20;
        return value;
    }

    private DarkSlider Slider(int min, int max, ref int y)
    {
        var t = new DarkSlider
        {
            Location = new Point(20, y),
            Size = new Size(430, 26),
            Minimum = min,
            Maximum = max,
            BackColor = Back,
            Track = Panel,
            Fill = Accent,
        };
        Add(t);
        y += 34;
        return t;
    }

    private CheckBox Check(string text, ref int y)
    {
        var c = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Location = new Point(20, y),
            ForeColor = Ink,
            FlatStyle = FlatStyle.Flat,
        };
        Add(c);
        y += 26;
        return c;
    }

    private Button MakeButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = Ink,
        };
        b.FlatAppearance.BorderColor = InkDim;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 34, 70);
        Add(b);
        return b;
    }

    // ---- state -------------------------------------------------------------

    private void Pull()
    {
        _speed.Value = (int)MathF.Round(_cfg.Speed * 100f);
        _speedValue.Text = $"{_speed.Value / 100f:0.00}x";
        _globe.Value = (int)MathF.Round(_cfg.GlobeScale * 100f);
        _globeValue.Text = $"{_globe.Value}%";
        _glowStroke.Value = (int)MathF.Round(_cfg.GlowStroke * 100f);
        _glowStrokeValue.Text = $"{_glowStroke.Value / 100f:0.00}x";
        _bloom.SelectedIndex = Math.Clamp(_cfg.Bloom, 0, 2);
        _fps.Value = Math.Clamp(_cfg.TargetFps, (int)_fps.Minimum, (int)_fps.Maximum);
        _hud.Checked = _cfg.ShowHud;
        _names.Checked = _cfg.ShowCityNames;
        _grid.Checked = _cfg.ShowGrid;
        _scan.Checked = _cfg.Scanlines;
        _perMonitor.Checked = !_cfg.SpanMonitors;
        _gpu.Checked = _cfg.UseGpu;
        _population.Checked = _cfg.ShowPopulation;
    }

    private void Push()
    {
        _cfg.Speed = _speed.Value / 100f;
        _cfg.GlobeScale = _globe.Value / 100f;
        _cfg.Bloom = _bloom.SelectedIndex;
        _cfg.GlowStroke = _glowStroke.Value / 100f;
        _cfg.TargetFps = (int)_fps.Value;
        _cfg.ShowHud = _hud.Checked;
        _cfg.ShowCityNames = _names.Checked;
        _cfg.ShowGrid = _grid.Checked;
        _cfg.Scanlines = _scan.Checked;
        _cfg.SpanMonitors = !_perMonitor.Checked;
        _cfg.UseGpu = _gpu.Checked;
        _cfg.ShowPopulation = _population.Checked;
    }

    /// <summary>A slider that obeys the palette; the themed TrackBar draws a white groove.</summary>
    private sealed class DarkSlider : Control
    {
        public int Minimum = 0;
        public int Maximum = 100;
        public Color Track = Color.DimGray;
        public Color Fill = Color.DodgerBlue;

        public event EventHandler ValueChanged;

        private int _value;
        private bool _dragging;

        public DarkSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = true;
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Clamp(value, Minimum, Maximum);
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private float Fraction => Maximum > Minimum ? (_value - Minimum) / (float)(Maximum - Minimum) : 0f;

        private int ThumbX => 9 + (int)(Fraction * (Width - 18));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int mid = Height / 2;
            var groove = new Rectangle(9, mid - 3, Width - 18, 6);
            using (var b = new SolidBrush(Track)) g.FillRectangle(b, groove);
            using (var p = new Pen(Color.FromArgb(70, Fill), 1f)) g.DrawRectangle(p, groove);

            int tx = ThumbX;
            using (var b = new SolidBrush(Color.FromArgb(120, Fill)))
                g.FillRectangle(b, 9, mid - 3, tx - 9, 6);
            using (var b = new SolidBrush(Fill))
                g.FillRectangle(b, tx - 4, mid - 9, 8, 18);

            if (Focused)
                using (var p = new Pen(Color.FromArgb(120, Fill), 1f))
                    g.DrawRectangle(p, tx - 6, mid - 11, 12, 22);
        }

        private void SetFromX(int x)
        {
            float f = Math.Clamp((x - 9) / (float)Math.Max(1, Width - 18), 0f, 1f);
            Value = Minimum + (int)MathF.Round(f * (Maximum - Minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _dragging = true;
            SetFromX(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value += Math.Sign(e.Delta) * Math.Max(1, (Maximum - Minimum) / 50);
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys key)
            => key is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (Maximum - Minimum) / 50);
            switch (e.KeyCode)
            {
                case Keys.Left: Value -= step; e.Handled = true; break;
                case Keys.Right: Value += step; e.Handled = true; break;
                case Keys.Home: Value = Minimum; e.Handled = true; break;
                case Keys.End: Value = Maximum; e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }
}
