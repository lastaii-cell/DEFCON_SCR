namespace DefconSaver;

/// <summary>
/// Turns a coastline ring into the screen-space silhouette polygon for one camera.
///
/// Orthographic projection is two-to-one: a point on the far side of the globe lands inside
/// the same disc as its near-side counterpart. Hidden vertices are therefore pushed radially
/// out to the limb, which closes the silhouette without any clipping maths. Two things have to
/// be guarded, and both caused visible artifacts before they were:
///
///   * a vertex close to the point directly behind the camera has no meaningful azimuth, so
///     keeping it let a coastline wind repeatedly around the limb, and
///   * consecutive limb vertices far apart in azimuth were joined by a chord straight across
///     the disc instead of following the edge.
/// </summary>
public static class LandFill
{
    /// <summary>
    /// How near the limb a hidden vertex has to project to be worth keeping, as a fraction of the
    /// globe radius. A vertex just over the horizon really does belong on the silhouette, because
    /// the land continues past the edge. One far behind the globe does not: it contributes nothing
    /// a viewer could see, and its azimuth is what lets a coastline wind around the limb several
    /// times over. The gap left by dropping it is bridged by an arc along the edge instead.
    /// </summary>
    public const float HorizonBand = 0.90f;

    private const float ArcStep = 0.06f;        // radians between inserted limb points

    /// <summary>
    /// Fills <paramref name="fill"/> with the silhouette polygon and returns its length.
    /// <paramref name="proj"/> and <paramref name="seen"/> receive the projected point and
    /// visibility of every ring vertex, which the coastline stroke pass reads afterwards.
    /// Both belong to the caller, so two threads can project the same ring at once.
    /// </summary>
    public static int Build(Ring r, ref Cam cam, ref PointF[] fill,
                            PointF[] proj, bool[] seen, out int visible)
    {
        int n = 0;
        visible = 0;
        bool prevOnLimb = false;
        float prevAz = 0f;
        float keepBeyond = cam.R * HorizonBand;

        for (int j = 0; j < r.Count; j++)
        {
            float depth = cam.Project(r.Px[j], r.Py[j], r.Pz[j], out float sx, out float sy);
            bool vis = depth > 0f;
            seen[j] = vis;

            if (vis)
            {
                proj[j] = new PointF(sx, sy);
                Add(ref fill, ref n, sx, sy);
                prevOnLimb = false;
                visible++;
                continue;
            }

            float dx = sx - cam.Cx, dy = sy - cam.Cy;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < keepBeyond) { proj[j] = new PointF(sx, sy); continue; }

            float k = cam.R / len;
            float lx = cam.Cx + dx * k, ly = cam.Cy + dy * k;
            proj[j] = new PointF(lx, ly);

            float az = MathF.Atan2(dy, dx);
            if (prevOnLimb) Arc(ref cam, prevAz, az, ref fill, ref n);
            Add(ref fill, ref n, lx, ly);
            prevAz = az;
            prevOnLimb = true;
        }

        return n;
    }

    /// <summary>Walks the limb from one azimuth to another the short way round.</summary>
    private static void Arc(ref Cam cam, float a0, float a1, ref PointF[] fill, ref int n)
    {
        float d = a1 - a0;
        if (d > MathF.PI) d -= MathF.Tau;
        else if (d < -MathF.PI) d += MathF.Tau;

        int steps = (int)(MathF.Abs(d) / ArcStep);
        for (int k = 1; k < steps; k++)
        {
            float a = a0 + d * k / steps;
            Add(ref fill, ref n, cam.Cx + cam.R * MathF.Cos(a), cam.Cy + cam.R * MathF.Sin(a));
        }
    }

    private static void Add(ref PointF[] fill, ref int n, float x, float y)
    {
        if (n == fill.Length) Array.Resize(ref fill, fill.Length * 2);
        fill[n++] = new PointF(x, y);
    }

    /// <summary>Crossing count and winding number of a polygon about a point, for diagnostics.</summary>
    public static (int Crossings, int Winding) Classify(PointF[] poly, int n, float px, float py)
    {
        int crossings = 0, winding = 0;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float yi = poly[i].Y, yj = poly[j].Y;
            if (yi <= py)
            {
                if (yj > py && Side(poly[i], poly[j], px, py) > 0) winding++;
            }
            else if (yj <= py && Side(poly[i], poly[j], px, py) < 0) winding--;

            if (yi > py != yj > py)
            {
                float t = (py - yi) / (yj - yi);
                if (px < poly[i].X + t * (poly[j].X - poly[i].X)) crossings++;
            }
        }
        return (crossings, winding);
    }

    private static float Side(PointF a, PointF b, float px, float py)
        => (b.X - a.X) * (py - a.Y) - (px - a.X) * (b.Y - a.Y);
}
