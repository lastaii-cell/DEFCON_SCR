"""Convert Natural Earth ne_50m_land.geojson into a compact binary ring blob.

Format (little-endian):
    magic  4s   "DFGL"
    ver    u16  1
    rings  u16
    per ring:
        count u16
        count * (i16 lon, i16 lat)   quantised at SCALE units per degree

Usage:
    python make_land_bin.py ne_50m_land.geojson ../Assets/land.bin

The input is Natural Earth 1:50m physical land, which is public domain. It is not
committed to this repository because it is 1.6 MB and never changes; Assets/land.bin
is the 50 KB product and is committed, so a normal build needs neither this script
nor its input. Fetch the GeoJSON from:

    https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_50m_land.geojson

or from https://www.naturalearthdata.com/downloads/50m-physical-vectors/50m-land/
"""
import json
import math
import struct
import sys

SCALE = 180.0          # int16 units per degree -> +-180 deg fits in +-32400
TOLERANCE = 0.20       # Douglas-Peucker tolerance, degrees
MIN_SPAN = 0.45        # drop rings whose bbox diagonal is smaller than this


def dp(points, tol):
    """Iterative Douglas-Peucker. points: list of (x, y)."""
    n = len(points)
    if n < 3:
        return points
    keep = [False] * n
    keep[0] = keep[-1] = True
    stack = [(0, n - 1)]
    tol2 = tol * tol
    while stack:
        lo, hi = stack.pop()
        if hi <= lo + 1:
            continue
        ax, ay = points[lo]
        bx, by = points[hi]
        dx, dy = bx - ax, by - ay
        seg2 = dx * dx + dy * dy
        best, besti = -1.0, -1
        for i in range(lo + 1, hi):
            px, py = points[i]
            if seg2 <= 0.0:
                d2 = (px - ax) ** 2 + (py - ay) ** 2
            else:
                t = ((px - ax) * dx + (py - ay) * dy) / seg2
                t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
                d2 = (px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2
            if d2 > best:
                best, besti = d2, i
        if best > tol2:
            keep[besti] = True
            stack.append((lo, besti))
            stack.append((besti, hi))
    return [p for p, k in zip(points, keep) if k]


def rings_of(geom):
    t = geom["type"]
    if t == "Polygon":
        return list(geom["coordinates"])
    if t == "MultiPolygon":
        return [r for poly in geom["coordinates"] for r in poly]
    return []


def main(src, dst):
    with open(src, "r", encoding="utf-8") as fh:
        data = json.load(fh)

    out, raw_pts, kept_pts = [], 0, 0
    for feat in data["features"]:
        for ring in rings_of(feat["geometry"]):
            pts = [(float(c[0]), float(c[1])) for c in ring]
            raw_pts += len(pts)
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            span = math.hypot(max(xs) - min(xs), max(ys) - min(ys))
            if span < MIN_SPAN:
                continue
            # Scale tolerance down for small features so islands keep their shape.
            tol = TOLERANCE * min(1.0, span / 12.0) if span < 12.0 else TOLERANCE
            simp = dp(pts, max(tol, 0.02))
            if len(simp) < 4:
                continue
            if simp[0] != simp[-1]:
                simp.append(simp[0])
            if len(simp) > 65535:
                simp = simp[:65535]
            kept_pts += len(simp)
            out.append(simp)

    out.sort(key=len, reverse=True)

    with open(dst, "wb") as fh:
        fh.write(b"DFGL")
        fh.write(struct.pack("<HH", 1, len(out)))
        for ring in out:
            fh.write(struct.pack("<H", len(ring)))
            for lon, lat in ring:
                lon = max(-180.0, min(180.0, lon))
                lat = max(-90.0, min(90.0, lat))
                fh.write(struct.pack("<hh", round(lon * SCALE), round(lat * SCALE)))

    print(f"rings {len(out)}  points {raw_pts} -> {kept_pts}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
