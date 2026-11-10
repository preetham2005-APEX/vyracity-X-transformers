using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card L2: the post-processing stack as Volumes (Assets/_Veyra/Settings/Post).
    /// <list type="bullet">
    /// <item>VP_Global_Day (on WorldEnvironment's base volume, priority 0), VP_Global_Golden (priority 1) and VP_Global_Night
    /// (priority 2); <see cref="PostVolumeDirector"/> sets the Golden / Night weights from the time of day. All three carry
    /// the same overrides, so URP's blend runs day → golden → night.</item>
    /// <item>District looks as director-weighted global volumes: Neon Mile (night only), Ironport and Blackridge (day only).
    /// Tunnels: local box volumes along the three tunnel sections of road_network_geometry.json (blend 25 m).</item>
    /// <item>Values: the card's, except the day bloom threshold / intensity, kept at the visual pass's calibration (1.25 /
    /// 0.55: sunlit paving reaches ~0.9 in HDR, and the card's own calibration rule forbids bloom on daylight concrete).
    /// Depth of field off; motion blur stays with ChaseCamera's speed-scaled volume (priority 50).</item>
    /// </list>
    /// Idempotent: profiles are updated in place and scene objects are found by name, never duplicated.
    /// </summary>
    public static class VeyraPostVolumes
    {
        public const string Dir = "Assets/_Veyra/Settings/Post";
        const string RoadsJson = "Assets/_Veyra/Data/road_network_geometry.json";
        const int VolumeLayer = 2; // Ignore Raycast: trigger boxes stay out of default raycasts

        static readonly Color NightShadows = new Color(0x1F / 255f, 0x4F / 255f, 0x5F / 255f);
        static readonly Color NightHighlights = new Color(1f, 0x5F / 255f, 0xA0 / 255f);
        static readonly Color Neutral = new Color(0.5f, 0.5f, 0.5f);

        [MenuItem("Veyra/Rendering/Build Post Volumes")]
        static void Menu()
        {
            var env = Object.FindAnyObjectByType<WorldEnvironment>();
            Debug.Log(env == null ? "Open the city scene first (no WorldEnvironment)." : SetupScene(env));
        }

        static T Ensure<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet(out T c)) { c.active = true; return c; }
            c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static VolumeProfile Profile(string name)
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Veyra/Settings")) AssetDatabase.CreateFolder("Assets/_Veyra", "Settings");
                AssetDatabase.CreateFolder("Assets/_Veyra/Settings", "Post");
            }
            string path = $"{Dir}/{name}.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, path);
            }
            return p;
        }

        struct Look
        {
            public float BloomThreshold, BloomIntensity, Exposure, Contrast, Saturation, Temperature, Tint, Chromatic, Grain, Vignette, SplitBalance;
            public Color SplitShadows, SplitHighlights;
            public Vector4 SmhShadows;
        }

        static void Global(VolumeProfile p, Look l)
        {
            Ensure<Tonemapping>(p).mode.Override(TonemappingMode.ACES);
            var b = Ensure<Bloom>(p);
            b.threshold.Override(l.BloomThreshold);
            b.intensity.Override(l.BloomIntensity);
            b.scatter.Override(0.7f);
            b.clamp.Override(65472f);
            b.highQualityFiltering.Override(true);
            var ca = Ensure<ColorAdjustments>(p);
            ca.postExposure.Override(l.Exposure);
            ca.contrast.Override(l.Contrast);
            ca.saturation.Override(l.Saturation);
            ca.colorFilter.Override(Color.white);
            var wb = Ensure<WhiteBalance>(p);
            wb.temperature.Override(l.Temperature);
            wb.tint.Override(l.Tint);
            var st = Ensure<SplitToning>(p);
            st.shadows.Override(l.SplitShadows);
            st.highlights.Override(l.SplitHighlights);
            st.balance.Override(l.SplitBalance);
            var smh = Ensure<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(l.SmhShadows);
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            Ensure<ChromaticAberration>(p).intensity.Override(l.Chromatic);
            var fg = Ensure<FilmGrain>(p);
            fg.type.Override(FilmGrainLookup.Thin1);
            fg.intensity.Override(l.Grain);
            var v = Ensure<Vignette>(p);
            v.intensity.Override(l.Vignette);
            v.smoothness.Override(0.45f);
            Ensure<DepthOfField>(p).mode.Override(DepthOfFieldMode.Off);
            EditorUtility.SetDirty(p);
        }

        /// <summary>Creates / updates every profile; returns them by name.</summary>
        public static Dictionary<string, VolumeProfile> BuildProfiles()
        {
            var d = new Dictionary<string, VolumeProfile>();
            var day = Profile("VP_Global_Day");
            Global(day, new Look
            {
                BloomThreshold = 1.25f, BloomIntensity = 0.55f, Exposure = 0f, Contrast = 8f, Saturation = 4f, Temperature = 3f, Tint = 0f,
                SplitShadows = Neutral, SplitHighlights = Neutral, SplitBalance = 0f, SmhShadows = new Vector4(0.97f, 0.99f, 1.04f, 0f),
                Chromatic = 0f, Grain = 0f, Vignette = 0.15f,
            });
            d["Day"] = day;
            var golden = Profile("VP_Global_Golden");
            Global(golden, new Look
            {
                BloomThreshold = 1.1f, BloomIntensity = 1.4f, Exposure = 0.1f, Contrast = 8f, Saturation = 8f, Temperature = 18f, Tint = 4f,
                SplitShadows = Neutral, SplitHighlights = Neutral, SplitBalance = 0f, SmhShadows = new Vector4(0.98f, 0.98f, 1.02f, 0f),
                Chromatic = 0f, Grain = 0f, Vignette = 0.18f,
            });
            d["Golden"] = golden;
            var night = Profile("VP_Global_Night");
            Global(night, new Look
            {
                BloomThreshold = 0.9f, BloomIntensity = 1.2f, Exposure = 0.35f, Contrast = 12f, Saturation = 10f, Temperature = -8f, Tint = 0f,
                SplitShadows = NightShadows, SplitHighlights = NightHighlights, SplitBalance = -20f, SmhShadows = new Vector4(1f, 1f, 1f, 0f),
                Chromatic = 0.06f, Grain = 0.12f, Vignette = 0.25f,
            });
            d["Night"] = night;

            // District and tunnel looks: absolute values chosen relative to the time of day they are weighted for.
            var neon = Profile("VP_Local_NeonMile");
            var nb = Ensure<Bloom>(neon); nb.intensity.Override(1.6f); nb.threshold.Override(0.9f);
            var nca = Ensure<ColorAdjustments>(neon); nca.saturation.Override(10f + 14f);
            Ensure<ChromaticAberration>(neon).intensity.Override(0.08f);
            EditorUtility.SetDirty(neon);
            d["NeonMile"] = neon;
            var iron = Profile("VP_Local_Ironport");
            var ica = Ensure<ColorAdjustments>(iron); ica.contrast.Override(8f - 5f); ica.colorFilter.Override(new Color(0.98f, 0.95f, 0.9f));
            Ensure<WhiteBalance>(iron).tint.Override(3f);
            EditorUtility.SetDirty(iron);
            d["Ironport"] = iron;
            var ridge = Profile("VP_Local_Blackridge");
            Ensure<ColorAdjustments>(ridge).contrast.Override(8f + 5f);
            Ensure<WhiteBalance>(ridge).temperature.Override(3f - 8f);
            EditorUtility.SetDirty(ridge);
            d["Blackridge"] = ridge;
            var tunnel = Profile("VP_Local_Tunnel");
            var tca = Ensure<ColorAdjustments>(tunnel); tca.postExposure.Override(0.6f); tca.colorFilter.Override(new Color(1f, 0.86f, 0.62f));
            EditorUtility.SetDirty(tunnel);
            d["Tunnel"] = tunnel;
            AssetDatabase.SaveAssets();
            return d;
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static Volume GlobalVolume(Transform parent, string name, VolumeProfile p, float priority)
        {
            var go = Child(parent, name);
            go.layer = VolumeLayer;
            var v = go.GetComponent<Volume>();
            if (v == null) v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.sharedProfile = p;
            v.weight = 0f;
            return v;
        }

        /// <summary>Builds the profiles and wires the volumes into the open scene (WorldEnvironment's scene). Saves it.</summary>
        public static string SetupScene(WorldEnvironment env)
        {
            var log = new StringBuilder("L2 post volumes\n");
            var prof = BuildProfiles();
            log.AppendLine("profiles: " + string.Join(", ", prof.Values.Select(p => p.name)));
            if (env.PostVolume != null)
            {
                env.PostVolume.sharedProfile = prof["Day"];
                env.PostVolume.priority = 0f;
                env.PostVolume.weight = 1f;
                EditorUtility.SetDirty(env.PostVolume);
                log.AppendLine($"base volume '{env.PostVolume.name}' -> VP_Global_Day");
            }
            var root = Child(env.transform, "PostVolumes").transform;
            var director = root.GetComponent<PostVolumeDirector>();
            if (director == null) director = root.gameObject.AddComponent<PostVolumeDirector>();
            director.Environment = env;
            director.Golden = GlobalVolume(root, "VP_Golden", prof["Golden"], 1f);
            director.Night = GlobalVolume(root, "VP_Night", prof["Night"], 2f);

            var net = RoadNetwork.Parse(File.ReadAllText(RoadsJson), RoadsJson);
            Vector2[] Poly(string id) => net.Districts.TryGetValue(id, out var dr) ? dr.Polygon.Select(q => new Vector2(q[0] - 4000f, q[1] - 3000f)).ToArray() : null;
            director.Areas = new[]
            {
                new PostVolumeDirector.Area { Id = "D6 Neon Mile", Volume = GlobalVolume(root, "VP_NeonMile", prof["NeonMile"], 5f), Polygon = Poly("D6"), Gate = PostVolumeDirector.TimeGate.NightOnly, Blend = 60f },
                new PostVolumeDirector.Area { Id = "D3 Ironport", Volume = GlobalVolume(root, "VP_Ironport", prof["Ironport"], 5f), Polygon = Poly("D3"), Gate = PostVolumeDirector.TimeGate.DayOnly, Blend = 80f },
                new PostVolumeDirector.Area { Id = "D4 Blackridge", Volume = GlobalVolume(root, "VP_Blackridge", prof["Blackridge"], 5f), Polygon = Poly("D4"), Gate = PostVolumeDirector.TimeGate.DayOnly, Blend = 120f },
            };
            foreach (var a in director.Areas) log.AppendLine($"area {a.Id}: {(a.Polygon?.Length ?? 0)} polygon points");

            // Tunnels: oriented boxes every ~100 m along each tunnel's road profile.
            var tunnels = Child(root, "Tunnels").transform;
            for (int i = tunnels.childCount - 1; i >= 0; i--) Object.DestroyImmediate(tunnels.GetChild(i).gameObject);
            int boxes = 0;
            foreach (var s in net.Structures.Where(x => x.Kind == "tunnel"))
            {
                var road = net.Road(s.Road);
                if (road == null) continue;
                float width = RoadClasses.TryGet(road.Cls, out var info) ? info.WidthM : 12f;
                var pts = road.ProfileXYZ;
                int i0 = Mathf.Clamp(Mathf.FloorToInt(s.S0 / 4f), 0, pts.Length - 1), i1 = Mathf.Clamp(Mathf.CeilToInt(s.S1 / 4f), 0, pts.Length - 1);
                for (int a = i0, n = 0; a < i1; a += 25, n++)
                {
                    int b = Mathf.Min(a + 25, i1);
                    Vector3 P(int k) => new Vector3(pts[k][0] - 4000f, pts[k][2], pts[k][1] - 3000f);
                    Vector3 pa = P(a), pb = P(b);
                    var go = new GameObject($"VP_Tunnel_{s.Name.Replace(' ', '_')}_{n:00}") { layer = VolumeLayer };
                    go.transform.SetParent(tunnels, false);
                    var dir = pb - pa;
                    go.transform.SetPositionAndRotation((pa + pb) * 0.5f + Vector3.up * 4f, Quaternion.LookRotation(new Vector3(dir.x, dir.y, dir.z).normalized, Vector3.up));
                    var box = go.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = new Vector3(width + 8f, 12f, dir.magnitude + 4f);
                    var v = go.AddComponent<Volume>();
                    v.isGlobal = false;
                    v.priority = 10f;
                    v.blendDistance = 25f;
                    v.sharedProfile = prof["Tunnel"];
                    boxes++;
                }
            }
            log.AppendLine($"tunnels: {boxes} local box volumes");
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(env.gameObject.scene);
            EditorSceneManager.SaveScene(env.gameObject.scene);
            return log.ToString();
        }
    }
}
