using System.Collections.Generic;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual iteration 24 (GTA V streets): the Veyra/Road asphalt already supports tyre-wear lanes and patch/crack
    /// repairs, but they were off (_TyreWear 0, _PatchAmount 0, no detail texture) and the city's road meshes had no
    /// across-the-road UV channel for the lanes. This component
    ///  * fills UV1 of the asphalt render meshes around the player (x = 0-1 across the carriageway from the nearest road
    ///    centre line, y = that road's lane count), one slice of vertices per frame within a 1.2 ms budget;
    ///  * generates a repair-patch and crack texture once and turns wear and patches on for the asphalt materials, at
    ///    runtime only (the material assets keep their values; in the editor they are restored on exit).
    /// The shader then draws wheel-path polish, oil drips down each lane, gutter grit along the kerbs (where rain pools
    /// first) and sealed patches with cracks.
    /// </summary>
    public sealed class RoadWear : MonoBehaviour
    {
        public Transform Player;
        public static int MeshesDone { get; private set; }
        public static int VerticesDone { get; private set; }

        sealed class Job { public Mesh Mesh; public int Count; public int Next; public Matrix4x4 L2W; }
        // iteration 29: one vertex list and one UV list reused for every mesh (fresh arrays per mesh fed the GC: 23 ms spikes)
        readonly List<Vector3> verts = new List<Vector3>(4096);
        readonly List<Vector2> uv1 = new List<Vector2>(4096);
        sealed class Saved { public Material M; public float Wear, Patch, PatchScale, Macro; public Texture Detail; public Vector2 DetailScale; }

        readonly List<MeshFilter> pending = new List<MeshFilter>();
        readonly List<Saved> saved = new List<Saved>();
        Job job;
        float pickTimer;
        static Texture2D patchTex;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        readonly HashSet<Mesh> seen = new HashSet<Mesh>();
        readonly HashSet<Material> configured = new HashSet<Material>();

        void Start()
        {
            ScanRenderers(FindObjectsByType<MeshRenderer>());
            // round 3: streamed cells (additive scenes) bring their roads later
            VeyraCity.World.Streaming.CellStreamer.CellLoaded += OnCellLoaded;
        }

        void OnCellLoaded(string cell, UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<MeshRenderer>();
            foreach (var go in scene.GetRootGameObjects()) list.AddRange(go.GetComponentsInChildren<MeshRenderer>(true));
            ScanRenderers(list);
        }

        void ScanRenderers(IEnumerable<MeshRenderer> renderers)
        {
            var mats = new HashSet<Material>();
            int before = pending.Count;
            foreach (var mr in renderers)
            {
                // iteration 28: the concrete road deck too; iteration 31: every material slot (the eased road ribbons that lie
                // 2-3 cm over the old surface on many streets carry [asphalt, concrete] and are what the camera sees there)
                bool road = false;
                foreach (var m in mr.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name != "Veyra/Road" || !(m.name.Contains("Asphalt") || m.name.Contains("Concrete_Deck"))) continue;
                    road = true;
                    mats.Add(m);
                }
                if (!road) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || !seen.Add(mf.sharedMesh)) continue;
                pending.Add(mf);
            }
            var tex = PatchTexture();
            foreach (var m in mats)
            {
                if (!configured.Add(m)) continue;
                saved.Add(new Saved { M = m, Wear = m.GetFloat("_TyreWear"), Patch = m.GetFloat("_PatchAmount"), PatchScale = m.GetFloat("_PatchScale"), Macro = m.GetFloat("_MacroVariation"),
                                      Detail = m.GetTexture("_DetailMap"), DetailScale = m.GetTextureScale("_DetailMap") });
                bool worn = m.name.Contains("Worn") || m.name.Contains("Concrete_Deck");
                m.SetFloat("_TyreWear", worn ? 1f : 0.6f);
                m.SetTexture("_DetailMap", tex);
                m.SetTextureScale("_DetailMap", new Vector2(1f / 14f, 1f / 14f));   // road UV0 is in metres
                m.SetFloat("_PatchAmount", worn ? 0.5f : 0.25f);   // iteration 25: patches every few hundred metres; World BUG-121: fewer
                m.SetFloat("_PatchScale", 11f);
                // iteration 30: stronger world-space tone variation (aged and repaired stretches); needs no UVs, so it also
                // tells whether runtime material changes reach the road renderers at all
                m.SetFloat("_MacroVariation", 0.3f);
            }
            Debug.Log($"[RoadWear] +{pending.Count - before} asphalt meshes ({pending.Count} pending), {configured.Count} materials");
        }

        void OnDestroy()
        {
            VeyraCity.World.Streaming.CellStreamer.CellLoaded -= OnCellLoaded;
            if (!Application.isEditor) return;   // players exit anyway; the editor must not keep runtime values in the assets
            foreach (var s in saved)
            {
                if (s.M == null) continue;
                s.M.SetFloat("_TyreWear", s.Wear);
                s.M.SetFloat("_PatchAmount", s.Patch);
                s.M.SetFloat("_PatchScale", s.PatchScale);
                s.M.SetFloat("_MacroVariation", s.Macro);
                s.M.SetTexture("_DetailMap", s.Detail);
                s.M.SetTextureScale("_DetailMap", s.DetailScale);
            }
        }

        void Update()
        {
            if (Player == null) return;
            long fc0 = VeyraCity.Core.FrameCost.Start();
            sw.Restart();
            if (job == null)
            {
                pickTimer -= Time.deltaTime;
                if (pickTimer <= 0f && pending.Count > 0)
                {
                    pickTimer = 0.2f;
                    var p = Player.position;
                    int best = -1;
                    float bestD = 900f * 900f;
                    for (int i = 0; i < pending.Count; i++)
                    {
                        var mf = pending[i];
                        if (mf == null) continue;
                        float d = (mf.transform.TransformPoint(mf.sharedMesh.bounds.center) - p).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best >= 0)
                    {
                        var mf = pending[best];
                        pending.RemoveAt(best);
                        var mesh = mf.sharedMesh;
                        mesh.GetVertices(verts);
                        uv1.Clear();
                        for (int k = 0; k < verts.Count; k++) uv1.Add(Vector2.zero);
                        job = new Job { Mesh = mesh, Count = verts.Count, L2W = mf.transform.localToWorldMatrix };
                    }
                }
            }
            while (job != null && sw.Elapsed.TotalMilliseconds < 0.8)
            {
                int end = Mathf.Min(job.Count, job.Next + 128);
                for (int i = job.Next; i < end; i++)
                {
                    var p = job.L2W.MultiplyPoint3x4(verts[i]);
                    if (!WorldData.NearestRoad(p, 24f, out var hit)) continue;
                    var road = WorldData.Roads[hit.Road].Data;
                    var t = new Vector3(hit.Tangent.x, 0f, hit.Tangent.z);
                    if (t.sqrMagnitude < 1e-6f) continue;
                    t.Normalize();
                    float lat = Vector3.Dot(p - hit.Point, new Vector3(t.z, 0f, -t.x));
                    uv1[i] = new Vector2(Mathf.Clamp01(0.5f + lat / Mathf.Max(4f, road.width)), Mathf.Max(1, road.lanes));
                }
                job.Next = end;
                if (job.Next >= job.Count)
                {
                    job.Mesh.SetUVs(1, uv1);   // UV channel 1 (TEXCOORD1): the shader's uv1
                    MeshesDone++;
                    VerticesDone += job.Count;
                    job = null;
                }
            }
            VeyraCity.Core.FrameCost.Stop("RoadWear", fc0);
        }

        /// <summary>Repair patches (darker, smoother sealed asphalt with a tar seam) and branching cracks; alpha = coverage.</summary>
        static Texture2D PatchTexture()
        {
            if (patchTex != null) return patchTex;
            const int n = 512;
            var px = new Color32[n * n];
            var rng = new System.Random(4242);
            // patches: rotated rectangles
            // World BUG-121 (RoadWear patches read as dark rectangles, CAM_16 / CAM_10 / CAM_13): patches only a shade darker
            // than the asphalt (tone 55-75, alpha ~120 with grain), a thin tar seam, edges feathered over 6 px, fewer and smaller
            for (int k = 0; k < 9; k++)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float hw = 14f + (float)rng.NextDouble() * 46f, hh = 10f + (float)rng.NextDouble() * 28f;
                float a = (float)rng.NextDouble() * Mathf.PI, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                int tone = 55 + rng.Next(20);
                int r = Mathf.CeilToInt(Mathf.Sqrt(hw * hw + hh * hh)) + 8;
                for (int y = (int)cy - r; y <= (int)cy + r; y++)
                    for (int x = (int)cx - r; x <= (int)cx + r; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float u = Mathf.Abs(dx * ca + dy * sa), v = Mathf.Abs(-dx * sa + dy * ca);
                        float outside = Mathf.Max(u - hw, v - hh);   // > 0 outside the rectangle
                        if (outside > 6f) continue;
                        int ix = ((x % n) + n) % n, iy = ((y % n) + n) % n;
                        var old = px[iy * n + ix];
                        bool seam = outside > -0.8f && outside < 0.4f;
                        float grain = (float)rng.NextDouble();
                        byte al = seam ? (byte)150 : outside > 0f ? (byte)(110f * (1f - outside / 6f)) : (byte)(105 + grain * 30f);
                        if (al <= old.a) continue;
                        byte t = seam ? (byte)24 : (byte)(tone + (int)(grain * 8f) - 4);
                        px[iy * n + ix] = new Color32(t, t, (byte)(t + 2), al);
                    }
            }
            // cracks: jagged random walks with branches
            for (int k = 0; k < 26; k++)
            {
                float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n, dir = (float)rng.NextDouble() * Mathf.PI * 2f;
                int len = 40 + rng.Next(160);
                for (int s = 0; s < len; s++)
                {
                    dir += ((float)rng.NextDouble() - 0.5f) * 0.9f;
                    x += Mathf.Cos(dir); y += Mathf.Sin(dir);
                    int ix = ((Mathf.RoundToInt(x) % n) + n) % n, iy = ((Mathf.RoundToInt(y) % n) + n) % n;
                    px[iy * n + ix] = new Color32(10, 10, 11, 245);
                    if (rng.NextDouble() < 0.5) px[iy * n + (ix + 1) % n] = new Color32(16, 16, 17, 160);
                    if (rng.NextDouble() < 0.02) dir += (rng.NextDouble() < 0.5 ? 1f : -1f) * 1.1f;   // branch-like kink
                }
            }
            patchTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "RoadPatches", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            patchTex.SetPixels32(px);
            patchTex.Apply(true, true);
            return patchTex;
        }
    }
}
