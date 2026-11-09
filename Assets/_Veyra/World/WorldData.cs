using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.World
{
    // JSON shapes written by Tools/World/build_world_data.py (Resources/World/*.json).
    [Serializable] public sealed class RoadJson { public string id, name, cls; public bool closed; public float width; public int lanes; public float speedKmh, length; public float[] points; public int[] structure; }
    [Serializable] public sealed class RoadsFile { public RoadJson[] roads; }
    [Serializable] public sealed class RouteJson { public string id, name, kind, district; public bool circuit; public int laps; public float designLength, length; public string[] roads; public float[] points; public int[] structure; }
    [Serializable] public sealed class RoutesFile { public RouteJson[] routes; }
    [Serializable] public sealed class LocationJson { public string id, name, category, district, road, crew, description; public float x, y, z; public Vector3 Position => new Vector3(x, y, z); }
    [Serializable] public sealed class LocationsFile { public LocationJson[] locations; }
    [Serializable] public sealed class DistrictJson { public string id, name, color; public float[] polygon; }
    [Serializable] public sealed class DistrictsFile { public DistrictJson[] districts; public float[] coastline, river, lake; public float lakeLevel; }

    public enum StructureKind { None = 0, Bridge = 1, Tunnel = 2, Elevated = 3 }

    /// <summary>
    /// A 3D polyline with arc-length parameterisation (roads, race routes, AI lines).
    /// Projection is incremental: pass the previous index as a hint so per-frame cost stays tiny.
    /// </summary>
    public sealed class PolylinePath
    {
        public readonly Vector3[] Points;
        public readonly float[] S;
        public readonly bool Closed;
        public readonly float Length;

        public PolylinePath(Vector3[] pts, bool closed)
        {
            Points = pts;
            Closed = closed;
            S = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) S[i] = S[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            Length = S[pts.Length - 1] + (closed ? Vector3.Distance(pts[pts.Length - 1], pts[0]) : 0f);
        }

        public static PolylinePath FromFlat(float[] flat, bool closed)
        {
            var pts = new Vector3[flat.Length / 3];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
            return new PolylinePath(pts, closed);
        }

        public int Count => Points.Length;

        public float Wrap(float s)
        {
            if (!Closed) return Mathf.Clamp(s, 0f, Length);
            s %= Length;
            return s < 0f ? s + Length : s;
        }

        /// <summary>Index of the segment containing arc length s (binary search).</summary>
        public int SegmentAt(float s)
        {
            s = Wrap(s);
            int lo = 0, hi = Points.Length - 1;
            if (s >= S[hi]) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (S[mid] <= s) lo = mid; else hi = mid;
            }
            return lo;
        }

        public Vector3 PositionAt(float s)
        {
            s = Wrap(s);
            int i = SegmentAt(s);
            int j = i + 1;
            float segEnd;
            if (j >= Points.Length)
            {
                if (!Closed) return Points[i];
                j = 0;
                segEnd = Length;
            }
            else segEnd = S[j];
            float t = Mathf.InverseLerp(S[i], segEnd, s);
            return Vector3.LerpUnclamped(Points[i], Points[j], t);
        }

        public Vector3 TangentAt(float s)
        {
            float a = s - 4f, b = s + 4f;
            if (!Closed) { a = Mathf.Max(0f, a); b = Mathf.Min(Length, b); }
            var d = PositionAt(b) - PositionAt(a);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>Signed horizontal curvature (1/m) around s, sampled over +-span metres.</summary>
        public float CurvatureAt(float s, float span = 10f)
        {
            Vector3 a = PositionAt(s - span), b = PositionAt(s), c = PositionAt(s + span);
            Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), bc = new Vector2(c.x - b.x, c.z - b.z);
            float la = ab.magnitude, lb = bc.magnitude;
            if (la < 1e-3f || lb < 1e-3f) return 0f;
            float cross = ab.x * bc.y - ab.y * bc.x;
            float angle = Mathf.Asin(Mathf.Clamp(cross / (la * lb), -1f, 1f));
            return angle / (0.5f * (la + lb));
        }

        /// <summary>
        /// Closest point to <paramref name="p"/> (horizontal), searching +-window segments around the hint.
        /// Returns arc length; hint is updated. window &lt;= 0 searches everything.
        /// </summary>
        public float Project(Vector3 p, ref int hint, int window = 40)
        {
            int n = Points.Length;
            int segCount = Closed ? n : n - 1;
            int from, to;
            if (window <= 0 || segCount <= window * 2) { from = 0; to = segCount - 1; }
            else { from = hint - window; to = hint + window; }
            float best = float.MaxValue, bestS = 0f;
            int bestI = hint;
            for (int k = from; k <= to; k++)
            {
                int i = k;
                if (Closed) { i %= segCount; if (i < 0) i += segCount; }
                else if (i < 0 || i >= segCount) continue;
                int j = (i + 1) % n;
                Vector3 a = Points[i], b = Points[j];
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
                Vector2 ap = new Vector2(p.x - a.x, p.z - a.z);
                float L2 = ab.sqrMagnitude;
                float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / L2) : 0f;
                Vector2 q = new Vector2(a.x, a.z) + ab * t;
                float d = (new Vector2(p.x, p.z) - q).sqrMagnitude + 0.25f * Mathf.Abs(Mathf.Lerp(a.y, b.y, t) - p.y);
                if (d < best)
                {
                    best = d;
                    bestI = i;
                    float segLen = Mathf.Sqrt(L2);
                    bestS = S[i] + t * segLen;
                }
            }
            hint = bestI;
            return bestS;
        }

        /// <summary>Signed lateral offset of p from the path at arc length s (+ = right of travel).</summary>
        public float LateralOffset(Vector3 p, float s)
        {
            var c = PositionAt(s);
            var t = TangentAt(s);
            var right = new Vector3(t.z, 0f, -t.x).normalized;
            return Vector3.Dot(p - c, right);
        }
    }

    public sealed class RoadPath
    {
        public RoadJson Data;
        public PolylinePath Path;
        public float HalfWidth => Data.width * 0.5f;
        public bool Divided => Data.cls == "MW" || Data.cls == "EX";
        public StructureKind StructureAt(int index) => Data.structure != null && index < Data.structure.Length ? (StructureKind)Data.structure[index] : StructureKind.None;
    }

    public sealed class RoutePath
    {
        public RouteJson Data;
        public PolylinePath Path;
    }

    /// <summary>Loads and caches the world data once; all queries are main-thread.</summary>
    public static class WorldData
    {
        static List<RoadPath> roads;
        static List<RoutePath> routes;
        static List<LocationJson> locations;
        static DistrictsFile districts;
        static Dictionary<long, List<(int road, int seg)>> roadGrid;
        const float GridCell = 40f;

        public static IReadOnlyList<RoadPath> Roads { get { EnsureLoaded(); return roads; } }
        public static IReadOnlyList<RoutePath> Routes { get { EnsureLoaded(); return routes; } }
        public static IReadOnlyList<LocationJson> Locations { get { EnsureLoaded(); return locations; } }
        public static DistrictsFile Districts { get { EnsureLoaded(); return districts; } }

        public static readonly Rect Bounds = new Rect(-4000f, -3000f, 8000f, 6000f);

        public static void EnsureLoaded()
        {
            if (roads != null) return;
            roads = new List<RoadPath>();
            routes = new List<RoutePath>();
            locations = new List<LocationJson>();
            var rf = Load<RoadsFile>("World/roads");
            if (rf?.roads != null)
                foreach (var r in rf.roads) roads.Add(new RoadPath { Data = r, Path = PolylinePath.FromFlat(r.points, r.closed) });
            var tf = Load<RoutesFile>("World/routes");
            if (tf?.routes != null)
                foreach (var r in tf.routes) routes.Add(new RoutePath { Data = r, Path = PolylinePath.FromFlat(r.points, r.circuit) });
            var lf = Load<LocationsFile>("World/locations");
            if (lf?.locations != null) locations.AddRange(lf.locations);
            districts = Load<DistrictsFile>("World/districts") ?? new DistrictsFile();
            BuildGrid();
        }

        static T Load<T>(string path) where T : class
        {
            var ta = Resources.Load<TextAsset>(path);
            if (ta == null) { Debug.LogWarning($"[World] missing Resources/{path}.json"); return null; }
            return JsonUtility.FromJson<T>(ta.text);
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static void BuildGrid()
        {
            roadGrid = new Dictionary<long, List<(int, int)>>();
            for (int r = 0; r < roads.Count; r++)
            {
                var p = roads[r].Path;
                int segs = p.Closed ? p.Count : p.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = p.Points[i];
                    var b = p.Points[(i + 1) % p.Count];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / GridCell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / GridCell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / GridCell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / GridCell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long k = Key(x, z);
                            if (!roadGrid.TryGetValue(k, out var list)) roadGrid[k] = list = new List<(int, int)>(8);
                            list.Add((r, i));
                        }
                }
            }
        }

        public struct RoadHit
        {
            public int Road;
            public int Segment;
            public float S;
            public float Distance;
            public Vector3 Point;
            public Vector3 Tangent;
            public RoadPath RoadPath => roads[Road];
        }

        /// <summary>Nearest road centreline point within maxDistance (horizontal), preferring similar elevation.</summary>
        public static bool NearestRoad(Vector3 p, float maxDistance, out RoadHit hit)
        {
            EnsureLoaded();
            hit = default;
            float best = float.MaxValue;
            int cells = Mathf.CeilToInt(maxDistance / GridCell);
            int cx = Mathf.FloorToInt(p.x / GridCell), cz = Mathf.FloorToInt(p.z / GridCell);
            for (int x = cx - cells; x <= cx + cells; x++)
                for (int z = cz - cells; z <= cz + cells; z++)
                {
                    if (!roadGrid.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (var (r, i) in list)
                    {
                        var path = roads[r].Path;
                        var a = path.Points[i];
                        var b = path.Points[(i + 1) % path.Count];
                        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                        float L2 = ab.sqrMagnitude;
                        float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / L2) : 0f;
                        var q = Vector3.Lerp(a, b, t);
                        float dh = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                        float score = dh + Mathf.Max(0f, Mathf.Abs(p.y - q.y) - 2f) * 3f;
                        if (dh <= maxDistance && score < best)
                        {
                            best = score;
                            hit = new RoadHit { Road = r, Segment = i, S = path.S[i] + t * Mathf.Sqrt(L2), Distance = dh, Point = q, Tangent = (b - a).normalized };
                        }
                    }
                }
            return best < float.MaxValue;
        }

        static readonly Dictionary<string, int> ClassRank = new Dictionary<string, int>
        { { "MW", 7 }, { "EX", 6 }, { "HW", 5 }, { "AR", 4 }, { "SV", 3 }, { "CO", 2 }, { "MT", 1 }, { "RU", 1 }, { "GR", 1 }, { "RW", 0 } };

        public static int Rank(RoadPath r) => ClassRank.TryGetValue(r.Data.cls, out int k) ? k : 0;

        /// <summary>
        /// Every road whose carriageway contains p (horizontal distance below half-width minus slack, within
        /// maxDy of the road surface), one hit per road (its closest segment).
        /// </summary>
        public static void RoadsAt(Vector3 p, float slack, float maxDy, List<RoadHit> results)
        {
            EnsureLoaded();
            results.Clear();
            int cx = Mathf.FloorToInt(p.x / GridCell), cz = Mathf.FloorToInt(p.z / GridCell);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!roadGrid.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (var (r, i) in list)
                    {
                        var path = roads[r].Path;
                        var a = path.Points[i];
                        var b = path.Points[(i + 1) % path.Count];
                        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                        float L2 = ab.sqrMagnitude;
                        float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / L2) : 0f;
                        var q = Vector3.Lerp(a, b, t);
                        float dh = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                        if (dh >= roads[r].HalfWidth - slack || Mathf.Abs(p.y - q.y) > maxDy) continue;
                        int existing = results.FindIndex(h => h.Road == r);
                        var hit = new RoadHit { Road = r, Segment = i, S = path.S[i] + t * Mathf.Sqrt(L2), Distance = dh, Point = q, Tangent = (b - a).normalized };
                        if (existing < 0) results.Add(hit);
                        else if (dh < results[existing].Distance) results[existing] = hit;
                    }
                }
        }

        public static DistrictJson DistrictAt(Vector3 p)
        {
            EnsureLoaded();
            if (districts?.districts == null) return null;
            foreach (var d in districts.districts)
                if (PointInPolygon(d.polygon, p.x, p.z)) return d;
            return null;
        }

        public static bool PointInPolygon(float[] poly, float x, float z)
        {
            bool inside = false;
            int n = poly.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = poly[i * 2], zi = poly[i * 2 + 1], xj = poly[j * 2], zj = poly[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi + 1e-9f) + xi) inside = !inside;
            }
            return inside;
        }

        public static RoutePath Route(string id)
        {
            EnsureLoaded();
            foreach (var r in routes) if (r.Data.id == id) return r;
            return null;
        }

        public static LocationJson Location(string id)
        {
            EnsureLoaded();
            foreach (var l in locations) if (l.id == id) return l;
            return null;
        }

        /// <summary>Ground height by raycast (world colliders), falling back to <paramref name="fallback"/>.</summary>
        public static float GroundHeight(Vector3 p, float fallback, int mask = ~0)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, out var hit, 80f, mask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallback;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }
    }
}
