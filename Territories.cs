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

    /// <summary>
    /// bloc, latMin, latMax, lonMin, lonMax. Tested in order and the first box containing the
    /// point wins, so a narrow claim can be placed ahead of a broad one. That ordering is the
    /// whole trick: with one priority per bloc instead, a box drawn wide enough to cover the
    /// Middle East also swallowed southern Anatolia, and Turkey came out in two colours.
    /// </summary>
    private static readonly float[][] Claims =
    {
        new[] { (float)NorthAmerica,   7f,   85f, -172f,  -50f },

        // Greenland, which the box above reaches only as far as -50E. The rest of it belonged
        // to no bloc and its coastline came out in the neutral colour - a conspicuous thing to
        // leave grey, given how much of the northern hemisphere it occupies on a globe. Split
        // in two so the second piece can reach east past Iceland without swallowing it: Iceland
        // stops at 67N and Greenland's north-east coast runs well above that.
        new[] { (float)NorthAmerica,  58f,   84f,  -73f,  -28f },
        new[] { (float)NorthAmerica,  68f,   84f,  -28f,  -10f },

        // Svalbard and Jan Mayen, north of where the European box stops.
        new[] { (float)Europe,        70f,   82f,  -10f,   26f },

        new[] { (float)SouthAmerica, -57f,   13f,  -93f,  -33f },

        // Southern Spain, which the African box reaches over the strait to claim. Morocco
        // stops at 35.92N, so 36 is the line that separates them.
        new[] { (float)Europe,        36f, 37.3f,   -8f,   -1f },

        // Anatolia, in two pieces: the west runs down to the Mediterranean coast, the east
        // stops above Syria and northern Iraq rather than cutting across them.
        new[] { (float)Europe,        36f, 42.5f,   25f,   36f },
        new[] { (float)Europe,      36.8f, 42.5f,   36f,   44f },

        // The Caucasus and the Caspian shore. Nothing reached this before: Russia began at
        // 45N, Europe ended at 45E and Asia began at 55E, leaving the ground between them
        // in no bloc at all and its coastline drawn in the neutral colour.
        new[] { (float)Russia,      38.5f,   45f,   44f,   56f },

        // The Primorsky coast, which sits below the 45N line the rest of Russia starts at.
        // Stops at 139E so Hokkaido, whose west coast is 139.4E, stays in Asia.
        new[] { (float)Russia,        42f,   45f,  130f,  139f },

        new[] { (float)Russia,        45f,   82f,   26f,  180f },
        new[] { (float)Russia,        60f,   78f, -180f, -168f },

        new[] { (float)Africa,       -36f, 37.2f,  -20f,   30f },
        new[] { (float)Africa,       -36f, 38.5f,   30f,   63f },

        new[] { (float)Europe,        34f,   72f,  -25f,   45f },
        new[] { (float)Asia,         -48f,   45f,   55f,  180f },
    };

    /// <summary>The territory containing a point, or -1 for no-man's-land (oceans, Antarctica).</summary>
    public static int At(float lat, float lon)
    {
        lon = Geo.Wrap180(lon);
        foreach (float[] c in Claims)
            if (lat >= c[1] && lat <= c[2] && lon >= c[3] && lon <= c[4])
                return (int)c[0];
        return -1;
    }

    /// <summary>The claims belonging to each bloc, for placing things inside one.</summary>
    private static readonly float[][][] Boxes = GroupClaims();

    private static float[][][] GroupClaims()
    {
        var all = new float[Count][][];
        for (int t = 0; t < Count; t++)
        {
            var mine = new List<float[]>();
            foreach (float[] c in Claims)
                if ((int)c[0] == t) mine.Add(new[] { c[1], c[2], c[3], c[4] });
            all[t] = mine.ToArray();
        }
        return all;
    }

    /// <summary>
    /// A box of the bloc's, chosen in proportion to its area. Uniform choice would drop a
    /// third of Europe's fleets into the little box that exists only to keep southern Spain
    /// out of Africa.
    /// </summary>
    private static float[] PickBox(int t, Random rng)
    {
        float[][] boxes = Boxes[t];
        if (boxes.Length == 1) return boxes[0];

        float total = 0f;
        foreach (float[] b in boxes) total += (b[1] - b[0]) * (b[3] - b[2]);

        float pick = (float)rng.NextDouble() * total;
        foreach (float[] b in boxes)
        {
            pick -= (b[1] - b[0]) * (b[3] - b[2]);
            if (pick <= 0f) return b;
        }
        return boxes[boxes.Length - 1];
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
        float[] b = PickBox(t, rng);
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
        for (int i = 0; i < 600; i++)
        {
            float[] b = PickBox(t, rng);
            lat = Math.Clamp(LatIn(b[0] - reachDeg, b[1] + reachDeg, rng), -78f, 80f);
            lon = Geo.Wrap180(b[2] - reachDeg + (float)rng.NextDouble() * (b[3] - b[2] + reachDeg * 2f));
            if (Geo.IsOpenSea(lat, lon, 1.6f)) return true;
        }
        lat = Centres[t][0];
        lon = Centres[t][1];
        return false;
    }
}
