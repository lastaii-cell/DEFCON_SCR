namespace DefconSaver;

/// <summary>
/// Draws the world through an <see cref="IDrawTarget"/>. Everything about what the globe looks
/// like lives here; nothing about how the pixels get there.
/// </summary>
public sealed class Scene
{
    private readonly Settings _cfg;
    private readonly float _lonOffset;
    private readonly bool _minimal;      // preview pane: no glow, no HUD

    private IDrawTarget _target;
    private float _cx, _cy, _radius, _ui;
    private Rectangle _globeBox;

    private bool _inGlow;
    private float _glowScale = 1f;
    private const float GlowAlpha = 0.50f;

    /// <summary>Set by the environment only for testing; otherwise the setting decides.</summary>
    private static readonly float? GlowStrokeOverride =
        float.TryParse(Environment.GetEnvironmentVariable("DEFCON_GLOWSTROKE"), out float gs)
            ? Math.Clamp(gs, 0.5f, 6f) : null;

    /// <summary>Minimum stroke width inside the glow layer, in that layer's own pixels.</summary>
    private float GlowStrokeFloor => GlowStrokeOverride ?? _cfg.GlowStroke;

    private int _lastDefcon = 5;
    private bool _lastAftermath;
    private string _alert;
    private Color _alertColour = Color.White;
    private float _alertT;

    private readonly PointF[][] _runCache = new PointF[8192][];
    private readonly PointF[] _glyph = new PointF[10];
    private PointF[] _poly = new PointF[8192];
    private int _polyN;
    private PointF[] _fill = new PointF[16384];

    private PointF[][] _ringProj;
    private bool[][] _ringVis;

    public Scene(Settings cfg, float lonOffset, bool minimal)
    {
        _cfg = cfg;
        _lonOffset = lonOffset;
        _minimal = minimal;
    }

    private bool Glowing => !_minimal && _cfg.Bloom > 0;

    private Color Col(Color c) => _inGlow ? Palette.Fade(c, GlowAlpha) : c;

    // ---- frame -------------------------------------------------------------

    public void Draw(IDrawTarget target, World w, float dt)
    {
        _target = target;
        _ui = target.Ui;
        _cx = target.Width * 0.5f;
        _cy = target.Height * 0.5f;
        _radius = Math.Min(target.Width, target.Height) * 0.43f * _cfg.GlobeScale;

        // The whole surface, not just the globe: the banner glows and the scanlines have to
        // cross the readout in the corner, both of which sit outside any box around the globe.
        _globeBox = new Rectangle(0, 0, target.Width, target.Height);

        if (w.Defcon != _lastDefcon || w.Aftermath != _lastAftermath)
        {
            _lastDefcon = w.Defcon;
            _lastAftermath = w.Aftermath;
            _alert = w.Aftermath ? "END OF SIMULATION" : $"DEFCON {w.Defcon}";
            _alertColour = Palette.Status;
            _alertT = 1f;
        }
        if (_alertT > 0f) _alertT = Math.Max(0f, _alertT - dt * 0.28f);

        float lon = w.CamLon + _lonOffset;

        target.BeginFrame();
        target.Backdrop(_cx, _cy, _radius);

        if (Glowing && target.WantsGlowLayer)
        {
            double tg = Profile.Now;
            _glowScale = target.GlowScale;
            _inGlow = true;
            target.BeginGlowLayer();

            var camGlow = new Cam();
            camGlow.Set(_cx * _glowScale, _cy * _glowScale, _radius * _glowScale, lon, w.CamLat);
            DrawWorld(ref camGlow, _glowScale, w);
            if (_cfg.ShowHud && !_minimal) DrawBannerGlow();

            target.EndGlowLayer(_globeBox);
            _inGlow = false;
            _glowScale = 1f;
            Profile.Mark("glow", tg);
        }

        var cam = new Cam();
        cam.Set(_cx, _cy, _radius, lon, w.CamLat);
        DrawWorld(ref cam, 1f, w);

        if (_cfg.ShowHud && !_minimal)
        {
            double th = Profile.Now;
            DrawHud(w);
            Profile.Mark("hud", th);
        }

        if (_cfg.Scanlines && !_minimal) target.Scanlines(_globeBox);

        target.EndFrame();
    }

    private void DrawWorld(ref Cam cam, float px, World w)
    {
        string tag = _inGlow ? ".g" : "";
        double t = Profile.Now;

        DrawLandFill(ref cam, px);
        t = Profile.Mark("land" + tag, t);

        DrawPopulation(ref cam, w);
        t = Profile.Mark("mist" + tag, t);

        // The graticule goes on after the land is filled, so it reads across continents as
        // well as ocean, and before the coastlines so those stay the brightest thing.
        if (_cfg.ShowGrid && !_inGlow) DrawGraticule(ref cam, px);
        t = Profile.Mark("grid" + tag, t);

        DrawCoastlines(ref cam, px);
        t = Profile.Mark("coast" + tag, t);

        DrawLimb(ref cam, px);
        if (!_inGlow) DrawRadarCoverage(ref cam, px, w);
        t = Profile.Mark("radar" + tag, t);

        DrawCities(ref cam, px, w);
        t = Profile.Mark("cities" + tag, t);

        DrawOrbitTracks(ref cam, w);

        DrawUnits(ref cam, px, w);
        t = Profile.Mark("units" + tag, t);

        DrawTracers(ref cam, px, w);
        DrawLasers(ref cam, w);
        DrawMissiles(ref cam, px, w);
        t = Profile.Mark("missiles" + tag, t);

        DrawBlasts(ref cam, px, w);
        Profile.Mark("blasts" + tag, t);
    }

    /// <summary>
    /// Stroke width for the layer being drawn. The glow layer is a quarter of the size, so a
    /// hairline there would come out under a pixel wide: GDI+ rounds that up to a solid line
    /// but Direct2D honours it and anti-aliases it away to nearly nothing, which is why the
    /// bloom looked so much weaker on the GPU. Holding a real floor keeps both honest, and a
    /// fatter source is what the blur wants anyway.
    /// </summary>
    private float Stroke(float width)
    {
        float px = _inGlow ? _glowScale : 1f;
        return Math.Max(_inGlow ? GlowStrokeFloor : 0.9f, width * px);
    }

    // ---- globe -------------------------------------------------------------

    private void DrawGraticule(ref Cam cam, float px)
    {
        float minorW = Stroke(1.0f);
        float majorW = Stroke(1.2f);
        Color minor = Col(Palette.Grid), major = Col(Palette.GridMajor);

        for (int lat = -75; lat <= 75; lat += 15)
        {
            bool isMajor = lat == 0;
            PolyReset();
            for (int d = 0; d <= 360; d += 5)
            {
                if (cam.ProjectLL(lat, d - 180f, out float sx, out float sy) > 0f) PolyAdd(sx, sy);
                else PolyFlush(isMajor ? major : minor, isMajor ? majorW : minorW);
            }
            PolyFlush(isMajor ? major : minor, isMajor ? majorW : minorW);
        }

        for (int lon = -180; lon < 180; lon += 15)
        {
            bool isMajor = lon is 0 or -180;
            PolyReset();
            // Run right up to the poles: stopping short leaves an undrawn cap that reads as
            // a dark hole once the camera tilts far enough to show it.
            for (int lat = -90; lat <= 90; lat += 5)
            {
                if (cam.ProjectLL(lat, lon, out float sx, out float sy) > 0f) PolyAdd(sx, sy);
                else PolyFlush(isMajor ? major : minor, isMajor ? majorW : minorW);
            }
            PolyFlush(isMajor ? major : minor, isMajor ? majorW : minorW);
        }
    }

    /// <summary>
    /// Projects every visible ring and fills its silhouette. Runs before the graticule, which
    /// would otherwise be buried under the land.
    /// </summary>
    private void DrawLandFill(ref Cam cam, float px)
    {
        cam.Axis(out float axc, out float ayc, out float azc);

        // The crisp fill is slightly translucent so the glow underneath still shows through.
        Color landColour = _inGlow
            ? Palette.Fade(Palette.Mix(Palette.Land, Palette.Coast, 0.20f), GlowAlpha)
            : Color.FromArgb(214, Palette.Land);

        float minSpan = 1.6f / Math.Max(1f, cam.R);

        Ring[] rings = Geo.Rings;
        _ringProj ??= new PointF[rings.Length][];
        _ringVis ??= new bool[rings.Length][];

        for (int ri = 0; ri < rings.Length; ri++)
        {
            Ring r = rings[ri];
            if (r.Span < minSpan) continue;
            if (r.Ax * axc + r.Ay * ayc + r.Az * azc <= r.MinDot) continue;

            PointF[] proj = _ringProj[ri] ??= new PointF[r.Count];
            bool[] vis = _ringVis[ri] ??= new bool[r.Count];

            int n = LandFill.Build(r, ref cam, ref _fill, proj, vis, out int visible);

            // A ring with nothing showing has no silhouette; its fill would be pure invention.
            if (visible == 0 || n < 3) continue;
            _target.Polygon(_fill, n, landColour);
        }
    }

    /// <summary>
    /// A haze over inhabited ground, built from a few concentric discs per city rather than a
    /// real radial gradient so both backends draw it identically and cheaply. Opacity follows
    /// the share of each city still alive, so a strike visibly thins the mist around it.
    /// </summary>
    private void DrawPopulation(ref Cam cam, World w)
    {
        if (_inGlow || !_cfg.ShowPopulation) return;

        const int rings = 8;
        const float perRing = 0.055f;

        foreach (City c in w.Cities)
        {
            if (c.Dead) continue;
            float depth = cam.Project(c.Px, c.Py, c.Pz, out float sx, out float sy);
            if (depth <= 0.03f) continue;

            float share = Math.Clamp(c.Alive / Math.Max(0.1f, c.Population), 0f, 1f);
            if (share <= 0.02f) continue;

            // Foreshorten toward the limb: a flat disc would otherwise spill off the edge.
            float degrees = 2.0f + MathF.Sqrt(c.Population) * 1.05f;
            float radius = cam.R * MathF.Sin(degrees * (float)Geo.D2R) * depth;
            if (radius < 1.5f) continue;

            Color ink = Palette.Fade(Palette.Mist, share * depth * perRing);
            for (int i = rings; i >= 1; i--)
            {
                float rr = radius * i / rings;
                _target.FillEllipse(sx, sy, rr, rr, ink);
            }
        }
    }

    /// <summary>Strokes the coastlines, reusing the projection the fill pass just did.</summary>
    private void DrawCoastlines(ref Cam cam, float px)
    {
        cam.Axis(out float axc, out float ayc, out float azc);

        var coast = new Color[7];
        float cw = Stroke(1.35f);
        for (int t = 0; t < 6; t++)
            coast[t] = Col(Palette.Mix(Palette.Coast, Palette.Faction[t], 0.75f));
        coast[6] = Col(Palette.CoastNeutral);

        // Anything under a pixel across is noise once it is drawn; skip it.
        float minSpan = 1.6f / Math.Max(1f, cam.R);
        Ring[] rings = Geo.Rings;
        if (_ringProj == null) return;

        for (int ri = 0; ri < rings.Length; ri++)
        {
            Ring r = rings[ri];
            if (r.Span < minSpan) continue;
            if (r.Ax * axc + r.Ay * ayc + r.Az * azc <= r.MinDot) continue;

            PointF[] proj = _ringProj[ri];
            bool[] vis = _ringVis[ri];
            if (proj == null) continue;

            int i = 0;
            while (i < r.Count)
            {
                if (!vis[i] || r.Ter[i] == 254) { i++; continue; }
                byte t = r.Ter[i];
                int start = i;
                while (i + 1 < r.Count && vis[i + 1] && r.Ter[i + 1] == t) i++;

                // Carry the run one vertex into the next bloc, and start the next run from that
                // same vertex. The segment straddling the border belongs to neither colour, so
                // without this every change of colour left a gap in the coastline.
                int end = i;
                bool joined = false;
                if (end + 1 < r.Count && vis[end + 1] && r.Ter[end + 1] != 254)
                {
                    end++;
                    joined = true;
                }

                int len = end - start + 1;
                if (len >= 2)
                {
                    PointF[] buf = RunBuf(len);
                    Array.Copy(proj, start, buf, 0, len);
                    _target.Polyline(buf, len, coast[t == 255 ? 6 : t], cw);
                }

                i = joined ? end : end + 1;
            }
        }
    }

    private void DrawLimb(ref Cam cam, float px)
        => _target.Ellipse(cam.Cx, cam.Cy, cam.R, cam.R, Col(Palette.Limb), Stroke(1.6f));

    // ---- world contents ----------------------------------------------------

    private void DrawRadarCoverage(ref Cam cam, float px, World w)
    {
        float width = Stroke(1f);
        foreach (Unit u in w.Units)
        {
            if (u.Kind != UnitKind.Radar || u.Fade <= 0f) continue;
            if (cam.Project(u.Px, u.Py, u.Pz, out _, out _) <= -0.25f) continue;

            Color c = Col(Palette.Fade(Palette.Faction[u.Faction], 0.16f * Math.Clamp(u.Fade, 0f, 1f)));
            SmallCircle(ref cam, c, width, u.Px, u.Py, u.Pz, 13f, 30);
        }
    }

    /// <summary>Traces a circle of constant angular radius on the sphere, skipping hidden arcs.</summary>
    private void SmallCircle(ref Cam cam, Color colour, float width,
                             float cxs, float cys, float czs, float radiusDeg, int steps,
                             float alt = 1f)
    {
        Geo.Basis(cxs, cys, czs, out float ux, out float uy, out float uz,
                                 out float vx, out float vy, out float vz);
        float rad = radiusDeg * (float)Geo.D2R;
        float ca = MathF.Cos(rad), sa = MathF.Sin(rad);

        PolyReset();
        for (int i = 0; i <= steps; i++)
        {
            float th = i * MathF.Tau / steps;
            float ct = MathF.Cos(th), st = MathF.Sin(th);
            float x = cxs * ca + (ux * ct + vx * st) * sa;
            float y = cys * ca + (uy * ct + vy * st) * sa;
            float z = czs * ca + (uz * ct + vz * st) * sa;
            if (cam.ProjectAlt(x, y, z, alt, out float sx, out float sy) > OrbitalHorizon(alt))
                PolyAdd(sx, sy);
            else PolyFlush(colour, width);
        }
        PolyFlush(colour, width);
    }

    private void DrawCities(ref Cam cam, float px, World w)
    {
        float scale = _ui * px;
        Color deadDot = Col(Color.FromArgb(150, 90, 100, 120));
        Color deadPen = Col(Color.FromArgb(160, 110, 120, 140));
        float deadW = Stroke(1.1f);

        foreach (City c in w.Cities)
        {
            if (cam.Project(c.Px, c.Py, c.Pz, out float sx, out float sy) <= 0f) continue;

            float rr = (1.1f + MathF.Sqrt(c.Population) * 0.5f) * scale;
            if (c.Dead)
            {
                float k = rr * 0.9f;
                _target.Line(sx - k, sy - k, sx + k, sy + k, deadPen, deadW);
                _target.Line(sx - k, sy + k, sx + k, sy - k, deadPen, deadW);
                continue;
            }

            Color dot = c.Territory >= 0 ? Col(Palette.Faction[c.Territory]) : deadDot;
            _target.FillEllipse(sx, sy, rr * 0.5f, rr * 0.5f, dot);

            if (c.Flash > 0f)
            {
                float fr = rr * (1f + 5f * (1f - c.Flash));
                _target.Ellipse(sx, sy, fr, fr, Col(Palette.Fade(Palette.Nuke, c.Flash)),
                                Stroke(1.6f));
            }

            if (_inGlow || !_cfg.ShowCityNames || _ui < 0.6f) continue;
            if (c.Population < 9f && c.Flash <= 0f) continue;

            Color tc = c.Territory >= 0 ? Palette.Faction[c.Territory] : Palette.Hud;
            _target.Text(c.Name, FontSlot.Small, Palette.Fade(tc, c.Flash > 0f ? 1f : 0.62f),
                         sx + rr * 0.8f, sy - _target.FontSize(FontSlot.Small) * 0.85f);
        }
    }

    /// <summary>
    /// The stretch of orbit immediately ahead of each platform, fading out with distance. A
    /// full great circle per platform rings the whole globe and buries everything under it;
    /// a leading arc says the same thing about where it is going and stays out of the way.
    /// </summary>
    private void DrawOrbitTracks(ref Cam cam, World w)
    {
        const int bands = 3;
        const int stepsPerBand = 14;
        const float span = 1.9f;           // radians of orbit shown, about 110 degrees

        foreach (Unit u in w.Units)
        {
            if (!u.IsOrbital || u.Fade <= 0f) continue;

            float fade = Math.Clamp(u.Fade, 0f, 1f);
            float dir = u.OrbitRate < 0f ? -1f : 1f;
            float width = Stroke(1f);
            float horizon = OrbitalHorizon(u.Alt);

            for (int band = 0; band < bands; band++)
            {
                float a0 = u.Theta + dir * span * band / bands;
                float a1 = u.Theta + dir * span * (band + 1) / bands;
                Color c = Col(Palette.Fade(Palette.Faction[u.Faction],
                                           fade * (0.30f - 0.085f * band)));

                PolyReset();
                for (int i = 0; i <= stepsPerBand; i++)
                {
                    float th = a0 + (a1 - a0) * i / stepsPerBand;
                    float ct = MathF.Cos(th), st = MathF.Sin(th);
                    float x = u.Ox * ct + u.Wx * st;
                    float y = u.Oy * ct + u.Wy * st;
                    float z = u.Oz * ct + u.Wz * st;
                    if (cam.ProjectAlt(x, y, z, u.Alt, out float sx, out float sy) > horizon)
                        PolyAdd(sx, sy);
                    else PolyFlush(c, width);
                }
                PolyFlush(c, width);
            }
        }
    }

    /// <summary>
    /// How far past the terminator something at this altitude can still be seen. A platform is
    /// high enough to clear the horizon well beyond the edge of the disc.
    /// </summary>
    private static float OrbitalHorizon(float alt)
        => -MathF.Sqrt(Math.Max(0f, 1f - 1f / (alt * alt)));

    private void DrawUnits(ref Cam cam, float px, World w)
    {
        float scale = _ui * px;
        float width = Stroke(1.3f);

        foreach (Unit u in w.Units)
        {
            if (u.Fade <= 0f) continue;
            float cull = u.IsOrbital ? OrbitalHorizon(u.Alt) : 0.02f;
            if (cam.ProjectAlt(u.Px, u.Py, u.Pz, u.Alt, out float sx, out float sy) <= cull) continue;

            float fade = Math.Clamp(u.Fade, 0f, 1f);
            Color line = Col(fade < 1f ? Palette.Fade(Palette.Faction[u.Faction], fade)
                                       : Palette.Faction[u.Faction]);
            Color fill = Col(Palette.Fade(Palette.Faction[u.Faction], 0.22f * fade));

            float cos = 1f, sin = 0f;
            if (!u.IsBase)  // bases never turn, so they skip the extra projection
            {
                float ang = ScreenAngle(ref cam, u.Lat, u.Lon, u.Heading) * (float)Geo.D2R;
                cos = MathF.Cos(ang); sin = MathF.Sin(ang);
            }
            DrawGlyph(u, line, fill, width, scale, sx, sy, cos, sin);
        }
    }

    /// <summary>Screen-space rotation, in degrees, for an icon on a given compass bearing.</summary>
    private static float ScreenAngle(ref Cam cam, float lat, float lon, float bearing)
    {
        Geo.Move(lat, lon, bearing, 0.6f, out float la, out float lo);
        cam.ProjectLL(lat, lon, out float x0, out float y0);
        cam.ProjectLL(la, lo, out float x1, out float y1);
        float dx = x1 - x0, dy = y1 - y0;
        if (dx * dx + dy * dy < 1e-6f) return 0f;
        return (float)(Math.Atan2(dy, dx) * Geo.R2D);
    }

    private void DrawGlyph(Unit u, Color line, Color fill, float w, float s,
                           float ox, float oy, float cos, float sin)
    {
        switch (u.Kind)
        {
            case UnitKind.Silo:
            {
                float k = 5.2f * s;
                Set(0, ox - k * 0.85f, oy + k * 0.7f);
                Set(1, ox + k * 0.85f, oy + k * 0.7f);
                Set(2, ox, oy - k);
                Set(3, ox - k * 0.85f, oy + k * 0.7f);
                _target.Polyline(Take(4), 4, line, w);
                _target.Line(ox - k, oy + k * 0.95f, ox + k, oy + k * 0.95f, line, w);
                break;
            }
            case UnitKind.Radar:
            {
                float k = 5.4f * s;
                // A dish arc, as a short polyline so every backend draws it the same way.
                for (int i = 0; i <= 6; i++)
                {
                    float a = (200f + i * (140f / 6f)) * (float)Geo.D2R;
                    Set(i, ox + MathF.Cos(a) * k, oy + MathF.Sin(a) * k);
                }
                _target.Polyline(Take(7), 7, line, w);
                float sweep = u.Sweep % MathF.Tau;
                _target.Line(ox, oy, ox + MathF.Cos(sweep) * k,
                             oy + MathF.Sin(sweep) * k * 0.55f - k * 0.2f, line, w);
                break;
            }
            case UnitKind.Airbase:
            {
                // A square field with a runway across it.
                float k = 4.8f * s;
                Set(0, ox - k, oy - k); Set(1, ox + k, oy - k);
                Set(2, ox + k, oy + k); Set(3, ox - k, oy + k); Set(4, ox - k, oy - k);
                _target.Polyline(Take(5), 5, line, w);
                _target.Line(ox - k * 0.7f, oy + k * 0.7f, ox + k * 0.7f, oy - k * 0.7f, line, w);
                break;
            }
            case UnitKind.Carrier:
            {
                float k = 7.2f * s, hh = k * 0.34f;
                Rot(0, ox, oy, cos, sin, -k, -hh); Rot(1, ox, oy, cos, sin, k, -hh);
                Rot(2, ox, oy, cos, sin, k, hh); Rot(3, ox, oy, cos, sin, -k, hh);
                _target.Polygon(Take(4), 4, fill);
                Rot(4, ox, oy, cos, sin, -k, -hh);
                _target.Polyline(Take(5), 5, line, w);
                Rot(0, ox, oy, cos, sin, -k * 0.55f, 0f); Rot(1, ox, oy, cos, sin, k * 0.8f, 0f);
                _target.Polyline(Take(2), 2, line, w);
                break;
            }
            case UnitKind.Battleship:
            {
                float k = 6.4f * s;
                Rot(0, ox, oy, cos, sin, k, 0f);
                Rot(1, ox, oy, cos, sin, k * 0.2f, -k * 0.36f);
                Rot(2, ox, oy, cos, sin, -k, -k * 0.3f);
                Rot(3, ox, oy, cos, sin, -k, k * 0.3f);
                Rot(4, ox, oy, cos, sin, k * 0.2f, k * 0.36f);
                _target.Polygon(Take(5), 5, fill);
                Rot(5, ox, oy, cos, sin, k, 0f);
                _target.Polyline(Take(6), 6, line, w);
                break;
            }
            case UnitKind.Sub:
            {
                float k = 6.2f * s;
                Rot(0, ox, oy, cos, sin, k, 0f);
                Rot(1, ox, oy, cos, sin, k * 0.1f, -k * 0.3f);
                Rot(2, ox, oy, cos, sin, -k, -k * 0.16f);
                Rot(3, ox, oy, cos, sin, -k, k * 0.16f);
                Rot(4, ox, oy, cos, sin, k * 0.1f, k * 0.3f);
                Rot(5, ox, oy, cos, sin, k, 0f);
                _target.Polyline(Take(6), 6, line, w);
                Rot(0, ox, oy, cos, sin, -k * 0.1f, -k * 0.28f);
                Rot(1, ox, oy, cos, sin, -k * 0.1f, -k * 0.66f);
                _target.Polyline(Take(2), 2, line, w);
                break;
            }
            case UnitKind.Bomber:
            {
                float k = 5.6f * s;
                Rot(0, ox, oy, cos, sin, k, 0f);
                Rot(1, ox, oy, cos, sin, -k * 0.5f, -k * 0.85f);
                Rot(2, ox, oy, cos, sin, -k * 0.15f, 0f);
                Rot(3, ox, oy, cos, sin, -k * 0.5f, k * 0.85f);
                Rot(4, ox, oy, cos, sin, k, 0f);
                _target.Polyline(Take(5), 5, line, w);
                break;
            }
            case UnitKind.Orbital:
            {
                float k = 7.4f * s;

                // A boxy bus with a solar wing either side.
                Rot(0, ox, oy, cos, sin, -k * 0.34f, -k * 0.42f);
                Rot(1, ox, oy, cos, sin, k * 0.34f, -k * 0.42f);
                Rot(2, ox, oy, cos, sin, k * 0.34f, k * 0.42f);
                Rot(3, ox, oy, cos, sin, -k * 0.34f, k * 0.42f);
                Rot(4, ox, oy, cos, sin, -k * 0.34f, -k * 0.42f);
                _target.Polygon(Take(4), 4, fill);
                _target.Polyline(Take(5), 5, line, w);

                for (int side = -1; side <= 1; side += 2)
                {
                    float inner = k * 0.4f * side, outer = k * 1.55f * side;
                    Rot(0, ox, oy, cos, sin, inner, -k * 0.52f);
                    Rot(1, ox, oy, cos, sin, outer, -k * 0.52f);
                    Rot(2, ox, oy, cos, sin, outer, k * 0.52f);
                    Rot(3, ox, oy, cos, sin, inner, k * 0.52f);
                    Rot(4, ox, oy, cos, sin, inner, -k * 0.52f);
                    _target.Polyline(Take(5), 5, line, w);
                }
                break;
            }
            case UnitKind.Fighter:
            {
                float k = 4.4f * s;
                Rot(0, ox, oy, cos, sin, k, 0f);
                Rot(1, ox, oy, cos, sin, -k * 0.6f, -k * 0.6f);
                Rot(2, ox, oy, cos, sin, -k * 0.25f, 0f);
                Rot(3, ox, oy, cos, sin, -k * 0.6f, k * 0.6f);
                Rot(4, ox, oy, cos, sin, k, 0f);
                _target.Polyline(Take(5), 5, line, w);
                break;
            }
        }
    }

    private void DrawTracers(ref Cam cam, float px, World w)
    {
        float width = Stroke(1.1f);
        foreach (Tracer t in w.Tracers)
        {
            if (cam.Project(t.Ax, t.Ay, t.Az, out float x0, out float y0) <= 0f) continue;
            if (cam.Project(t.Bx, t.By, t.Bz, out float x1, out float y1) <= 0f) continue;
            float a = Math.Clamp(t.Life / t.MaxLife, 0f, 1f);
            _target.Line(x0, y0, x1, y1,
                         Col(Palette.Fade(Palette.Hot(Palette.Faction[t.Faction], 0.5f), a)), width);
        }
    }

    /// <summary>
    /// A hot white core inside a wider sheath in the firing bloc's colour. Both passes draw it,
    /// so the glow layer picks it up and the beam blooms.
    /// </summary>
    private void DrawLasers(ref Cam cam, World w)
    {
        foreach (Laser l in w.Lasers)
        {
            if (cam.ProjectAlt(l.Ax, l.Ay, l.Az, l.Alt, out float x0, out float y0)
                <= OrbitalHorizon(l.Alt)) continue;
            if (cam.Project(l.Bx, l.By, l.Bz, out float x1, out float y1) <= 0f) continue;

            float a = Math.Clamp(l.Life / l.MaxLife, 0f, 1f);
            _target.Line(x0, y0, x1, y1,
                         Col(Palette.Fade(Palette.Hot(Palette.Faction[l.Faction], 0.45f), a * 0.85f)),
                         Stroke(5.5f));
            _target.Line(x0, y0, x1, y1, Col(Palette.Fade(Color.White, a)), Stroke(1.9f));
        }
    }

    private void DrawMissiles(ref Cam cam, float px, World w)
    {
        float scale = _ui * px;
        float thin = Stroke(1.1f);
        float hotW = Stroke(1.8f);
        Color interceptor = Col(Color.FromArgb(170, 210, 230, 255));
        Color asat = Col(Color.FromArgb(210, 255, 236, 180));
        Color head = Col(Palette.Nuke);

        foreach (Missile m in w.Missiles)
        {
            if (m.Nuke)
            {
                // The whole flown arc, brightening toward the warhead.
                float t = m.T;
                Color cold = Col(Palette.Fade(Palette.Faction[m.Faction], 0.26f));
                Color warm = Col(Palette.Fade(Palette.Faction[m.Faction], 0.62f));
                Color hot = Col(Palette.Hot(Palette.Faction[m.Faction], 0.6f));
                ArcBand(ref cam, m, 0f, t * 0.60f, cold, thin);
                ArcBand(ref cam, m, t * 0.60f, t * 0.87f, warm, thin);
                ArcBand(ref cam, m, t * 0.87f, t, hot, hotW);
            }
            else
            {
                Color streak = m.TargetPlatform != null ? asat : interceptor;
                PolyReset();
                for (int i = 0; i < m.TrailCount; i++)
                {
                    float depth = cam.ProjectAlt(m.Tx[i], m.Ty[i], m.Tz[i], m.Ta[i],
                                                 out float sx, out float sy);
                    if (depth > OrbitalHorizon(m.Ta[i])) PolyAdd(sx, sy);
                    else PolyFlush(streak, thin);
                }
                PolyFlush(streak, thin);
            }

            float headFloor = m.Nuke ? 0f : OrbitalHorizon(m.Alt);
            if (cam.ProjectAlt(m.Px, m.Py, m.Pz, m.Alt, out float hx, out float hy) > headFloor)
            {
                float r = (m.Nuke ? 2.1f : 1.3f) * scale;
                _target.FillEllipse(hx, hy, r, r, head);
            }
        }
    }

    /// <summary>Draws the stretch of a missile path between two flight fractions.</summary>
    private void ArcBand(ref Cam cam, Missile m, float u0, float u1, Color colour, float width)
    {
        if (u1 - u0 < 1e-4f) return;
        int steps = Math.Clamp((int)((u1 - u0) * 60f) + 1, 2, 26);

        PolyReset();
        for (int i = 0; i <= steps; i++)
        {
            float u = u0 + (u1 - u0) * i / steps;
            Missile.ArcPoint(m, u, out float x, out float y, out float z);
            float alt = 1f + m.Arc * MathF.Sin(MathF.PI * u);
            if (cam.ProjectAlt(x, y, z, alt, out float sx, out float sy) > 0f) PolyAdd(sx, sy);
            else PolyFlush(colour, width);
        }
        PolyFlush(colour, width);
    }

    private void DrawBlasts(ref Cam cam, float px, World w)
    {
        float scale = _ui * px;
        foreach (Blast b in w.Blasts)
        {
            float t = Math.Clamp(b.T, 0f, 1f);
            float radius = b.MaxRad * MathF.Sqrt(t) * (float)Geo.R2D;
            float alpha = 1f - t;

            if (cam.ProjectAlt(b.Px, b.Py, b.Pz, b.Alt, out float sx, out float sy)
                > OrbitalHorizon(b.Alt) - 0.2f)
            {
                float fade = MathF.Pow(1f - t, 1.5f);
                float core = fade * 26f * scale * (b.MaxRad > 0.03f ? 1f : 0.35f);

                // A broad, faint disc laid into the glow layer only. It is blurred on the way
                // back up, which is what gives a detonation the wide soft corona the game has
                // rather than a hard white dot.
                if (_inGlow && core > 0.4f)
                {
                    // Nested discs rather than one big one: a single filled circle upscaled out
                    // of the glow layer keeps a hard rim and reads as a grey plate.
                    for (int ring = 4; ring >= 1; ring--)
                    {
                        float halo = core * (0.8f + 0.62f * ring);
                        _target.FillEllipse(sx, sy, halo, halo,
                                            Col(Palette.Fade(Palette.Nuke, alpha * 0.24f)));
                    }
                }

                if (core > 0.4f)
                {
                    _target.FillEllipse(sx, sy, core, core, Col(Palette.Fade(
                        Palette.Mix(Palette.Nuke, Palette.Faction[b.Faction], t * 0.7f), alpha)));
                }
                if (t < 0.3f)
                {
                    float f = 1f - t / 0.3f;
                    float fr = core * (0.5f + 0.9f * f);
                    _target.FillEllipse(sx, sy, fr, fr, Col(Palette.Fade(Color.White, f)));
                }
            }

            SmallCircle(ref cam, Col(Palette.Fade(Palette.Hot(Palette.Faction[b.Faction], 0.6f), alpha)),
                        Stroke(2.4f), b.Px, b.Py, b.Pz, radius, 28, b.Alt);
        }
    }

    // ---- HUD ---------------------------------------------------------------

    /// <summary>
    /// Lays the alert banner into the glow layer so it blooms like everything else on the
    /// globe. It needs its own font slot because that layer is a quarter of the size.
    /// </summary>
    private void DrawBannerGlow()
    {
        if (_alertT <= 0f || string.IsNullOrEmpty(_alert)) return;
        float a = _alertT < 0.25f ? _alertT / 0.25f : 1f;
        float huge = _target.FontSize(FontSlot.Huge);
        var box = new RectangleF(0f,
                                 (_target.Height * 0.5f - _radius * 0.62f) * _glowScale,
                                 _target.Width * _glowScale,
                                 huge * 1.6f * _glowScale);
        _target.TextIn(_alert, FontSlot.HugeGlow, Col(Palette.Fade(_alertColour, a)), box, TextAlign.Centre);
    }

    private void DrawHud(World w)
    {
        float m = 22f * _ui;
        float fBig = _target.FontSize(FontSlot.Big);
        float fMed = _target.FontSize(FontSlot.Medium);

        // The readiness readout reads as one instrument, so it is all one colour.
        Color statusBright = Palette.Status;
        Color statusMid = Palette.Fade(Palette.Status, 0.72f);
        Color statusDim = Palette.Fade(Palette.Status, 0.42f);

        _target.Text("DEFCON", FontSlot.Big, statusBright, m, m);

        float cellW = 26f * _ui, cellH = 24f * _ui;
        float y = m + fBig * 1.5f;
        for (int i = 0; i < 5; i++)
        {
            int level = 5 - i;
            float x = m + i * (cellW + 4f * _ui);
            bool on = !w.Aftermath && level == w.Defcon;
            var cell = new RectangleF(x, y, cellW, cellH);
            if (on) _target.FillRect(cell, Palette.Fade(Palette.Status, 0.22f));
            _target.DrawRect(cell, on ? statusBright : statusDim, on ? 2f * _ui : 1f * _ui);
            _target.TextIn(level.ToString(), FontSlot.Medium,
                           on ? statusBright : statusDim, cell, TextAlign.Centre);
        }

        string sub = w.Aftermath
            ? $"RESET IN {Fmt(w.PhaseTime)}"
            : w.Defcon > 1 ? $"DEFCON {w.Defcon - 1} IN {Fmt(w.PhaseTime)}"
                           : $"EXCHANGE ENDS IN {Fmt(w.PhaseTime)}";
        _target.Text(sub, FontSlot.Medium, statusMid, m, y + cellH + 6f * _ui);
        _target.Text(w.ScenarioName, FontSlot.Small, statusDim,
                     m, y + cellH + 6f * _ui + fMed * 1.6f);

        _target.TextIn($"T+{Fmt(w.Clock)}", FontSlot.Big, statusBright,
                       new RectangleF(0, m, _target.Width - m, fBig * 1.4f), TextAlign.Right);
        _target.TextIn($"DEAD {w.TotalDead():#,##0.0}M", FontSlot.Medium, statusMid,
                       new RectangleF(0, m + fBig * 1.6f, _target.Width - m, fMed * 1.5f), TextAlign.Right);

        float ty = m + fBig * 1.6f + fMed * 3.2f;
        float rowH = fMed * 1.65f;
        for (int f = 0; f < World.Factions; f++)
        {
            if (!w.Playing[f]) continue;
            Color c = Palette.Faction[f];
            float rx = _target.Width - m;
            _target.FillRect(new RectangleF(rx - 9f * _ui, ty + rowH * 0.22f, 9f * _ui, fMed * 0.95f), c);

            int units = w.CountUnits(f, _ => true);
            _target.TextIn($"{Territories.ShortNames[f]}  {units,3}U  {w.Kills[f],7:#,##0.0}M",
                           FontSlot.Medium, Palette.Fade(c, 0.92f),
                           new RectangleF(0, ty, rx - 14f * _ui, rowH), TextAlign.Right);
            ty += rowH;
        }

        float ly = _target.Height - m - w.Log.Count * fMed * 1.5f;
        foreach (Ticker t in w.Log)
        {
            float a = Math.Clamp(1.6f - t.Age * 0.045f, 0.18f, 1f);
            _target.Text(t.Text, FontSlot.Medium, Palette.Fade(t.Colour, a), m, ly);
            ly += fMed * 1.5f;
        }

        if (_alertT > 0f)
        {
            float a = _alertT < 0.25f ? _alertT / 0.25f : 1f;
            _target.TextIn(_alert, FontSlot.Huge, Palette.Fade(_alertColour, a * 0.9f),
                           new RectangleF(0, _target.Height * 0.5f - _radius * 0.62f,
                                          _target.Width, _target.FontSize(FontSlot.Huge) * 1.6f),
                           TextAlign.Centre);
        }

        if (w.Aftermath) DrawScoreboard(w);
    }

    private void DrawScoreboard(World w)
    {
        int[] order = Enumerable.Range(0, World.Factions)
                                .Where(f => w.Playing[f])
                                .OrderByDescending(f => w.Kills[f]).ToArray();

        float fMed = _target.FontSize(FontSlot.Medium);
        float rowH = fMed * 1.75f;
        float bw = 440f * _ui, bh = rowH * (order.Length + 2.6f);
        float bx = (_target.Width - bw) * 0.5f, by = _target.Height * 0.5f + _radius * 0.28f;
        if (by + bh > _target.Height) by = _target.Height - bh - 12f * _ui;

        _target.FillRect(new RectangleF(bx, by, bw, bh), Color.FromArgb(180, 2, 8, 20));
        _target.DrawRect(new RectangleF(bx, by, bw, bh), Palette.Fade(Palette.Limb, 0.7f), 1.2f * _ui);

        _target.Text("FINAL TALLY", FontSlot.Medium, Palette.Hud, bx + 14f * _ui, by + 10f * _ui);
        _target.TextIn("KILLED      LOST", FontSlot.Medium, Palette.HudDim,
                       new RectangleF(0, by + 10f * _ui, bx + bw - 14f * _ui, rowH), TextAlign.Right);

        float y = by + 10f * _ui + rowH * 1.25f;
        foreach (int f in order)
        {
            _target.Text(Territories.Names[f], FontSlot.Medium, Palette.Faction[f], bx + 14f * _ui, y);
            _target.TextIn($"{w.Kills[f],8:#,##0.0}M {w.Losses[f],8:#,##0.0}M", FontSlot.Medium,
                           Palette.Faction[f],
                           new RectangleF(0, y, bx + bw - 14f * _ui, rowH), TextAlign.Right);
            y += rowH;
        }
    }

    private static string Fmt(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int s = (int)seconds;
        return $"{s / 60:00}:{s % 60:00}";
    }

    // ---- small helpers -----------------------------------------------------

    private void PolyReset() => _polyN = 0;

    private void PolyAdd(float x, float y)
    {
        if (_polyN == _poly.Length) Array.Resize(ref _poly, _poly.Length * 2);
        _poly[_polyN++] = new PointF(x, y);
    }

    private void PolyFlush(Color colour, float width)
    {
        if (_polyN >= 2) _target.Polyline(_poly, _polyN, colour, width);
        _polyN = 0;
    }

    private PointF[] RunBuf(int n)
        => n < _runCache.Length ? _runCache[n] ??= new PointF[n] : new PointF[n];

    private void Set(int i, float x, float y) => _glyph[i] = new PointF(x, y);

    private void Rot(int i, float ox, float oy, float cos, float sin, float x, float y)
        => _glyph[i] = new PointF(ox + x * cos - y * sin, oy + x * sin + y * cos);

    private PointF[] Take(int n)
    {
        PointF[] buf = RunBuf(n);
        Array.Copy(_glyph, buf, n);
        return buf;
    }
}
