namespace DefconSaver;

/// <summary>The DEFCON look: near-black navy space, glowing blue landmasses, saturated bloc colours.</summary>
public static class Palette
{
    public static readonly Color SpaceOuter = Color.FromArgb(255, 0, 2, 8);
    public static readonly Color SpaceInner = Color.FromArgb(255, 3, 10, 26);

    public static readonly Color OceanEdge = Color.FromArgb(255, 2, 7, 22);
    public static readonly Color OceanCore = Color.FromArgb(255, 8, 26, 60);

    /// <summary>
    /// Deep enough that the continents read as mass rather than as bright shapes, and the
    /// coastlines stay the brightest thing on the globe. Override with DEFCON_LAND=r,g,b.
    /// </summary>
    public static readonly Color Land = FromEnv("DEFCON_LAND", Color.FromArgb(255, 9, 19, 54));

    public static readonly Color LandNeutral = Color.FromArgb(255, 16, 28, 62);
    public static readonly Color Coast = Color.FromArgb(255, 132, 178, 255);
    public static readonly Color CoastNeutral = Color.FromArgb(255, 96, 128, 186);

    /// <summary>The haze over inhabited ground, thinning as the people in it die.</summary>
    public static readonly Color Mist = Color.FromArgb(255, 116, 168, 224);

    public static readonly Color Grid = Color.FromArgb(255, 16, 46, 96);
    public static readonly Color GridMajor = Color.FromArgb(255, 30, 74, 142);
    public static readonly Color Limb = Color.FromArgb(255, 74, 132, 214);

    public static readonly Color Hud = Color.FromArgb(255, 150, 190, 240);
    public static readonly Color HudDim = Color.FromArgb(255, 80, 112, 160);
    public static readonly Color Alert = Color.FromArgb(255, 255, 70, 190);

    /// <summary>Every announcement of a change in readiness state.</summary>
    public static readonly Color Status = Color.FromArgb(255, 82, 240, 124);
    public static readonly Color Nuke = Color.FromArgb(255, 255, 248, 214);

    /// <summary>One colour per bloc, in Territories index order.</summary>
    public static readonly Color[] Faction =
    {
        Color.FromArgb(255,  53, 198, 255),   // North America - ice blue
        Color.FromArgb(255, 255, 210,  63),   // South America - amber
        Color.FromArgb(255,  77, 230, 110),   // Europe        - green
        Color.FromArgb(255, 255,  59,  71),   // Russia        - red
        Color.FromArgb(255, 199, 125, 255),   // Africa        - violet
        Color.FromArgb(255, 255, 138,  61),   // Asia          - orange
    };

    public static Color Fade(Color c, float alpha)
    {
        int a = (int)(Math.Clamp(alpha, 0f, 1f) * c.A);
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Pushes a colour toward white, for flashes and bloom sources.</summary>
    public static Color Hot(Color c, float t) => Mix(c, Color.White, t);

    private static Color FromEnv(string name, Color fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        string[] parts = value.Split(',');
        if (parts.Length < 3) return fallback;
        return int.TryParse(parts[0], out int r)
            && int.TryParse(parts[1], out int g)
            && int.TryParse(parts[2], out int b)
            ? Color.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255))
            : fallback;
    }
}
