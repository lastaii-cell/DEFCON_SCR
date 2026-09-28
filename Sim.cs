namespace DefconSaver;

public enum UnitKind { Silo, Radar, Airbase, Carrier, Battleship, Sub, Bomber, Fighter, Orbital }

/// <summary>
/// A group of ships that sails as one. Surface fleets and submarine packs are both this;
/// they differ only in what is in them and where they are allowed to go. The leader picks
/// destinations and everyone else keeps station on it.
/// </summary>
public sealed class Fleet
{
    public readonly List<Unit> Members = new();
    public Unit Lead;

    /// <summary>The senior survivor, promoting one if the last leader has been sunk.</summary>
    public Unit Commander()
    {
        if (Lead != null && Lead.Alive) return Lead;
        Lead = null;
        foreach (Unit m in Members)
            if (m.Alive) { Lead = m; break; }
        return Lead;
    }
}

public sealed class Unit
{
    public UnitKind Kind;
    public int Faction;
    public float Lat, Lon;
    public float Px, Py, Pz;
    public float Heading;
    public float Speed;          // degrees per second
    public float TgtLat, TgtLon;
    public bool HasTarget;
    public int Ammo;
    public float Cooldown;
    public float Hp = 1f;
    public bool Alive = true;
    public float Fade;           // 0..1 fade-in
    public float Sweep;          // radar / rotor phase
    public float Life = float.MaxValue;
    public float Wobble;
    public float RepickIn;       // seconds before another destination may be chosen
    public float Stuck;          // seconds spent unable to move

    // Ships only. A member holds station at a bearing and range measured from its leader,
    // both relative to the leader's heading, so the shape turns with the formation.
    public Fleet Fleet;
    public float StationBearing;
    public float StationRange;

    /// <summary>Distance from the centre of the globe, 1 being the surface.</summary>
    public float Alt = 1.002f;

    // Orbital platforms only: an orbit is a great circle, so two perpendicular unit vectors
    // and a phase along them place the platform exactly.
    public float Ox, Oy, Oz, Wx, Wy, Wz;
    public float Theta, OrbitRate;

    /// <summary>A city just struck from orbit, to be hit again while it is still overhead.</summary>
    public City Relock;

    /// <summary>Interceptors a platform still has for the shots coming at it.</summary>
    public int Interceptors;

    /// <summary>Seconds before a platform may put up another interceptor.</summary>
    public float Guard;

    /// <summary>The deck or field an aircraft flew from; null for anything else.</summary>
    public Unit Home;

    public bool IsNaval => Home != null && Home.Kind == UnitKind.Carrier;

    public bool IsShip => Kind is UnitKind.Carrier or UnitKind.Battleship or UnitKind.Sub;
    public bool IsAir => Kind is UnitKind.Bomber or UnitKind.Fighter;
    public bool IsBase => Kind is UnitKind.Silo or UnitKind.Radar or UnitKind.Airbase;
    public bool IsOrbital => Kind == UnitKind.Orbital;

    public void SetPos(float lat, float lon)
    {
        Lat = lat; Lon = Geo.Wrap180(lon);
        Geo.ToVec(Lat, Lon, out Px, out Py, out Pz);
    }
}

public sealed class Missile
{
    public int Faction;
    public bool Nuke;
    public bool Alive = true;

    public float Ax, Ay, Az;     // launch point
    public float Bx, By, Bz;     // aim point
    public float T, Duration;
    public float Arc;            // peak altitude as a fraction of the globe radius
    public float Omega, SinOmega;   // great-circle angle from A to B, cached for the slerp

    public float Px, Py, Pz;     // current position
    public float Alt = 1f;
    public float Lat, Lon;

    public Missile Prey;         // interceptors only
    public bool Hunted;          // an interceptor is already on its way to this one
    public Unit TargetPlatform;  // anti-satellite shots only
    public float Speed;          // chasing shots only
    public City TargetCity;

    public const int TrailMax = 40;
    public readonly float[] Tx = new float[TrailMax];
    public readonly float[] Ty = new float[TrailMax];
    public readonly float[] Tz = new float[TrailMax];
    public readonly float[] Ta = new float[TrailMax];
    public int TrailCount;
    public float TrailClock;

    /// <summary>A point at fraction <paramref name="u"/> along the launch-to-target great circle.</summary>
    public static void ArcPoint(Missile m, float u, out float x, out float y, out float z)
    {
        float k1, k2;
        if (m.SinOmega < 1e-4f) { k1 = 1f - u; k2 = u; }
        else
        {
            k1 = MathF.Sin((1f - u) * m.Omega) / m.SinOmega;
            k2 = MathF.Sin(u * m.Omega) / m.SinOmega;
        }
        x = m.Ax * k1 + m.Bx * k2;
        y = m.Ay * k1 + m.By * k2;
        z = m.Az * k1 + m.Bz * k2;
        float len = MathF.Sqrt(x * x + y * y + z * z);
        if (len < 1e-6f) len = 1f;
        x /= len; y /= len; z /= len;
    }

    public void PushTrail()
    {
        if (TrailCount == TrailMax)
        {
            Array.Copy(Tx, 1, Tx, 0, TrailMax - 1);
            Array.Copy(Ty, 1, Ty, 0, TrailMax - 1);
            Array.Copy(Tz, 1, Tz, 0, TrailMax - 1);
            Array.Copy(Ta, 1, Ta, 0, TrailMax - 1);
            TrailCount--;
        }
        Tx[TrailCount] = Px; Ty[TrailCount] = Py; Tz[TrailCount] = Pz; Ta[TrailCount] = Alt;
        TrailCount++;
    }
}

public sealed class Blast
{
    public float Px, Py, Pz;                 // centre
    public float Ux, Uy, Uz, Vx, Vy, Vz;     // orthonormal basis for the ring
    public float T, Duration;
    public float MaxRad;                     // radians
    public float Alt = 1f;                   // 1 at the surface, higher for a kill in orbit
    public int Faction;
    public bool Alive = true;
}

/// <summary>A beam fired from orbit at the ground. Short-lived; the damage is instant.</summary>
public sealed class Laser
{
    public float Ax, Ay, Az;     // platform, a unit vector
    public float Alt;            // its distance from the centre
    public float Bx, By, Bz;     // where it lands, on the surface
    public float Life, MaxLife;
    public int Faction;
}

public sealed class Tracer
{
    public float Ax, Ay, Az, Bx, By, Bz;
    public float Life, MaxLife;
    public int Faction;
}

public sealed class Ticker
{
    public string Text;
    public Color Colour;
    public float Age;
}

public sealed class World
{
    public const int Factions = Territories.Count;

    public readonly Random Rng;
    public City[] Cities;
    public readonly List<Unit> Units = new();
    public readonly List<Missile> Missiles = new();
    public readonly List<Blast> Blasts = new();
    public readonly List<Tracer> Tracers = new();
    public readonly List<Laser> Lasers = new();
    public readonly List<Ticker> Log = new();

    public readonly int[] Team = new int[Factions];
    public readonly float[] Kills = new float[Factions];
    public readonly float[] Losses = new float[Factions];
    public readonly bool[] Playing = new bool[Factions];

    // Running totals for the orbital campaign, handy in the HUD and in diagnostics.
    public int PlatformsRevealed, LasersFired, AsatsLaunched, PlatformsLost, GuardsLaunched, AsatsStopped;

    public int Defcon = 5;
    public bool Aftermath;
    public float PhaseTime;      // seconds left in the current phase
    public float Clock;          // seconds since the scenario started
    public string ScenarioName = "";

    // Camera, shared by every monitor.
    public float CamLat = 22f, CamLon = 0f;
    public float SpinRate = 3.2f;          // degrees per second

    private static readonly float[] PhaseSeconds = { 26f, 165f, 28f, 30f, 30f, 42f };
    // indexed by Defcon: [0]=aftermath, [1]..[5]

    private const int MaxMissiles = 420;

    /// <summary>Surface ships keep clear of the ice; only submarines work under it.</summary>
    private const float SurfaceLatLimit = 66f;

    private static bool Barred(Unit u, float lat, float lon)
        => Geo.IsLand(lat, lon)
           || (u.Kind != UnitKind.Sub && MathF.Abs(lat) > SurfaceLatLimit);

    public World(int seed)
    {
        Rng = new Random(seed);
        Geo.Load();
        Reset();
    }

    // ---- scenario setup ----------------------------------------------------

    public void Reset()
    {
        Units.Clear(); Missiles.Clear(); Blasts.Clear(); Tracers.Clear(); Lasers.Clear(); Log.Clear();
        Array.Clear(Kills); Array.Clear(Losses);
        Cities = CityData.Build();

        PlatformsRevealed = LasersFired = AsatsLaunched = PlatformsLost = 0;
        GuardsLaunched = AsatsStopped = 0;
        Defcon = 5;
        Aftermath = false;
        PhaseTime = PhaseSeconds[5];
        Clock = 0f;

        SetUpTeams();
        for (int f = 0; f < Factions; f++)
            if (Playing[f]) Deploy(f);

        CamLat = 14f + (float)Rng.NextDouble() * 24f;
        CamLon = (float)Rng.NextDouble() * 360f;
        SpinRate = 2.6f + (float)Rng.NextDouble() * 1.6f;
        if (Rng.Next(2) == 0) SpinRate = -SpinRate;

        Say("DEFCON 5 - FORCES DEPLOYING", Palette.Status);
        Say(ScenarioName, Palette.HudDim);
    }

    private void SetUpTeams()
    {
        for (int f = 0; f < Factions; f++) { Playing[f] = true; Team[f] = f; }

        var order = Enumerable.Range(0, Factions).OrderBy(_ => Rng.Next()).ToArray();
        switch (Rng.Next(4))
        {
            case 0:     // three against three
                for (int i = 0; i < Factions; i++) Team[order[i]] = i < 3 ? 0 : 1;
                ScenarioName = "SCENARIO: TWO BLOCS";
                break;
            case 1:     // three pairs
                for (int i = 0; i < Factions; i++) Team[order[i]] = i / 2;
                ScenarioName = "SCENARIO: THREE PACTS";
                break;
            case 2:     // two against four
                for (int i = 0; i < Factions; i++) Team[order[i]] = i < 2 ? 0 : 1;
                ScenarioName = "SCENARIO: ASYMMETRIC";
                break;
            default:    // everyone for themselves
                for (int i = 0; i < Factions; i++) Team[order[i]] = i;
                ScenarioName = "SCENARIO: FREE FOR ALL";
                break;
        }
    }

    public bool Hostile(int a, int b) => a != b && Playing[a] && Playing[b] && Team[a] != Team[b];

    private void Deploy(int f)
    {
        float stagger = 0f;

        for (int i = 0; i < 7; i++)
        {
            BaseSite(f, out float la, out float lo);
            Add(UnitKind.Silo, f, la, lo, stagger += 0.09f, ammo: 8 + Rng.Next(5));
        }
        for (int i = 0; i < 4; i++)
        {
            BaseSite(f, out float la, out float lo);
            Add(UnitKind.Radar, f, la, lo, stagger += 0.09f);
        }
        for (int i = 0; i < 3; i++)
        {
            BaseSite(f, out float la, out float lo);
            Add(UnitKind.Airbase, f, la, lo, stagger += 0.09f, ammo: 6);
        }

        // Surface fleets: a carrier and its escorts. Kept clear of the ice, like any ship
        // that has to stay on top of the water.
        int surface = 2 + Rng.Next(2);
        for (int i = 0; i < surface; i++)
            if (FleetAnchor(f, surfaceOnly: true, out float la, out float lo))
                Squadron(f, la, lo, 2 + Rng.Next(4), ref stagger, submarines: false);

        // Submarine packs hunt on their own and may go under the pole, so they get their own
        // groups rather than riding along with a carrier that cannot follow them there.
        int packs = 1 + Rng.Next(2);
        for (int i = 0; i < packs; i++)
            if (FleetAnchor(f, surfaceOnly: false, out float la, out float lo))
                Squadron(f, la, lo, 2 + Rng.Next(4), ref stagger, submarines: true);
    }

    /// <summary>
    /// Open water for a group to form up in, or false if none was found. Keeps its distance
    /// from ships already placed for the first few tries, then takes what it can get rather
    /// than leaving a bloc a fleet short in a busy sea.
    /// </summary>
    private bool FleetAnchor(int f, bool surfaceOnly, out float lat, out float lon)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            if (!Territories.RandomSea(f, Rng, 22f, out lat, out lon)) continue;
            if (surfaceOnly && MathF.Abs(lat) > SurfaceLatLimit - 4f) continue;
            if (attempt < 18 && Crowded(lat, lon, 11f)) continue;
            return true;
        }

        lat = 0f; lon = 0f;
        return false;
    }

    private bool Crowded(float lat, float lon, float withinDeg)
    {
        foreach (Unit u in Units)
            if (u.IsShip && Geo.Dist(u.Lat, u.Lon, lat, lon) < withinDeg) return true;
        return false;
    }

    /// <summary>
    /// Builds one group around an anchor. The first ship leads and the rest are given a
    /// station astern of it, alternating quarters so a fleet under way reads as two short
    /// columns rather than a huddle.
    /// </summary>
    private void Squadron(int f, float lat, float lon, int size, ref float stagger, bool submarines)
    {
        var fleet = new Fleet();
        float course = Rng.Next(360);

        for (int j = 0; j < size; j++)
        {
            float bearing = j == 0 ? 0f : 180f + (j % 2 == 0 ? -30f : 30f);
            float range = j == 0 ? 0f : 1.6f + 1.1f * j;

            Geo.Move(lat, lon, course + bearing, range, out float sla, out float slo);
            if (Geo.IsLand(sla, slo)) { sla = lat; slo = lon; }

            UnitKind k = submarines ? UnitKind.Sub
                       : (j == 0 ? UnitKind.Carrier : UnitKind.Battleship);
            int ammo = submarines ? 5 : k == UnitKind.Carrier ? CarrierWing : 0;
            Unit u = Add(k, f, sla, slo, stagger += 0.06f, ammo: ammo);
            u.Speed = submarines ? 0.30f : 0.24f;
            u.Heading = course;
            u.Fleet = fleet;
            u.StationBearing = bearing;
            u.StationRange = range;
            fleet.Members.Add(u);
        }

        fleet.Lead = fleet.Members.Count > 0 ? fleet.Members[0] : null;
    }

    /// <summary>Launchers sit near the people they are defending, not scattered over tundra.</summary>
    private bool BaseSite(int f, out float lat, out float lon)
    {
        for (int i = 0; i < 80; i++)
        {
            City c = Cities[Rng.Next(Cities.Length)];
            if (c.Territory != f) continue;
            Geo.Move(c.Lat, c.Lon, Rng.Next(360), 1.5f + (float)Rng.NextDouble() * 7f,
                     out lat, out lon);
            if (Geo.IsLand(lat, lon) && Territories.At(lat, lon) == f) return true;
        }
        return Territories.RandomLand(f, Rng, out lat, out lon);
    }

    /// <summary>
    /// Brings the orbital weapons platforms out of hiding. Each gets its own great-circle
    /// orbit, so they sweep across the globe on different planes rather than in a line.
    /// </summary>
    private void RevealOrbitals()
    {
        for (int f = 0; f < Factions; f++)
        {
            if (!Playing[f]) continue;
            int count = 1 + Rng.Next(2);
            for (int i = 0; i < count; i++)
            {
                var u = new Unit
                {
                    Kind = UnitKind.Orbital,
                    Faction = f,
                    Ammo = 10 + Rng.Next(7),
                    Interceptors = OrbitalGuards,
                    Alt = 1.24f,
                    Fade = -0.3f * i,
                    Cooldown = 1f + (float)Rng.NextDouble() * 3f,
                    Theta = (float)Rng.NextDouble() * MathF.Tau,
                    OrbitRate = (0.030f + (float)Rng.NextDouble() * 0.020f) * (Rng.Next(2) == 0 ? 1f : -1f),
                };

                // A random orbit normal, then any two perpendicular axes in that plane.
                float nx, ny, nz, len;
                do
                {
                    nx = (float)Rng.NextDouble() * 2f - 1f;
                    ny = (float)Rng.NextDouble() * 2f - 1f;
                    nz = (float)Rng.NextDouble() * 2f - 1f;
                    len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                } while (len < 0.2f);
                nx /= len; ny /= len; nz /= len;

                Geo.Basis(nx, ny, nz, out u.Ox, out u.Oy, out u.Oz, out u.Wx, out u.Wy, out u.Wz);
                Units.Add(u);
                OrbitalMove(u, 0f);
                PlatformsRevealed++;
            }
        }
        Say("ORBITAL PLATFORMS UNMASKED", Palette.Status);
    }

    private Unit Add(UnitKind kind, int faction, float lat, float lon, float delay, int ammo = 0)
    {
        var u = new Unit
        {
            Kind = kind,
            Faction = faction,
            Ammo = ammo,
            Fade = -delay,
            Sweep = (float)Rng.NextDouble() * MathF.Tau,
            Cooldown = (float)Rng.NextDouble() * 6f,
            Wobble = (float)Rng.NextDouble() * MathF.Tau,
            Heading = Rng.Next(360),
        };
        u.Alt = kind switch
        {
            UnitKind.Bomber or UnitKind.Fighter => 1.018f,
            UnitKind.Orbital => 1.24f,
            _ => 1.002f,
        };
        u.SetPos(lat, lon);
        Units.Add(u);
        return u;
    }

    // ---- per-frame update --------------------------------------------------

    public void Update(float dt)
    {
        Clock += dt;
        AdvancePhase(dt);

        // Snapshot the count: scrambling aircraft appends to the list mid-pass.
        int unitCount = Units.Count;
        for (int i = 0; i < unitCount; i++) UpdateUnit(Units[i], dt);
        for (int i = Missiles.Count - 1; i >= 0; i--) UpdateMissile(Missiles[i], dt);
        for (int i = Blasts.Count - 1; i >= 0; i--) UpdateBlast(Blasts[i], dt);

        for (int i = Tracers.Count - 1; i >= 0; i--)
        {
            Tracers[i].Life -= dt;
            if (Tracers[i].Life <= 0f) Tracers.RemoveAt(i);
        }

        for (int i = Lasers.Count - 1; i >= 0; i--)
        {
            Lasers[i].Life -= dt;
            if (Lasers[i].Life <= 0f) Lasers.RemoveAt(i);
        }

        foreach (City c in Cities) if (c.Flash > 0f) c.Flash = Math.Max(0f, c.Flash - dt * 1.1f);
        foreach (Ticker t in Log) t.Age += dt;
        while (Log.Count > 7) Log.RemoveAt(0);

        Missiles.RemoveAll(m => !m.Alive);
        Blasts.RemoveAll(b => !b.Alive);
        Units.RemoveAll(u => !u.Alive);

        if (Defcon <= 2 && !Aftermath) Combat(dt);
        UpdateCamera(dt);
    }

    private void AdvancePhase(float dt)
    {
        PhaseTime -= dt;
        if (PhaseTime > 0f) return;

        if (Aftermath) { Reset(); return; }

        if (Defcon > 1)
        {
            Defcon--;
            PhaseTime = PhaseSeconds[Defcon];
            switch (Defcon)
            {
                case 4: Say("DEFCON 4 - FLEETS UNDER WAY", Palette.Status); break;
                case 3: Say("DEFCON 3 - AIR PATROLS LAUNCHED", Palette.Status); break;
                case 2: Say("DEFCON 2 - WEAPONS FREE", Palette.Status); break;
                case 1:
                    Say("DEFCON 1 - NUCLEAR RELEASE AUTHORISED", Palette.Status);
                    RevealOrbitals();
                    break;
            }
        }
        else
        {
            Aftermath = true;
            PhaseTime = PhaseSeconds[0];
            Say("EXCHANGE COMPLETE - END OF SIMULATION", Palette.Status);
        }
    }

    private void UpdateUnit(Unit u, float dt)
    {
        if (u.Fade < 1f) u.Fade = Math.Min(1f, u.Fade + dt * 0.9f);
        u.Sweep += dt * (u.Kind == UnitKind.Radar ? 1.5f : 3f);
        if (u.Cooldown > 0f) u.Cooldown -= dt;

        if (u.Guard > 0f) u.Guard -= dt;

        if (u.IsAir)
        {
            u.Life -= dt;
            if (u.Life <= 0f)
            {
                u.Alive = false;
                // Recovered aboard rather than lost, if there is still a deck to land on.
                if (u.IsNaval && u.Home.Alive && u.Home.Ammo < CarrierWing) u.Home.Ammo++;
                return;
            }
        }

        if (u.IsOrbital) { OrbitalUpdate(u, dt); return; }
        if (u.IsShip || u.IsAir) Steer(u, dt);
        if ((u.Kind == UnitKind.Airbase || u.Kind == UnitKind.Carrier) && Defcon <= 3) Scramble(u, dt);
        if (Defcon == 1 && !Aftermath) NuclearRelease(u);
    }

    private static void OrbitalMove(Unit u, float dt)
    {
        float prevLat = u.Lat, prevLon = u.Lon;
        u.Theta += u.OrbitRate * dt;

        float c = MathF.Cos(u.Theta), s = MathF.Sin(u.Theta);
        float x = u.Ox * c + u.Wx * s;
        float y = u.Oy * c + u.Wy * s;
        float z = u.Oz * c + u.Wz * s;

        Geo.ToLatLon(x, y, z, out float lat, out float lon);
        if (dt > 0f) u.Heading = Geo.Bearing(prevLat, prevLon, lat, lon);
        u.SetPos(lat, lon);
    }

    private void OrbitalUpdate(Unit u, float dt)
    {
        OrbitalMove(u, dt);

        if (Defcon != 1 || Aftermath) return;

        GuardAgainstAsat(u);
        if (u.Cooldown > 0f || u.Ammo <= 0 || u.Fade < 1f) return;

        // A city hit from orbit gets a second pass while the platform is still above it. If
        // it has gone over the horizon in the meantime the re-engagement is simply dropped.
        City target = null;
        bool again = false;
        if (u.Relock != null)
        {
            if (Visible(u, u.Relock)) { target = u.Relock; again = true; }
            u.Relock = null;
        }

        if (target == null)
        {
            float best = 0f;
            for (int i = 0; i < 24; i++)
            {
                City c = Cities[Rng.Next(Cities.Length)];
                if (!Visible(u, c)) continue;
                float score = c.Alive * (u.Px * c.Px + u.Py * c.Py + u.Pz * c.Pz);
                if (score > best) { best = score; target = c; }
            }
        }
        if (target == null) return;

        u.Ammo--;
        if (again)
        {
            // A pair averages 1.55 s then 6.0 s, so 7.55 s for two shots against the 7.5 s
            // one shot used to take: twice as often overall, in twos rather than singly.
            u.Cooldown = 4.5f + (float)Rng.NextDouble() * 3.0f;
        }
        else
        {
            u.Relock = target;
            u.Cooldown = 1.1f + (float)Rng.NextDouble() * 0.9f;   // the follow-up, shortly after
        }
        FireLaser(u, target, again);
    }

    /// <summary>
    /// Puts an interceptor in the way of an anti-satellite shot already climbing toward this
    /// platform. Five apiece, one at a time, and never two at the same missile: a platform can
    /// hold off a few rounds but not an exchange's worth.
    /// </summary>
    private void GuardAgainstAsat(Unit u)
    {
        if (u.Interceptors <= 0 || u.Guard > 0f || Missiles.Count == 0) return;

        Missile inbound = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < Missiles.Count; i++)
        {
            Missile m = Missiles[i];
            if (!m.Alive || m.Hunted || m.TargetPlatform != u) continue;

            float dx = m.Px * m.Alt - u.Px * u.Alt;
            float dy = m.Py * m.Alt - u.Py * u.Alt;
            float dz = m.Pz * m.Alt - u.Pz * u.Alt;
            float d = dx * dx + dy * dy + dz * dz;
            if (d < bestD) { bestD = d; inbound = m; }
        }
        if (inbound == null) return;

        u.Interceptors--;
        u.Guard = 1.4f + (float)Rng.NextDouble() * 1.2f;
        inbound.Hunted = true;

        var g = new Missile
        {
            Faction = u.Faction,
            Nuke = false,
            Prey = inbound,
            Speed = 11f,
            Duration = 8f,
            Alt = u.Alt,
            Px = u.Px, Py = u.Py, Pz = u.Pz,
            Lat = u.Lat, Lon = u.Lon,
        };
        g.Ax = g.Px; g.Ay = g.Py; g.Az = g.Pz;
        Missiles.Add(g);
        GuardsLaunched++;
    }

    /// <summary>
    /// Whether a platform can engage a city: hostile, still alive, and inside the cone beneath
    /// it rather than round the far side of the world.
    /// </summary>
    private bool Visible(Unit u, City c)
        => !c.Dead && c.Territory >= 0 && Hostile(u.Faction, c.Territory)
           && u.Px * c.Px + u.Py * c.Py + u.Pz * c.Pz >= 0.6f;

    private void FireLaser(Unit u, City c, bool again = false)
    {
        var beam = new Laser
        {
            Faction = u.Faction,
            Alt = u.Alt,
            Life = 0.6f,
            MaxLife = 0.6f,
            Ax = u.Px, Ay = u.Py, Az = u.Pz,
        };
        Geo.ToVec(c.Lat, c.Lon, out beam.Bx, out beam.By, out beam.Bz);
        Lasers.Add(beam);
        LasersFired++;

        float killed = Detonate(c.Lat, c.Lon, u.Faction, c, announce: false, yield: 0.5f);
        Say($"ORBITAL {(again ? "RESTRIKE" : "STRIKE")} - {c.Name} - {killed:0.0}M DEAD",
            Palette.Hot(Palette.Faction[u.Faction], 0.45f));
    }

    /// <summary>How near its station counts as on it. Below this the ship stops steering.</summary>
    private const float StationSlack = 0.6f;

    /// <summary>Aircraft a carrier can have in the air at once, and the size of its complement.</summary>
    private const int CarrierWing = 6;

    /// <summary>Interceptors a platform carries against anti-satellite shots.</summary>
    private const int OrbitalGuards = 5;

    /// <summary>
    /// Chance one of them works. At a certainty five interceptors made a platform almost
    /// unkillable - 26 of 28 shots stopped in testing, and platform losses fell from nine to
    /// two - which took the threat out of the silos entirely.
    /// </summary>
    private const double OrbitalGuardHit = 0.55;

    /// <summary>
    /// Where a follower should be: off its leader, at the bearing and range it was given,
    /// rotated with the leader's course. Returns false for a leader, a lone ship, or a group
    /// with nothing left afloat. Falls back to the mirrored quarter and then to the leader's
    /// own position when the station itself is over land or above the ice.
    /// </summary>
    private bool Station(Unit u, out Unit lead, out float lat, out float lon)
    {
        lead = null; lat = 0f; lon = 0f;
        if (u.Fleet == null) return false;

        lead = u.Fleet.Commander();
        if (lead == null || lead == u) return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            float bearing = attempt == 0 ? u.StationBearing : 360f - u.StationBearing;
            Geo.Move(lead.Lat, lead.Lon, lead.Heading + bearing, u.StationRange,
                     out lat, out lon);
            if (!Geo.IsLand(lat, lon) && !Barred(u, lat, lon)) return true;
        }

        lat = lead.Lat; lon = lead.Lon;
        return true;
    }

    private void Steer(Unit u, float dt)
    {
        if (u.RepickIn > 0f) u.RepickIn -= dt;

        float speed = u.Speed;
        float want;

        if (Station(u, out Unit lead, out float sla, out float slo))
        {
            float gap = Geo.ShortDist(u.Lat, u.Lon, sla, slo);
            if (gap < StationSlack)
            {
                // On station. Steering at a point a third of a degree away is what made ships
                // pirouette, so match the leader's course instead and simply cruise with it.
                u.HasTarget = false;
                want = lead.Heading;
            }
            else
            {
                u.TgtLat = sla; u.TgtLon = slo; u.HasTarget = true;
                want = Geo.Bearing(u.Lat, u.Lon, sla, slo);

                // Enough to close a gap opened by a turn, not enough to look like a race.
                speed *= Math.Clamp(gap, 1f, 1.7f);
            }
        }
        else
        {
            bool arrived = u.HasTarget && Geo.Dist(u.Lat, u.Lon, u.TgtLat, u.TgtLon) < 1.2f;
            if ((!u.HasTarget || arrived) && u.RepickIn <= 0f)
            {
                PickDestination(u);
                // A ship wedged in a bay can fail this over and over. Waiting between attempts
                // is what stops it spinning on the spot: its heading only moves when the plan
                // does.
                u.RepickIn = u.HasTarget ? 1f : 3f + (float)Rng.NextDouble() * 4f;
            }

            want = u.HasTarget ? Geo.Bearing(u.Lat, u.Lon, u.TgtLat, u.TgtLon) : u.Heading;
        }

        if (u.IsShip) want = ClearHeading(u, want);

        // Turn at a limited rate rather than snapping. Without this a ship near a coast flips
        // between avoidance options every frame and the icon flickers back and forth.
        float maxTurn = (u.IsAir ? 110f : 30f) * dt;
        u.Heading = StepHeading(u.Heading, want, maxTurn);

        Geo.Move(u.Lat, u.Lon, u.Heading, speed * dt, out float la, out float lo);

        if (u.IsShip && Barred(u, la, lo))
        {
            // Hold station and keep coming about; do not thrash the destination.
            u.Stuck += dt;
            if (u.Stuck > 6f)
            {
                u.Stuck = 0f;
                u.HasTarget = false;
                u.RepickIn = 0f;
            }
            return;
        }

        u.Stuck = 0f;
        u.SetPos(la, lo);
    }

    /// <summary>
    /// The smallest departure from the wanted bearing that keeps clear water ahead. Looking
    /// several steps out rather than one means a ship comes about early and gently instead of
    /// discovering the coast at the last moment.
    /// </summary>
    private static float ClearHeading(Unit u, float want)
    {
        float look = Math.Max(1.4f, u.Speed * 8f);
        Geo.Move(u.Lat, u.Lon, want, look, out float dla, out float dlo);
        if (!Barred(u, dla, dlo)) return want;

        for (int step = 1; step <= 8; step++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float h = want + sign * step * 20f;
                Geo.Move(u.Lat, u.Lon, h, look, out float la, out float lo);
                if (!Barred(u, la, lo)) return h;
            }
        }
        return want + 180f;     // boxed in: turn about
    }

    private static float StepHeading(float from, float to, float maxStep)
    {
        float d = Geo.Wrap180(to - from);
        if (d > maxStep) d = maxStep;
        else if (d < -maxStep) d = -maxStep;
        return Geo.Wrap180(from + d);
    }

    private void PickDestination(Unit u)
    {
        u.HasTarget = false;

        if (u.Kind == UnitKind.Sub && Defcon <= 2)
        {
            // Creep toward a firing position off an enemy coast.
            City c = PickEnemyCity(u.Faction, u.Lat, u.Lon, 180f);
            if (c != null)
            {
                for (int i = 0; i < 40; i++)
                {
                    float b = Geo.Bearing(c.Lat, c.Lon, u.Lat, u.Lon) + Rng.Next(-60, 61);
                    Geo.Move(c.Lat, c.Lon, b, 14f + (float)Rng.NextDouble() * 18f,
                             out float la, out float lo);
                    if (Geo.IsOpenSea(la, lo, 1.4f) && !Barred(u, la, lo))
                    {
                        u.TgtLat = la; u.TgtLon = lo; u.HasTarget = true; return;
                    }
                }
            }
        }

        if (u.IsAir)
        {
            float reach = u.Kind == UnitKind.Bomber ? 130f : 26f;
            City c = PickEnemyCity(u.Faction, u.Lat, u.Lon, reach);
            if (c != null)
            {
                Geo.Move(c.Lat, c.Lon, Rng.Next(360), (float)Rng.NextDouble() * 6f,
                         out u.TgtLat, out u.TgtLon);
                u.HasTarget = true;
                return;
            }
            Geo.Move(u.Lat, u.Lon, Rng.Next(360), 8f + (float)Rng.NextDouble() * 14f,
                     out u.TgtLat, out u.TgtLon);
            u.HasTarget = true;
            return;
        }

        for (int i = 0; i < 60; i++)
        {
            Geo.Move(u.Lat, u.Lon, Rng.Next(360), 4f + (float)Rng.NextDouble() * 16f,
                     out float la, out float lo);
            if (Geo.IsOpenSea(la, lo, 1.4f) && !Barred(u, la, lo))
            {
                u.TgtLat = la; u.TgtLon = lo; u.HasTarget = true; return;
            }
        }
    }

    private void Scramble(Unit host, float dt)
    {
        if (host.Cooldown > 0f || host.Ammo <= 0) return;

        bool deck = host.Kind == UnitKind.Carrier;
        if (deck)
        {
            // A carrier's limit is its own deck, not the bloc's total air activity.
            int wing = 0;
            foreach (Unit u in Units)
                if (u.Alive && u.IsAir && u.Home == host) wing++;
            if (wing >= CarrierWing) return;
        }
        else
        {
            int airborne = 0;
            foreach (Unit u in Units)
                if (u.IsAir && u.Faction == host.Faction) airborne++;
            if (airborne >= 10) return;
        }

        host.Cooldown = deck ? 4f + (float)Rng.NextDouble() * 5f
                             : 7f + (float)Rng.NextDouble() * 9f;
        host.Ammo--;

        bool bomber = Defcon <= 2 && Rng.Next(3) != 0;
        Unit a = Add(bomber ? UnitKind.Bomber : UnitKind.Fighter, host.Faction,
                     host.Lat, host.Lon, 0f, ammo: bomber ? 2 : 0);
        a.Fade = 0.4f;
        a.Speed = bomber ? 0.95f : 1.8f;
        a.Life = bomber ? 260f : 150f;
        a.Home = host;
    }

    private void NuclearRelease(Unit u)
    {
        if (u.Cooldown > 0f || u.Ammo <= 0) return;
        if (Missiles.Count >= MaxMissiles) return;

        // Silos will spend a round on a platform overhead rather than a city.
        if (u.Kind == UnitKind.Silo && Rng.Next(100) < 16)
        {
            Unit platform = PickEnemyPlatform(u);
            if (platform != null)
            {
                u.Ammo--;
                u.Cooldown = 9f + (float)Rng.NextDouble() * 9f;
                LaunchAsat(u, platform);
                return;
            }
        }

        float reach = u.Kind switch
        {
            UnitKind.Silo => 125f,
            UnitKind.Sub => 48f,
            UnitKind.Bomber => 9f,
            _ => 0f,
        };
        if (reach <= 0f) return;

        // A quarter of the salvo goes after the other side's launchers rather than its cities.
        float tLat, tLon;
        City city = null;
        if (Rng.Next(4) == 0)
        {
            Unit tgt = PickEnemyBase(u.Faction, u.Lat, u.Lon, reach);
            if (tgt == null) return;
            tLat = tgt.Lat; tLon = tgt.Lon;
        }
        else
        {
            city = PickEnemyCity(u.Faction, u.Lat, u.Lon, reach);
            if (city == null) return;
            tLat = city.Lat; tLon = city.Lon;
        }

        u.Ammo--;
        u.Cooldown = u.Kind switch
        {
            UnitKind.Silo => 7f + (float)Rng.NextDouble() * 13f,
            UnitKind.Sub => 11f + (float)Rng.NextDouble() * 16f,
            _ => 18f,
        };

        LaunchNuke(u.Faction, u.Lat, u.Lon, tLat, tLon, city);

        if (Rng.Next(5) == 0)
            Say($"LAUNCH DETECTED - {Territories.ShortNames[u.Faction]}", Palette.Faction[u.Faction]);
    }

    private void LaunchNuke(int faction, float sLat, float sLon, float tLat, float tLon, City city)
    {
        float dist = Geo.Dist(sLat, sLon, tLat, tLon);
        var m = new Missile
        {
            Faction = faction,
            Nuke = true,
            TargetCity = city,
            Duration = Math.Clamp(dist / 3.2f, 7f, 48f),
            Arc = Math.Clamp(dist / 180f, 0.05f, 1f) * 0.075f,
        };
        Geo.ToVec(sLat, sLon, out m.Ax, out m.Ay, out m.Az);
        Geo.ToVec(tLat, tLon, out m.Bx, out m.By, out m.Bz);
        m.Omega = MathF.Acos(Math.Clamp(m.Ax * m.Bx + m.Ay * m.By + m.Az * m.Bz, -1f, 1f));
        m.SinOmega = MathF.Sin(m.Omega);
        m.Px = m.Ax; m.Py = m.Ay; m.Pz = m.Az;
        Missiles.Add(m);
    }

    /// <summary>A hostile platform high enough above the silo's horizon to be shot at.</summary>
    private Unit PickEnemyPlatform(Unit silo)
    {
        Unit best = null;
        float bestDot = 0.45f;
        foreach (Unit u in Units)
        {
            if (!u.Alive || !u.IsOrbital || !Hostile(silo.Faction, u.Faction)) continue;
            float dot = silo.Px * u.Px + silo.Py * u.Py + silo.Pz * u.Pz;
            if (dot > bestDot) { bestDot = dot; best = u; }
        }
        return best;
    }

    private void LaunchAsat(Unit silo, Unit platform)
    {
        var m = new Missile
        {
            Faction = silo.Faction,
            Nuke = false,
            TargetPlatform = platform,
            Speed = 0.15f,       // globe radii per second
            Duration = 40f,
            Alt = 1f,
            Px = silo.Px, Py = silo.Py, Pz = silo.Pz,
            Ax = silo.Px, Ay = silo.Py, Az = silo.Pz,
            Lat = silo.Lat, Lon = silo.Lon,
        };
        Missiles.Add(m);
        AsatsLaunched++;
    }

    private void UpdateMissile(Missile m, float dt)
    {
        if (!m.Alive) return;

        if (m.TargetPlatform != null) { UpdateAsat(m, dt); return; }

        if (m.Prey != null)
        {
            if (!m.Prey.Alive) { m.Alive = false; return; }
            m.Duration -= dt;
            if (m.Duration <= 0f) { m.Alive = false; return; }

            Geo.ToLatLon(m.Prey.Px, m.Prey.Py, m.Prey.Pz, out float plat, out float plon);
            Geo.ToLatLon(m.Px, m.Py, m.Pz, out float lat, out float lon);
            float d = Geo.Dist(lat, lon, plat, plon);
            if (d < 1.4f)
            {
                // Only a platform's own interceptors chase an anti-satellite shot; the point
                // defence down on the surface filters for warheads.
                bool asat = m.Prey.TargetPlatform != null;
                m.Alive = false;

                if (asat && Rng.NextDouble() > OrbitalGuardHit)
                {
                    // A miss. The shot keeps climbing and the platform has to spend another
                    // round on it, which is what stops five interceptors being a force field.
                    m.Prey.Hunted = false;
                    AddBlast(plat, plon, 0.7f, 0.5f, m.Faction, m.Prey.Alt);
                    return;
                }

                m.Prey.Alive = false;
                if (asat) AsatsStopped++;
                AddBlast(plat, plon, 1.1f, 0.7f, m.Faction, m.Prey.Alt);
                return;
            }
            float step = Math.Min(m.Speed * dt, d);
            Geo.Move(lat, lon, Geo.Bearing(lat, lon, plat, plon), step, out float nlat, out float nlon);
            Geo.ToVec(nlat, nlon, out m.Px, out m.Py, out m.Pz);
            m.Lat = nlat; m.Lon = nlon;
            m.Alt = m.Prey.Alt;
        }
        else
        {
            m.T += dt / m.Duration;
            if (m.T >= 1f)
            {
                m.Alive = false;
                Geo.ToLatLon(m.Bx, m.By, m.Bz, out float lat, out float lon);
                Detonate(lat, lon, m.Faction, m.TargetCity);
                return;
            }
            Slerp(m, m.T);
            m.Alt = 1f + m.Arc * MathF.Sin(MathF.PI * m.T);
        }

        // Ballistic trails are drawn analytically from the launch point, so only
        // the steering interceptors need a recorded path.
        if (m.Prey == null) return;
        m.TrailClock += dt;
        if (m.TrailClock >= 0.045f) { m.TrailClock = 0f; m.PushTrail(); }
    }

    /// <summary>
    /// Straight up at the target, in free space rather than along the surface: the missile
    /// carries its own altitude and closes on wherever the platform has moved to.
    /// </summary>
    private void UpdateAsat(Missile m, float dt)
    {
        Unit t = m.TargetPlatform;
        m.Duration -= dt;
        if (!t.Alive || m.Duration <= 0f) { m.Alive = false; return; }

        float px = m.Px * m.Alt, py = m.Py * m.Alt, pz = m.Pz * m.Alt;
        float dx = t.Px * t.Alt - px, dy = t.Py * t.Alt - py, dz = t.Pz * t.Alt - pz;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist < 0.025f)
        {
            m.Alive = false;
            t.Alive = false;
            PlatformsLost++;
            AddBlast(t.Lat, t.Lon, 2.2f, 1.6f, m.Faction, t.Alt);
            Say($"{Territories.ShortNames[t.Faction]} PLATFORM DESTROYED",
                Palette.Faction[t.Faction]);
            return;
        }

        float step = Math.Min(m.Speed * dt, dist);
        px += dx / dist * step;
        py += dy / dist * step;
        pz += dz / dist * step;

        float len = MathF.Sqrt(px * px + py * py + pz * pz);
        if (len < 1e-5f) len = 1f;
        m.Px = px / len; m.Py = py / len; m.Pz = pz / len;
        m.Alt = len;
        Geo.ToLatLon(m.Px, m.Py, m.Pz, out m.Lat, out m.Lon);

        m.TrailClock += dt;
        if (m.TrailClock >= 0.045f) { m.TrailClock = 0f; m.PushTrail(); }
    }

    private static void Slerp(Missile m, float u)
    {
        Missile.ArcPoint(m, u, out m.Px, out m.Py, out m.Pz);
        Geo.ToLatLon(m.Px, m.Py, m.Pz, out m.Lat, out m.Lon);
    }

    /// <param name="yield">
    /// Scales the casualties, the fireball and the radius in which it wrecks units. One is a
    /// warhead. The area it reaches is deliberately not scaled: a weaker strike covers the
    /// same ground and simply kills less of it.
    /// </param>
    private float Detonate(float lat, float lon, int faction, City aimed, bool announce = true,
                           float yield = 1f)
    {
        float spread = MathF.Sqrt(yield);
        AddBlast(lat, lon, 5.2f * spread, 3.6f * spread, faction);

        float killed = 0f;
        foreach (City c in Cities)
        {
            if (c.Dead) continue;
            float d = Geo.Dist(lat, lon, c.Lat, c.Lon);
            if (d > 4.2f) continue;
            float share = Math.Clamp(1f - d / 4.2f, 0f, 1f);
            float loss = c.Alive * (0.35f + 0.55f * share) * yield;
            c.Alive = Math.Max(0f, c.Alive - loss);
            c.Flash = 1f;
            c.Hits++;
            killed += loss;
            if (c.Territory >= 0) Losses[c.Territory] += loss;
        }
        Kills[faction] += killed;

        foreach (Unit u in Units)
        {
            if (!u.Alive || u.IsAir || u.IsOrbital) continue;
            if (Geo.Dist(lat, lon, u.Lat, u.Lon) < 2.6f * spread) u.Alive = false;
        }

        if (announce && aimed != null && killed > 0.4f)
            Say($"{aimed.Name} HIT - {killed:0.0}M DEAD", Palette.Faction[faction]);
        return killed;
    }

    private void AddBlast(float lat, float lon, float maxRadDeg, float duration, int faction,
                          float alt = 1f)
    {
        var b = new Blast
        {
            Duration = duration,
            MaxRad = maxRadDeg * (float)Geo.D2R,
            Faction = faction,
            Alt = alt,
        };
        Geo.ToVec(lat, lon, out b.Px, out b.Py, out b.Pz);

        // An orthonormal basis in the tangent plane, for drawing the shock ring.
        float ax = Math.Abs(b.Pz) < 0.9f ? 0f : 1f;
        float ay = 0f, az = Math.Abs(b.Pz) < 0.9f ? 1f : 0f;
        b.Ux = ay * b.Pz - az * b.Py;
        b.Uy = az * b.Px - ax * b.Pz;
        b.Uz = ax * b.Py - ay * b.Px;
        float ul = MathF.Sqrt(b.Ux * b.Ux + b.Uy * b.Uy + b.Uz * b.Uz);
        b.Ux /= ul; b.Uy /= ul; b.Uz /= ul;
        b.Vx = b.Py * b.Uz - b.Pz * b.Uy;
        b.Vy = b.Pz * b.Ux - b.Px * b.Uz;
        b.Vz = b.Px * b.Uy - b.Py * b.Ux;

        Blasts.Add(b);
    }

    private void UpdateBlast(Blast b, float dt)
    {
        b.T += dt / b.Duration;
        if (b.T >= 1f) b.Alive = false;
    }

    // ---- conventional combat and missile defence ---------------------------

    private void Combat(float dt)
    {
        // Ships and fighters trading fire at close range.
        for (int i = 0; i < Units.Count; i++)
        {
            Unit a = Units[i];
            if (!a.Alive || a.IsBase || a.IsOrbital || a.Cooldown > 0f) continue;

            float range = a.IsAir ? 7f : 6.5f;
            Unit best = null;
            float bestD = range;
            for (int j = 0; j < Units.Count; j++)
            {
                Unit b = Units[j];
                if (!b.Alive || b.IsBase || b.IsOrbital || !Hostile(a.Faction, b.Faction)) continue;
                if (a.Kind == UnitKind.Fighter && !a.IsNaval
                    && b.Kind != UnitKind.Bomber && b.Kind != UnitKind.Fighter) continue;
                float d = Geo.Dist(a.Lat, a.Lon, b.Lat, b.Lon);
                if (d < bestD) { bestD = d; best = b; }
            }
            if (best == null) continue;

            a.Cooldown = 1.4f + (float)Rng.NextDouble() * 2.6f;
            Tracers.Add(new Tracer
            {
                Ax = a.Px, Ay = a.Py, Az = a.Pz,
                Bx = best.Px, By = best.Py, Bz = best.Pz,
                Life = 0.32f, MaxLife = 0.32f, Faction = a.Faction,
            });
            best.Hp -= 0.22f + (float)Rng.NextDouble() * 0.2f;
            if (best.Hp <= 0f)
            {
                best.Alive = false;
                AddBlast(best.Lat, best.Lon, 1.0f, 0.8f, a.Faction);
                if (best.Kind == UnitKind.Carrier)
                    Say($"{Territories.ShortNames[best.Faction]} CARRIER LOST", Palette.Faction[best.Faction]);
            }
        }

        // Point defence against inbound warheads.
        if (Defcon > 1 || Missiles.Count == 0) return;
        for (int i = 0; i < Units.Count; i++)
        {
            Unit d = Units[i];
            if (!d.Alive || d.IsOrbital) continue;
            if (!d.IsNaval && d.Kind != UnitKind.Silo
                && d.Kind != UnitKind.Battleship && d.Kind != UnitKind.Carrier) continue;
            if (d.Cooldown > 0f || Rng.NextDouble() > dt * 0.9) continue;

            Missile prey = null;
            float bestD = 26f;
            for (int j = 0; j < Missiles.Count; j++)
            {
                Missile m = Missiles[j];
                if (!m.Alive || !m.Nuke || m.Prey != null || !Hostile(d.Faction, m.Faction)) continue;
                if (m.T < 0.45f || m.T > 0.93f) continue;
                float dd = Geo.Dist(d.Lat, d.Lon, m.Lat, m.Lon);
                if (dd < bestD) { bestD = dd; prey = m; }
            }
            if (prey == null) continue;

            d.Cooldown = 4f + (float)Rng.NextDouble() * 7f;
            var interceptor = new Missile
            {
                Faction = d.Faction,
                Nuke = false,
                Prey = prey,
                Speed = 9f,
                Duration = 9f,
                Alt = 1.02f,
            };
            Geo.ToVec(d.Lat, d.Lon, out interceptor.Px, out interceptor.Py, out interceptor.Pz);
            interceptor.Ax = interceptor.Px; interceptor.Ay = interceptor.Py; interceptor.Az = interceptor.Pz;
            interceptor.Lat = d.Lat; interceptor.Lon = d.Lon;
            Missiles.Add(interceptor);
        }
    }

    // ---- target selection --------------------------------------------------

    private City PickEnemyCity(int faction, float lat, float lon, float reach)
    {
        City best = null;
        float bestScore = 0f;
        for (int i = 0; i < 18; i++)
        {
            City c = Cities[Rng.Next(Cities.Length)];
            if (c.Dead || c.Territory < 0 || !Hostile(faction, c.Territory)) continue;
            float d = Geo.Dist(lat, lon, c.Lat, c.Lon);
            if (d > reach) continue;
            float score = c.Alive * (1f + (float)Rng.NextDouble());
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    private Unit PickEnemyBase(int faction, float lat, float lon, float reach)
    {
        Unit best = null;
        for (int i = 0; i < 24; i++)
        {
            Unit u = Units[Rng.Next(Units.Count)];
            if (!u.Alive || !u.IsBase || !Hostile(faction, u.Faction)) continue;
            if (Geo.Dist(lat, lon, u.Lat, u.Lon) > reach) continue;
            best = u;
            break;
        }
        return best;
    }

    // ---- camera ------------------------------------------------------------

    public bool CameraLocked;

    /// <summary>
    /// A steady spin with a slow drift in latitude. It deliberately does not chase the action:
    /// re-aiming on every launch and detonation moved the target several times a second once
    /// DEFCON 1 opened, and the view jumped about instead of turning.
    /// </summary>
    private void UpdateCamera(float dt)
    {
        if (CameraLocked) return;

        CamLon = Geo.Wrap180(CamLon + SpinRate * dt);

        float rest = 18f + 10f * MathF.Sin(Clock * 0.035f);
        CamLat += (rest - CamLat) * Math.Min(1f, dt * 0.12f);
    }

    // ---- helpers -----------------------------------------------------------

    public void Say(string text, Color colour)
    {
        if (string.IsNullOrEmpty(text)) return;
        Log.Add(new Ticker { Text = text, Colour = colour });
    }

    public int CountUnits(int faction, Func<Unit, bool> filter)
    {
        int n = 0;
        foreach (Unit u in Units)
            if (u.Alive && u.Faction == faction && filter(u)) n++;
        return n;
    }

    public float TotalDead()
    {
        float t = 0f;
        foreach (float v in Losses) t += v;
        return t;
    }
}
