using System.Collections.Generic;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.UI
{
    /// <summary>
    /// Road network as a graph for GPS routing (the world map's waypoint route; GTA V's purple line). Nodes are road ends and
    /// at-grade junctions: road ends touching another road, and crossings of two roads' centre lines found with a 60 m segment
    /// grid (crossings more than 3 m apart in height are bridges, not junctions). Edges are the road stretches between
    /// consecutive junctions, so a route can be drawn back along the real road curves. A* with a straight-line heuristic.
    /// Built once on first use (~a few hundred ms for the whole city).
    /// </summary>
    public static class RoadGraph
    {
        public struct Edge { public int To; public int Road; public float S0, S1, Length; }

        static readonly List<Vector3> nodes = new List<Vector3>();
        static readonly List<List<Edge>> adj = new List<List<Edge>>();
        static bool built;

        public static int NodeCount { get { Build(); return nodes.Count; } }

        static void Build()
        {
            if (built) return;
            built = true;
            var roads = WorldData.Roads;
            var cuts = new List<float>[roads.Count];
            for (int r = 0; r < roads.Count; r++) cuts[r] = new List<float> { 0f, roads[r].Path.Length };

            // T-junctions: a road end lying on another road
            for (int r = 0; r < roads.Count; r++)
            {
                var path = roads[r].Path;
                if (path.Closed) continue;
                foreach (float s in new[] { 0f, path.Length })
                {
                    var p = path.PositionAt(s);
                    if (WorldData.NearestRoad(p, 14f, out var hit) && hit.Road != r && Mathf.Abs(hit.Point.y - p.y) < 3f)
                        cuts[hit.Road].Add(hit.S);
                }
            }
            // X-junctions: centre-line crossings, found per 60 m cell
            const float Cell = 60f;
            var grid = new Dictionary<long, List<(int r, int i)>>();
            for (int r = 0; r < roads.Count; r++)
            {
                var pts = roads[r].Path.Points;
                int n = roads[r].Path.Closed ? pts.Length : pts.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / Cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long key = ((long)x << 32) ^ (uint)z;
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<(int, int)>();
                            list.Add((r, i));
                        }
                }
            }
            var seen = new HashSet<long>();
            foreach (var list in grid.Values)
                for (int u = 0; u < list.Count; u++)
                    for (int v = u + 1; v < list.Count; v++)
                    {
                        var (ra, ia) = list[u];
                        var (rb, ib) = list[v];
                        if (ra == rb) continue;
                        long pairKey = ((long)Mathf.Min(ra * 100000 + ia, rb * 100000 + ib) << 32) ^ (uint)Mathf.Max(ra * 100000 + ia, rb * 100000 + ib);
                        if (!seen.Add(pairKey)) continue;
                        var pa = roads[ra].Path;
                        var pb = roads[rb].Path;
                        Vector3 a0 = pa.Points[ia], a1 = pa.Points[(ia + 1) % pa.Points.Length];
                        Vector3 b0 = pb.Points[ib], b1 = pb.Points[(ib + 1) % pb.Points.Length];
                        if (!Intersect(a0, a1, b0, b1, out float ta, out float tb)) continue;
                        float ya = Mathf.Lerp(a0.y, a1.y, ta), yb = Mathf.Lerp(b0.y, b1.y, tb);
                        if (Mathf.Abs(ya - yb) > 3f) continue;   // a bridge over the other road
                        cuts[ra].Add(pa.S[ia] + ta * Vector3.Distance(a0, a1));
                        cuts[rb].Add(pb.S[ib] + tb * Vector3.Distance(b0, b1));
                    }

            // nodes (merged within 8 m) and edges along each road
            for (int r = 0; r < roads.Count; r++)
            {
                var path = roads[r].Path;
                var c = cuts[r];
                c.Sort();
                int prevNode = -1;
                float prevS = 0f;
                for (int k = 0; k < c.Count; k++)
                {
                    if (k > 0 && c[k] - c[k - 1] < 4f) continue;
                    int node = NodeAt(path.PositionAt(c[k]));
                    if (prevNode >= 0 && node != prevNode)
                    {
                        float len = c[k] - prevS;
                        adj[prevNode].Add(new Edge { To = node, Road = r, S0 = prevS, S1 = c[k], Length = len });
                        adj[node].Add(new Edge { To = prevNode, Road = r, S0 = c[k], S1 = prevS, Length = len });
                    }
                    prevNode = node;
                    prevS = c[k];
                }
            }
            Debug.Log($"[RoadGraph] {nodes.Count} junction nodes");
        }

        static readonly Dictionary<long, List<int>> nodeGrid = new Dictionary<long, List<int>>();

        static int NodeAt(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / 16f), cz = Mathf.FloorToInt(p.z / 16f);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                    if (nodeGrid.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                        foreach (int i in list)
                            if ((nodes[i] - p).sqrMagnitude < 64f && Mathf.Abs(nodes[i].y - p.y) < 3f) return i;
            nodes.Add(p);
            adj.Add(new List<Edge>());
            long key = ((long)cx << 32) ^ (uint)cz;
            if (!nodeGrid.TryGetValue(key, out var l)) nodeGrid[key] = l = new List<int>();
            l.Add(nodes.Count - 1);
            return nodes.Count - 1;
        }

        static bool Intersect(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out float ta, out float tb)
        {
            ta = tb = 0f;
            float dax = a1.x - a0.x, daz = a1.z - a0.z, dbx = b1.x - b0.x, dbz = b1.z - b0.z;
            float den = dax * dbz - daz * dbx;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float ex = b0.x - a0.x, ez = b0.z - a0.z;
            ta = (ex * dbz - ez * dbx) / den;
            tb = (ex * daz - ez * dax) / den;
            return ta >= 0f && ta <= 1f && tb >= 0f && tb <= 1f;
        }

        /// <summary>
        /// Route from one world point to another along the roads; the result is a polyline following the road curves
        /// (empty when either point is more than 120 m from a road or no path exists). Length in metres.
        /// </summary>
        public static bool Route(Vector3 from, Vector3 to, List<Vector3> polyline, out float length)
        {
            Build();
            polyline.Clear();
            length = 0f;
            if (!WorldData.NearestRoad(from, 120f, out var hs) || !WorldData.NearestRoad(to, 120f, out var ht)) return false;
            var roads = WorldData.Roads;
            // virtual start/end nodes on their roads, linked to the neighbouring junctions of that road
            int start = nodes.Count, goal = nodes.Count + 1;
            var extra = new Dictionary<int, List<Edge>> { [start] = new List<Edge>(), [goal] = new List<Edge>() };
            void Attach(int vNode, WorldData.RoadHit h, bool outgoing)
            {
                // find the edge of this road that contains h.S
                for (int n = 0; n < adj.Count; n++)
                    foreach (var e in adj[n])
                    {
                        if (e.Road != h.Road || e.S0 > e.S1) continue;   // each stretch once (forward copy)
                        if (h.S < e.S0 - 0.5f || h.S > e.S1 + 0.5f) continue;
                        int a = n, b = e.To;
                        if (outgoing)
                        {
                            extra[vNode].Add(new Edge { To = a, Road = e.Road, S0 = h.S, S1 = e.S0, Length = Mathf.Abs(h.S - e.S0) });
                            extra[vNode].Add(new Edge { To = b, Road = e.Road, S0 = h.S, S1 = e.S1, Length = Mathf.Abs(e.S1 - h.S) });
                        }
                        else
                        {
                            if (!extra.ContainsKey(a)) extra[a] = new List<Edge>();
                            if (!extra.ContainsKey(b)) extra[b] = new List<Edge>();
                            extra[a].Add(new Edge { To = vNode, Road = e.Road, S0 = e.S0, S1 = h.S, Length = Mathf.Abs(h.S - e.S0) });
                            extra[b].Add(new Edge { To = vNode, Road = e.Road, S0 = e.S1, S1 = h.S, Length = Mathf.Abs(e.S1 - h.S) });
                        }
                        return;
                    }
            }
            Attach(start, hs, true);
            Attach(goal, ht, false);
            if (hs.Road == ht.Road) extra[start].Add(new Edge { To = goal, Road = hs.Road, S0 = hs.S, S1 = ht.S, Length = Mathf.Abs(ht.S - hs.S) });

            Vector3 goalPos = ht.Point;
            Vector3 PosOf(int n) => n == start ? hs.Point : n == goal ? ht.Point : nodes[n];
            var dist = new Dictionary<int, float> { [start] = 0f };
            var cameFrom = new Dictionary<int, (int from, Edge via)>();
            var open = new List<(float f, int n)> { (Vector3.Distance(hs.Point, goalPos), start) };
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].f < open[best].f) best = i;
                int cur = open[best].n;
                open.RemoveAt(best);
                if (cur == goal) break;
                if (!closed.Add(cur)) continue;
                IEnumerable<Edge> edges = cur < adj.Count ? adj[cur] : (IEnumerable<Edge>)new List<Edge>();
                foreach (var list in new[] { edges, extra.TryGetValue(cur, out var ex) ? ex : null })
                {
                    if (list == null) continue;
                    foreach (var e in list)
                    {
                        float nd = dist[cur] + e.Length;
                        if (dist.TryGetValue(e.To, out float old) && old <= nd) continue;
                        dist[e.To] = nd;
                        cameFrom[e.To] = (cur, e);
                        open.Add((nd + Vector3.Distance(PosOf(e.To), goalPos), e.To));
                    }
                }
            }
            if (!dist.TryGetValue(goal, out length)) return false;
            // walk back and sample each stretch along its road
            var chain = new List<Edge>();
            for (int n = goal; n != start; n = cameFrom[n].from) chain.Add(cameFrom[n].via);
            chain.Reverse();
            foreach (var e in chain)
            {
                var path = roads[e.Road].Path;
                float span = e.S1 - e.S0;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(span) / 8f));
                for (int k = 0; k <= steps; k++) polyline.Add(path.PositionAt(e.S0 + span * k / steps));
            }
            return polyline.Count > 1;
        }
    }
}
