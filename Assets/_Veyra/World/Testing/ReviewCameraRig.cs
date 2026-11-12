using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.Testing
{
    /// <summary>Where a review camera (and, for CHASE, the car) goes.</summary>
    public struct ReviewPose
    {
        public bool ok;
        public string error;
        public Vector3 camPos;
        public Quaternion camRot;
        public float fov;
        public bool hasCar;
        public Vector3 carPos;
        public Quaternion carRot;
        public string note;
    }

    /// <summary>
    /// Card S5: placement, rendering and contact sheets for review cameras, shared by the Editor window (Veyra/Review/Capture)
    /// and the player (-reviewCapture). No Shader.Find and no runtime materials: cameras render the scene as it is.
    /// </summary>
    public static class ReviewCameraRig
    {
        public const int Width = 2560, Height = 1440;
        public const int SheetColumns = 6, ThumbWidth = 640, ThumbHeight = 360, LabelBar = 34;

        /// <summary>Computes the camera (and car) pose. Needs colliders in the scene for "+h" heights and surface snapping.</summary>
        public static ReviewPose Pose(ReviewCameraSet.Entry e, RoadNetworkData net)
        {
            var p = new ReviewPose { fov = e.fov };
            if (e.kind == ReviewCameraSet.Kind.Fixed)
            {
                var pos = e.position;
                if (e.heightAboveGround)
                {
                    bool found = GroundY(pos.x, pos.z, out float g, out string what);
                    pos.y = g + e.position.y;
                    p.note = found ? $"ground {g:0.0} m ({what})" : $"no ground found under ({pos.x:0}, {pos.z:0}); used {g:0.0}";
                }
                p.camPos = pos;
                var dir = e.lookAt - pos;
                p.camRot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
                p.ok = true;
                return p;
            }

            var road = net?.Road(e.roadId);
            if (road == null) { p.error = $"unknown road '{e.roadId}'"; return p; }
            int dirSign = e.direction >= 0 ? 1 : -1;
            float ahead = Mathf.Max(10f, e.lookAhead);
            var lane = RouteAutopilot.BuildLanePath(road, e.distanceAlongRoad_m, e.distanceAlongRoad_m + dirSign * ahead, 20f, out _);
            var lanePoint = lane.PositionAt(0f);
            float y = SurfaceYNear(lanePoint, out var normal, out string surface);
            p.carPos = new Vector3(lanePoint.x, y, lanePoint.z);
            var fwd = lane.PositionAt(4f) - lanePoint;
            var flat = new Vector3(fwd.x, 0f, fwd.z).normalized;
            var along = Vector3.ProjectOnPlane(fwd, normal).normalized;
            p.carRot = Quaternion.LookRotation(along.sqrMagnitude > 1e-6f ? along : flat, normal);
            p.hasCar = true;
            p.camPos = p.carPos - flat * e.back + Vector3.up * e.up;
            var target = lane.PositionAt(ahead) + Vector3.up * 1.2f;
            p.camRot = Quaternion.LookRotation((target - p.camPos).normalized, Vector3.up);
            p.note = $"car on {surface} at {p.carPos.ToString("F1")}, profile y {lanePoint.y:0.0}";
            p.ok = true;
            return p;
        }

        static bool IsGround(Collider c) => c is TerrainCollider || c.GetComponentInParent<VehicleSurface>() != null;

        /// <summary>Height of the first terrain or road surface under (x, z), skipping buildings and props.</summary>
        public static bool GroundY(float x, float z, out float y, out string what)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 3200f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
                if (h.rigidbody == null && IsGround(h.collider)) { y = h.point.y; what = h.collider.name; return true; }
            foreach (var t in Terrain.activeTerrains)
            {
                var tp = t.transform.position; var ts = t.terrainData.size;
                if (x >= tp.x && x <= tp.x + ts.x && z >= tp.z && z <= tp.z + ts.z) { y = t.SampleHeight(new Vector3(x, 0f, z)) + tp.y; what = t.name; return true; }
            }
            y = 0f; what = "none";
            return false;
        }

        /// <summary>The drivable surface nearest the road-profile height at p (decks, tunnels and roads under bridges stay apart).</summary>
        public static float SurfaceYNear(Vector3 p, out Vector3 normal, out string what)
        {
            normal = Vector3.up; what = "road profile (no collider found)";
            float best = 6f, y = p.y;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.rigidbody != null) continue;
                float d = Mathf.Abs(h.point.y - p.y);
                if (d < best) { best = d; y = h.point.y; normal = h.normal; what = h.collider.name; }
            }
            return y;
        }

        /// <summary>A temporary camera that renders like the game camera (URP post-processing on).</summary>
        public static Camera CreateCamera(string name = "[ReviewCamera]")
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 15000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.enabled = false; // rendered on demand only
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;   // BUG-074: STP (URP asset) does temporal AA; SMAA on top only costs
            return cam;
        }

        public static void Apply(Camera cam, ReviewPose pose)
        {
            cam.transform.SetPositionAndRotation(pose.camPos, pose.camRot);
            cam.fieldOfView = pose.fov;
            cam.aspect = (float)Width / Height;
        }

        /// <summary>Renders the camera once and discards the image, so per-camera history (motion vectors, TAA, exposure) matches
        /// its current pose before <see cref="Render"/>.</summary>
        public static void WarmUp(Camera cam, int width = Width, int height = Height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)width / height;
            cam.Render();
            cam.targetTexture = prevTarget;
            RenderTexture.ReleaseTemporary(rt);
        }

        /// <summary>Renders the camera into a new Width x Height texture.</summary>
        public static Texture2D Render(Camera cam, int width = Width, int height = Height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)width / height;
            cam.Render();
            cam.targetTexture = prevTarget;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        public static void SavePng(Texture2D tex, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }

        /// <summary>A labelled contact sheet: <see cref="SheetColumns"/> columns of 640 x 360 thumbnails (6 x 3 for 18 cameras).</summary>
        public static Texture2D ContactSheet(IList<(string label, Texture2D image)> shots, string title)
        {
            int cols = SheetColumns, rows = Mathf.Max(1, Mathf.CeilToInt(shots.Count / (float)cols));
            int cellH = ThumbHeight + LabelBar, titleH = 46, gap = 6;
            int w = cols * ThumbWidth + (cols + 1) * gap, h = titleH + rows * cellH + (rows + 1) * gap;
            var px = new Color32[w * h];
            var bg = new Color32(18, 20, 24, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            PixelFont.Draw(px, w, h, title, gap + 4, 10, 4, new Color32(235, 235, 235, 255));
            var rt = RenderTexture.GetTemporary(ThumbWidth, ThumbHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var thumb = new Texture2D(ThumbWidth, ThumbHeight, TextureFormat.RGB24, false);
            for (int i = 0; i < shots.Count; i++)
            {
                int c = i % cols, r = i / cols;
                int x0 = gap + c * (ThumbWidth + gap), yTop = titleH + gap + r * (cellH + gap);
                if (shots[i].image != null)
                {
                    Graphics.Blit(shots[i].image, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    thumb.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), 0, 0);
                    thumb.Apply();
                    RenderTexture.active = prev;
                    var tp = thumb.GetPixels32();
                    for (int ty = 0; ty < ThumbHeight; ty++)
                    {
                        int dy = h - 1 - (yTop + (ThumbHeight - 1 - ty));
                        System.Array.Copy(tp, ty * ThumbWidth, px, dy * w + x0, ThumbWidth);
                    }
                }
                PixelFont.Draw(px, w, h, $"{i + 1}. {shots[i].label}", x0 + 4, yTop + ThumbHeight + 8, 3, new Color32(240, 240, 240, 255));
            }
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(thumb);
            var sheet = new Texture2D(w, h, TextureFormat.RGB24, false);
            sheet.SetPixels32(px);
            sheet.Apply();
            return sheet;
        }

        /// <summary>Screenshots/&lt;yyyy-MM-dd&gt;_&lt;card&gt;[_&lt;suffix&gt;] under a root folder.</summary>
        public static string OutputDir(string root, string card, string suffix = null) =>
            Path.Combine(root, "Screenshots", System.DateTime.Now.ToString("yyyy-MM-dd") + "_" + card + (string.IsNullOrEmpty(suffix) ? "" : "_" + suffix));
    }
}
