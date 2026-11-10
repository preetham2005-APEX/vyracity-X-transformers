using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// The sky layer over the procedural skybox (visual iteration 5): GTA V's drifting cloud decks, golden-hour cloud colour,
    /// horizon haze and Cyberpunk's orange night glow, plus stars and a moon. Draws a sphere with the Veyra/SkyDome material
    /// from the library slot <see cref="VeyraMat.SkyDome"/>; the shader pins it to the far plane around whichever camera
    /// renders it (main camera and the car reflection probe alike). Each frame it passes the weather's cloudiness and the
    /// fog colour (so the horizon haze meets the fogged skyline); everything else comes from the VC globals.
    /// </summary>
    public sealed class SkyDome : MonoBehaviour
    {
        static readonly int CloudinessId = Shader.PropertyToID("_Cloudiness");
        static readonly int HazeColorId = Shader.PropertyToID("_HazeColor");

        Material material;
        MeshRenderer meshRenderer;
        float cloudiness = -1f;

        void Start()
        {
            var lib = VeyraMaterialLibrary.Active;
            var template = lib != null && lib.TryGet(VeyraMat.SkyDome, out var m) ? m : null;
            if (template == null)
            {
                Debug.LogWarning("[SkyDome] no SkyDome material in the library (Veyra/Materials/Build Sky Dome); sky layer off");
                enabled = false;
                return;
            }
            material = new Material(template) { name = "SkyDome (instance)" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = BuildSphere(32, 16);
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;
        }

        void LateUpdate()
        {
            if (material == null) return;
            var env = WorldEnvironment.Instance;
            float c = env != null ? env.Cloudiness : 0.3f;
            // a little cloud on clear days (GTA's skies are rarely empty); the weather drives the rest
            c = Mathf.Lerp(0.32f, 1f, Mathf.Clamp01(c));
            if (Mathf.Abs(c - cloudiness) > 1e-3f) { cloudiness = c; material.SetFloat(CloudinessId, c); }
            material.SetColor(HazeColorId, RenderSettings.fogColor);
            var cam = Camera.main;
            if (cam != null) transform.position = cam.transform.position;   // culling only; the shader centres on each camera
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        /// <summary>Unit UV sphere with huge bounds (never culled: the shader places it at the far plane).</summary>
        static Mesh BuildSphere(int seg, int rings)
        {
            var verts = new Vector3[(seg + 1) * (rings + 1)];
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * (r / (float)rings - 0.5f);
                for (int s = 0; s <= seg; s++)
                {
                    float lon = 2f * Mathf.PI * s / seg;
                    verts[r * (seg + 1) + s] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }
            }
            var tris = new int[seg * rings * 6];
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris[k++] = a; tris[k++] = b; tris[k++] = a + 1;
                    tris[k++] = a + 1; tris[k++] = b; tris[k++] = b + 1;
                }
            var mesh = new Mesh { name = "SkyDomeSphere", vertices = verts, triangles = tris };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }
    }
}
