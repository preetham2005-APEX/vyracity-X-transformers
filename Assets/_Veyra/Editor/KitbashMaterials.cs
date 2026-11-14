using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase C: materials for the kitbash buildings from the packs' own textures (Art/Textures/Imported/&lt;source&gt;/&lt;material&gt;_*),
    /// which the kitbash export strips from the FBX (C1 run 1: every kit model rendered plain white).
    /// <list type="bullet">
    /// <item>One URP Lit material per source material: Art/Materials/Kitbash/&lt;source&gt;/M_KB_&lt;material&gt;.mat (editor-made asset,
    /// referenced by the scene; no runtime material creation).</item>
    /// <item>_baseColor → _BaseMap (sRGB, BC7); glTF _metallicRoughness (G roughness, B metal) → a packed Unity metallic map
    /// T_&lt;material&gt;_MS.png (R metallic, A smoothness = 1 − roughness; Smoothness Source = Metallic Alpha, CLAUDE.md);
    /// _normal → normal map; _emissive → HDR emission (×1.5).</item>
    /// <item>Glass materials (name contains "glass") get smoothness 0.9 and a dark blue-grey tint when the texture is missing.</item>
    /// </list>
    /// Also "Veyra/Buildings/Kitbash Contact Sheet": every usable catalogue entry rendered on one sheet (Logs/C/kitbash_contact_sheet.png).
    /// </summary>
    public static class KitbashMaterials
    {
        const string TexRoot = "Assets/_Veyra/Art/Textures/Imported";
        const string MatRoot = "Assets/_Veyra/Art/Materials/Kitbash";
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public static int Created, Reused, NoTexture;

        static string Find(string dir, string mat, string suffix)
        {
            if (!Directory.Exists(dir)) return null;
            foreach (var ext in new[] { ".png", ".jpeg", ".jpg", ".tga" })
            {
                string p = $"{dir}/{mat}_{suffix}{ext}";
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static Material For(string source, string mat)
        {
            string key = source + "/" + mat;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            string dir = $"{MatRoot}/{source}";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/M_KB_{Safe(mat)}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) { Reused++; cache[key] = m; return m; }
            string texDir = $"{TexRoot}/{source}";
            string bc = Find(texDir, mat, "baseColor"), mr = Find(texDir, mat, "metallicRoughness"), nm = Find(texDir, mat, "normal"), em = Find(texDir, mat, "emissive");
            bool glass = mat.ToLowerInvariant().Contains("glass");
            // Glazing gets Veyra/KitGlass so the kit's buildings light up at night. On plain URP/Lit they have no
            // emission at all, which is why assembling the city from the kit turned downtown dark: the lit-window
            // work in VC_FacadeSurface only ever reached the shell buildings. KitGlass draws no window grid - the
            // kit's mullions are real geometry - and lights each panel per floor and per bay from world position.
            m = new Material(Shader.Find(glass ? "Veyra/KitGlass" : "Universal Render Pipeline/Lit")) { name = "M_KB_" + Safe(mat) };   // Editor code: allowed
            if (glass)
            {
                m.SetColor("_GlassColor", new Color(0.10f, 0.13f, 0.17f));
                m.SetFloat("_GlassSmoothness", 0.90f);
                m.SetFloat("_KitFloorHeight", 3.6f);
                m.SetFloat("_KitBayWidth", 3.0f);   // half-bay: a full 6 m bay lit as one panel reads as a block of light, not a window
                m.SetFloat("_LitFractionMin", 0.25f);
                m.SetFloat("_LitFractionMax", 0.7f);
                m.SetFloat("_WindowEmission", 1.7f);
                // towers and offices dim after 23:00; houses and low-rise do not
                m.SetFloat("_Office", source.Contains("Downtown") || source.Contains("Midrise") ? 1f : 0f);
                m.SetFloat("_Seed", Mathf.Abs(source.GetHashCode() % 997));
            }
            if (bc != null) { Import(bc, false, true); m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(bc)); }
            else { NoTexture++; m.SetColor("_BaseColor", glass ? new Color(0.18f, 0.22f, 0.26f) : new Color(0.55f, 0.53f, 0.5f)); }
            m.SetFloat("_Smoothness", glass ? 0.9f : 0.35f);
            // Veyra/KitGlass declares none of the URP/Lit map keywords below, so a glass material must not pick them
            // up: an enabled keyword a shader does not declare is dead weight on the material and a variant hazard.
            if (!glass && mr != null)
            {
                string packed = PackMetallic(mr, dir, mat);
                if (packed != null)
                {
                    m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packed));
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_SmoothnessTextureChannel", 0f);   // metallic alpha
                    m.SetFloat("_Smoothness", 1f);
                }
            }
            if (!glass && nm != null) { Import(nm, true, false); m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nm)); m.EnableKeyword("_NORMALMAP"); }
            if (!glass && em != null)
            {
                Import(em, false, true);
                m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(em));
                m.SetColor("_EmissionColor", Color.white * 1.5f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            Created++;
            cache[key] = m;
            return m;
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

        static void Import(string path, bool normal, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            bool dirty = false;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type) { ti.textureType = type; dirty = true; }
            if (ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
            if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; dirty = true; }
            if (dirty) ti.SaveAndReimport();
        }

        /// <summary>glTF metallicRoughness (G roughness, B metallic) → Unity metallic map (R metallic, A smoothness).</summary>
        static string PackMetallic(string src, string dir, string mat)
        {
            string outPath = $"{dir}/T_{Safe(mat)}_MS.png";
            if (!File.Exists(outPath))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!t.LoadImage(File.ReadAllBytes(src))) { Object.DestroyImmediate(t); return null; }
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    byte metal = px[i].b, rough = px[i].g;
                    px[i] = new Color32(metal, 0, 0, (byte)(255 - rough));
                }
                var o = new Texture2D(t.width, t.height, TextureFormat.RGBA32, true, true);
                o.SetPixels32(px);
                o.Apply();
                File.WriteAllBytes(outPath, o.EncodeToPNG());
                Object.DestroyImmediate(t); Object.DestroyImmediate(o);
                AssetDatabase.ImportAsset(outPath);
            }
            Import(outPath, false, false);
            return outPath;
        }

        /// <summary>Assigns the pack materials to every renderer of a kit instance, slot by slot (manifest order).</summary>
        public static void Apply(GameObject go, KitbashCatalog.Entry e)
        {
            if (e.SourceMaterials == null || e.SourceMaterials.Length == 0) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string have = mats[i] != null ? mats[i].name : "";
                    string src = e.SourceMaterials.FirstOrDefault(s => have.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                 ?? e.SourceMaterials[Mathf.Min(i, e.SourceMaterials.Length - 1)];
                    mats[i] = For(e.Source, src);
                }
                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------ contact sheet

        [MenuItem("Veyra/Buildings/Kitbash Contact Sheet")]
        public static void SheetMenu() => Debug.Log(ContactSheet());

        public static string ContactSheet()
        {
            var cat = AssetDatabase.LoadAssetAtPath<KitbashCatalog>(KitbashCatalogBuilder.CatalogPath);
            if (cat == null) return "no catalog";
            var list = cat.Entries.Where(e => !e.Excluded && e.Prefab != null).ToList();
            const int T = 256, Cols = 12;
            int rows = Mathf.CeilToInt(list.Count / (float)Cols);
            var sheet = new Texture2D(Cols * T, rows * T, TextureFormat.RGB24, false);
            var holder = new GameObject("[KB sheet]") { hideFlags = HideFlags.HideAndDontSave };
            var camGo = new GameObject("[KB cam]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.8f); cam.enabled = false; cam.fieldOfView = 30f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.renderShadows = false;
            var lightGo = new GameObject("[KB sun]") { hideFlags = HideFlags.HideAndDontSave };
            var sun = lightGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f; lightGo.transform.rotation = Quaternion.Euler(45f, 135f, 0f);
            var rt = new RenderTexture(T, T, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(T, T, TextureFormat.RGB24, false);
            var sb = new StringBuilder("Kitbash contact sheet (row-major, 12 per row)\n");
            var far = new Vector3(0f, -5000f, 0f);   // away from the city
            for (int k = 0; k < list.Count; k++)
            {
                var e = list[k];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(e.Prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.position = far;
                Apply(inst, e);
                var b = new Bounds(far, Vector3.zero);
                bool any = false;
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                camGo.transform.position = b.center + new Vector3(-0.8f, 0.45f, -1f).normalized * size * 1.9f;
                camGo.transform.LookAt(b.center);
                cam.nearClipPlane = size * 0.05f; cam.farClipPlane = size * 6f;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, T, T), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                int cx = k % Cols, cy = rows - 1 - k / Cols;
                sheet.SetPixels(cx * T, cy * T, T, T, read.GetPixels());
                sb.AppendLine($"{k,3} {e.Id} [{string.Join("/", e.Styles)}] {e.Footprint.x:0}x{e.Footprint.y:0} m, {e.Height:0} m");
                Object.DestroyImmediate(inst);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(read);
            Object.DestroyImmediate(holder); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
            Directory.CreateDirectory("Logs/C");
            File.WriteAllBytes("Logs/C/kitbash_contact_sheet.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            File.WriteAllText("Logs/C/kitbash_contact_sheet.txt", sb.ToString());
            AssetDatabase.SaveAssets();
            return $"contact sheet: {list.Count} entries -> Logs/C/kitbash_contact_sheet.png; kit materials created {Created}, reused {Reused}, without base texture {NoTexture}";
        }
    }
}
