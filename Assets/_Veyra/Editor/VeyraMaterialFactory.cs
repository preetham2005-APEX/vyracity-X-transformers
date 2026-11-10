using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Gameplay;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card F1: builds the material side of the runtime (Shader.Find is allowed here, in Editor code only).
    /// <list type="number">
    /// <item>Template materials (T_Lit, T_UnlitAdditive, T_UnlitAlpha, T_ParticleAlpha, T_ParticleAdditive, VC_Debug_Unlit) in
    /// Assets/_Veyra/World/Generated/Materials, keeping the GUIDs of the ones CityBuilder made before.</item>
    /// <item>The VeyraMaterialLibrary asset with a slot per <see cref="VeyraMat"/> key.</item>
    /// <item>_EMISSION on every material WorldEnvironment animates as emissive (brightness is a property, never a keyword).</item>
    /// <item>The library assigned to GameFlow and WorldEnvironment, or to a [VeyraMaterials] holder, in every build scene.</item>
    /// <item>VC_Variants.shadervariants: the variants of the library materials plus the variants the Editor tracked while
    /// rendering the 18 review cameras (day, golden, dusk, night, wet), limited to URP and project shaders; added to
    /// Graphics Settings &gt; Preloaded Shaders and referenced by the library for the warm-up.</item>
    /// </list>
    /// Every step is idempotent: a second run changes nothing.
    /// </summary>
    public static class VeyraMaterialFactory
    {
        public const string Dir = "Assets/_Veyra/World/Generated/Materials";
        public const string LibraryPath = Dir + "/VeyraMaterialLibrary.asset";
        public const string VariantsPath = "Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants";
        const string UrpLit = "Universal Render Pipeline/Lit";
        const string UrpUnlit = "Universal Render Pipeline/Unlit";
        const string UrpParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        sealed class Spec
        {
            public VeyraMat Key;
            public string Asset, Shader;
            public bool Transparent, Additive, Emission;
            public Spec(VeyraMat key, string asset, string shader, bool transparent, bool additive, bool emission)
            { Key = key; Asset = asset; Shader = shader; Transparent = transparent; Additive = additive; Emission = emission; }
        }

        static readonly Spec[] Templates =
        {
            new Spec(VeyraMat.FxLit, "T_Lit", UrpLit, false, false, true),
            new Spec(VeyraMat.FxUnlitAdditive, "T_UnlitAdditive", UrpUnlit, true, true, false),
            new Spec(VeyraMat.FxUnlitAlpha, "T_UnlitAlpha", UrpUnlit, true, false, false),
            new Spec(VeyraMat.FxParticleAlpha, "T_ParticleAlpha", UrpParticlesUnlit, true, false, false),
            new Spec(VeyraMat.FxParticleAdditive, "T_ParticleAdditive", UrpParticlesUnlit, true, true, false),
            new Spec(VeyraMat.DebugUnlit, "VC_Debug_Unlit", UrpUnlit, false, false, false),
        };

        /// <summary>Slots filled from existing material assets (not created here).</summary>
        static readonly (VeyraMat key, string path)[] Existing =
        {
            (VeyraMat.VehiclePlaceholderBody, "Assets/_Veyra/Art/TestTrack/M_Placeholder_CarBody.mat"),
            (VeyraMat.VehiclePlaceholderWheel, "Assets/_Veyra/Art/TestTrack/M_Placeholder_Wheel.mat"),
        };

        // ------------------------------------------------------------------ menu

        [MenuItem("Veyra/Materials/Build Material Library")]
        static void BuildLibraryMenu() => Debug.Log("[VeyraMaterialFactory] " + BuildLibrary());

        [MenuItem("Veyra/Materials/Assign Library To Build Scenes")]
        static void AssignMenu() => Debug.Log("[VeyraMaterialFactory] " + AssignToBuildScenes());

        [MenuItem("Veyra/Materials/Build Shader Variants (library only)")]
        static void VariantsMenu() => Debug.Log("[VeyraMaterialFactory] " + BuildVariants(null));

        [MenuItem("Veyra/Materials/Capture Shader Variants From Review Cameras")]
        static void CaptureMenu() => Debug.Log("[VeyraMaterialFactory] " + StartVariantCapture());

        // ------------------------------------------------------------------ 1–3: templates, library, emission

        /// <summary>Creates or updates the templates and the library, and puts _EMISSION on the animated emissives. Returns a report.</summary>
        public static string BuildLibrary()
        {
            var report = new StringBuilder();
            Directory.CreateDirectory(Dir);
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<VeyraMaterialLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
                report.AppendLine($"created {LibraryPath}");
            }
            var slots = new Dictionary<VeyraMat, Material>();
            foreach (var s in lib.Slots) if (s != null && s.Key != VeyraMat.None && s.Material != null) slots[s.Key] = s.Material;

            foreach (var spec in Templates)
            {
                var m = EnsureTemplate(spec, out bool created);
                slots[spec.Key] = m;
                report.AppendLine($"{(created ? "created" : "updated")} {spec.Key,-22} {AssetDatabase.GetAssetPath(m)} ({spec.Shader}{(spec.Transparent ? spec.Additive ? ", transparent additive" : ", transparent alpha" : "")}{(spec.Emission ? ", _EMISSION on" : "")})");
            }
            foreach (var (key, path) in Existing)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null) { slots[key] = m; report.AppendLine($"linked  {key,-22} {path}"); }
                else report.AppendLine($"MISSING {key,-22} {path}");
            }

            var newSlots = slots.OrderBy(kv => (int)kv.Key).Select(kv => new VeyraMaterialLibrary.Slot { Key = kv.Key, Material = kv.Value }).ToList();
            if (!SameSlots(lib.Slots, newSlots)) { lib.Slots = newSlots; EditorUtility.SetDirty(lib); }
            var missing = lib.MissingKeys();
            report.AppendLine(missing.Count == 0 ? $"library complete: {lib.Slots.Count} slots" : "library INCOMPLETE, empty slots: " + string.Join(", ", missing));
            report.Append(EnsureEmissionKeywords());
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        static bool SameSlots(List<VeyraMaterialLibrary.Slot> a, List<VeyraMaterialLibrary.Slot> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] == null || a[i].Key != b[i].Key || a[i].Material != b[i].Material) return false;
            return true;
        }

        static Material EnsureTemplate(Spec spec, out bool created)
        {
            string path = $"{Dir}/{spec.Asset}.mat";
            var shader = Shader.Find(spec.Shader);
            if (shader == null) throw new InvalidOperationException($"shader '{spec.Shader}' not found (is URP installed?)");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = m == null;
            if (created)
            {
                m = new Material(shader) { name = spec.Asset };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != shader) m.shader = shader;
            if (spec.Transparent) SetupTransparent(m, spec.Additive);
            else SetupOpaque(m);
            if (spec.Emission)
            {
                m.EnableKeyword("_EMISSION");
                if (created) m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP transparent set-up (the same values URP's material inspector writes); Editor only.</summary>
        static void SetupTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        static void SetupOpaque(Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.renderQueue = -1;
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        /// <summary>
        /// WorldEnvironment changes only _EmissionColor at runtime (card F1: no keyword toggles), so every material it animates
        /// as emissive must have _EMISSION on in its asset; otherwise its night glow would never show in a player.
        /// </summary>
        public static string EnsureEmissionKeywords()
        {
            var env = UnityEngine.Object.FindAnyObjectByType<WorldEnvironment>(FindObjectsInactive.Include);
            if (env == null) return "emission keywords: no WorldEnvironment in the open scene (skipped)\n";
            int ok = 0, fixedCount = 0;
            var fixedNames = new List<string>();
            foreach (var e in env.Emissives)
            {
                if (e.Material == null) continue;
                if (e.Material.IsKeywordEnabled("_EMISSION")) { ok++; continue; }
                e.Material.EnableKeyword("_EMISSION");
                e.Material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(e.Material);
                fixedCount++;
                fixedNames.Add(e.Material.name);
            }
            return $"emission keywords: {env.Emissives.Count} WorldEnvironment emissives, {ok} already had _EMISSION, {fixedCount} turned on{(fixedCount > 0 ? " (" + string.Join(", ", fixedNames) + ")" : "")}\n";
        }

        // ------------------------------------------------------------------ 4: scenes

        /// <summary>Assigns the library in every enabled build scene (GameFlow, WorldEnvironment, else a [VeyraMaterials] holder).</summary>
        public static string AssignToBuildScenes()
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(LibraryPath);
            if (lib == null) return "no library: run Build Material Library first";
            var active = EditorSceneManager.GetActiveScene();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return $"ABORT: scene {EditorSceneManager.GetSceneAt(i).path} has unsaved changes";
            string returnTo = active.path;
            var report = new StringBuilder();
            foreach (var bs in EditorBuildSettings.scenes)
            {
                if (!bs.enabled) continue;
                var scene = EditorSceneManager.OpenScene(bs.path, OpenSceneMode.Single);
                // Opening a scene unloads unused assets, which destroys the library loaded before it: load it again.
                lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(LibraryPath);
                var done = new List<string>();
                int changed = 0;
                foreach (var gf in UnityEngine.Object.FindObjectsByType<GameFlow>(FindObjectsInactive.Include))
                {
                    changed += SetLibrary(gf, lib);
                    done.Add("GameFlow");
                }
                foreach (var env in UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsInactive.Include))
                {
                    changed += SetLibrary(env, lib);
                    done.Add("WorldEnvironment");
                }
                if (done.Count == 0)
                {
                    var holder = UnityEngine.Object.FindAnyObjectByType<VeyraMaterialsHolder>(FindObjectsInactive.Include);
                    if (holder == null)
                    {
                        holder = new GameObject("[VeyraMaterials]").AddComponent<VeyraMaterialsHolder>();
                        changed++;
                    }
                    changed += SetLibrary(holder, lib);
                    done.Add("VeyraMaterialsHolder");
                }
                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                report.AppendLine($"{bs.path}: {string.Join(", ", done)} -> library ({(changed > 0 ? $"{changed} change(s), saved" : "already assigned")})");
            }
            if (!string.IsNullOrEmpty(returnTo)) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
            return report.ToString();
        }

        static int SetLibrary(UnityEngine.Object component, VeyraMaterialLibrary lib)
        {
            if (lib == null) throw new InvalidOperationException("library asset not loaded");
            var so = new SerializedObject(component);
            var p = so.FindProperty("materials");
            if (p == null) throw new InvalidOperationException($"{component.GetType().Name} has no serialized field 'materials'");
            if (p.objectReferenceValue == lib) return 0;
            p.objectReferenceValue = lib;
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        // ------------------------------------------------------------------ 5: shader variants

        static MethodInfo ShaderUtilMethod(string name) =>
            typeof(ShaderUtil).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        public static bool CanTrackVariants => ShaderUtilMethod("ClearCurrentShaderVariantCollection") != null && ShaderUtilMethod("SaveCurrentShaderVariantCollection") != null;

        static EditorApplication.CallbackFunction captureWait;
        public static bool CaptureRunning => captureWait != null;
        public static string LastCaptureReport { get; private set; } = "";

        /// <summary>
        /// Clears the Editor's variant tracking, renders the 18 review cameras of the open scene (card S5), then saves the tracked
        /// variants and merges them into VC_Variants.shadervariants. Asynchronous; poll <see cref="CaptureRunning"/>.
        /// </summary>
        public static string StartVariantCapture()
        {
            if (CaptureRunning || ReviewCaptureRunner.IsRunning) return "busy: a capture is running";
            if (!CanTrackVariants) return "UnityEditor.ShaderUtil has no Clear/SaveCurrentShaderVariantCollection in this Unity; use Build Shader Variants (library only)";
            ShaderUtilMethod("ClearCurrentShaderVariantCollection").Invoke(null, null);
            string status = ReviewCaptureRunner.Start("F1_variants");
            bool sawRunning = false;
            double t0 = EditorApplication.timeSinceStartup;
            captureWait = () =>
            {
                if (ReviewCaptureRunner.IsRunning) { sawRunning = true; return; }
                // In Play mode the runtime capture only starts a few frames later (it waits for GameFlow): give it 30 s to begin.
                if (!sawRunning && EditorApplication.timeSinceStartup - t0 < 30.0) return;
                EditorApplication.update -= captureWait;
                captureWait = null;
                // Unity writes the tracked collection as an asset, so the temporary file must be inside Assets/.
                const string tracked = "Assets/_Veyra/Art/Shaders/_tracked_tmp.shadervariants";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(VariantsPath));
                    ShaderUtilMethod("SaveCurrentShaderVariantCollection").Invoke(null, new object[] { tracked });
                    AssetDatabase.ImportAsset(tracked);
                    var trackedSvc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(tracked);
                    if (trackedSvc == null) throw new InvalidOperationException("the Editor did not write the tracked variants to " + tracked);
                    LastCaptureReport = (sawRunning ? "" : "warning: the review capture finished before the first check\n") +
                                        $"tracked while rendering the review cameras: {trackedSvc.shaderCount} shaders, {trackedSvc.variantCount} variants\n" + BuildVariants(trackedSvc);
                }
                catch (Exception ex)
                {
                    LastCaptureReport = "variant capture FAILED: " + (ex.InnerException ?? ex).Message;
                    Debug.LogException(ex);
                }
                finally
                {
                    if (AssetDatabase.LoadMainAssetAtPath(tracked) != null) AssetDatabase.DeleteAsset(tracked);
                }
                Debug.Log("[VeyraMaterialFactory] " + LastCaptureReport);
            };
            EditorApplication.update += captureWait;
            return "variant capture started: " + status;
        }

        /// <summary>Keeps tracked variants of URP shaders and project shaders (Assets/); drops editor-only and legacy built-ins.</summary>
        static bool KeepShader(Shader s)
        {
            if (s == null) return false;
            string path = AssetDatabase.GetAssetPath(s);
            if (path.StartsWith("Assets/")) return true;
            if (path.StartsWith("Packages/com.unity.render-pipelines.universal/") || path.StartsWith("Packages/com.unity.render-pipelines.core/")) return true;
            return false;
        }

        /// <summary>Rebuilds VC_Variants from the library materials (+ <paramref name="tracked"/>), links it in the library and Preloaded Shaders.</summary>
        public static string BuildVariants(ShaderVariantCollection tracked)
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(LibraryPath);
            if (lib == null) return "no library: run Build Material Library first";
            Directory.CreateDirectory(Path.GetDirectoryName(VariantsPath));
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(VariantsPath);
            if (svc == null)
            {
                svc = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(svc, VariantsPath);
            }
            var wanted = new List<ShaderVariantCollection.ShaderVariant>();
            int rejected = 0;
            foreach (var slot in lib.Slots)
            {
                if (slot?.Material == null) continue;
                var m = slot.Material;
                bool added = false;
                foreach (var pass in new[] { PassType.ScriptableRenderPipeline, PassType.ScriptableRenderPipelineDefaultUnlit })
                {
                    try { wanted.Add(new ShaderVariantCollection.ShaderVariant(m.shader, pass, m.shaderKeywords)); added = true; }
                    catch (ArgumentException) { }
                }
                if (!added) rejected++;
            }
            int fromLibrary = wanted.Count, trackedKept = 0, trackedDropped = 0, trackedInvalid = 0;
            if (tracked != null)
            {
                var so = new SerializedObject(tracked);
                var shaders = so.FindProperty("m_Shaders");
                for (int i = 0; i < shaders.arraySize; i++)
                {
                    var entry = shaders.GetArrayElementAtIndex(i);
                    var shader = entry.FindPropertyRelative("first").objectReferenceValue as Shader;
                    var variants = entry.FindPropertyRelative("second.variants");
                    if (!KeepShader(shader)) { trackedDropped += variants.arraySize; continue; }
                    for (int v = 0; v < variants.arraySize; v++)
                    {
                        var vv = variants.GetArrayElementAtIndex(v);
                        string keywords = vv.FindPropertyRelative("keywords").stringValue;
                        var pass = (PassType)vv.FindPropertyRelative("passType").intValue;
                        try
                        {
                            wanted.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, string.IsNullOrWhiteSpace(keywords) ? Array.Empty<string>() : keywords.Split(' ')));
                            trackedKept++;
                        }
                        catch (ArgumentException) { trackedInvalid++; }
                    }
                }
            }
            else if (svc.variantCount > fromLibrary)
            {
                return $"VC_Variants already holds {svc.variantCount} variants (a capture); rebuilding from the library only would drop them. Use Capture Shader Variants From Review Cameras.";
            }
            svc.Clear();
            foreach (var v in wanted) svc.Add(v);
            EditorUtility.SetDirty(svc);
            if (lib.Variants != svc) { lib.Variants = svc; EditorUtility.SetDirty(lib); }
            string preload = AddToPreloadedShaders(svc);
            AssetDatabase.SaveAssets();
            return $"VC_Variants: {svc.shaderCount} shaders, {svc.variantCount} variants (library materials {fromLibrary}, tracked kept {trackedKept}, " +
                   $"tracked dropped {trackedDropped} (editor/legacy shaders), invalid {trackedInvalid}, library materials without a valid pass {rejected}); {preload}";
        }

        static string AddToPreloadedShaders(ShaderVariantCollection svc)
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) return "GraphicsSettings.asset not found";
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_PreloadedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == svc) return "already in Preloaded Shaders";
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = svc;
            so.ApplyModifiedPropertiesWithoutUndo();
            return "added to Preloaded Shaders";
        }

        /// <summary>The library slots, Preloaded Shaders and Always Included Shaders as text (for Logs/F1).</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(LibraryPath);
            sb.AppendLine($"library {LibraryPath}: {(lib == null ? "missing" : lib.Slots.Count + " slots")}");
            if (lib != null)
                foreach (var s in lib.Slots)
                    sb.AppendLine($"  {VeyraMaterialLibrary.CategoryOf(s.Key),-8} {s.Key,-24} {(s.Material != null ? AssetDatabase.GetAssetPath(s.Material) + " | " + s.Material.shader.name + " | keywords: " + string.Join(" ", s.Material.shaderKeywords) : "EMPTY")}");
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs != null)
            {
                var so = new SerializedObject(gs);
                foreach (var prop in new[] { "m_AlwaysIncludedShaders", "m_PreloadedShaders" })
                {
                    var list = so.FindProperty(prop);
                    sb.AppendLine($"{prop}: {list.arraySize}");
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                        sb.AppendLine($"  {(o == null ? "null" : o.name)} ({(o == null ? "" : AssetDatabase.GetAssetPath(o))})");
                    }
                }
            }
            if (lib != null && lib.Variants != null) sb.AppendLine($"variants: {AssetDatabase.GetAssetPath(lib.Variants)} — {lib.Variants.shaderCount} shaders, {lib.Variants.variantCount} variants");
            return sb.ToString();
        }
    }
}
