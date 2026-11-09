using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Builds a drivable vehicle from a <see cref="VehicleDefinition"/> and an optional visual model.
    /// Model naming follows CLAUDE.md §4 (Wheel_FL… Caliper_FL…). Without a model it builds a
    /// clearly-placeholder primitive body so the physics can be driven and tested.
    /// </summary>
    public static class VehicleFactory
    {
        public static readonly string[] WheelNodeNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        public static readonly string[] CaliperNodeNames = { "Caliper_FL", "Caliper_FR", "Caliper_RL", "Caliper_RR" };

        public static VehicleController Create(VehicleDefinition def, GameObject visualPrefab, SurfaceDefinition defaultSurface,
                                               Vector3 position, Quaternion rotation, bool interpolate,
                                               Material bodyMaterial = null, Material wheelMaterial = null)
        {
            var root = new GameObject(def.DisplayName);
            root.transform.SetPositionAndRotation(position, rotation);
            var rb = root.AddComponent<Rigidbody>();
            rb.interpolation = interpolate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = interpolate ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;

            var wheels = new Transform[4];
            var calipers = new Transform[4];
            if (visualPrefab != null)
            {
                var model = Object.Instantiate(visualPrefab, root.transform, false);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                for (int i = 0; i < 4; i++)
                {
                    wheels[i] = FindDeep(model.transform, WheelNodeNames[i]);
                    calipers[i] = FindDeep(model.transform, CaliperNodeNames[i]);
                }
            }
            else
            {
                BuildPlaceholder(root.transform, def, wheels, bodyMaterial, wheelMaterial);
            }

            if (root.GetComponentInChildren<Collider>() == null) AddBodyCollider(root, def);
            var bodyMat = new PhysicsMaterial("VehicleBody")
            {
                dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var c in root.GetComponentsInChildren<Collider>()) c.sharedMaterial = bodyMat;

            var vc = root.AddComponent<VehicleController>();
            vc.DefaultSurface = defaultSurface;
            vc.SetVisuals(wheels, calipers);
            vc.Initialize(def);
            return vc;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static void AddBodyCollider(GameObject root, VehicleDefinition def)
        {
            Vector3 d = def.Chassis.Dimensions;
            float bottom = 0.20f;
            float top = Mathf.Max(bottom + 0.4f, d.y * 0.92f);
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(d.x * 0.96f, top - bottom, d.z * 0.97f);
            box.center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
        }

        static void BuildPlaceholder(Transform root, VehicleDefinition def, Transform[] wheels, Material body, Material wheel)
        {
            Vector3 d = def.Chassis.Dimensions;
            var model = new GameObject("PlaceholderModel").transform;
            model.SetParent(root, false);

            var lower = Prim(PrimitiveType.Cube, "Body_Lower", model, body);
            lower.localScale = new Vector3(d.x, d.y * 0.42f, d.z);
            lower.localPosition = new Vector3(0f, 0.22f + d.y * 0.21f, 0f);
            var cabin = Prim(PrimitiveType.Cube, "Body_Cabin", model, body);
            cabin.localScale = new Vector3(d.x * 0.82f, d.y * 0.36f, d.z * 0.48f);
            cabin.localPosition = new Vector3(0f, 0.22f + d.y * 0.42f + d.y * 0.18f, -d.z * 0.05f);
            var nose = Prim(PrimitiveType.Cube, "Nose_Marker", model, wheel);
            nose.localScale = new Vector3(d.x * 0.6f, 0.08f, 0.1f);
            nose.localPosition = new Vector3(0f, 0.22f + d.y * 0.3f, d.z * 0.5f + 0.02f);

            float halfBase = def.Wheelbase * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                var axle = i < 2 ? def.Front : def.Rear;
                bool left = i % 2 == 0;
                var w = Prim(PrimitiveType.Cylinder, VehicleFactory.WheelNodeNames[i], root, wheel);
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
                w.localScale = new Vector3(axle.Tyre.Radius * 2f, Mathf.Max(0.1f, axle.Tyre.Width) * 0.5f, axle.Tyre.Radius * 2f);
                w.localPosition = new Vector3((left ? -0.5f : 0.5f) * axle.TrackWidth, axle.Tyre.Radius, i < 2 ? halfBase : -halfBase);
                // A spoke bar so wheel rotation is visible.
                var spoke = Prim(PrimitiveType.Cube, "Spoke", w, body);
                spoke.localScale = new Vector3(0.9f, 1.05f, 0.12f);
                wheels[i] = w;
            }
        }

        static Transform Prim(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
