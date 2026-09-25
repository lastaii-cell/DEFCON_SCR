using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DefconSaver;

/// <summary>An orthographic camera looking at a unit sphere.</summary>
public struct Cam
{
    public float Cx, Cy, R;
    public float SinLon, CosLon, SinLat, CosLat;

    public void Set(float cx, float cy, float r, float lonDeg, float latDeg)
    {
        Cx = cx; Cy = cy; R = r;
        double lon = lonDeg * Geo.D2R, lat = latDeg * Geo.D2R;
        SinLon = (float)Math.Sin(lon); CosLon = (float)Math.Cos(lon);
        SinLat = (float)Math.Sin(lat); CosLat = (float)Math.Cos(lat);
    }

    /// <summary>Projects a unit-sphere vector. The return value is the facing term: &gt; 0 means visible.</summary>
    public float Project(float px, float py, float pz, out float sx, out float sy)
    {
        float a = px * CosLon + py * SinLon;
        float b = py * CosLon - px * SinLon;
        float depth = a * CosLat + pz * SinLat;
        float up = pz * CosLat - a * SinLat;
        sx = Cx + R * b;
        sy = Cy - R * up;
        return depth;
    }

    /// <summary>As <see cref="Project"/>, but with the point lifted to a radius of <paramref name="alt"/>.</summary>
    public float ProjectAlt(float px, float py, float pz, float alt, out float sx, out float sy)
    {
        float a = px * CosLon + py * SinLon;
        float b = py * CosLon - px * SinLon;
        float depth = a * CosLat + pz * SinLat;
        float up = pz * CosLat - a * SinLat;
        sx = Cx + R * b * alt;
        sy = Cy - R * up * alt;
        return depth;
    }

    public float ProjectLL(float latDeg, float lonDeg, out float sx, out float sy)
    {
        Geo.ToVec(latDeg, lonDeg, out float x, out float y, out float z);
        return Project(x, y, z, out sx, out sy);
    }

    /// <summary>Clamps a point behind the limb onto the limb circle, so silhouette fills stay closed.</summary>
    public void ProjectClamped(float px, float py, float pz, out float sx, out float sy)
    {
        float depth = Project(px, py, pz, out sx, out sy);
        if (depth >= 0f) return;
        float dx = sx - Cx, dy = sy - Cy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) { sx = Cx + R; sy = Cy; return; }
        float k = R / len;
        sx = Cx + dx * k;
        sy = Cy + dy * k;
    }

    /// <summary>The direction the camera looks from, as a unit vector.</summary>
    public void Axis(out float x, out float y, out float z)
    {
        x = CosLat * CosLon;
        y = CosLat * SinLon;
        z = SinLat;
    }
}

/// <summary>One closed coastline ring, pre-resolved to unit-sphere vectors.</summary>
public sealed class Ring
{
    public float[] Px, Py, Pz;
    public int Count;
    public float Ax, Ay, Az;   // bounding-cap axis
    public float MinDot;       // wholly hidden when dot(axis, camAxis) <= MinDot
    public int Territory = -1;
    public byte[] Ter;         // per-point bloc, 255 for unclaimed
    public float Span;         // angular radius, radians
}

public static class Geo
{
    public const double D2R = Math.PI / 180.0;
    public const double R2D = 180.0 / Math.PI;

    public const int MaskW = 1440;
    public const int MaskH = 720;

    public static Ring[] Rings { get; private set; } = Array.Empty<Ring>();
    private static bool[] _land = Array.Empty<bool>();

    public static void ToVec(float latDeg, float lonDeg, out float x, out float y, out float z)
    {
        double la = latDeg * D2R, lo = lonDeg * D2R;
        double cl = Math.Cos(la);
        x = (float)(cl * Math.Cos(lo));
        y = (float)(cl * Math.Sin(lo));
        z = (float)Math.Sin(la);
    }

    public static void ToLatLon(float x, float y, float z, out float lat, out float lon)
    {
        lat = (float)(Math.Asin(Math.Clamp(z, -1f, 1f)) * R2D);
        lon = (float)(Math.Atan2(y, x) * R2D);
    }

    /// <summary>Great-circle distance in degrees.</summary>
    public static float Dist(float lat1, float lon1, float lat2, float lon2)
    {
        ToVec(lat1, lon1, out float ax, out float ay, out float az);
        ToVec(lat2, lon2, out float bx, out float by, out float bz);
        float d = Math.Clamp(ax * bx + ay * by + az * bz, -1f, 1f);
        return (float)(Math.Acos(d) * R2D);
    }

    /// <summary>Initial bearing from one point to another, in degrees clockwise from north.</summary>
    public static float Bearing(float lat1, float lon1, float lat2, float lon2)
    {
        double p1 = lat1 * D2R, p2 = lat2 * D2R, dl = (lon2 - lon1) * D2R;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return (float)(Math.Atan2(y, x) * R2D);
    }

    /// <summary>Moves a point <paramref name="distDeg"/> degrees along a bearing.</summary>
    public static void Move(float lat, float lon, float bearing, float distDeg,
                            out float outLat, out float outLon)
    {
        double p1 = lat * D2R, l1 = lon * D2R, b = bearing * D2R, d = distDeg * D2R;
        double sp = Math.Sin(p1), cp = Math.Cos(p1), sd = Math.Sin(d), cd = Math.Cos(d);
        double p2 = Math.Asin(Math.Clamp(sp * cd + cp * sd * Math.Cos(b), -1.0, 1.0));
        double l2 = l1 + Math.Atan2(Math.Sin(b) * sd * cp, cd - sp * Math.Sin(p2));
        outLat = (float)(p2 * R2D);
        outLon = Wrap180((float)(l2 * R2D));
    }

    public static float Wrap180(float lon)
    {
        lon %= 360f;
        if (lon > 180f) lon -= 360f;
        else if (lon < -180f) lon += 360f;
        return lon;
    }

    public static bool IsLand(float lat, float lon)
    {
        if (_land.Length == 0) return false;
        int mx = (int)((Wrap180(lon) + 180f) * (MaskW / 360f));
        int my = (int)((90f - lat) * (MaskH / 180f));
        if (mx < 0) mx = 0; else if (mx >= MaskW) mx = MaskW - 1;
        if (my < 0) my = 0; else if (my >= MaskH) my = MaskH - 1;
        return _land[my * MaskW + mx];
    }

    public static bool IsSea(float lat, float lon) => !IsLand(lat, lon);

    /// <summary>True when every point within <paramref name="marginDeg"/> of here is open water.</summary>
    public static bool IsOpenSea(float lat, float lon, float marginDeg)
    {
        if (IsLand(lat, lon)) return false;
        for (int b = 0; b < 8; b++)
        {
            Move(lat, lon, b * 45f, marginDeg, out float la, out float lo);
            if (IsLand(la, lo)) return false;
        }
        return true;
    }

    // ---- loading -----------------------------------------------------------

    public static void Load()
    {
        if (Rings.Length > 0) return;
        Rings = ReadRings();
        _land = BuildMask(Rings);
        foreach (Ring r in Rings)
        {
            ToLatLon(r.Ax, r.Ay, r.Az, out float la, out float lo);
            r.Territory = Territories.At(la, lo);

            // Afro-Eurasia is a single ring, so the bloc has to be resolved per point:
            // that is what lets each coastline glow in its own colour.
            r.Ter = new byte[r.Count];
            for (int j = 0; j < r.Count; j++)
            {
                ToLatLon(r.Px[j], r.Py[j], r.Pz[j], out float pla, out float plo);
                // Antarctica's ring detours to the pole to close itself; 254 means "fill but
                // do not stroke", which keeps that seam from drawing a line across the ice.
                if (MathF.Abs(pla) > 89f) { r.Ter[j] = 254; continue; }
                int t = Territories.At(pla, plo);
                r.Ter[j] = (byte)(t < 0 ? 255 : t);
            }
        }
    }

    /// <summary>An orthonormal pair spanning the tangent plane at a point on the sphere.</summary>
    public static void Basis(float px, float py, float pz,
                             out float ux, out float uy, out float uz,
                             out float vx, out float vy, out float vz)
    {
        float ax = MathF.Abs(pz) < 0.9f ? 0f : 1f;
        const float ay = 0f;
        float az = MathF.Abs(pz) < 0.9f ? 1f : 0f;
        ux = ay * pz - az * py;
        uy = az * px - ax * pz;
        uz = ax * py - ay * px;
        float l = MathF.Sqrt(ux * ux + uy * uy + uz * uz);
        if (l < 1e-6f) { ux = 1f; uy = 0f; uz = 0f; l = 1f; }
        ux /= l; uy /= l; uz /= l;
        vx = py * uz - pz * uy;
        vy = pz * ux - px * uz;
        vz = px * uy - py * ux;
    }

    private static Ring[] ReadRings()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream s = asm.GetManifestResourceStream("DefconSaver.Assets.land.bin")
                         ?? throw new InvalidOperationException("land.bin resource missing");
        using var br = new BinaryReader(s);

        if (br.ReadUInt32() != 0x4C474644u) throw new InvalidDataException("bad land.bin magic");
        br.ReadUInt16();                       // format version
        int ringCount = br.ReadUInt16();

        var rings = new Ring[ringCount];
        const float inv = 1f / 180f;

        for (int i = 0; i < ringCount; i++)
        {
            int n = br.ReadUInt16();
            var ring = new Ring { Count = n, Px = new float[n], Py = new float[n], Pz = new float[n] };
            float ax = 0, ay = 0, az = 0;
            for (int j = 0; j < n; j++)
            {
                float lon = br.ReadInt16() * inv;
                float lat = br.ReadInt16() * inv;
                ToVec(lat, lon, out float x, out float y, out float z);
                ring.Px[j] = x; ring.Py[j] = y; ring.Pz[j] = z;
                ax += x; ay += y; az += z;
            }
            float len = MathF.Sqrt(ax * ax + ay * ay + az * az);
            if (len < 1e-6f) { ax = ring.Px[0]; ay = ring.Py[0]; az = ring.Pz[0]; len = 1f; }
            ring.Ax = ax / len; ring.Ay = ay / len; ring.Az = az / len;

            float minDot = 1f;
            for (int j = 0; j < n; j++)
            {
                float d = ring.Ax * ring.Px[j] + ring.Ay * ring.Py[j] + ring.Az * ring.Pz[j];
                if (d < minDot) minDot = d;
            }
            ring.Span = MathF.Acos(Math.Clamp(minDot, -1f, 1f));
            ring.MinDot = -MathF.Sin(Math.Min(ring.Span, MathF.PI * 0.5f)) - 0.02f;
            rings[i] = ring;
        }
        return rings;
    }

    /// <summary>Rasterises the rings into an equirectangular land/sea mask.</summary>
    private static bool[] BuildMask(Ring[] rings)
    {
        var mask = new bool[MaskW * MaskH];
        using var bmp = new Bitmap(MaskW, MaskH, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            g.SmoothingMode = SmoothingMode.None;
            using var brush = new SolidBrush(Color.White);

            foreach (Ring r in rings)
            {
                if (r.Count < 3) continue;
                var pts = new PointF[r.Count];
                bool wraps = false;
                float prev = 0f;
                for (int j = 0; j < r.Count; j++)
                {
                    ToLatLon(r.Px[j], r.Py[j], r.Pz[j], out float la, out float lo);
                    if (j > 0 && Math.Abs(lo - prev) > 180f) wraps = true;
                    prev = lo;
                    pts[j] = new PointF((lo + 180f) * (MaskW / 360f), (90f - la) * (MaskH / 180f));
                }
                g.FillPolygon(brush, pts, FillMode.Winding);

                // Rings straddling the antimeridian are drawn a second time, shifted,
                // so the half that wrapped off one edge still lands on the mask.
                if (wraps)
                {
                    var shifted = new PointF[r.Count];
                    for (int j = 0; j < r.Count; j++)
                    {
                        float x = pts[j].X;
                        shifted[j] = new PointF(x < MaskW * 0.5f ? x + MaskW : x - MaskW, pts[j].Y);
                    }
                    g.FillPolygon(brush, shifted, FillMode.Winding);
                }
            }
        }

        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, MaskW, MaskH),
                                     ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[bd.Stride];
            for (int y = 0; y < MaskH; y++)
            {
                Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, bd.Stride);
                int o = y * MaskW;
                for (int x = 0; x < MaskW; x++) mask[o + x] = row[x * 3] > 100;
            }
        }
        finally { bmp.UnlockBits(bd); }
        return mask;
    }
}
