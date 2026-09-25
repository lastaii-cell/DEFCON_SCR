using System.Diagnostics;

namespace DefconSaver;

/// <summary>Owns the windows, the world, and the frame loop.</summary>
public sealed class SaverHost
{
    private readonly Settings _cfg;
    private readonly List<SaverForm> _forms = new();
    private World _world;
    private bool _stop;
    private string _exitReason = "unknown";
    private IntPtr _previewParent = IntPtr.Zero;

    private readonly int _seed;
    private readonly float _freezeAt;

    public SaverHost(Settings cfg)
    {
        _cfg = cfg;
        // A fixed seed and a frozen clock make the two backends render the very same world,
        // which is the only way to compare them pixel for pixel.
        _seed = int.TryParse(Environment.GetEnvironmentVariable("DEFCON_SEED"), out int s)
            ? s : Environment.TickCount;
        _freezeAt = float.TryParse(Environment.GetEnvironmentVariable("DEFCON_FREEZE_AT"), out float f)
            ? f : 0f;
        _fpsLog = Environment.GetEnvironmentVariable("DEFCON_FPSLOG");
        _uncapped = Environment.GetEnvironmentVariable("DEFCON_UNCAP") == "1";
    }

    private readonly string _fpsLog;
    private readonly bool _uncapped;
    private readonly List<double> _frameTimes = new();

    public void RunFullScreen()
    {
        Screen[] screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            float offset = _cfg.SpanMonitors ? 0f : i * (360f / Math.Max(1, screens.Length));
            var f = new SaverForm(_cfg, screens[i].Bounds, offset, SaverMode.FullScreen);
            f.ExitRequested += (sender, _) =>
            {
                _exitReason = "input: " + ((SaverForm)sender).LastQuitReason;
                _stop = true;
            };
            _forms.Add(f);
        }

        foreach (SaverForm f in _forms) { f.Show(); f.CreateTarget(); }
        if (_forms.Count > 0) { _forms[0].Activate(); _forms[0].Focus(); }
        Cursor.Hide();

        try
        {
            _world = new World(_seed);
            Loop(_cfg.TargetFps);
        }
        finally
        {
            Cursor.Show();
            WriteFpsLog();
            CloseAll();
            if (Environment.GetEnvironmentVariable("DEFCON_TRACE") == "1")
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "DefconSaver.log"),
                        $"{DateTime.Now:s}  fullscreen loop ended: {_exitReason}{Environment.NewLine}");
                }
                catch { }
            }
        }
    }

    public void RunPreview(IntPtr parent)
    {
        if (parent == IntPtr.Zero || !SaverForm.IsWindow(parent)) return;
        _previewParent = parent;

        var f = new SaverForm(_cfg, new Rectangle(0, 0, 160, 120), 0f, SaverMode.Preview);
        f.AttachToPreview(parent);
        _forms.Add(f);
        f.Show();
        f.CreateTarget();

        try
        {
            _world = new World(_seed);
            Loop(20);
        }
        finally { CloseAll(); }
    }

    /// <summary>Runs the saver in an ordinary window, for trying it out without taking the screen.</summary>
    public void RunWindowed(int width, int height)
    {
        var f = new SaverForm(_cfg, new Rectangle(90, 70, width, height), 0f, SaverMode.Window);
        f.FormClosed += (_, _) => _stop = true;
        _forms.Add(f);
        f.Show();
        f.Activate();
        f.CreateTarget();

        try
        {
            _world = new World(_seed);
            Loop(_cfg.TargetFps);
        }
        finally { WriteFpsLog(); CloseAll(); }
    }

    private void WriteFpsLog()
    {
        if (_fpsLog == null || _frameTimes.Count < 20) return;
        try
        {
            // Drop the first second; the thread pool and the driver both need a moment.
            var times = _frameTimes.Skip(Math.Min(60, _frameTimes.Count / 4)).OrderBy(v => v).ToList();
            double avg = times.Average();
            string backend = _forms.Count > 0 && _forms[0].Target != null
                ? _forms[0].Target.Backend : "unknown";
            File.WriteAllText(_fpsLog,
                $"backend {backend}   frames {times.Count}   " +
                $"avg {avg:0.00} ms ({1000.0 / avg:0} fps)   " +
                $"median {times[times.Count / 2]:0.00}   p95 {times[(int)(times.Count * 0.95)]:0.00}" +
                Environment.NewLine);
        }
        catch { }
    }

    private void Loop(int targetFps)
    {
        // Parallel.For grows the pool by hill-climbing, which would leave the first seconds
        // of every run stuttering. A screensaver would rather have the threads up front.
        ThreadPool.GetMinThreads(out _, out int ioMin);
        ThreadPool.SetMinThreads(Environment.ProcessorCount, ioMin);

        if (_freezeAt > 0f)
            while (_world.Clock < _freezeAt) _world.Update(1f / 60f);

        var sw = Stopwatch.StartNew();
        double last = 0;
        double frame = _uncapped ? 0.0 : 1.0 / Math.Clamp(targetFps, 10, 144);
        double slowFor = 0;

        while (!_stop)
        {
            Application.DoEvents();
            if (_stop) break;
            if (_previewParent != IntPtr.Zero && !SaverForm.IsWindow(_previewParent)) break;

            bool anyLive = false;
            foreach (SaverForm f in _forms) if (!f.IsDisposed) { anyLive = true; break; }
            if (!anyLive) { _exitReason = "all forms disposed"; break; }

            double now = sw.Elapsed.TotalSeconds;
            double elapsed = now - last;
            if (elapsed < frame) { Thread.Sleep(1); continue; }
            last = now;

            float dt = (float)Math.Min(elapsed, 0.12);
            if (_freezeAt > 0f)
            {
                dt = 0f;        // world already wound forward, hold it still
            }
            else
            {
                _world.Update(dt * _cfg.Speed);
            }

            double t0 = sw.Elapsed.TotalSeconds;
            RenderAll(dt);
            double cost = sw.Elapsed.TotalSeconds - t0;
            if (_fpsLog != null) _frameTimes.Add(cost * 1000.0);

            // If the machine cannot keep up, shed bloom rather than stutter.
            slowFor = cost > frame * 1.35 ? slowFor + elapsed : Math.Max(0, slowFor - elapsed * 0.5);
            if (slowFor > 2.5 && _cfg.Bloom > 0)
            {
                _cfg.Bloom--;
                foreach (SaverForm f in _forms) f.Target?.Invalidate();
                slowFor = 0;
            }
        }
    }

    /// <summary>
    /// Draws every monitor's frame, then hands them to the windows. The drawing itself only
    /// touches per-renderer buffers, so with more than one screen it is worth splitting across
    /// threads; the blit has to happen back here, on the thread that owns the windows.
    /// </summary>
    private void RenderAll(float dt)
    {
        int count = _forms.Count;
        var sizes = new Size[count];

        for (int i = 0; i < count; i++)
        {
            SaverForm f = _forms[i];
            sizes[i] = f.IsDisposed || !f.IsHandleCreated ? Size.Empty : f.ClientSize;
        }

        bool offThread = count > 1;
        for (int i = 0; i < count && offThread; i++)
            if (_forms[i].Target is { CanDrawOffThread: false }) offThread = false;

        if (offThread)
        {
            System.Threading.Tasks.Parallel.For(0, count, i => DrawOne(i, sizes[i], dt));
        }
        else
        {
            for (int i = 0; i < count; i++) DrawOne(i, sizes[i], dt);
        }

        for (int i = 0; i < count; i++)
            if (!_forms[i].IsDisposed) _forms[i].Target?.Present();
    }

    private void DrawOne(int index, Size size, float dt)
    {
        if (size.Width < 8 || size.Height < 8) return;
        SaverForm f = _forms[index];
        IDrawTarget target = f.Target;
        if (target == null || f.IsDisposed) return;
        if (!target.Resize(size.Width, size.Height)) return;
        f.Scene.Draw(target, _world, dt);
    }

    private void CloseAll()
    {
        foreach (SaverForm f in _forms)
        {
            try { if (!f.IsDisposed) { f.Hide(); f.Close(); f.Dispose(); } }
            catch { /* shutting down anyway */ }
        }
        _forms.Clear();
    }
}
