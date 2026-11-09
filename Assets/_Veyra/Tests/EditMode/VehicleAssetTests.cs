using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.EditorTools;

namespace VeyraCity.Tests
{
    /// <summary>VEHICLE_ASSET_PIPELINE import checklist items that can be checked automatically.</summary>
    public class VehicleAssetTests
    {
        static GameObject Kite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAssetTools.KitePrefab);
            if (prefab == null) Assert.Ignore("Arden Kite not imported yet (VehicleAssetTools.ImportKite)");
            return prefab;
        }

        static Transform Find(Transform p, string name)
        {
            if (p.name == name) return p;
            foreach (Transform c in p) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        static Vector3 MeshCentre(Transform root, string name)
        {
            var t = Find(root, name);
            Assert.IsNotNull(t, name);
            var mf = t.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf, name + " has no mesh");
            return root.InverseTransformPoint(t.TransformPoint(mf.sharedMesh.bounds.center));
        }

        [Test]
        public void Kite_FacesPlusZ_LeftIsMinusX_WheelPivotsAtHubs()
        {
            var root = Kite().transform;
            Vector3 fl = root.InverseTransformPoint(Find(root, "Wheel_FL").position);
            Vector3 rr = root.InverseTransformPoint(Find(root, "Wheel_RR").position);
            Assert.AreEqual(1.275f, fl.z, 0.01f, "front axle at +Z");
            Assert.AreEqual(-0.75f, fl.x, 0.01f, "left wheels at -X");
            Assert.AreEqual(0.316f, fl.y, 0.01f, "hub height = tyre radius");
            Assert.AreEqual(-1.275f, rr.z, 0.01f);
            Assert.AreEqual(0.75f, rr.x, 0.01f);
            Assert.Greater(MeshCentre(root, "Lights_Head").z, 1.6f, "headlights at the front");
            Assert.Less(MeshCentre(root, "Lights_Tail").z, -1.6f, "tail lights at the back");
        }

        [Test]
        public void Kite_CalipersFollowSteerOnly_NotParentedToWheels()
        {
            var root = Kite().transform;
            foreach (var corner in new[] { "FL", "FR", "RL", "RR" })
            {
                var caliper = Find(root, "Caliper_" + corner);
                var wheel = Find(root, "Wheel_" + corner);
                Assert.IsNotNull(caliper); Assert.IsNotNull(wheel);
                Assert.IsFalse(caliper.IsChildOf(wheel), corner);
                Assert.Less(Vector3.Distance(caliper.position, wheel.position), 0.01f, "caliper pivot at the hub");
            }
        }

        [Test]
        public void Kite_CollisionPrefixes_BecomeColliders_WithoutRenderers()
        {
            var root = Kite();
            var ucx = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("UCX_")).ToArray();
            Assert.GreaterOrEqual(ucx.Length, 1);
            foreach (var t in ucx)
            {
                var mc = t.GetComponent<MeshCollider>();
                Assert.IsNotNull(mc, t.name);
                Assert.IsTrue(mc.convex, t.name);
                Assert.IsNull(t.GetComponent<MeshRenderer>(), t.name + " must not render");
            }
            var ubx = Find(root.transform, "UBX_Chassis");
            Assert.IsNotNull(ubx);
            Assert.IsNotNull(ubx.GetComponent<BoxCollider>());
            Assert.IsNull(ubx.GetComponent<MeshRenderer>());
        }

        [Test]
        public void Kite_HasLods_AndNoMissingMaterials()
        {
            var root = Kite();
            var lod = root.GetComponent<LODGroup>();
            Assert.IsNotNull(lod);
            Assert.AreEqual(3, lod.lodCount);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    Assert.IsNotNull(m, r.name);
                    Assert.IsTrue(m.name.StartsWith("M_"), $"{r.name} uses {m.name}");
                }
        }

        [Test]
        public void Kite_Dimensions_MatchSpec()
        {
            var root = Kite().transform;
            var body = Find(root, "Body_LOD0").GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(4.30f, body.size.z, 0.05f, "length");
            Assert.AreEqual(1.78f, body.size.x, 0.05f, "width");
        }
    }
}
