using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 27 (CAM_17: the Falcon runway and taxiways were blank concrete). WorldBuildBatch step `airfield`.
    /// Paint on the road-network centrelines (class RW; the data is the authority):
    /// <list type="bullet">
    /// <item>runway (the RW road named "Runway"): threshold piano keys at both ends, the designator of each landing direction
    /// computed from the centreline bearing (block numerals), touchdown-zone bars, aiming-point blocks 300 m in, 30 m centreline
    /// dashes with 20 m gaps, solid edge lines, and edge lights every 60 m (night-lit Lamp_Head studs);</item>
    /// <item>taxiways (the other RW roads): solid yellow centreline, holding-position bars (2 solid + 2 dashed) 60 m from the
    /// runway centreline where a link meets it.</item>
    /// </list>
    /// Paint 5 cm above the profile (flat, max grade 0.8 %). Own plain paint materials. Idempotent: World/Airfield rebuilt.
    /// Report Logs/C/airfield.txt.
    /// </summary>
    public static class AirfieldBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Airfield";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const float Lift = 0.05f;

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        static Material LitMat(string name, Color col, float smooth)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", col); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder("Airfield (iteration 27)\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var old = world.transform.Find("Airfield");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Directory.CreateDirectory(MeshDir);
            var white = LitMat("M_Air_PaintWhite", new Color(0.82f, 0.82f, 0.8f), 0.35f);
            var yellow = LitMat("M_Air_PaintYellow", new Color(0.78f, 0.56f, 0.06f), 0.35f);
            var lamp = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Lamp_Head.mat");
            var parts = new Dictionary<Material, MB>();
            MB Get(Material m) { if (!parts.TryGetValue(m, out var mb)) parts[m] = mb = new MB(); return mb; }

            var rws = WorldData.Roads.Where(r => r.Data.cls == "RW").ToList();
            var runway = rws.FirstOrDefault(r => r.Data.name != null && r.Data.name.Contains("Runway"));
            if (runway == null) return "FAIL: no RW road named Runway\n";
            var path = runway.Path; float L = path.Length, hw = runway.HalfWidth;

            // a flat rectangle on the profile: along s0..s1, across o0..o1 (metres from the centreline, right positive)
            void Paint(Material m, float s0, float s1, float o0, float o1)
            {
                var a = path.PositionAt(s0); var b = path.PositionAt(s1);
                var t = path.TangentAt((s0 + s1) * 0.5f); t.y = 0f; t.Normalize();
                var right = new Vector3(t.z, 0f, -t.x);
                var mb = Get(m); int i = mb.V.Count;
                mb.V.Add(a + right * o0 + Vector3.up * Lift); mb.V.Add(a + right * o1 + Vector3.up * Lift);
                mb.V.Add(b + right * o1 + Vector3.up * Lift); mb.V.Add(b + right * o0 + Vector3.up * Lift);
                for (int k = 0; k < 4; k++) mb.UV.Add(Vector2.zero);
                if (Vector3.Cross(mb.V[i + 1] - mb.V[i], mb.V[i + 2] - mb.V[i]).y >= 0f) mb.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else mb.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            // edge lines and centreline dashes
            Paint(white, 0f, L, -hw + 0.6f, -hw + 1.5f);
            Paint(white, 0f, L, hw - 1.5f, hw - 0.6f);
            int dashes = 0;
            for (float s = 120f; s + 30f < L - 120f; s += 50f) { Paint(white, s, s + 30f, -0.45f, 0.45f); dashes++; }

            // both ends: piano keys, designator, touchdown zone, aiming point; sEnd is the threshold, dir +1 lands towards +s
            var designators = new List<string>();
            foreach (var (sEnd, dir) in new[] { (0f, 1f), (L, -1f) })
            {
                float S(float d) => sEnd + dir * d;
                var tan = path.TangentAt(Mathf.Clamp(S(50f), 0f, L)) * dir; tan.y = 0f;
                float bearing = Mathf.Repeat(Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg, 360f);
                int num = Mathf.RoundToInt(bearing / 10f); if (num == 0) num = 36;
                string des = num.ToString("00");
                designators.Add($"{des} (bearing {bearing:0}°)");
                // 8 + 8 keys, 30 m long, 1.8 m wide, from 6 m in
                float keyW = 1.8f, gap = (hw - 3f - 8 * keyW) / 8f;
                for (int k = 0; k < 8; k++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float o0 = side * (3f + k * (keyW + gap)), o1 = o0 + side * keyW;
                        Paint(white, Mathf.Min(S(6f), S(36f)), Mathf.Max(S(6f), S(36f)), Mathf.Min(o0, o1), Mathf.Max(o0, o1));
                    }
                // designator numerals 18 m tall from 48 m in; readable on approach (bottom towards the threshold)
                Numeral(des, (along, across, len, wid) => Paint(white, Mathf.Min(S(along), S(along + len)), Mathf.Max(S(along), S(along + len)),
                        dir > 0 ? across : -across - wid, dir > 0 ? across + wid : -across), 48f);
                // touchdown zone: 3 pairs of bar groups at 150, 300 (aiming point), 450, 600 m
                foreach (float d in new[] { 150f, 450f, 600f })
                    foreach (float side in new[] { -1f, 1f })
                        for (int b = 0; b < (d < 300f ? 3 : d < 500f ? 2 : 1); b++)
                        {
                            float o = side * (6f + b * 3f);
                            Paint(white, Mathf.Min(S(d), S(d + 22f)), Mathf.Max(S(d), S(d + 22f)), Mathf.Min(o, o + side * 1.8f), Mathf.Max(o, o + side * 1.8f));
                        }
                foreach (float side in new[] { -1f, 1f })
                {
                    float o = side * 7f;
                    Paint(white, Mathf.Min(S(300f), S(345f)), Mathf.Max(S(300f), S(345f)), Mathf.Min(o, o + side * 9f), Mathf.Max(o, o + side * 9f));
                }
            }
            // edge lights every 60 m, 0.5 m outside the paved edge
            int lights = 0;
            if (lamp != null)
                for (float s = 0f; s <= L; s += 60f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var p = path.PositionAt(s); var t = path.TangentAt(s); t.y = 0f; t.Normalize();
                        var q = p + new Vector3(t.z, 0f, -t.x) * side * (hw + 0.5f);
                        Box(Get(lamp), q + Vector3.up * 0.22f, t, 0.22f, 0.44f, 0.22f);
                        lights++;
                    }

            // taxiways: yellow centreline; holding bars where a link ends within 80 m of the runway centreline
            int holds = 0;
            foreach (var tw in rws.Where(r => r != runway))
            {
                var tp = tw.Path; float tl = tp.Length;
                void TPaint(float s0, float s1, float o0, float o1)
                {
                    var a = tp.PositionAt(s0); var b = tp.PositionAt(s1);
                    var t = tp.TangentAt((s0 + s1) * 0.5f); t.y = 0f; t.Normalize();
                    var right = new Vector3(t.z, 0f, -t.x);
                    var mb = Get(yellow); int i = mb.V.Count;
                    mb.V.Add(a + right * o0 + Vector3.up * (Lift + 0.005f)); mb.V.Add(a + right * o1 + Vector3.up * (Lift + 0.005f));
                    mb.V.Add(b + right * o1 + Vector3.up * (Lift + 0.005f)); mb.V.Add(b + right * o0 + Vector3.up * (Lift + 0.005f));
                    for (int k = 0; k < 4; k++) mb.UV.Add(Vector2.zero);
                    if (Vector3.Cross(mb.V[i + 1] - mb.V[i], mb.V[i + 2] - mb.V[i]).y >= 0f) mb.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                    else mb.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                }
                // centreline, kept off the runway surface
                for (float s = 0f; s < tl; s += 4f)
                {
                    float e = Mathf.Min(s + 4f, tl);
                    var mid = tp.PositionAt((s + e) * 0.5f);
                    if (DistToRunway(mid) < hw + 2f) continue;
                    TPaint(s, e, -0.08f, 0.08f);
                }
                foreach (float sEnd in new[] { 0f, tl })
                {
                    if (DistToRunway(tp.PositionAt(sEnd)) > hw + 80f) continue;
                    // walk in from the runway end until 60 m from its centreline
                    float dirIn = sEnd == 0f ? 1f : -1f, s = sEnd;
                    while (s >= 0f && s <= tl && DistToRunway(tp.PositionAt(s)) < 60f) s += dirIn * 0.5f;
                    float thw = tw.HalfWidth - 0.6f;
                    for (int k = 0; k < 2; k++) TPaint(s + dirIn * k * 0.9f, s + dirIn * (k * 0.9f + 0.3f), -thw, thw);
                    for (int k = 2; k < 4; k++)
                        for (float o = -thw; o < thw; o += 1.8f) TPaint(s + dirIn * k * 0.9f, s + dirIn * (k * 0.9f + 0.3f), o, Mathf.Min(o + 0.9f, thw));
                    holds++;
                }
            }
            float DistToRunway(Vector3 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    var a = path.Points[i]; var b = path.Points[i + 1];
                    Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                    float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (ap - ab * t).magnitude);
                }
                return best;
            }

            var root = new GameObject("Airfield") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            var keep = new HashSet<string>();
            foreach (var kv in parts)
            {
                if (kv.Value.T.Count == 0 || kv.Key == null) continue;
                string name = $"AF_{kv.Key.name}";
                var mesh = Save(name, kv.Value);
                keep.Add(name);
                var g = new GameObject(kv.Key.name) { isStatic = true };
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = ShadowCastingMode.Off;
                var lg = g.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.0005f, new Renderer[] { r }) });
                lg.RecalculateBounds();
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            sb.AppendLine($"  runway {runway.Data.id} '{runway.Data.name}' {L:0} m x {hw * 2f:0} m: designators {string.Join(", ", designators)}, {dashes} centreline dashes, {lights} edge lights");
            sb.AppendLine($"  {rws.Count - 1} taxiways: yellow centrelines, {holds} holding positions");
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/airfield.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// Block numerals (seven segments, 18 m tall, 7 m wide, 1.8 m strokes): paint(along, across, length, width) per stroke,
        /// "along" measured from the threshold (bottom of the digit nearest the threshold), two digits side by side.
        /// </summary>
        static void Numeral(string text, System.Action<float, float, float, float> paint, float start)
        {
            const float H = 18f, W = 7f, T = 1.8f, Gap = 3f;
            // segments: a top, b upper right, c lower right, d bottom, e lower left, f upper left, g middle
            string[] seg = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };
            float total = text.Length * W + (text.Length - 1) * Gap;
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                float x0 = -total * 0.5f + i * (W + Gap);   // across, left edge of the digit (left as seen on approach)
                foreach (char s in seg[d])
                    switch (s)
                    {
                        case 'a': paint(start + H - T, x0, T, W); break;
                        case 'd': paint(start, x0, T, W); break;
                        case 'g': paint(start + H * 0.5f - T * 0.5f, x0, T, W); break;
                        case 'b': paint(start + H * 0.5f, x0 + W - T, H * 0.5f, T); break;
                        case 'c': paint(start, x0 + W - T, H * 0.5f, T); break;
                        case 'e': paint(start, x0, H * 0.5f, T); break;
                        case 'f': paint(start + H * 0.5f, x0, H * 0.5f, T); break;
                    }
            }
        }

        static void Box(MB m, Vector3 c, Vector3 fwd, float sx, float sy, float sz)
        {
            fwd.y = 0f; fwd.Normalize();
            var ax = new Vector3(fwd.z, 0f, -fwd.x) * sx * 0.5f; var az = fwd * sz * 0.5f; var ay = Vector3.up * sy * 0.5f;
            Vector3 P(int x, int y, int z) => c + ax * x + ay * y + az * z;
            void Q(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 n)
            {
                int i = m.V.Count; m.V.Add(a); m.V.Add(b); m.V.Add(cc); m.V.Add(d);
                for (int k = 0; k < 4; k++) m.UV.Add(Vector2.zero);
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), n) >= 0f) m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else m.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            Q(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -az);
            Q(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), az);
            Q(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), -ax);
            Q(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), ax);
            Q(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
        }

        static Mesh Save(string name, MB mb)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(mb.V); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
