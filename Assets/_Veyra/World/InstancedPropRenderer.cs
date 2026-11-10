using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// GPU-instanced renderer for vegetation and street furniture (tens of thousands of instances from the
    /// world placement CSVs). Instances are grouped in spatial cells; each frame, visible cells within the
    /// prototype's draw distance are drawn with RenderMeshInstanced, using the low LOD beyond LodDistance.
    /// </summary>
    [ExecuteAlways]
    public sealed class InstancedPropRenderer : MonoBehaviour
    {
        [Serializable]
        public sealed class Prototype
        {
            public string Name;
            public Mesh Mesh;
            public Material[] Materials;
            public Mesh LowMesh;
            public Material[] LowMaterials;
            public float LodDistance = 120f;
            public float DrawDistance = 600f;
            public float ShadowDistance = 120f;
            [Tooltip("Lamp heads etc. that StreetLightPool lights at night (local offset of the light)")] public Vector3 LightOffset;
            public bool IsLamp;
        }

        [Serializable]
        public sealed class Cell
        {
            public int Prototype;
            public Bounds Bounds;
            [Tooltip("x, y, z, yaw degrees")] public Vector4[] Placements;
            public float[] Scales;
            [NonSerialized] public Matrix4x4[] Matrices;
        }

        public Prototype[] Prototypes = Array.Empty<Prototype>();
        public Cell[] Cells = Array.Empty<Cell>();
        [Range(0.25f, 2f)] public float DistanceScale = 1f;
        public bool Shadows = true;

        readonly Plane[] planes = new Plane[6];
        public static InstancedPropRenderer Instance { get; private set; }

        void OnEnable()
        {
            Instance = this;
            Rebuild();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (Instance == this) Instance = null;
        }

        public void Rebuild()
        {
            if (Cells == null) return;
            foreach (var c in Cells)
            {
                if (c.Placements == null) { c.Matrices = Array.Empty<Matrix4x4>(); continue; }
                c.Matrices = new Matrix4x4[c.Placements.Length];
                for (int i = 0; i < c.Placements.Length; i++)
                {
                    var p = c.Placements[i];
                    float s = c.Scales != null && i < c.Scales.Length ? c.Scales[i] : 1f;
                    c.Matrices[i] = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, p.w, 0f), Vector3.one * s);
                }
            }
        }

        /// <summary>
        /// Culls and submits the cells for each camera as it starts rendering, so every camera gets its own visible
        /// set: the player camera, the on-demand review cameras, the Scene view and reflection probes (BUG-067; this
        /// used to cull once per frame against Camera.main and draw that set into every camera, so review cameras
        /// elsewhere showed no trees or lamps). Shadow casting is unchanged: only cells in view and within
        /// ShadowDistance cast shadows (off-screen casters cost too much without a measurement; see BUG-067).
        /// </summary>
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || cam.cameraType == CameraType.Preview) return;
            if (Cells == null || Prototypes == null || !SystemInfo.supportsInstancing) return;
            if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            foreach (var c in Cells)
            {
                if (c.Matrices == null || c.Matrices.Length == 0 || c.Prototype < 0 || c.Prototype >= Prototypes.Length) continue;
                var proto = Prototypes[c.Prototype];
                float dist = Mathf.Sqrt(c.Bounds.SqrDistance(eye));
                if (dist > proto.DrawDistance * DistanceScale) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, c.Bounds)) continue;
                bool low = dist > proto.LodDistance * DistanceScale && proto.LowMesh != null;
                var mesh = low ? proto.LowMesh : proto.Mesh;
                var mats = low && proto.LowMaterials != null && proto.LowMaterials.Length > 0 ? proto.LowMaterials : proto.Materials;
                if (mesh == null || mats == null) continue;
                bool shadows = Shadows && dist < proto.ShadowDistance;
                for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
                {
                    if (mats[sm] == null) continue;
                    var rp = new RenderParams(mats[sm])
                    {
                        worldBounds = c.Bounds,
                        shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                        receiveShadows = true,
                        layer = gameObject.layer,
                        camera = cam,
                    };
                    Graphics.RenderMeshInstanced(rp, mesh, sm, c.Matrices);
                }
            }
        }

        /// <summary>World positions of lamp heads within radius of p (for the street light pool).</summary>
        public int CollectLamps(Vector3 p, float radius, Vector3[] buffer, out int count)
        {
            count = 0;
            if (Cells == null) return 0;
            float r2 = radius * radius;
            foreach (var c in Cells)
            {
                if (c.Prototype < 0 || c.Prototype >= Prototypes.Length || !Prototypes[c.Prototype].IsLamp) continue;
                if (c.Bounds.SqrDistance(p) > r2) continue;
                var off = Prototypes[c.Prototype].LightOffset;
                for (int i = 0; i < c.Placements.Length && count < buffer.Length; i++)
                {
                    var pl = c.Placements[i];
                    var pos = new Vector3(pl.x, pl.y, pl.z);
                    if ((pos - p).sqrMagnitude > r2) continue;
                    float s = c.Scales != null && i < c.Scales.Length ? c.Scales[i] : 1f;
                    buffer[count++] = pos + Quaternion.Euler(0f, pl.w, 0f) * (off * s);
                }
            }
            return count;
        }
    }
}
