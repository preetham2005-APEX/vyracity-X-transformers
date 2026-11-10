using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.EditorTools;
using VeyraCity.World;

namespace VeyraCity.Tests
{
    /// <summary>Cards F2/F3: palette materials (library, shaders, textures, albedo calibration) and the Veyra shader globals.</summary>
    public class PaletteTests
    {
        static VeyraMaterialLibrary Library() => AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);

        [Test]
        public void EveryPaletteSpec_IsBuilt_UsesItsShader_AndHasTextures()
        {
            var lib = Library();
            Assert.IsNotNull(lib);
            foreach (var spec in VeyraPalette.Specs)
            {
                Assert.IsTrue(lib.TryGet(spec.Key, out var m), $"{spec.Name} ({spec.Key}) is not in the library (Veyra/Materials/Palette/Build ...)");
                Assert.AreEqual(spec.Shader, m.shader.name, spec.Name);
                Assert.IsTrue(m.shader.isSupported, spec.Name);
                if (spec.TexSet != null) Assert.IsNotNull(m.GetTexture("_BaseMap"), spec.Name + " albedo");
                Assert.IsTrue(m.enableInstancing, spec.Name + " GPU instancing");
                Assert.AreEqual(spec.Name, m.name);
            }
        }

        [Test]
        public void PaletteAlbedo_IsInsideItsCalibratedRange()
        {
            var lib = Library();
            foreach (var spec in VeyraPalette.Specs)
            {
                if (!lib.TryGet(spec.Key, out var m)) continue;
                float a = VeyraPalette.MeanAlbedo(m);
                Assert.That(a, Is.InRange(spec.MinAlbedo, spec.MaxAlbedo), $"{spec.Name}: mean linear albedo {a:0.000}");
                Assert.That(a, Is.InRange(0.02f, 0.9f), spec.Name);
            }
        }

        [Test]
        public void PaletteTextures_AreBC7OnPC_AndNormalMapsAreMarked()
        {
            var lib = Library();
            foreach (var spec in VeyraPalette.Specs.Where(s => s.TexSet != null))
            {
                if (!lib.TryGet(spec.Key, out var m)) continue;
                foreach (var prop in new[] { "_BaseMap", "_BumpMap", "_MaskMap" })
                {
                    if (!m.HasProperty(prop)) continue;
                    var t = m.GetTexture(prop);
                    if (t == null) continue;
                    var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                    var pc = imp.GetPlatformTextureSettings("Standalone");
                    Assert.IsTrue(pc.overridden && pc.format == TextureImporterFormat.BC7, $"{t.name}: BC7 on PC");
                    Assert.IsTrue(Mathf.IsPowerOfTwo(t.width) && Mathf.IsPowerOfTwo(t.height), t.name + " power of two");
                    if (prop == "_BumpMap") Assert.AreEqual(TextureImporterType.NormalMap, imp.textureType, t.name);
                    if (prop == "_MaskMap") Assert.IsFalse(imp.sRGBTexture, t.name + " mask is linear");
                    if (prop == "_BaseMap") Assert.IsTrue(imp.sRGBTexture, t.name + " albedo is sRGB");
                }
            }
        }

        [Test]
        public void NightFactor_FollowsTheCardCurve()
        {
            Assert.AreEqual(0f, VeyraGlobals.NightFactorAt(12f), 1e-4f);
            Assert.AreEqual(0f, VeyraGlobals.NightFactorAt(17.9f), 1e-4f);
            Assert.AreEqual(0.5f, VeyraGlobals.NightFactorAt(19f), 1e-3f);
            Assert.AreEqual(1f, VeyraGlobals.NightFactorAt(20.5f), 1e-4f);
            Assert.AreEqual(1f, VeyraGlobals.NightFactorAt(3f), 1e-4f);
            Assert.AreEqual(1f, VeyraGlobals.NightFactorAt(5.5f), 1e-4f);
            Assert.AreEqual(0f, VeyraGlobals.NightFactorAt(7.1f), 1e-4f);
            Assert.AreEqual(1f, VeyraGlobals.NightFactorAt(24f), 1e-4f);
        }

        [Test]
        public void Globals_AreOnlyWrittenWhenTheyChange()
        {
            VeyraGlobals.Invalidate();
            VeyraGlobals.Apply(21f, 0.4f, 0.2f, 0.3f, Vector3.right, Vector3.up);
            int after = VeyraGlobals.Writes;
            VeyraGlobals.Apply(21f, 0.4f, 0.2f, 0.3f, Vector3.right, Vector3.up);
            Assert.AreEqual(after, VeyraGlobals.Writes, "an unchanged frame must not write");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_VC_NightFactor"), 1e-4f);
            Assert.AreEqual(0.4f, Shader.GetGlobalFloat("_VC_Wetness"), 1e-4f);
            VeyraGlobals.Apply(21f, 0.9f, 0.2f, 0.3f, Vector3.right, Vector3.up);
            Assert.AreEqual(after + 1, VeyraGlobals.Writes, "only the changed property is written");
            VeyraGlobals.Invalidate();
        }
    }
}
