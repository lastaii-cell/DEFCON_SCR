using Silk.NET.SDL;
using SkiaSharp;

namespace DefconSaver;

/// <summary>
/// The Linux host. Two modes on purpose: a window, and a mode that renders stills straight to
/// PNG without touching SDL at all. If the window never appears on a given compositor, the
/// stills still say whether the port itself works, which separates a rendering problem from a
/// windowing one instead of leaving both suspect.
/// </summary>
internal static unsafe class Program
{
    private const int Seed = 20260925;

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "--fullscreen";

        try
        {
            return mode switch
            {
                "--frames" => Frames(args),
                "--window" => Run(args, fullscreen: false),
                "--fullscreen" => Run(args, fullscreen: true),
                "--help" or "-h" => Usage(),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"defcon-saver: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("defcon-saver [--fullscreen | --window [w h] | --frames <dir> [w h]]");
        Console.WriteLine();
        Console.WriteLine("  --fullscreen   the screensaver, filling the screen (default)");
        Console.WriteLine("  --window       in a window, for a look without taking the screen");
        Console.WriteLine("  --frames       render ten stills across one scenario to <dir> and exit,");
        Console.WriteLine("                 which needs no compositor and no SDL");
        Console.WriteLine();
        Console.WriteLine("Any key or mouse movement ends the fullscreen and window modes.");
        return 0;
    }

    private static int Size(string[] args, int index, int fallback)
        => args.Length > index && int.TryParse(args[index], out int v) && v >= 64 ? v : fallback;

    // ---- stills, no windowing involved -------------------------------------

    private static int Frames(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : "frames";
        int w = Size(args, 2, 1920), h = Size(args, 3, 1080);
        Directory.CreateDirectory(dir);

        Settings cfg = Config.Load(out string source);
        Console.WriteLine($"settings: {source}");
        Geo.Load();

        var world = new World(Seed);
        using var target = new SkiaTarget(cfg);
        target.Resize(w, h);
        var scene = new Scene(cfg, 0f, false);

        float[] stamps = { 5f, 38f, 74f, 118f, 150f, 190f, 235f, 275f, 300f, 322f };
        const float step = 1f / 30f;
        float clock = 0f;
        int i = 0;

        foreach (float mark in stamps)
        {
            while (clock < mark) { world.Update(step); clock += step; }
            scene.Draw(target, world, step);

            string path = Path.Combine(dir, $"f{i:00}_t{(int)mark:000}s_d{world.Defcon}.png");
            using (SKImage img = SKImage.FromBitmap(target.Frame))
            using (SKData data = img.Encode(SKEncodedImageFormat.Png, 90))
            using (FileStream fs = File.Create(path))
                data.SaveTo(fs);

            Console.WriteLine($"{Path.GetFileName(path)}  units {world.Units.Count}");
            i++;
        }

        Console.WriteLine($"wrote {i} frames to {Path.GetFullPath(dir)}");
        return 0;
    }

    // ---- the live saver ----------------------------------------------------

    private static int Run(string[] args, bool fullscreen)
    {
        Settings cfg = Config.Load(out string source);
        Geo.Load();

        Sdl sdl = Sdl.GetApi();
        if (sdl.Init(Sdl.InitVideo) < 0)
        {
            Console.Error.WriteLine($"SDL_Init failed: {sdl.GetErrorS()}");
            return 1;
        }

        int w = Size(args, 1, 1280), h = Size(args, 2, 800);

        var flags = WindowFlags.Shown | WindowFlags.Resizable;

        Silk.NET.SDL.Window* win = sdl.CreateWindow(
            "DEFCON", Sdl.WindowposCentered, Sdl.WindowposCentered, w, h, (uint)flags);
        if (win == null)
        {
            Console.Error.WriteLine($"SDL_CreateWindow failed: {sdl.GetErrorS()}");
            sdl.Quit();
            return 1;
        }

        Renderer* ren = sdl.CreateRenderer(win, -1, (uint)RendererFlags.Accelerated);
        if (ren == null) ren = sdl.CreateRenderer(win, -1, 0);   // software, rather than nothing
        if (ren == null)
        {
            Console.Error.WriteLine($"SDL_CreateRenderer failed: {sdl.GetErrorS()}");
            sdl.DestroyWindow(win);
            sdl.Quit();
            return 1;
        }

        if (fullscreen)
        {
            if (sdl.SetWindowFullscreen(win, (uint)WindowFlags.FullscreenDesktop) < 0)
                Console.Error.WriteLine($"fullscreen refused: {sdl.GetErrorS()}");
            sdl.ShowCursor(0);
        }

        sdl.GetRendererOutputSize(ren, ref w, ref h);

        using var target = new SkiaTarget(cfg);
        target.Resize(w, h);
        var scene = new Scene(cfg, 0f, false);
        var world = new World(Seed);

        // SDL_PIXELFORMAT_ARGB8888 matches the Bgra8888 bitmap Skia fills on a little-endian
        // machine, so the frame uploads without a per-pixel conversion.
        Texture* tex = sdl.CreateTexture(ren, Sdl.PixelformatArgb8888,
                                         (int)TextureAccess.Streaming, w, h);
        if (tex == null)
        {
            Console.Error.WriteLine($"SDL_CreateTexture failed: {sdl.GetErrorS()}");
            sdl.DestroyRenderer(ren);
            sdl.DestroyWindow(win);
            sdl.Quit();
            return 1;
        }

        Console.WriteLine($"DEFCON saver  {w}x{h}  {(fullscreen ? "fullscreen" : "window")}");
        Console.WriteLine($"  video driver : {sdl.GetCurrentVideoDriverS()}");
        Console.WriteLine($"  drawing      : {target.Backend}, bloom {cfg.Bloom}, " +
                          $"scanlines {(cfg.Scanlines ? "on" : "off")}");
        Console.WriteLine($"  settings     : {source}");

        float frame = 1f / Math.Clamp(cfg.TargetFps, 20, 120);
        long ticks = Environment.TickCount64;
        bool running = true;
        var e = new Event();

        while (running)
        {
            while (sdl.PollEvent(ref e) == 1)
            {
                var type = (EventType)e.Type;
                if (type is EventType.Quit or EventType.Keydown or EventType.Mousebuttondown)
                    running = false;
                else if (type == EventType.Mousemotion &&
                         (Math.Abs(e.Motion.Xrel) > 2 || Math.Abs(e.Motion.Yrel) > 2))
                    running = false;
                else if (type == EventType.Windowevent &&
                         (WindowEventID)e.Window.Event == WindowEventID.SizeChanged)
                {
                    sdl.GetRendererOutputSize(ren, ref w, ref h);
                    target.Resize(w, h);
                    sdl.DestroyTexture(tex);
                    tex = sdl.CreateTexture(ren, Sdl.PixelformatArgb8888,
                                            (int)TextureAccess.Streaming, w, h);
                }
            }
            if (!running) break;

            long now = Environment.TickCount64;
            float dt = Math.Clamp((now - ticks) / 1000f, 0f, 0.25f);
            ticks = now;

            world.Update(dt * cfg.Speed);
            scene.Draw(target, world, dt);

            SKBitmap bmp = target.Frame;
            sdl.UpdateTexture(tex, (Silk.NET.Maths.Rectangle<int>*)null,
                              (void*)bmp.GetPixels(), bmp.RowBytes);
            sdl.RenderClear(ren);
            sdl.RenderCopy(ren, tex, (Silk.NET.Maths.Rectangle<int>*)null, (Silk.NET.Maths.Rectangle<int>*)null);
            sdl.RenderPresent(ren);

            int spare = (int)((frame - (Environment.TickCount64 - now) / 1000f) * 1000f);
            if (spare > 1) System.Threading.Thread.Sleep(spare);
        }

        sdl.DestroyTexture(tex);
        sdl.DestroyRenderer(ren);
        sdl.DestroyWindow(win);
        sdl.Quit();
        return 0;
    }
}
