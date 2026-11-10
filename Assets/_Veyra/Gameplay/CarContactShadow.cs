using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Soft contact shadow under a car (the dark, blurred ground darkening that "plants" cars in GTA V and in
    /// Cyberpunk's path-traced shots, and that SSAO alone does not give at chase distance). A quad with a radial
    /// gradient, laid on the ground below the body by a raycast and faded with height so it never floats.
    /// </summary>
    public sealed class CarContactShadow : MonoBehaviour
    {
        [Range(0f, 1f)] public float Strength = 0.62f;
        static Material shadowMat;
        static Mesh quad;
        Transform shadow;
        VehicleController car;
        float halfW, halfL;
        static readonly RaycastHit[] hits = new RaycastHit[6];

        public void Bind(VehicleController v)
        {
            car = v;
            var d = v.Definition.Chassis.Dimensions;
            halfW = d.x * 0.5f * 1.12f;
            halfL = d.z * 0.5f * 1.08f;
            Ensure();
            var go = new GameObject("ContactShadow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = shadowMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            shadow = go.transform;
            shadow.localScale = new Vector3(halfW * 2f, 1f, halfL * 2f);
        }

        static void Ensure()
        {
            if (shadowMat != null) return;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ContactShadowTex" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // rounded-rectangle falloff: dense under the body, soft at the edge
                    float u = Mathf.Abs((x + 0.5f) / n * 2f - 1f), v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    float d = Mathf.Pow(Mathf.Pow(u, 4f) + Mathf.Pow(v, 4f), 0.25f);
                    float a = Mathf.Clamp01((1f - d) / 0.45f);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a * a * (3f - 2f * a)));
                }
            tex.Apply();
            shadowMat = RuntimeMaterials.NewParticle("CarContactShadow", false);
            shadowMat.SetTexture("_BaseMap", tex);
            shadowMat.SetColor("_BaseColor", Color.white);
            shadowMat.renderQueue = 2995;     // after opaque ground, before skid marks and transparents
            quad = new Mesh { name = "ContactShadowQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateNormals();
            quad.RecalculateBounds();
        }

        void LateUpdate()
        {
            if (car == null || shadow == null) return;
            Vector3 origin = car.transform.position + Vector3.up * 1.5f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit ground = default;
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody != car.Body && hits[i].distance < best) { best = hits[i].distance; ground = hits[i]; }
            if (best == float.MaxValue) { shadow.gameObject.SetActive(false); return; }
            float height = Mathf.Max(0f, best - 1.5f);
            float fade = Mathf.Clamp01(1f - height / 2.5f);
            shadow.gameObject.SetActive(fade > 0.02f);
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, ground.normal).normalized;
            shadow.SetPositionAndRotation(ground.point + ground.normal * 0.03f, Quaternion.LookRotation(fwd, ground.normal));
            shadow.localScale = new Vector3(halfW * 2f * (1f + height * 0.3f), 1f, halfL * 2f * (1f + height * 0.3f));
            if (mpb == null) { mpb = new MaterialPropertyBlock(); rend = shadow.GetComponent<MeshRenderer>(); }
            mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, Strength * fade));
            rend.SetPropertyBlock(mpb);
        }

        MaterialPropertyBlock mpb;
        MeshRenderer rend;
    }
}
