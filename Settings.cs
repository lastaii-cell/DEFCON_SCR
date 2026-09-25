using Microsoft.Win32;

namespace DefconSaver;

public sealed class Settings
{
    private const string Key = @"Software\DefconSaver";

    public float Speed = 1.0f;           // simulation rate multiplier
    public int Bloom = 2;                // 0 off, 1 single pass, 2 double pass
    public bool ShowHud = true;
    public bool ShowCityNames = true;
    public bool ShowGrid = true;
    public bool Scanlines = false;
    public int TargetFps = 60;
    public bool SpanMonitors = false;    // false: each monitor gets its own longitude
    public float GlobeScale = 1.0f;
    public bool UseGpu = true;         // Direct2D when available, GDI+ otherwise
    public bool ShowPopulation = true; // haze over inhabited ground
    public float GlowStroke = 1.8f;    // minimum stroke width inside the glow layer

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            using RegistryKey k = Registry.CurrentUser.OpenSubKey(Key);
            if (k == null) return s;
            s.Speed = GetFloat(k, "Speed", s.Speed, 0.25f, 3f);
            s.Bloom = Math.Clamp(GetInt(k, "Bloom", s.Bloom), 0, 2);
            s.ShowHud = GetBool(k, "ShowHud", s.ShowHud);
            s.ShowCityNames = GetBool(k, "ShowCityNames", s.ShowCityNames);
            s.ShowGrid = GetBool(k, "ShowGrid", s.ShowGrid);
            s.Scanlines = GetBool(k, "Scanlines", s.Scanlines);
            s.TargetFps = Math.Clamp(GetInt(k, "TargetFps", s.TargetFps), 20, 120);
            s.SpanMonitors = GetBool(k, "SpanMonitors", s.SpanMonitors);
            s.GlobeScale = GetFloat(k, "GlobeScale", s.GlobeScale, 0.6f, 1.25f);
            s.UseGpu = GetBool(k, "UseGpu", s.UseGpu);
            s.ShowPopulation = GetBool(k, "ShowPopulation", s.ShowPopulation);
            s.GlowStroke = GetFloat(k, "GlowStroke", s.GlowStroke, 0.9f, 3.0f);
        }
        catch { /* defaults are fine */ }
        return s;
    }

    public void Save()
    {
        try
        {
            using RegistryKey k = Registry.CurrentUser.CreateSubKey(Key);
            if (k == null) return;
            k.SetValue("Speed", Speed.ToString("0.###"));
            k.SetValue("Bloom", Bloom);
            k.SetValue("ShowHud", ShowHud ? 1 : 0);
            k.SetValue("ShowCityNames", ShowCityNames ? 1 : 0);
            k.SetValue("ShowGrid", ShowGrid ? 1 : 0);
            k.SetValue("Scanlines", Scanlines ? 1 : 0);
            k.SetValue("TargetFps", TargetFps);
            k.SetValue("SpanMonitors", SpanMonitors ? 1 : 0);
            k.SetValue("GlobeScale", GlobeScale.ToString("0.###"));
            k.SetValue("UseGpu", UseGpu ? 1 : 0);
            k.SetValue("ShowPopulation", ShowPopulation ? 1 : 0);
            k.SetValue("GlowStroke", GlowStroke.ToString("0.##"));
        }
        catch { /* read-only registry: run with defaults */ }
    }

    private static int GetInt(RegistryKey k, string name, int fallback)
    {
        object v = k.GetValue(name);
        if (v == null) return fallback;
        return int.TryParse(v.ToString(), out int r) ? r : fallback;
    }

    private static bool GetBool(RegistryKey k, string name, bool fallback)
        => GetInt(k, name, fallback ? 1 : 0) != 0;

    private static float GetFloat(RegistryKey k, string name, float fallback, float lo, float hi)
    {
        object v = k.GetValue(name);
        if (v == null) return fallback;
        return float.TryParse(v.ToString(), out float r) ? Math.Clamp(r, lo, hi) : fallback;
    }
}
