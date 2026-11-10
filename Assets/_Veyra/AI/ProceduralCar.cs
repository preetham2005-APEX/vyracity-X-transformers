using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.AI
{
    /// <summary>
    /// Low-poly placeholder car for traffic until licensed traffic models are imported: a body and cabin
    /// extruded from side profiles, four wheels and emissive head/tail lights. Paint comes from a
    /// MaterialPropertyBlock on renderers named "Body".
    /// </summary>
    public static class ProceduralCar
    {
        static Material body, glass, tyre, head, tail;

        static Material Lit(string name, Color c, float smooth, float metal, Color? emission = null)
        {
            var m = VeyraCity.World.RuntimeMaterials.NewLit(name); // FxLit template: _EMISSION already on (black)
            if (m == null) return null;                             // RuntimeMaterials logged why
            m.enableInstancing = true;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue) m.SetColor("_EmissionColor", emission.Value);
            return m;
        }

        static void EnsureMaterials()
        {
            if (body != null) return;
            body = Lit("Traffic_Body", Color.white, 0.72f, 0.45f);
            glass = Lit("Traffic_Glass", new Color(0.04f, 0.05f, 0.06f), 0.92f, 0.1f);
            tyre = Lit("Traffic_Tyre", new Color(0.03f, 0.03f, 0.03f), 0.25f, 0f);
            head = Lit("Traffic_Head", new Color(0.9f, 0.9f, 0.85f), 0.9f, 0f, new Color(2.2f, 2.1f, 1.8f));
            tail = Lit("Traffic_Tail", new Color(0.4f, 0.02f, 0.02f), 0.9f, 0f, new Color(2.4f, 0.1f, 0.05f));
        }

        public static GameObject Build(Transform parent, int variant)
        {
            EnsureMaterials();
            var root = new GameObject("TrafficCar");
            root.transform.SetParent(parent, false);
            float len, width, h0, hood, roof;
            Vector2[] lower, cabin;
            switch (variant)
            {
                case 1: // hatchback
                    len = 4.0f; width = 1.76f; h0 = 0.32f; hood = 0.92f; roof = 1.48f;
                    lower = new[] { new Vector2(-2.0f, h0), new Vector2(2.0f, h0), new Vector2(2.02f, 0.72f), new Vector2(1.7f, hood), new Vector2(-1.95f, hood + 0.05f), new Vector2(-2.02f, 0.7f) };
                    cabin = new[] { new Vector2(-1.9f, hood), new Vector2(1.05f, hood), new Vector2(0.15f, roof), new Vector2(-1.75f, roof - 0.02f) };
                    break;
                case 2: // van
                    len = 4.9f; width = 1.95f; h0 = 0.36f; hood = 1.05f; roof = 2.1f;
                    lower = new[] { new Vector2(-2.45f, h0), new Vector2(2.45f, h0), new Vector2(2.47f, 0.85f), new Vector2(2.1f, hood), new Vector2(-2.45f, hood), new Vector2(-2.47f, 0.8f) };
                    cabin = new[] { new Vector2(-2.43f, hood), new Vector2(1.95f, hood), new Vector2(1.35f, roof), new Vector2(-2.43f, roof) };
                    break;
                default: // sedan
                    len = 4.6f; width = 1.8f; h0 = 0.3f; hood = 0.86f; roof = 1.42f;
                    lower = new[] { new Vector2(-2.3f, h0), new Vector2(2.3f, h0), new Vector2(2.32f, 0.66f), new Vector2(1.95f, hood), new Vector2(-2.2f, hood + 0.04f), new Vector2(-2.32f, 0.68f) };
                    cabin = new[] { new Vector2(-1.45f, hood + 0.02f), new Vector2(0.95f, hood), new Vector2(0.1f, roof), new Vector2(-1.15f, roof) };
                    break;
            }
            var b = Part(root.transform, "Body", Extrude(lower, width), body);
            var c = Part(root.transform, "Cabin_Glass", Extrude(cabin, width * 0.84f), glass);
            // roof panel in body colour
            var roofPts = new[] { cabin[3] + new Vector2(0.05f, 0f), cabin[2] - new Vector2(0.05f, 0f), cabin[2] + new Vector2(-0.08f, 0.04f), cabin[3] + new Vector2(0.08f, 0.04f) };
            Part(root.transform, "Body_Roof", Extrude(roofPts, width * 0.8f), body);
            float r = 0.33f;
            float wb = len * 0.31f;
            foreach (var (x, z) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(w.GetComponent<Collider>());
                w.name = "Wheel";
                w.transform.SetParent(root.transform, false);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                w.transform.localScale = new Vector3(r * 2f, 0.11f, r * 2f);
                w.transform.localPosition = new Vector3(x * (width * 0.5f - 0.12f), r, z * wb);
                w.GetComponent<Renderer>().sharedMaterial = tyre;
            }
            foreach (var x in new[] { -1f, 1f })
            {
                Light(root.transform, "HeadLight", new Vector3(x * width * 0.33f, hood - 0.12f, lower[2].x - 0.02f), new Vector3(0.36f, 0.1f, 0.05f), head);
                Light(root.transform, "TailLight", new Vector3(x * width * 0.36f, hood - 0.1f, lower[5].x + 0.02f), new Vector3(0.34f, 0.1f, 0.05f), tail);
            }
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, (h0 + roof) * 0.5f, 0f);
            col.size = new Vector3(width, roof - h0, len);
            return root;
        }

        public enum Style { Sedan, Hatch, Muscle, Super, GT }

        /// <summary>
        /// Driveable-car visual sized from a vehicle definition (CLAUDE.md §4 node names Wheel_FL.. so the
        /// physics drives the wheels). Used until a licensed model exists for that car.
        /// </summary>
        public static GameObject BuildForVehicle(float length, float width, float height, float wheelbase, float trackF, float trackR, float radiusF, float radiusR, Style style)
        {
            EnsureMaterials();
            var root = new GameObject("ProceduralCar_" + style);
            float L = length, H = height, hl = L * 0.5f;
            float h0 = Mathf.Max(radiusF, radiusR) * 0.62f;
            Vector2 P(float x, float y) => new Vector2(x * hl, y);
            Vector2[] lower, cabin;
            switch (style)
            {
                case Style.Hatch:
                    lower = new[] { P(-1f, h0), P(1f, h0), P(1.01f, H * 0.48f), P(0.84f, H * 0.64f), P(-0.98f, H * 0.66f), P(-1.01f, H * 0.5f) };
                    cabin = new[] { P(-0.95f, H * 0.64f), P(0.5f, H * 0.64f), P(0.05f, H), P(-0.88f, H * 0.98f) };
                    break;
                case Style.Muscle:
                    lower = new[] { P(-1f, h0), P(1f, h0), P(1.01f, H * 0.5f), P(0.96f, H * 0.62f), P(-0.98f, H * 0.64f), P(-1.01f, H * 0.5f) };
                    cabin = new[] { P(-0.62f, H * 0.62f), P(0.18f, H * 0.62f), P(-0.18f, H), P(-0.5f, H * 0.99f) };
                    break;
                case Style.Super:
                    lower = new[] { P(-1f, h0), P(1f, h0 * 0.9f), P(1.02f, H * 0.34f), P(0.55f, H * 0.56f), P(-0.95f, H * 0.68f), P(-1.01f, H * 0.5f) };
                    cabin = new[] { P(-0.55f, H * 0.66f), P(0.42f, H * 0.52f), P(0.05f, H), P(-0.35f, H * 0.98f) };
                    break;
                case Style.GT:
                    lower = new[] { P(-1f, h0), P(1f, h0), P(1.01f, H * 0.45f), P(0.92f, H * 0.58f), P(-0.98f, H * 0.62f), P(-1.01f, H * 0.48f) };
                    cabin = new[] { P(-0.62f, H * 0.6f), P(0.2f, H * 0.58f), P(-0.12f, H), P(-0.45f, H * 0.99f) };
                    break;
                default:
                    lower = new[] { P(-1f, h0), P(1f, h0), P(1.01f, H * 0.47f), P(0.86f, H * 0.6f), P(-0.96f, H * 0.62f), P(-1.01f, H * 0.48f) };
                    cabin = new[] { P(-0.66f, H * 0.62f), P(0.4f, H * 0.6f), P(0.04f, H), P(-0.52f, H) };
                    break;
            }
            Part(root.transform, "Body", Extrude(lower, width), body);
            Part(root.transform, "Cabin_Glass", Extrude(cabin, width * 0.82f), glass);
            var roofPts = new[] { cabin[3] + new Vector2(0.06f, -0.02f), cabin[2] - new Vector2(0.06f, 0.02f), cabin[2] + new Vector2(-0.1f, 0.03f), cabin[3] + new Vector2(0.1f, 0.03f) };
            Part(root.transform, "Body_Roof", Extrude(roofPts, width * 0.78f), body);
            if (style == Style.Super || style == Style.Muscle)
            {
                var wing = new[] { P(-0.98f, H * 0.78f), P(-0.84f, H * 0.78f), P(-0.84f, H * 0.82f), P(-0.98f, H * 0.83f) };
                Part(root.transform, "Body_Wing", Extrude(wing, width * 0.86f), body);
            }
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                float r = front ? radiusF : radiusR;
                float tw = front ? trackF : trackR;
                var hub = new GameObject(names[i]).transform;
                hub.SetParent(root.transform, false);
                hub.localPosition = new Vector3((left ? -0.5f : 0.5f) * tw, r, (front ? 0.5f : -0.5f) * wheelbase);
                var tyreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(tyreGo.GetComponent<Collider>());
                tyreGo.name = "Tyre";
                tyreGo.transform.SetParent(hub, false);
                tyreGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tyreGo.transform.localScale = new Vector3(r * 2f, 0.12f, r * 2f);
                tyreGo.GetComponent<Renderer>().sharedMaterial = tyre;
                var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(rim.GetComponent<Collider>());
                rim.name = "Rim";
                rim.transform.SetParent(hub, false);
                rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rim.transform.localPosition = new Vector3(left ? -0.02f : 0.02f, 0f, 0f);
                rim.transform.localScale = new Vector3(r * 1.35f, 0.125f, r * 1.35f);
                rim.GetComponent<Renderer>().sharedMaterial = RimMaterial();
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.name = "Spoke";
                spoke.transform.SetParent(hub, false);
                spoke.transform.localPosition = new Vector3(left ? -0.07f : 0.07f, 0f, 0f);
                spoke.transform.localScale = new Vector3(0.02f, r * 1.25f, 0.07f);
                spoke.GetComponent<Renderer>().sharedMaterial = tyre;
            }
            float frontZ = lower[2].x, rearZ = lower[5].x;
            foreach (var x in new[] { -1f, 1f })
            {
                Light(root.transform, "Lights_Head", new Vector3(x * width * 0.34f, lower[2].y - 0.06f, frontZ - 0.01f), new Vector3(0.38f, 0.09f, 0.05f), head);
                Light(root.transform, "Lights_Tail", new Vector3(x * width * 0.36f, lower[5].y + 0.03f, rearZ + 0.01f), new Vector3(0.40f, 0.09f, 0.05f), tail);
            }
            return root;
        }

        static Material rimMat;
        static Material RimMaterial()
        {
            if (rimMat == null) rimMat = Lit("Car_Rim", new Color(0.72f, 0.73f, 0.75f), 0.8f, 1f);
            return rimMat;
        }

        static void Light(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();

        /// <summary>Extrudes a convex side profile (x = along length, y = up) across the width (flat shaded).</summary>
        public static Mesh Extrude(Vector2[] profile, float width)
        {
            int key = profile.Length * 7919 + Mathf.RoundToInt(width * 1000f);
            foreach (var p in profile) key = key * 31 + Mathf.RoundToInt(p.x * 100f) * 17 + Mathf.RoundToInt(p.y * 100f);
            if (cache.TryGetValue(key, out var cached)) return cached;
            var v = new List<Vector3>();
            var t = new List<int>();
            float hw = width * 0.5f;
            int n = profile.Length;
            // caps (fan)
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -hw : hw;
                int b0 = v.Count;
                for (int i = 0; i < n; i++) v.Add(new Vector3(x, profile[i].y, profile[i].x));
                for (int i = 1; i < n - 1; i++)
                {
                    if (side == 0) { t.Add(b0); t.Add(b0 + i); t.Add(b0 + i + 1); }
                    else { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
                }
            }
            // sides
            for (int i = 0; i < n; i++)
            {
                var a = profile[i];
                var c = profile[(i + 1) % n];
                int b0 = v.Count;
                v.Add(new Vector3(-hw, a.y, a.x));
                v.Add(new Vector3(-hw, c.y, c.x));
                v.Add(new Vector3(hw, c.y, c.x));
                v.Add(new Vector3(hw, a.y, a.x));
                t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 1);
                t.Add(b0); t.Add(b0 + 3); t.Add(b0 + 2);
            }
            var mesh = new Mesh { name = "ExtrudedProfile" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Winding depends on profile orientation; flip if normals point inward.
            var nrm = mesh.normals;
            var centre = mesh.bounds.center;
            int inward = 0;
            for (int i = 0; i < v.Count; i++) if (Vector3.Dot(nrm[i], v[i] - centre) < 0f) inward++;
            if (inward > v.Count / 2)
            {
                for (int i = 0; i < t.Count; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
            }
            cache[key] = mesh;
            return mesh;
        }
    }
}
