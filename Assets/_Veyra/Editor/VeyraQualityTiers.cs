using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card L1: URP assets and quality levels for PC (RTX 4070, 1440p, 60+ FPS) plus the later Android tiers.
    /// <list type="bullet">
    /// <item>Assets/_Veyra/Settings/VC_URP_&lt;tier&gt;.asset + VC_Renderer_&lt;tier&gt;.asset for PC_Ultra, PC_High (default),
    /// PC_Medium, Mobile_Mid, Mobile_Low. The old Assets/Settings/PC_RPAsset / PC_Renderer become PC_High and Mobile_RPAsset /
    /// Mobile_Renderer become Mobile_Mid (moved, so every GUID reference stays valid); the others are copies.</item>
    /// <item>Quality levels with the same names, in the order Mobile_Low, Mobile_Mid, PC_Medium, PC_High, PC_Ultra;
    /// Standalone defaults to PC_High, Android to Mobile_Mid.</item>
    /// <item>Per-tier values from the card (shadows, cascades, STP upscaling, HDR, depth/opaque textures, probe blending, SRP
    /// Batcher, LOD cross-fade, SSAO, decals). The GPU Resident Drawer stays off: the hand-written Veyra shaders (roads,
    /// facades, neon) have no DOTS-instancing variant, so it would only take the few URP/Lit renderers.</item>
    /// </list>
    /// Idempotent: a second run changes nothing. CityBuilder calls <see cref="Apply"/> instead of overwriting every URP asset
    /// (BUG-045).
    /// </summary>
    public static class VeyraQualityTiers
    {
        public const string Dir = "Assets/_Veyra/Settings";

        public sealed class Tier
        {
            public string Name;
            public bool Mobile;
            public float ShadowDistance, RenderScale;
            public int Cascades, MainShadowRes, AdditionalShadowAtlas, SoftShadowQuality; // 0 off, 1 low, 2 medium, 3 high
            // Keyword-relevant values (soft-shadow quality, SSAO on/off) are the same on every PC tier: each different value adds a
            // full set of URP/Lit variants to the Windows build (L1 measured 2,880 ForwardLit variants per API with mixed tiers).
            // PC_Medium saves through shadow resolution, cascades, render scale and half-resolution SSAO instead.
            public bool Hdr, Ssao, Stp, SsaoHalfRes;
            public Vector3 Split4 = new Vector3(0.06f, 0.18f, 0.45f);
            public float Split2 = 0.25f;
        }

        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "Mobile_Low", Mobile = true, ShadowDistance = 25f, Cascades = 1, MainShadowRes = 1024, AdditionalShadowAtlas = 512, SoftShadowQuality = 0, Hdr = false, Ssao = false, Stp = false, RenderScale = 0.75f },
            new Tier { Name = "Mobile_Mid", Mobile = true, ShadowDistance = 40f, Cascades = 2, MainShadowRes = 2048, AdditionalShadowAtlas = 1024, SoftShadowQuality = 1, Hdr = true, Ssao = false, Stp = false, RenderScale = 0.85f },
            new Tier { Name = "PC_Medium", ShadowDistance = 120f, Cascades = 2, MainShadowRes = 2048, AdditionalShadowAtlas = 2048, SoftShadowQuality = 3, Hdr = true, Ssao = true, SsaoHalfRes = true, Stp = true, RenderScale = 0.8f },
            new Tier { Name = "PC_High", ShadowDistance = 160f, Cascades = 4, MainShadowRes = 4096, AdditionalShadowAtlas = 4096, SoftShadowQuality = 3, Hdr = true, Ssao = true, Stp = true, RenderScale = 0.9f },
            new Tier { Name = "PC_Ultra", ShadowDistance = 250f, Cascades = 4, MainShadowRes = 4096, AdditionalShadowAtlas = 4096, SoftShadowQuality = 3, Hdr = true, Ssao = true, Stp = true, RenderScale = 1.0f },
        };

        static readonly Dictionary<string, (string asset, string renderer)> Legacy = new Dictionary<string, (string, string)>
        {
            ["PC_High"] = ("Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset"),
            ["Mobile_Mid"] = ("Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset"),
        };

        public static string AssetPath(string tier) => $"{Dir}/VC_URP_{tier}.asset";
        public static string RendererPath(string tier) => $"{Dir}/VC_Renderer_{tier}.asset";

        [MenuItem("Veyra/Rendering/Build Quality Tiers")]
        static void Menu() => Debug.Log(Apply());

        public static string Apply()
        {
            var log = new StringBuilder("L1 quality tiers\n");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Veyra", "Settings");
            // 1. Move the two existing assets into place (keeps GUIDs: GraphicsSettings, QualitySettings, scenes stay linked).
            foreach (var kv in Legacy)
            {
                Move(kv.Value.asset, AssetPath(kv.Key), log);
                Move(kv.Value.renderer, RendererPath(kv.Key), log);
            }
            // 2. Copies for the other tiers (PC from PC_High, mobile from Mobile_Mid), each with its own renderer data.
            foreach (var t in Tiers)
            {
                if (File.Exists(AssetPath(t.Name))) continue;
                string src = t.Mobile ? "Mobile_Mid" : "PC_High";
                AssetDatabase.CopyAsset(RendererPath(src), RendererPath(t.Name));
                AssetDatabase.CopyAsset(AssetPath(src), AssetPath(t.Name));
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath(t.Name));
                var so = new SerializedObject(a);
                var list = so.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath(t.Name));
                so.FindProperty("m_DefaultRendererIndex").intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"created {AssetPath(t.Name)} + {RendererPath(t.Name)} (copy of {src})");
            }
            AssetDatabase.SaveAssets();
            // 3. Tier values.
            foreach (var t in Tiers) log.Append(Configure(t));
            // 4. Quality levels.
            log.Append(ConfigureQualityLevels());
            var high = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetPath("PC_High"));
            if (GraphicsSettings.defaultRenderPipeline != high) { GraphicsSettings.defaultRenderPipeline = high; log.AppendLine("GraphicsSettings default pipeline = PC_High"); }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static void Move(string from, string to, StringBuilder log)
        {
            if (File.Exists(to) || !File.Exists(from)) return;
            string err = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(err) ? $"moved {from} -> {to}" : $"MOVE FAILED {from}: {err}");
        }

        static bool Set(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null) return false;
            if (p.propertyType == SerializedPropertyType.Float) { if (Mathf.Approximately(p.floatValue, v)) return false; p.floatValue = v; return true; }
            if (p.propertyType == SerializedPropertyType.Boolean) { bool b = v != 0f; if (p.boolValue == b) return false; p.boolValue = b; return true; }
            int i = Mathf.RoundToInt(v);
            if (p.intValue == i) return false;
            p.intValue = i;
            return true;
        }

        static string Configure(Tier t)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath(t.Name));
            var r = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath(t.Name));
            if (a == null || r == null) return $"MISSING {t.Name}\n";
            var so = new SerializedObject(a);
            int changes = 0;
            void S(string p, float v) { if (Set(so, p, v)) changes++; }
            S("m_SupportsHDR", t.Hdr ? 1 : 0);
            S("m_HDRColorBufferPrecision", 0);           // 32-bit (R11G11B10); 64-bit only if bloom bands
            S("m_RequireDepthTexture", 1);
            S("m_RequireOpaqueTexture", t.Mobile ? 0 : 1);
            S("m_MSAA", t.Stp ? 1 : (t.Mobile ? 2 : 4));
            S("m_RenderScale", t.RenderScale);
            S("m_UpscalingFilter", t.Stp ? (int)UpscalingFilterSelection.STP : (int)UpscalingFilterSelection.Auto);
            S("m_EnableLODCrossFade", 1);
            S("m_MainLightShadowsSupported", 1);
            S("m_MainLightShadowmapResolution", t.MainShadowRes);
            S("m_AdditionalLightShadowsSupported", 1);
            S("m_AdditionalLightsShadowmapResolution", t.AdditionalShadowAtlas);
            S("m_AdditionalLightsShadowResolutionTierLow", t.AdditionalShadowAtlas / 8);
            S("m_AdditionalLightsShadowResolutionTierMedium", t.AdditionalShadowAtlas / 4);
            S("m_AdditionalLightsShadowResolutionTierHigh", t.AdditionalShadowAtlas / 2);
            S("m_ReflectionProbeBlending", 1);
            S("m_ReflectionProbeBoxProjection", 1);
            S("m_ShadowDistance", t.ShadowDistance);
            S("m_ShadowCascadeCount", t.Cascades);
            S("m_Cascade2Split", t.Split2);
            var c4 = so.FindProperty("m_Cascade4Split");
            if (c4 != null && c4.vector3Value != t.Split4) { c4.vector3Value = t.Split4; changes++; }
            S("m_CascadeBorder", 0.1f);
            // Biases: the PC values tuned in the visual pass (depth 0.1, normal 0.5: no peter-panning on lamp poles, no acne at 4096
            // over 160 m); mobile keeps URP's 1 / 1 for its coarser maps.
            S("m_ShadowDepthBias", t.Mobile ? 1.0f : 0.1f);
            S("m_ShadowNormalBias", t.Mobile ? 1.0f : 0.5f);
            S("m_SoftShadowsSupported", t.SoftShadowQuality > 0 ? 1 : 0);
            if (t.SoftShadowQuality > 0) S("m_SoftShadowQuality", t.SoftShadowQuality);
            S("m_UseSRPBatcher", 1);
            S("m_GPUResidentDrawerMode", 0);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (r.renderingMode != RenderingMode.ForwardPlus) { r.renderingMode = RenderingMode.ForwardPlus; changes++; }
            foreach (var f in r.rendererFeatures)
            {
                if (f is ScreenSpaceAmbientOcclusion ssao)
                {
                    if (ssao.isActive != t.Ssao) { ssao.SetActive(t.Ssao); changes++; }
                    var fso = new SerializedObject(ssao);
                    if (Set(fso, "m_Settings.Downsample", t.SsaoHalfRes ? 1 : 0)) { changes++; fso.ApplyModifiedPropertiesWithoutUndo(); }
                }
            }
            if (changes > 0) { EditorUtility.SetDirty(a); EditorUtility.SetDirty(r); }
            return $"{t.Name,-11} shadows {t.ShadowDistance} m x{t.Cascades} @{t.MainShadowRes}, soft {t.SoftShadowQuality}, scale {t.RenderScale} {(t.Stp ? "STP" : "MSAA")}, HDR {t.Hdr}, SSAO {t.Ssao}: {changes} change(s)\n";
        }

        static string ConfigureQualityLevels()
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs == null) return "QualitySettings.asset not found\n";
            var so = new SerializedObject(qs);
            var arr = so.FindProperty("m_QualitySettings");
            var log = new StringBuilder();
            // Rename the two old levels first (PC -> PC_High, Mobile -> Mobile_Mid), then add the missing ones by copying.
            for (int i = 0; i < arr.arraySize; i++)
            {
                var n = arr.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (n.stringValue == "PC") { n.stringValue = "PC_High"; log.AppendLine("quality level PC -> PC_High"); }
                if (n.stringValue == "Mobile") { n.stringValue = "Mobile_Mid"; log.AppendLine("quality level Mobile -> Mobile_Mid"); }
            }
            int Index(string name) { for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i; return -1; }
            string[] order = Tiers.Select(t => t.Name).ToArray();
            foreach (var t in Tiers)
            {
                if (Index(t.Name) >= 0) continue;
                int src = Index(t.Mobile ? "Mobile_Mid" : "PC_High");
                if (src < 0) src = arr.arraySize - 1;
                arr.InsertArrayElementAtIndex(src);
                arr.GetArrayElementAtIndex(src + 1).FindPropertyRelative("name").stringValue = t.Name;
                log.AppendLine($"quality level {t.Name} added");
            }
            // Sort into the tier order (bubble by moves) and point each level at its URP asset.
            for (int target = 0; target < order.Length; target++)
            {
                int at = Index(order[target]);
                if (at >= 0 && at != target) arr.MoveArrayElement(at, target);
            }
            foreach (var t in Tiers)
            {
                var e = arr.GetArrayElementAtIndex(Index(t.Name));
                var rp = e.FindPropertyRelative("customRenderPipeline");
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetPath(t.Name));
                if (rp.objectReferenceValue != asset) rp.objectReferenceValue = asset;
                var aa = e.FindPropertyRelative("antiAliasing");
                if (aa != null) aa.intValue = t.Stp ? 0 : (t.Mobile ? 2 : 4);
                // BUG-068 (D1): realtime reflection probes on the PC tiers (the world box probes and the car probe render only
                // with this on); mobile keeps them off.
                var rrp = e.FindPropertyRelative("realtimeReflectionProbes");
                if (rrp != null && rrp.boolValue != !t.Mobile) { rrp.boolValue = !t.Mobile; log.AppendLine($"{t.Name}: realtimeReflectionProbes = {!t.Mobile}"); }
                var sd = e.FindPropertyRelative("shadowDistance");
                if (sd != null) sd.floatValue = t.ShadowDistance;
            }
            // Platform defaults: Standalone -> PC_High, Android -> Mobile_Mid.
            var defs = so.FindProperty("m_PerPlatformDefaultQuality");
            if (defs != null)
                for (int i = 0; i < defs.arraySize; i++)
                {
                    var e = defs.GetArrayElementAtIndex(i);
                    string plat = e.FindPropertyRelative("first").stringValue;
                    var v = e.FindPropertyRelative("second");
                    if (plat == "Standalone") v.intValue = Index("PC_High");
                    else if (plat == "Android") v.intValue = Index("Mobile_Mid");
                }
            var cur = so.FindProperty("m_CurrentQuality");
            if (cur != null) cur.intValue = Index("PC_High");
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            log.AppendLine("quality levels: " + string.Join(", ", QualitySettings.names));
            return log.ToString();
        }
    }
}
