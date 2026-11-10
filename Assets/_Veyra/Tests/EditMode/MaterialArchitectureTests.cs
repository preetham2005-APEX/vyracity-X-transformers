using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.EditorTools;
using VeyraCity.World;

namespace VeyraCity.Tests
{
    /// <summary>
    /// Card F1 guard rails (BUG-002): runtime code never looks shaders up by name or toggles shader keywords, every runtime
    /// material comes from the VeyraMaterialLibrary, and the library is complete, assigned in every build scene and preloaded.
    /// </summary>
    public class MaterialArchitectureTests
    {
        // The needles are split so this file does not match itself.
        static readonly string ShaderFind = "Shader" + ".Find(";
        static readonly string[] KeywordCalls = { "Enable" + "Keyword(", "Disable" + "Keyword(", "Set" + "Keyword(" };

        /// <summary>C# files that end up in players: everything under Assets/ except Editor folders and the editor-only test assemblies.</summary>
        static string[] RuntimeScripts() =>
            Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/Editor/") && !p.Contains("/_Veyra/Tests/"))
                .ToArray();

        static string Rel(string p) => "Assets" + p.Substring(Application.dataPath.Length);

        [Test]
        public void NoRuntimeShaderFind()
        {
            var offenders = RuntimeScripts()
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
                .Where(x => x.line.Contains(ShaderFind) && !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///"))
                .Select(x => $"{Rel(x.f)}:{x.i + 1}: {x.line.Trim()}")
                .ToList();
            Assert.IsEmpty(offenders, "runtime code must not call " + ShaderFind + " (shaders are stripped from players; use the VeyraMaterialLibrary):\n" + string.Join("\n", offenders));
        }

        [Test]
        public void RuntimeCode_TogglesNoShaderKeywords()
        {
            var offenders = RuntimeScripts()
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
                .Where(x => KeywordCalls.Any(k => x.line.Contains(k)) && !x.line.TrimStart().StartsWith("//"))
                .Select(x => $"{Rel(x.f)}:{x.i + 1}: {x.line.Trim()}")
                .ToList();
            Assert.IsEmpty(offenders, "runtime keyword toggles need variants nobody guarantees in a build; keep keywords in the material assets:\n" + string.Join("\n", offenders));
        }

        static VeyraMaterialLibrary Library()
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            Assert.IsNotNull(lib, VeyraMaterialFactory.LibraryPath + " is missing (Veyra/Materials/Build Material Library)");
            return lib;
        }

        [Test]
        public void Library_IsComplete_AndEveryShaderIsSupported()
        {
            var lib = Library();
            Assert.IsEmpty(lib.MissingKeys(), "empty slots");
            foreach (var s in lib.Slots)
            {
                Assert.IsNotNull(s.Material.shader, s.Key + " has no shader");
                Assert.IsTrue(s.Material.shader.isSupported, $"{s.Key}: {s.Material.shader.name} is not supported");
                StringAssert.DoesNotContain("InternalErrorShader", s.Material.shader.name, s.Key.ToString());
                StringAssert.DoesNotContain("FallbackError", s.Material.shader.name, s.Key.ToString());
            }
            CollectionAssert.AreEqual(lib.Slots.Select(s => (int)s.Key).OrderBy(k => k).ToArray(), lib.Slots.Select(s => (int)s.Key).ToArray(), "slots sorted by key (grouped by category)");
        }

        [Test]
        public void Templates_CarryTheirKeywordsInTheAsset()
        {
            var lib = Library();
            Assert.IsTrue(lib.Get(VeyraMat.FxLit).IsKeywordEnabled("_EMISSION"), "FxLit needs _EMISSION on (emission is a property at runtime)");
            foreach (var k in new[] { VeyraMat.FxUnlitAdditive, VeyraMat.FxUnlitAlpha, VeyraMat.FxParticleAlpha, VeyraMat.FxParticleAdditive })
            {
                var m = lib.Get(k);
                Assert.IsTrue(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), k + " transparent keyword");
                Assert.AreEqual(3000, m.renderQueue, k + " render queue");
                Assert.AreEqual(0f, m.GetFloat("_ZWrite"), k + " ZWrite");
            }
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, lib.Get(VeyraMat.FxParticleAdditive).GetFloat("_DstBlend"), "additive blend");
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, lib.Get(VeyraMat.FxParticleAlpha).GetFloat("_DstBlend"), "alpha blend");
            Assert.IsFalse(lib.Get(VeyraMat.DebugUnlit).IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), "DebugUnlit is opaque");
        }

        [Test]
        public void RuntimeMaterials_CopyTheTemplatesOfTheActiveLibrary()
        {
            var lib = Library();
            VeyraMaterialLibrary.Activate(lib, null);
            var lit = RuntimeMaterials.NewLit("test_lit");
            var add = RuntimeMaterials.NewParticle("test_add", true);
            var alpha = RuntimeMaterials.NewUnlitTransparent("test_alpha", false);
            try
            {
                Assert.AreEqual(lib.Get(VeyraMat.FxLit).shader, lit.shader);
                Assert.IsTrue(lit.IsKeywordEnabled("_EMISSION"));
                Assert.AreEqual("test_lit", lit.name);
                Assert.AreEqual(lib.Get(VeyraMat.FxParticleAdditive).shader, add.shader);
                Assert.IsTrue(add.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
                Assert.AreEqual(lib.Get(VeyraMat.FxUnlitAlpha).shader, alpha.shader);
                Assert.AreNotSame(lib.Get(VeyraMat.FxLit), lit, "a copy, never the shared template");
            }
            finally
            {
                Object.DestroyImmediate(lit);
                Object.DestroyImmediate(add);
                Object.DestroyImmediate(alpha);
            }
        }

        [Test]
        public void EveryBuildScene_ReferencesTheLibrary()
        {
            string guid = AssetDatabase.AssetPathToGUID(VeyraMaterialFactory.LibraryPath);
            Assert.IsNotEmpty(guid);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Assert.IsNotEmpty(scenes);
            foreach (var path in scenes)
                StringAssert.Contains(guid, File.ReadAllText(path), $"{path} does not reference the VeyraMaterialLibrary (Veyra/Materials/Assign Library To Build Scenes)");
        }

        [Test]
        public void ShaderVariants_AreLinkedAndPreloaded()
        {
            var lib = Library();
            Assert.IsNotNull(lib.Variants, "library.Variants");
            Assert.AreEqual(VeyraMaterialFactory.VariantsPath, AssetDatabase.GetAssetPath(lib.Variants));
            Assert.Greater(lib.Variants.variantCount, 0);
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            var list = new SerializedObject(gs).FindProperty("m_PreloadedShaders");
            bool found = false;
            for (int i = 0; i < list.arraySize; i++) found |= list.GetArrayElementAtIndex(i).objectReferenceValue == lib.Variants;
            Assert.IsTrue(found, "VC_Variants must be in Graphics Settings > Preloaded Shaders");
        }
    }
}
