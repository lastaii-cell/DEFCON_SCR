namespace DefconSaver;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* older shell */ }

        Settings cfg = Settings.Load();
        (char mode, IntPtr handle) = Parse(args);

        try
        {
            switch (mode)
            {
                case 's':
                case 't':
                    new SaverHost(cfg).RunFullScreen();
                    break;

                case 'p':
                    new SaverHost(cfg).RunPreview(handle);
                    break;

                case 'w':               // /w [width height] - run in a normal window
                {
                    int ww = args.Length > 1 && int.TryParse(args[1], out int a) ? a : 1280;
                    int wh = args.Length > 2 && int.TryParse(args[2], out int b) ? b : 760;
                    new SaverHost(cfg).RunWindowed(ww, wh);
                    break;
                }

                case 'a':               // "change password" - nothing to do
                    break;

                case 'x':               // /x <camLat> <camLon> - fill-geometry diagnostics
                    Diagnose(args);
                    break;

                case 'b':               // /b <file> [w h frames] - steady-state frame timing
                    Benchmark(cfg, args);
                    break;

                case 'm':               // /m <file> [seconds] - ship movement statistics
                    Motion(args);
                    break;

                case 'd':               // /d <file> - which drawing backend this machine gets
                    ProbeBackend(cfg, args);
                    break;

                case 'g':               // /g <dir> - dump still frames, for checking the look
                    Capture(cfg, args);
                    break;

                default:
                    using (var f = new ConfigForm(cfg)) f.ShowDialog();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            // A screensaver must never leave a dialog sitting on a locked machine.
            if (mode is not ('s' or 't' or 'p' or 'g' or 'x' or 'b' or 'm' or 'd'))
                MessageBox.Show(ex.ToString(), "DEFCON Screensaver",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Accepts /s, /S, -s, /p 1234, /p:1234, /c, /c:1234 - all the forms Windows uses.</summary>
    private static (char Mode, IntPtr Handle) Parse(string[] args)
    {
        if (args.Length == 0) return ('c', IntPtr.Zero);

        string a = args[0].Trim();
        if (a.StartsWith('/') || a.StartsWith('-')) a = a[1..];
        if (a.Length == 0) return ('c', IntPtr.Zero);

        char mode = char.ToLowerInvariant(a[0]);

        string num;
        int sep = a.IndexOfAny(new[] { ':', '=' });
        if (sep >= 0) num = a[(sep + 1)..];
        else num = args.Length > 1 ? args[1].Trim() : "";

        IntPtr handle = IntPtr.Zero;
        if (long.TryParse(num, out long v) && v != 0) handle = new IntPtr(v);
        return (mode, handle);
    }

    /// <summary>Renders a spread of moments through one scenario to PNGs. Diagnostic only.</summary>
    private static void Capture(Settings cfg, string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "defcon-shots");
        int width = args.Length > 2 && int.TryParse(args[2], out int cw) ? cw : 1920;
        int height = args.Length > 3 && int.TryParse(args[3], out int ch) ? ch : 1080;
        Directory.CreateDirectory(dir);

        Profile.On = true;
        Geo.Load();
        var world = new World(20260925);
        using var target = new GdiTarget(cfg, IntPtr.Zero, false);
        target.Resize(width, height);
        var scene = new Scene(cfg, 0f, false);

        if (args.Length > 4 && float.TryParse(args[4], out float camLat))
        {
            world.CameraLocked = true;
            world.CamLat = camLat;
            world.CamLon = args.Length > 5 && float.TryParse(args[5], out float camLon) ? camLon : 0f;
        }

        float[] stamps = { 5f, 38f, 74f, 118f, 150f, 190f, 235f, 275f, 300f, 322f };
        const float step = 1f / 30f;
        float clock = 0f;
        int i = 0;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (float mark in stamps)
        {
            while (clock < mark) { world.Update(step); clock += step; }

            timer.Restart();
            scene.Draw(target, world, step);
            Bitmap frame = target.Frame;
            double ms = timer.Elapsed.TotalMilliseconds;

            string name = Path.Combine(dir, $"f{i:00}_t{(int)mark:000}s_d{world.Defcon}.png");
            frame.Save(name, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"{Path.GetFileName(name)}  {ms:0.0}ms  " +
                              $"m={world.Missiles.Count} b={world.Blasts.Count} u={world.Units.Count}");
            Console.WriteLine("    " + Profile.Dump());
            i++;
        }
    }

    /// <summary>
    /// Reports, for one camera, which coastline rings make trouble for the limb-clamped fill:
    /// how much of the limb their hidden portion sweeps, and the worst single azimuth jump.
    /// </summary>
    private static void Diagnose(string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "defcon-diag.txt");
        float camLat = args.Length > 2 && float.TryParse(args[2], out float a) ? a : 25f;
        float camLon = args.Length > 3 && float.TryParse(args[3], out float b) ? b : 105f;
        var sb = new System.Text.StringBuilder();

        Geo.Load();
        var cam = new Cam();
        cam.Set(500f, 500f, 430f, camLon, camLat);
        cam.Axis(out float ax, out float ay, out float az);

        var rows = new List<(float sweep, float jump, float minRad, int idx, int hidden, int vis, float clat, float clon)>();

        for (int i = 0; i < Geo.Rings.Length; i++)
        {
            Ring r = Geo.Rings[i];
            if (r.Ax * ax + r.Ay * ay + r.Az * az <= r.MinDot) continue;   // culled, never drawn

            float sweep = 0f, jump = 0f, minRad = float.MaxValue, prevAz = 0f;
            int hidden = 0, vis = 0;
            bool prevClamped = false;

            for (int j = 0; j < r.Count; j++)
            {
                float depth = cam.Project(r.Px[j], r.Py[j], r.Pz[j], out float sx, out float sy);
                if (depth >= 0f) { vis++; prevClamped = false; continue; }
                hidden++;

                float dx = sx - cam.Cx, dy = sy - cam.Cy;
                float rad = MathF.Sqrt(dx * dx + dy * dy) / cam.R;
                if (rad < minRad) minRad = rad;

                float azm = MathF.Atan2(dy, dx) * (float)Geo.R2D;
                if (prevClamped)
                {
                    float d = Geo.Wrap180(azm - prevAz);
                    sweep += MathF.Abs(d);
                    if (MathF.Abs(d) > jump) jump = MathF.Abs(d);
                }
                prevAz = azm;
                prevClamped = true;
            }

            if (hidden == 0) continue;
            Geo.ToLatLon(r.Ax, r.Ay, r.Az, out float clat, out float clon);
            rows.Add((sweep, jump, minRad == float.MaxValue ? 1f : minRad, i, hidden, vis, clat, clon));
        }


        // Which rings' silhouette polygons actually cover a probe pixel, and under which fill rule.
        float probeX = args.Length > 4 && float.TryParse(args[4], out float qx) ? qx : cam.Cx;
        float probeY = args.Length > 5 && float.TryParse(args[5], out float qy) ? qy : cam.Cy - cam.R * 0.83f;
        sb.AppendLine();
        sb.AppendLine($"probe pixel ({probeX:0},{probeY:0})  [disc centre {cam.Cx:0},{cam.Cy:0} radius {cam.R:0}]");

        var fill = new PointF[16384];
        foreach (Ring r in Geo.Rings)
        {
            if (r.Ax * ax + r.Ay * ay + r.Az * az <= r.MinDot) continue;
            var proj = new PointF[r.Count];
            var seen = new bool[r.Count];
            int n = LandFill.Build(r, ref cam, ref fill, proj, seen, out int visible);
            if (visible == 0 || n < 3) continue;
            (int crossings, int winding) = LandFill.Classify(fill, n, probeX, probeY);
            if (crossings % 2 == 0 && winding == 0) continue;
            Geo.ToLatLon(r.Ax, r.Ay, r.Az, out float rlat, out float rlon);
            sb.AppendLine($"  ring {Array.IndexOf(Geo.Rings, r),4} pts {r.Count,5} vis {visible,5} " +
                          $"fillpts {n,5}  crossings {crossings,3} winding {winding,3}  " +
                          $"centroid {rlat,6:0.0},{rlon,7:0.0}");
        }
        sb.AppendLine($"camera lat={camLat} lon={camLon}   rings drawn with hidden points: {rows.Count}");
        sb.AppendLine("  sweep    jump   minRad  ring  hidden  visible  centroid");
        foreach (var row in rows.OrderByDescending(x => x.sweep).Take(8))
            sb.AppendLine($"  {row.sweep,7:0.0} {row.jump,7:0.0} {row.minRad,7:0.000} {row.idx,5} " +
                          $"{row.hidden,7} {row.vis,8}  {row.clat,6:0.0},{row.clon,7:0.0}");
        File.WriteAllText(outPath, sb.ToString());
    }

    /// <summary>
    /// Renders many frames back to back and reports the spread. Short runs are misleading:
    /// the thread pool takes a moment to grow, so the first frames are slower than the
    /// steady state a screensaver actually runs at.
    /// </summary>
    private static void Benchmark(Settings cfg, string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "defcon-bench.txt");
        int width = args.Length > 2 && int.TryParse(args[2], out int bw) ? bw : 1920;
        int height = args.Length > 3 && int.TryParse(args[3], out int bh) ? bh : 1080;
        int frames = args.Length > 4 && int.TryParse(args[4], out int bf) ? bf : 200;

        ThreadPool.GetMinThreads(out _, out int ioMin);
        ThreadPool.SetMinThreads(Environment.ProcessorCount, ioMin);

        Geo.Load();
        var world = new World(20260925);
        using var target = new GdiTarget(cfg, IntPtr.Zero, false);
        target.Resize(width, height);
        var scene = new Scene(cfg, 0f, false);
        const float step = 1f / 60f;

        // Settle into DEFCON 1, where there is most to draw.
        while (world.Clock < 170f) world.Update(step);

        int warm = Math.Max(30, frames / 4);
        for (int i = 0; i < warm; i++)
        {
            world.Update(step);
            scene.Draw(target, world, step);
        }

        Profile.On = true;
        var times = new List<double>(frames);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            world.Update(step);
            timer.Restart();
            scene.Draw(target, world, step);
            times.Add(timer.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        double avg = times.Average();
        double p50 = times[times.Count / 2];
        double p95 = times[(int)(times.Count * 0.95)];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{width}x{height}  backend={target.Backend}  bloom={cfg.Bloom} " +
                      $"scanlines={cfg.Scanlines} grid={cfg.ShowGrid} hud={cfg.ShowHud}  " +
                      $"cores={Environment.ProcessorCount}");
        sb.AppendLine($"{frames} frames after {warm} warm-up   " +
                      $"avg {avg:0.0} ms ({1000.0 / avg:0} fps)   median {p50:0.0}   p95 {p95:0.0}");
        sb.AppendLine("per-stage total over the timed frames (ms):");
        sb.AppendLine("  " + Profile.Dump());
        File.WriteAllText(outPath, sb.ToString());
    }

    /// <summary>
    /// Runs the simulation headless and reports how ships actually move: how fast their
    /// headings swing, and how much of the time they are pinned against a coast.
    /// </summary>
    private static void Motion(string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "defcon-motion.txt");
        float seconds = args.Length > 2 && float.TryParse(args[2], out float sec) ? sec : 240f;

        Geo.Load();
        var world = new World(20260925);
        // Matches the frame step the saver runs at, so times found here line up with
        // DEFCON_FREEZE_AT when reproducing a moment on screen.
        const float step = 1f / 60f;

        var lastHeading = new Dictionary<Unit, float>();
        var lastPos = new Dictionary<Unit, (float Lat, float Lon)>();
        var turnRates = new List<float>();
        var progress = new List<float>();
        long fastSamples = 0, turnSamples = 0;
        float maxSurfaceLat = 0f, maxSubLat = 0f;
        long surfaceOverIce = 0, surfaceSamples = 0;
        var beamTimes = new List<float>();

        // Displacement is sampled over a window, not per frame: a single frame moves a ship
        // ~0.008 degrees, which is below the precision of an acos-based great-circle distance.
        const float window = 0.5f;
        float clock = 0f, sampleClock = 0f;

        while (clock < seconds)
        {
            world.Update(step);
            clock += step;
            sampleClock += step;
            if (world.Lasers.Count > 0 && beamTimes.Count < 8 &&
                (beamTimes.Count == 0 || clock - beamTimes[^1] > 1f))
                beamTimes.Add(clock);

            foreach (Unit u in world.Units)
            {
                if (!u.IsShip || !u.Alive) continue;

                float absLat = MathF.Abs(u.Lat);
                if (u.Kind == UnitKind.Sub)
                {
                    if (absLat > maxSubLat) maxSubLat = absLat;
                }
                else
                {
                    surfaceSamples++;
                    if (absLat > maxSurfaceLat) maxSurfaceLat = absLat;
                    if (absLat > 66f) surfaceOverIce++;
                }

                if (lastHeading.TryGetValue(u, out float prevHeading))
                {
                    float turn = MathF.Abs(Geo.Wrap180(u.Heading - prevHeading)) / step;
                    turnRates.Add(turn);
                    turnSamples++;
                    if (turn > 45f) fastSamples++;
                }
                lastHeading[u] = u.Heading;
            }

            if (sampleClock < window) continue;
            sampleClock = 0f;

            foreach (Unit u in world.Units)
            {
                if (!u.IsShip || !u.Alive) continue;
                if (lastPos.TryGetValue(u, out (float Lat, float Lon) prev))
                {
                    float dLat = u.Lat - prev.Lat;
                    float dLon = Geo.Wrap180(u.Lon - prev.Lon) * MathF.Cos(u.Lat * (float)Geo.D2R);
                    float moved = MathF.Sqrt(dLat * dLat + dLon * dLon);
                    float expected = Math.Max(1e-5f, u.Speed * window);
                    progress.Add(Math.Clamp(moved / expected, 0f, 2f));
                }
                lastPos[u] = (u.Lat, u.Lon);
            }
        }

        turnRates.Sort();
        progress.Sort();
        int held = progress.Count(v => v < 0.25f);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{seconds:0} s of simulation");
        if (turnRates.Count > 0)
            sb.AppendLine($"  heading change over {turnSamples} frame samples: " +
                          $"median {turnRates[turnRates.Count / 2]:0.00} deg/s   " +
                          $"p99 {turnRates[(int)(turnRates.Count * 0.99)]:0.00}   max {turnRates[^1]:0.00}");
        sb.AppendLine($"  frames turning faster than 45 deg/s: {fastSamples} " +
                      $"({100.0 * fastSamples / Math.Max(1, turnSamples):0.00} %)");
        if (progress.Count > 0)
            sb.AppendLine($"  speed made good over {progress.Count} half-second windows: " +
                          $"median {progress[progress.Count / 2]:0.00} of full speed   " +
                          $"held up (<25%): {held} ({100.0 * held / progress.Count:0.0} %)");
        sb.AppendLine($"  furthest from the equator: surface ships {maxSurfaceLat:0.0} deg, " +
                      $"submarines {maxSubLat:0.0} deg");
        sb.AppendLine($"  surface-ship samples beyond 66 deg: {surfaceOverIce} of {surfaceSamples}");
        sb.AppendLine($"  orbital: {world.PlatformsRevealed} platforms revealed, " +
                      $"{world.LasersFired} laser strikes, {world.AsatsLaunched} anti-satellite shots, " +
                      $"{world.PlatformsLost} platforms destroyed");
        sb.AppendLine("  beams visible at: " + string.Join(", ", beamTimes.Select(v => $"{v:0.0}s")));
        File.WriteAllText(outPath, sb.ToString());
    }

    /// <summary>Reports which drawing backend this machine will actually get, and why.</summary>
    private static void ProbeBackend(Settings cfg, string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "defcon-backend.txt");
        using var form = new Form
        {
            Text = "Backend probe",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(640, 400),
        };
        form.Show();
        Application.DoEvents();

        var sb = new System.Text.StringBuilder();
        D2DTarget gpu = D2DTarget.TryCreate(cfg, form.Handle, false, 640, 400, out string error);
        if (gpu != null)
        {
            sb.AppendLine("Direct2D: available");
            gpu.Dispose();
        }
        else
        {
            sb.AppendLine("Direct2D: NOT available - " + error);
        }
        sb.AppendLine("UseGpu setting: " + cfg.UseGpu);
        File.WriteAllText(outPath, sb.ToString());
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "DefconSaver.log");
            File.AppendAllText(path, $"{DateTime.Now:s}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nothing more we can do */ }
    }
}
