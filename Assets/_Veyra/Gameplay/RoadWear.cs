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
        sealed class Saved { public Material M; public float Wear, Patch, PatchScale; public Texture Detail; public Vector2 DetailScale; }

        readonly List<MeshFilter> pending = new List<MeshFilter>();
        readonly List<Saved> saved = new List<Saved>();
        Job job;
        float pickTimer;
        static Texture2D patchTex;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        void Start()
        {
            var seen = new HashSet<Mesh>();
            var mats = new HashSet<Material>();
            foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = mr.sharedMaterial;
                // iteration 28: the concrete road deck too (the city's most used road surface; iterations 24-27 only touched
                // the two asphalt materials, so most streets in the captures showed no wear at all)
                if (m == null || m.shader == null || m.shader.name != "Veyra/Road" || !(m.name.Contains("Asphalt") || m.name.Contains("Concrete_Deck"))) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || !seen.Add(mf.sharedMesh)) continue;
                pending.Add(mf);
                mats.Add(m);
            }
            var tex = PatchTexture();
            foreach (var m in mats)
            {
                saved.Add(new Saved { M = m, Wear = m.GetFloat("_TyreWear"), Patch = m.GetFloat("_PatchAmount"), PatchScale = m.GetFloat("_PatchScale"),
                                      Detail = m.GetTexture("_DetailMap"), DetailScale = m.GetTextureScale("_DetailMap") });
                bool worn = m.name.Contains("Worn") || m.name.Contains("Concrete_Deck");
                m.SetFloat("_TyreWear", worn ? 1f : 0.6f);
                m.SetTexture("_DetailMap", tex);
                m.SetTextureScale("_DetailMap", new Vector2(1f / 14f, 1f / 14f));   // road UV0 is in metres
                m.SetFloat("_PatchAmount", worn ? 0.85f : 0.45f);   // iteration 25: patches every few hundred metres
                m.SetFloat("_PatchScale", 11f);
            }
            Debug.Log($"[RoadWear] {pending.Count} asphalt meshes, {mats.Count} materials");
        }

        void OnDestroy()
        {
            if (!Application.isEditor) return;   // players exit anyway; the editor must not keep runtime values in the assets
            foreach (var s in saved)
            {
                if (s.M == null) continue;
                s.M.SetFloat("_TyreWear", s.Wear);
                s.M.SetFloat("_PatchAmount", s.Patch);
                s.M.SetFloat("_PatchScale", s.PatchScale);
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
            for (int k = 0; k < 14; k++)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float hw = 18f + (float)rng.NextDouble() * 70f, hh = 12f + (float)rng.NextDouble() * 40f;
                float a = (float)rng.NextDouble() * Mathf.PI, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                byte tone = (byte)(30 + rng.Next(18));
                int r = Mathf.CeilToInt(Mathf.Sqrt(hw * hw + hh * hh)) + 3;
                for (int y = (int)cy - r; y <= (int)cy + r; y++)
                    for (int x = (int)cx - r; x <= (int)cx + r; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float u = Mathf.Abs(dx * ca + dy * sa), v = Mathf.Abs(-dx * sa + dy * ca);
                        if (u > hw + 2.5f || v > hh + 2.5f) continue;
                        bool seam = u > hw - 0.5f || v > hh - 0.5f;
                        int ix = ((x % n) + n) % n, iy = ((y % n) + n) % n;
                        px[iy * n + ix] = seam ? new Color32(14, 14, 15, 235) : new Color32(tone, tone, (byte)(tone + 2), 200);
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
