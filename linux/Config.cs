namespace DefconSaver;

/// <summary>
/// The Linux stand-in for the registry and the /c dialog. Settings.Load returns bare defaults
/// off Windows, so the values come from ~/.config/defcon-saver/config instead - a plain
/// key = value file, written out with every key the first time the saver runs so there is
/// something to edit rather than a list to look up.
/// </summary>
internal static class Config
{
    private static string Dir =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "defcon-saver");

    private static string File_ => Path.Combine(Dir, "config");

    public static Settings Load(out string source)
    {
        Settings s = Settings.Load();

        // Scanlines are off by default on Windows but they are a large part of what the
        // thing looks like, so the generated file turns them on. Delete the line to go back
        // to the shared default.
        s.Scanlines = true;

        try
        {
            if (!System.IO.File.Exists(File_))
            {
                Write(s);
                source = $"{File_} (written with defaults)";
                return s;
            }

            foreach (string raw in System.IO.File.ReadAllLines(File_))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line[..eq].Trim().ToLowerInvariant();
                string val = line[(eq + 1)..].Trim();

                switch (key)
                {
                    case "speed":       s.Speed = Clamp(val, s.Speed, 0.25f, 3f); break;
                    case "globe":       s.GlobeScale = Clamp(val, s.GlobeScale, 0.6f, 1.25f); break;
                    case "glowstroke":  s.GlowStroke = Clamp(val, s.GlowStroke, 0.9f, 3f); break;
                    case "bloom":       s.Bloom = (int)Clamp(val, s.Bloom, 0, 2); break;
                    case "fps":         s.TargetFps = (int)Clamp(val, s.TargetFps, 20, 120); break;
                    case "scanlines":   s.Scanlines = Flag(val, s.Scanlines); break;
                    case "hud":         s.ShowHud = Flag(val, s.ShowHud); break;
                    case "cities":      s.ShowCityNames = Flag(val, s.ShowCityNames); break;
                    case "grid":        s.ShowGrid = Flag(val, s.ShowGrid); break;
                    case "population":  s.ShowPopulation = Flag(val, s.ShowPopulation); break;
                }
            }
            source = File_;
        }
        catch (Exception ex)
        {
            source = $"defaults ({ex.GetType().Name} reading {File_})";
        }
        return s;
    }

    private static float Clamp(string v, float fallback, float lo, float hi)
        => float.TryParse(v, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float r)
            ? Math.Clamp(r, lo, hi) : fallback;

    private static bool Flag(string v, bool fallback) => v.ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback,
    };

    private static void Write(Settings s)
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(File_, $"""
            # DEFCON Globe screensaver
            # Edit and restart. Anything missing or unparseable falls back to the default.

            speed      = {s.Speed:0.##}     # simulation rate, 0.25 to 3
            globe      = {s.GlobeScale:0.##}     # globe size, 0.6 to 1.25 of the default radius
            fps        = {s.TargetFps}       # frame cap, 20 to 120

            bloom      = {s.Bloom}        # 0 off, 1 soft, 2 full
            glowstroke = {s.GlowStroke:0.##}     # how far the bloom reaches, 0.9 to 3

            scanlines  = {Yes(s.Scanlines)}     # the CRT raster
            hud        = {Yes(s.ShowHud)}     # readout, tallies and log
            cities     = {Yes(s.ShowCityNames)}     # city labels
            grid       = {Yes(s.ShowGrid)}     # latitude / longitude grid
            population = {Yes(s.ShowPopulation)}     # haze over inhabited ground

            """);
    }

    private static string Yes(bool b) => b ? "on " : "off";
}
