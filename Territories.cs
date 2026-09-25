namespace DefconSaver;

/// <summary>
/// The six blocs the world is carved into, in the spirit of DEFCON's territory map.
/// Each is a set of lat/lon boxes; boxes are tested in a fixed priority order so that
/// the overlaps (Mediterranean, Caucasus, Middle East) resolve the way an atlas would.
/// </summary>
public static class Territories
{
    public const int Count = 6;

    public const int NorthAmerica = 0;
    public const int SouthAmerica = 1;
    public const int Europe = 2;
    public const int Russia = 3;
    public const int Africa = 4;
    public const int Asia = 5;

    public static readonly string[] Names =
    {
        "NORTH AMERICA", "SOUTH AMERICA", "EUROPE", "RUSSIA", "AFRICA", "ASIA"
    };

    public static readonly string[] ShortNames = { "N.AM", "S.AM", "EUR", "RUS", "AFR", "ASI" };

    /// <summary>latMin, latMax, lonMin, lonMax.</summary>
    private static readonly float[][][] Boxes =
    {
        /* 0 N.America */ new[] { new[] {   7f,  85f, -172f,  -50f } },
        /* 1 S.America */ new[] { new[] { -57f,  13f,  -93f,  -33f } },
        /* 2 Europe    */ new[] { new[] {  34f,  72f,  -25f,   45f } },
        /* 3 Russia    */ new[] { new[] {  45f,  78f,   26f,  180f },
                                  new[] {  60f,  78f, -180f, -168f } },
        /* 4 Africa    */ new[] { new[] { -36f, 37.2f, -20f,   30f },
                                  new[] { -36f, 38.5f,  30f,   63f } },
        /* 5 Asia      */ new[] { new[] { -48f,  45f,   55f,  180f } },
    };

    /// <summary>Priority order: the crowded boxes are resolved before the loose ones.</summary>
    private static readonly int[] Order = { NorthAmerica, SouthAmerica, Russia, Africa, Europe, Asia };

    /// <summary>The territory containing a point, or -1 for no-man's-land (oceans, Antarctica).</summary>
    public static int At(float lat, float lon)
    {
        lon = Geo.Wrap180(lon);
        foreach (int t in Order)
        {
            foreach (float[] b in Boxes[t])
                if (lat >= b[0] && lat <= b[1] && lon >= b[2] && lon <= b[3])
                    return t;
        }
        return -1;
    }

    /// <summary>A rough centre for each bloc, used to aim the camera and seed fleets.</summary>
    public static readonly float[][] Centres =
    {
        new[] {  44f, -100f },
        new[] { -15f,  -60f },
        new[] {  50f,   10f },
        new[] {  58f,   62f },
        new[] {   4f,   20f },
        new[] {  25f,  105f },
    };

    /// <summary>
    /// Picks a latitude with equal probability per unit of surface area. Sampling latitude
    /// uniformly would pile everything into the Arctic, where a degree of longitude is tiny.
    /// </summary>
    private static float LatIn(float latMin, float latMax, Random rng)
    {
        float a = MathF.Sin(Math.Clamp(latMin, -89.9f, 89.9f) * (float)Geo.D2R);
        float b = MathF.Sin(Math.Clamp(latMax, -89.9f, 89.9f) * (float)Geo.D2R);
        return MathF.Asin(a + (float)rng.NextDouble() * (b - a)) * (float)Geo.R2D;
    }

    /// <summary>A lat/lon point drawn from somewhere inside the bloc.</summary>
    public static void RandomPoint(int t, Random rng, out float lat, out float lon)
    {
        float[][] boxes = Boxes[t];
        float[] b = boxes[rng.Next(boxes.Length)];
        lat = LatIn(b[0], b[1], rng);
        lon = b[2] + (float)rng.NextDouble() * (b[3] - b[2]);
    }

    /// <summary>Finds a land point inside the bloc, or falls back to its centre.</summary>
    public static bool RandomLand(int t, Random rng, out float lat, out float lon)
    {
        for (int i = 0; i < 400; i++)
        {
            RandomPoint(t, rng, out lat, out lon);
            if (Geo.IsLand(lat, lon) && At(lat, lon) == t) return true;
        }
        lat = Centres[t][0];
        lon = Centres[t][1];
        return false;
    }

    /// <summary>Finds open water within <paramref name="reachDeg"/> of the bloc.</summary>
    public static bool RandomSea(int t, Random rng, float reachDeg, out float lat, out float lon)
    {
        float[][] boxes = Boxes[t];
        for (int i = 0; i < 600; i++)
        {
            float[] b = boxes[rng.Next(boxes.Length)];
            lat = Math.Clamp(LatIn(b[0] - reachDeg, b[1] + reachDeg, rng), -78f, 80f);
            lon = Geo.Wrap180(b[2] - reachDeg + (float)rng.NextDouble() * (b[3] - b[2] + reachDeg * 2f));
            if (Geo.IsOpenSea(lat, lon, 1.6f)) return true;
        }
        lat = Centres[t][0];
        lon = Centres[t][1];
        return false;
    }
}
