using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S4 axis round-trip test, Unity side. Builds Assets/_Veyra/Scenes/Test/T_AxisTest.unity with the three FBX exports
    /// of Tools/Blender/vc_axis_test.py at (0,0,0) / identity, a 10 m ProBuilder reference cube and the D1 district blockout
    /// GLB (glTFast), then measures directions, positions, sizes and LOD groups and writes Logs/S4/axis_test.txt.
    /// Re-runnable: the scene is rebuilt from scratch.
    /// </summary>
    public static class AxisTestTools
    {
        public const string TestDir = "Assets/_Veyra/Art/Models/_Test";
        public const string ScenePath = "Assets/_Veyra/Scenes/Test/T_AxisTest.unity";
        public const string PropFbx = TestDir + "/VC_AxisTest_Prop.fbx";
        public const string WorldFbx = TestDir + "/VC_AxisTest_World.fbx";
        public const string WorldForwardZFbx = TestDir + "/VC_AxisTest_World_forwardZ.fbx";
        public const string D1Glb = TestDir + "/blockout_D1_crown_downtown.glb";

        [MenuItem("Veyra/Test/Build And Measure Axis Test")]
        public static void Menu() => Debug.Log(BuildAndMeasure());

        public static string BuildAndMeasure()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard the open scene first.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var sb = new StringBuilder();
            sb.AppendLine($"S4 axis round-trip test — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — Unity {Application.unityVersion}");
            bool allOk = true;

            var prop = Place(PropFbx, "AxisTest_Prop (VC_Unity_Prop)");
            var world = Place(WorldFbx, "AxisTest_World (VC_Unity_World)");
            var forwardZ = Place(WorldForwardZFbx, "AxisTest_World_forwardZ (axis_forward Z, evidence)");

            var cubeMesh = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one * 10f);
            cubeMesh.gameObject.name = "ProBuilder_RefCube_10m";
            cubeMesh.transform.position = new Vector3(-20f, 5f, 0f);
            cubeMesh.ToMesh();
            cubeMesh.Refresh();
            var cubeR = cubeMesh.GetComponent<Renderer>();
            if (cubeR.sharedMaterial == null) cubeR.sharedMaterial = BuiltinMaterials.defaultMaterial;

            sb.AppendLine();
            allOk &= Report(sb, "WORLD preset", world, expectN: "+Z", expectE: "+X", expectMarker: new Vector3(100f, 5f, 200f));
            allOk &= Report(sb, "PROP preset (front = Blender -Y must face +Z, so N -> -Z, E -> -X)", prop, expectN: "-Z", expectE: "-X", expectMarker: null);
            allOk &= Report(sb, "WORLD exported with axis_forward 'Z' instead of '-Z' (must be identical: Unity bakes the axis conversion)", forwardZ, expectN: "+Z", expectE: "+X", expectMarker: new Vector3(100f, 5f, 200f));
            var cs = cubeR.bounds.size;
            bool cubeOk = Approx(cs, Vector3.one * 10f, 0.01f);
            sb.AppendLine($"{(cubeOk ? "PASS" : "FAIL")}  ProBuilder reference cube size {cs.ToString("F3")} (expected 10 m)");
            allOk &= cubeOk;

            // glTF check: D1 blockout through glTFast, with the package root fix.
            sb.AppendLine();
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(D1Glb);
            if (glb == null) { sb.AppendLine($"FAIL  {D1Glb} not imported (is glTFast installed?)"); allOk = false; }
            else
            {
                var raw = (GameObject)PrefabUtility.InstantiatePrefab(glb);
                raw.name = "D1_blockout_raw (no fix)";
                raw.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var fixedGo = VeyraGltf.InstantiatePackageGlb(glb);
                fixedGo.name = "D1_blockout (VeyraGltf fix: root (0,180,0))";
                allOk &= GlbReport(sb, raw, fixedGo);
                raw.SetActive(false);
                fixedGo.SetActive(false); // keep the axis objects readable from above; enable to inspect the district
            }
            forwardZ.SetActive(false);

            sb.Insert(0, (allOk ? "S4 AXIS TEST: PASS" : "S4 AXIS TEST: FAIL") + Environment.NewLine);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            string log = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "S4");
            Directory.CreateDirectory(log);
            File.WriteAllText(Path.Combine(log, "axis_test.txt"), sb.ToString(), new UTF8Encoding(false));
            return sb.ToString();
        }

        static GameObject Place(string path, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException(path + " is not imported");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static string Dir(Vector3 v)
        {
            v.y = 0f;
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.z)) return v.x >= 0f ? "+X" : "-X";
            return v.z >= 0f ? "+Z" : "-Z";
        }

        static bool Approx(Vector3 a, Vector3 b, float tol) => Mathf.Abs(a.x - b.x) <= tol && Mathf.Abs(a.y - b.y) <= tol && Mathf.Abs(a.z - b.z) <= tol;

        static bool Report(StringBuilder sb, string title, GameObject inst, string expectN, string expectE, Vector3? expectMarker)
        {
            bool ok = true;
            var root = inst.transform;
            sb.AppendLine($"== {title}: {inst.name}");
            bool rootOk = root.localScale == Vector3.one && root.rotation == Quaternion.identity;
            sb.AppendLine($"{(rootOk ? "PASS" : "FAIL")}  root position {root.position} rotation {root.eulerAngles} scale {root.localScale}");
            ok &= rootOk;
            var nonUnit = root.GetComponentsInChildren<Transform>(true).Where(t => t != root && (t.localScale != Vector3.one || t.localRotation != Quaternion.identity)).Select(t => $"{t.name} rot {t.localEulerAngles.ToString("F0")} scale {t.localScale}").ToList();
            bool scalesOk = root.GetComponentsInChildren<Transform>(true).All(t => t.localScale == Vector3.one);
            sb.AppendLine($"{(scalesOk ? "PASS" : "FAIL")}  all scales 1; {(nonUnit.Count == 0 ? "all child rotations identity (axis conversion baked)" : "rotated children: " + string.Join("; ", nonUnit))}");
            ok &= scalesOk;

            string Arrow(string n) { var t = Find(root, n); var r = t != null ? t.GetComponent<Renderer>() : null; return r != null ? Dir(r.bounds.center - root.position) : "missing"; }
            string dn = Arrow("VC_AXIS_N"), de = Arrow("VC_AXIS_E");
            var nr = Find(root, "VC_AXIS_N")?.GetComponent<Renderer>();
            sb.AppendLine($"{(dn == expectN ? "PASS" : "FAIL")}  N arrow points {dn} (expected {expectN}); bounds centre {(nr != null ? nr.bounds.center.ToString("F1") : "-")}");
            sb.AppendLine($"{(de == expectE ? "PASS" : "FAIL")}  E arrow points {de} (expected {expectE})");
            ok &= dn == expectN && de == expectE;

            // Unity's importer puts ONE LODGroup on the file root for every *_LOD<n> mesh in the file.
            var lg = root.GetComponentInChildren<LODGroup>(true);
            var lod0 = Find(root, "VC_AXIS_BOX_LOD0")?.GetComponent<Renderer>();
            if (lod0 != null)
            {
                var s = lod0.bounds.size;
                bool sizeOk = Approx(s, new Vector3(10f, 3f, 4f), 0.01f);
                sb.AppendLine($"{(sizeOk ? "PASS" : "FAIL")}  box LOD0 size {s.ToString("F3")} (expected 10 x 3 x 4: 10 m east-west, 3 m up, 4 m north-south); base y {lod0.bounds.min.y:F3}");
                ok &= sizeOk;
            }
            else { sb.AppendLine("FAIL  VC_AXIS_BOX_LOD0 missing"); ok = false; }
            if (lg != null)
            {
                var lods = lg.GetLODs();
                string desc = string.Join(", ", lods.Select((l, i) => $"LOD{i} {string.Join("+", l.renderers.Where(r => r != null).Select(r => r.name))} ({l.renderers.Where(r => r != null).Sum(r => Tris(r)) } tris, {l.screenRelativeTransitionHeight:0.###})"));
                bool lodOk = lods.Length == 3 && Enumerable.Range(0, 3).All(i => lods[i].renderers.Any(r => r != null && r.name == $"VC_AXIS_BOX_LOD{i}"));
                sb.AppendLine($"{(lodOk ? "PASS" : "FAIL")}  LODGroup on '{lg.name}' (the file root): {lods.Length} levels: {desc}");
                ok &= lodOk;
            }
            else { sb.AppendLine("FAIL  no LODGroup created for VC_AXIS_BOX_LOD0/1/2"); ok = false; }

            if (expectMarker.HasValue)
            {
                var m = Find(root, "VC_AXIS_MARKER")?.GetComponent<Renderer>();
                var c = m != null ? m.bounds.center : new Vector3(float.NaN, float.NaN, float.NaN);
                bool mOk = m != null && Approx(c, expectMarker.Value, 0.01f) && Approx(m.bounds.size, Vector3.one * 2f, 0.01f);
                sb.AppendLine($"{(mOk ? "PASS" : "FAIL")}  marker centre {c.ToString("F2")} size {(m != null ? m.bounds.size.ToString("F2") : "-")} (expected {expectMarker.Value.ToString("F0")}, 2 m)");
                ok &= mOk;
            }
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(inst))) as ModelImporter;
            if (imp != null) sb.AppendLine($"INFO  importer: globalScale {imp.globalScale}, convertUnits {imp.useFileScale}, bakeAxisConversion {imp.bakeAxisConversion}, readable {imp.isReadable}, lightmapUVs {imp.generateSecondaryUV}, fileScale {imp.fileScale}");
            sb.AppendLine($"=> {(ok ? "PASS" : "FAIL")}");
            sb.AppendLine();
            return ok;
        }

        static int Tris(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return 0;
            int t = 0;
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) t += (int)mf.sharedMesh.GetIndexCount(i) / 3;
            return t;
        }

        /// <summary>World-space vertices of a renderer's mesh (works for non-readable meshes in the Editor).</summary>
        static Vector3[] WorldVertices(MeshFilter mf)
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(mf.sharedMesh))
            {
                var md = data[0];
                var v = new Unity.Collections.NativeArray<Vector3>(md.vertexCount, Unity.Collections.Allocator.Temp);
                md.GetVertices(v);
                var w = new Vector3[v.Length];
                var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
                v.Dispose();
                return w;
            }
        }

        static bool GlbReport(StringBuilder sb, GameObject raw, GameObject fixedGo)
        {
            sb.AppendLine("== glTF check: VEYRA_CITY_WORLD_DESIGN/08_3D_ASSETS/district_blockouts/blockout_D1_crown_downtown.glb via glTFast");
            bool ok = true;
            foreach (var (label, go) in new[] { ("without fix (root identity)", raw), ($"with VeyraGltf fix (root {VeyraGltf.PackageRootEuler})", fixedGo) })
            {
                sb.AppendLine($"-- {label}");
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                var b = mfs.FirstOrDefault(m => m.name.Contains("structures_buildings"));
                Vector3 spire = new Vector3(float.NaN, float.NaN, float.NaN);
                if (b != null)
                {
                    var w = WorldVertices(b);
                    var top = w.Where(p => p.y > 300f).ToArray();
                    if (top.Length > 0)
                    {
                        float cx = top.Average(p => p.x), cz = top.Average(p => p.z);
                        float baseY = w.Where(p => Mathf.Abs(p.x - cx) < 30f && Mathf.Abs(p.z - cz) < 30f).Min(p => p.y);
                        spire = new Vector3(cx, baseY, cz);
                        sb.AppendLine($"      Veyra Spire (vertices above 300 m, {top.Length}): centre ({cx:F1}, {cz:F1}), base y {baseY:F2}, top y {top.Max(p => p.y):F1}");
                    }
                }
                else sb.AppendLine("      no structures_buildings mesh found");
                var arrow = mfs.FirstOrDefault(m => m.name.Contains("NORTH_ARROW"));
                string arrowDir = "missing";
                if (arrow != null)
                {
                    var w = WorldVertices(arrow);
                    var lo = new Vector3(w.Min(p => p.x), w.Min(p => p.y), w.Min(p => p.z));
                    var hi = new Vector3(w.Max(p => p.x), w.Max(p => p.y), w.Max(p => p.z));
                    bool alongZ = hi.z - lo.z >= hi.x - lo.x;
                    float a0 = alongZ ? lo.z : lo.x, a1 = alongZ ? hi.z : hi.x, len = a1 - a0;
                    // The head (a 12 m disc on this marker, or a triangle on a classic arrow) is the wider half; the tail is the shaft.
                    float Spread(Func<Vector3, bool> half) { var s = w.Where(half).Select(p => alongZ ? p.x : p.z).ToArray(); return s.Length > 0 ? s.Max() - s.Min() : 0f; }
                    float mid = a0 + 0.5f * len;
                    float spreadLo = Spread(p => (alongZ ? p.z : p.x) < mid);
                    float spreadHi = Spread(p => (alongZ ? p.z : p.x) >= mid);
                    bool headAtHigh = spreadHi > spreadLo;
                    arrowDir = (headAtHigh ? "+" : "-") + (alongZ ? "Z" : "X");
                    sb.AppendLine($"      NORTH_ARROW bounds {lo.ToString("F0")}..{hi.ToString("F0")}; widest across the low/high half {spreadLo:F1} / {spreadHi:F1} m → head points {arrowDir}");
                }
                bool spireOk = !float.IsNaN(spire.x) && Mathf.Abs(spire.x - 170f) < 3f && Mathf.Abs(spire.z + 670f) < 3f && Mathf.Abs(spire.y - 10f) < 1.5f;
                bool northOk = arrowDir == "+Z";
                if (go == fixedGo)
                {
                    sb.AppendLine($"{(spireOk ? "PASS" : "FAIL")}  Spire at {spire.ToString("F1")} (expected (170, ~10, -670))");
                    sb.AppendLine($"{(northOk ? "PASS" : "FAIL")}  NORTH_ARROW points {arrowDir} (expected +Z)");
                    ok &= spireOk && northOk;
                }
                else sb.AppendLine($"INFO  without the fix: Spire at {spire.ToString("F1")}, arrow {arrowDir}");
            }
            sb.AppendLine($"=> {(ok ? "PASS" : "FAIL")}");
            return ok;
        }
    }
}
