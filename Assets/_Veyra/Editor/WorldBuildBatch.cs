using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Batch entry for the World track: runs the named world builders in order on the authoring scene and saves it once.
    /// <code>Unity.exe -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraCity.EditorTools.WorldBuildBatch.Run -veyraSteps roads,grass,terrainmaps</code>
    /// Steps: roads (BUG-111 road render meshes), grass (grass masks), terrainmaps (terrain layer PBR maps), trees (authored trees).
    /// Report: Logs/B1/world_build.txt. Exit 0 on success, 1 on any exception.
    /// </summary>
    public static class WorldBuildBatch
    {
        public static void Run()
        {
            int code = 0;
            var log = new StringBuilder($"World build batch {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            try
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-veyraSteps");
                string steps = i >= 0 && i + 1 < args.Length ? args[i + 1] : "roads,grass,terrainmaps";
                var scene = EditorSceneManager.OpenScene(CellDistributor.AuthoringScene, OpenSceneMode.Single);
                foreach (var raw in steps.Split(','))
                {
                    string s = raw.Trim().ToLowerInvariant();
                    var t0 = DateTime.Now;
                    switch (s)
                    {
                        case "roads": log.Append(RoadRenderBuilder.Build()); break;
                        case "grass": log.Append(GrassMasks.Apply()); break;
                        case "regrass":   // replant (GrassBuilder), then the masks
                        {
                            var wr = scene.GetRootGameObjects().First(g => g.name == "World").transform;
                            GrassBuilder.AddGrass(wr, "Assets/_Veyra/World/Generated");
                            log.Append(GrassMasks.Apply());
                            break;
                        }
                        case "terrainmaps": log.Append(TerrainLayerMaps.Run()); break;
                        case "trees": log.Append(TreeSwap.Run()); break;
                        case "seawalls": log.Append(SeawallBuilder.Build()); break;
                        case "catalog": log.Append(KitbashCatalogBuilder.Build()); break;
                        case "assemble": log.Append(BuildingAssembler.Run()); break;
                        case "neon": log.Append(NeonSignBuilder.Build()); break;
                        case "shore": log.Append(ShorelineBuilder.Build()); break;
                        case "pylons": log.Append(BridgePylonSmoother.Run()); break;
                        case "plaza": log.Append(PlazaBuilder.Run()); break;
                        case "hwlights": log.Append(HighwayLightsBuilder.Run()); break;
                        case "propclear": log.Append(PropRoadClear.Run()); break;
                        case "landmarks": log.Append(LandmarkBuilder.Run()); break;
                        case "port": log.Append(PortBuilder.Run()); break;
                        case "airfield": log.Append(AirfieldBuilder.Run()); break;
                        case "meadow": log.Append(MeadowPass.Run()); break;
                        case "lampfill": log.Append(LampFill.Run()); break;
                        case "profparse": { int pa = Array.IndexOf(args, "-veyraProf"); log.Append(ProfileParse.Run(pa >= 0 ? args[pa + 1] : "Logs/ITER/prof.raw")); break; }
                        case "lmaudit":   // iteration 23: what stands on every registry landmark point (renderers within 25 m)
                        {
                            var reg = VeyraCity.World.Data.LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
                            var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
                            foreach (var loc in reg.All.Where(l => l.Category == "Landmark"))
                            {
                                var c = loc.Unity;
                                var near = rs.Where(r => r.enabled && !(r is ParticleSystemRenderer) && r.bounds.size.x < 1500f
                                    && c.x > r.bounds.min.x - 25f && c.x < r.bounds.max.x + 25f && c.z > r.bounds.min.z - 25f && c.z < r.bounds.max.z + 25f).ToList();
                                var names = near.Select(r => (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key);
                                log.AppendLine($"  {loc.Id} {loc.Name} ({c.x:0},{c.y:0.0},{c.z:0}): {near.Count} renderers, top {(near.Count > 0 ? near.Max(r => r.bounds.max.y) : 0f):0.0} m | {string.Join("; ", names)}");
                            }
                            break;
                        }
                        case "boxprobe":   // iteration 23: BoxColliders within 30 m (x/z) of -veyraProbe x,z (RooftopDetails dresses every box top)
                        {
                            int pi = Array.IndexOf(args, "-veyraProbe");
                            var xz = args[pi + 1].Replace("m", "-").Split(',');
                            var p = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                            foreach (var bc in UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include))
                            {
                                var b = bc.bounds;
                                if (p.x < b.min.x - 30f || p.x > b.max.x + 30f || p.z < b.min.z - 30f || p.z > b.max.z + 30f) continue;
                                string path = bc.name; for (var t = bc.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                                var r = bc.GetComponentInParent<Renderer>();
                                log.AppendLine($"  box {path} | y {b.min.y:0.0}-{b.max.y:0.0} | x {b.min.x:0}-{b.max.x:0} z {b.min.z:0}-{b.max.z:0} | rot {bc.transform.eulerAngles} | size {bc.size} scale {bc.transform.lossyScale}");
                            }
                            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                            {
                                var b = r.bounds;
                                if (b.max.y < 100f || p.x < b.min.x - 30f || p.x > b.max.x + 30f || p.z < b.min.z - 30f || p.z > b.max.z + 30f) continue;
                                if (b.size.x > 600f || b.size.z > 600f) continue;
                                string path = r.name; for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                                log.AppendLine($"  tall {path} | enabled {r.enabled} | y {b.min.y:0.0}-{b.max.y:0.0}");
                                var mf = r.GetComponent<MeshFilter>();
                                if (mf == null || mf.sharedMesh == null) continue;
                                // connected parts (welded at 1 cm) whose top is above 100 m and that lie within 60 m of the probe
                                var mesh = mf.sharedMesh; var vv = mesh.vertices; var tt = mesh.triangles; var l2w = r.transform.localToWorldMatrix;
                                var wv = vv.Select(v => l2w.MultiplyPoint3x4(v)).ToArray();
                                var key = new System.Collections.Generic.Dictionary<Vector3Int, int>(); var id = new int[wv.Length];
                                for (int k = 0; k < wv.Length; k++) { var q = Vector3Int.RoundToInt(wv[k] * 100f); if (!key.TryGetValue(q, out id[k])) key[q] = id[k] = k; }
                                var par = Enumerable.Range(0, wv.Length).ToArray();
                                int Find(int a) { while (par[a] != a) a = par[a] = par[par[a]]; return a; }
                                for (int k = 0; k < tt.Length; k += 3) { int a = Find(id[tt[k]]); par[Find(id[tt[k + 1]])] = a; par[Find(id[tt[k + 2]])] = a; }
                                var groups = new System.Collections.Generic.Dictionary<int, (Bounds b, int tris)>();
                                for (int k = 0; k < tt.Length; k += 3)
                                {
                                    int g = Find(id[tt[k]]);
                                    var tb = new Bounds(wv[tt[k]], Vector3.zero); tb.Encapsulate(wv[tt[k + 1]]); tb.Encapsulate(wv[tt[k + 2]]);
                                    groups[g] = groups.TryGetValue(g, out var e) ? (Enc(e.b, tb), e.tris + 1) : (tb, 1);
                                }
                                static Bounds Enc(Bounds a, Bounds c) { a.Encapsulate(c); return a; }
                                foreach (var g in groups.Values.Where(g => g.b.max.y > 100f && Mathf.Abs(g.b.center.x - p.x) < 60f && Mathf.Abs(g.b.center.z - p.z) < 60f).OrderBy(g => g.b.min.y))
                                    log.AppendLine($"    part tris {g.tris} | x {g.b.min.x:0.0}-{g.b.max.x:0.0} y {g.b.min.y:0.0}-{g.b.max.y:0.0} z {g.b.min.z:0.0}-{g.b.max.z:0.0}");
                            }
                            break;
                        }
                        case "probe":   // renderers within 12 m (x/z) of -veyraProbe x,z
                        {
                            int pi = Array.IndexOf(args, "-veyraProbe");
                            foreach (var pt in args[pi + 1].Split(';'))
                            {
                            var xz = pt.Replace("m", "-").Split(',');
                            var p = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                            log.AppendLine($" probe {p.x:0},{p.z:0}");
                            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                            {
                                var b = r.bounds;
                                if (p.x < b.min.x - 12f || p.x > b.max.x + 12f || p.z < b.min.z - 12f || p.z > b.max.z + 12f) continue;
                                if (b.size.x > 600f || b.size.z > 600f) continue;
                                var mf = r.GetComponent<MeshFilter>();
                                string path = r.name; for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                                log.AppendLine($"  {path} | enabled {r.enabled} | mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} | mats {string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null"))} | y {b.min.y:0.0}-{b.max.y:0.0}");
                            }
                            }
                            break;
                        }
                        case "poolshot":   // iteration 13 debug: editor render of a neon light pool at 22:00
                        {
                            var env = UnityEngine.Object.FindAnyObjectByType<VeyraCity.World.WorldEnvironment>(FindObjectsInactive.Include);
                            float t0h = env != null ? env.TimeOfDay : 12f;
                            if (env != null) { env.SetTime(22f); env.SetWetness(1f); }
                            var pool = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include).Where(r => r.sharedMaterial != null && r.sharedMaterial.name == "M_NeonPool_Magenta" && r.bounds.center.x > -700f && r.bounds.center.x < -500f).OrderBy(r => r.bounds.size.magnitude).FirstOrDefault();
                            if (pool == null) { log.AppendLine("no pool"); break; }
                            var turnedOn = new System.Collections.Generic.List<GameObject>();
                            for (var tr = pool.transform; tr != null; tr = tr.parent) if (!tr.gameObject.activeSelf) { tr.gameObject.SetActive(true); turnedOn.Add(tr.gameObject); }
                            var b = pool.bounds;
                            var camGo = new GameObject("PoolCam"); var cam = camGo.AddComponent<Camera>();
                            cam.transform.position = b.center + new Vector3(12f, 7f, 12f);
                            cam.transform.LookAt(b.center); cam.fieldOfView = 60f; cam.farClipPlane = 2000f;
                            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
                            var pm = pool.sharedMaterial; var keepM = new Material(pm);
                            void Shot(string tag)
                            {
                                cam.Render();
                                RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                                System.IO.File.WriteAllBytes($"Logs/ITER/poolshot_{tag}.png", tex.EncodeToPNG());
                                RenderTexture.active = null;
                            }
                            Shot("asis");
                            var em = pm.GetTexture("_EmissionMap") as Texture2D;
                            log.AppendLine($"  map {(em != null ? em.name + " " + em.width + "x" + em.height + " readable " + em.isReadable : "null")}, shader {pm.shader.name}, queue {pm.renderQueue}");
                            var mf2 = pool.GetComponent<MeshFilter>().sharedMesh; var uvs = mf2.uv;
                            log.AppendLine($"  mesh {mf2.name} verts {mf2.vertexCount} uv {uvs.Length} uvmax {(uvs.Length > 0 ? uvs.Max(u => u.y) : -1)} bounds {pool.bounds}");
                            pm.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                            Shot("white");
                            pm.CopyPropertiesFromMaterial(keepM); pm.shaderKeywords = keepM.shaderKeywords; pm.renderQueue = keepM.renderQueue;
                            foreach (var h in Physics.RaycastAll(b.center + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
                                log.AppendLine($"  hit {h.collider.name} (layer {h.collider.gameObject.layer}, trigger {h.collider.isTrigger}) y {h.point.y:0.00}");
                            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                            {
                                var rb = r.bounds;
                                if (r == pool || rb.size.y > 40f || !rb.Contains(new Vector3(b.center.x, rb.center.y, b.center.z))) continue;
                                if (Mathf.Abs(rb.max.y - b.center.y) < 1.5f) log.AppendLine($"  renderer {r.name} ({(r.sharedMaterial != null ? r.sharedMaterial.name : "-")}) top {rb.max.y:0.00}");
                            }
                            log.AppendLine($"poolshot {pool.name} at {b.center} size {b.size}; emission {pool.sharedMaterial.GetColor("_EmissionColor")}");
                            UnityEngine.Object.DestroyImmediate(camGo);
                            foreach (var g in turnedOn) g.SetActive(false);
                            if (env != null) { env.SetTime(t0h); env.SetWetness(0f); }
                            break;
                        }
                        case "rayprobe":   // -veyraRay camX,camY,camZ,tgtX,tgtY,tgtZ,fov,u,v (u,v 0-1 from bottom-left, 16:9)
                        {
                            int ri = Array.IndexOf(args, "-veyraRay");
                            var f = args[ri + 1].Replace("m", "-").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                            var camGo = new GameObject("RayCam"); var cam = camGo.AddComponent<Camera>();
                            cam.transform.position = new Vector3(f[0], f[1], f[2]); cam.transform.LookAt(new Vector3(f[3], f[4], f[5]));
                            cam.fieldOfView = f[6]; cam.aspect = 16f / 9f;
                            var ray = cam.ViewportPointToRay(new Vector3(f[7], f[8], 0f));
                            log.AppendLine($"ray from {ray.origin} dir {ray.direction}");
                            var hits = new System.Collections.Generic.List<(float d, string s)>();
                            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                                if (!r.bounds.IntersectRay(ray, out float d)) continue;
                                var mf = r.GetComponent<MeshFilter>();
                                string path = r.name; for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                                hits.Add((d, $"  {d,7:0.0} m {path} | mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} | mats {string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null"))} | bounds {r.bounds.min} - {r.bounds.max}"));
                            }
                            foreach (var h in hits.OrderBy(h => h.d).Take(40)) log.AppendLine(h.s);
                            UnityEngine.Object.DestroyImmediate(camGo);
                            break;
                        }
                        case "camshot":   // -veyraRay camX,camY,camZ,tgtX,tgtY,tgtZ,fov -> Logs/ITER/camshot.png (authoring scene, edit mode)
                        {
                            int ri = Array.IndexOf(args, "-veyraRay");
                            var f = args[ri + 1].Replace("m", "-").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                            var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
                            cam.transform.position = new Vector3(f[0], f[1], f[2]); cam.transform.LookAt(new Vector3(f[3], f[4], f[5]));
                            cam.fieldOfView = f[6]; cam.farClipPlane = 5000f;
                            int bi = Array.IndexOf(args, "-veyraLodBias");
                            if (bi >= 0) QualitySettings.lodBias = float.Parse(args[bi + 1], System.Globalization.CultureInfo.InvariantCulture);
                            var envS = UnityEngine.Object.FindAnyObjectByType<VeyraCity.World.WorldEnvironment>(FindObjectsInactive.Include);
                            int hi = Array.IndexOf(args, "-veyraHour");
                            float hour0 = envS != null ? envS.TimeOfDay : 12f;
                            if (envS != null && hi >= 0) envS.SetTime(float.Parse(args[hi + 1], System.Globalization.CultureInfo.InvariantCulture));
                            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.aspect = 16f / 9f;
                            cam.Render();
                            RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                            System.IO.File.WriteAllBytes("Logs/ITER/camshot.png", tex.EncodeToPNG());
                            if (envS != null && hi >= 0) envS.SetTime(hour0);
                            RenderTexture.active = null;
                            UnityEngine.Object.DestroyImmediate(camGo);
                            break;
                        }
                        case "sheet": log.Append(KitbashMaterials.ContactSheet()); break;
                        case "pso": log.Append(PsoCollectionBuilder.Build()); break;   // merge Logs/PSO traces (-veyraTracePSO)
                        default: log.AppendLine($"unknown step '{s}'"); code = 1; break;
                    }
                    log.AppendLine($"[{s}] {(DateTime.Now - t0).TotalSeconds:0} s");
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); log.AppendLine("EXCEPTION " + e); code = 1; }
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/world_build.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
