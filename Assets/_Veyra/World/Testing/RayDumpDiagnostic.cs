using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeyraCity.Testing
{
    /// <summary>
    /// World iteration 23 (BUG-123: a frame over the CAM_05 tower that only the player draws). Development players only:
    /// <c>-veyraRayDump camX,camY,camZ,yawMin,yawMax,pitchMin,pitchMax</c> casts a 9 × 9 fan of rays from the camera point
    /// (yaw from +Z towards +X, pitch up, degrees) and logs, every 2 s from 3 s after start-up and at quit, every enabled renderer whose
    /// bounds the fan crosses above 60 m, with its scene, hierarchy path, mesh and materials → Logs/ITER/raydump.txt.
    /// Inert without the argument.
    /// </summary>
    public sealed class RayDumpDiagnostic : MonoBehaviour
    {
        Vector3 cam;
        float y0, y1, p0, p1;
        float next = 3f;
        int pass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Debug.isDebugBuild) return;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-veyraRayDump");
            if (i < 0 || i + 1 >= args.Length) return;
            var f = args[i + 1].Split(',');
            if (f.Length < 7) return;
            float P(int k) => float.Parse(f[k], CultureInfo.InvariantCulture);
            var go = new GameObject("RayDumpDiagnostic");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<RayDumpDiagnostic>();
            d.cam = new Vector3(P(0), P(1), P(2));
            d.y0 = P(3); d.y1 = P(4); d.p0 = P(5); d.p1 = P(6);
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 2f;
            Dump();
        }

        void OnApplicationQuit() => Dump();

        void Dump()
        {
            pass++;
            var sb = new StringBuilder($"pass {pass} t {Time.realtimeSinceStartup:0} cam {cam}\n");
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled || r.bounds.max.y < 60f) continue;
                bool hit = false;
                for (int a = 0; a < 9 && !hit; a++)
                    for (int b = 0; b < 9 && !hit; b++)
                    {
                        float yaw = Mathf.Lerp(y0, y1, a / 8f) * Mathf.Deg2Rad, pitch = Mathf.Lerp(p0, p1, b / 8f) * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                        if (r.bounds.IntersectRay(new Ray(cam, dir))) hit = true;
                    }
                if (!hit) continue;
                string path = r.name;
                for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                var mf = r.GetComponent<MeshFilter>();
                var mats = r.sharedMaterials;
                var names = new string[mats.Length];
                for (int m = 0; m < mats.Length; m++) names[m] = mats[m] != null ? mats[m].name : "null";
                var bb = r.bounds;
                sb.Append($"{r.gameObject.scene.name} | {path} | {r.GetType().Name} | mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} | {string.Join(",", names)} | x {bb.min.x:0}-{bb.max.x:0} y {bb.min.y:0}-{bb.max.y:0} z {bb.min.z:0}-{bb.max.z:0}\n");
            }
            Directory.CreateDirectory("E:/VeyraCity/Logs/ITER");
            File.AppendAllText("E:/VeyraCity/Logs/ITER/raydump.txt", sb.ToString());
        }
    }
}
