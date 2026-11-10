using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-108 diagnosis: at each driving-wall site from the V1 autotest, walks the autopilot's lane (and the lane centre and
    /// edge) every 0.5 m and reports (a) the topmost drivable surface and every collider under it, (b) height steps of that
    /// top surface above 0.05 m between neighbouring samples, and (c) anything a forward ray at 0.15 / 0.4 / 0.8 m above the
    /// surface hits within 1 m (what the car body runs into). Read-only: it changes nothing in the scene.
    /// Batch entry: <c>-executeMethod VeyraCity.EditorTools.RoadWallProbe.BatchRun</c> (writes Logs/BUG108/wall_probe.txt).
    /// </summary>
    public static class RoadWallProbe
    {
        /// <summary>(road id, absolute s of the wall, travel direction along s, label) from Logs/V1/B108_*/stats.json.</summary>
        public static readonly (string road, float s, int dir, string label)[] Sites =
        {
            ("R1", 7334f, 1, "R_BRIDGES seg 2 (7130 + 204)"),
            ("A2", 1850f, 1, "R_BRIDGES seg 8 (1483 + 367)"),
            ("A10", 1666f, 1, "R_BRIDGES seg 10 (1304 + 362)"),
            ("H3", 896f, 1, "R_BRIDGES seg 12 (584 + 312)"),
            ("A4", 604f, -1, "R_DOWNTOWN_NIGHT seg 3 (697 - 93)"),
            ("A4", 678f, -1, "R_DOWNTOWN_NIGHT seg 3 spawn (697 - 19, spawn_off_road)"),
            ("A22", 203f, 1, "R_DOWNTOWN_NIGHT seg 4 (0 + 203)"),
            ("A22", 350f, 1, "R_DOWNTOWN_NIGHT seg 4 (0 + 350)"),
            ("H2", 30f, 1, "R_BRIDGES seg 4 start (spawn_off_road u 25, airborne u 32 after the drive surface)"),
            ("A22", 1852f, 1, "C2 validator: Regent Street Bridge step 0.55 m at s 1851.8 -> 1852.8 (drive surface)"),
        };

        [MenuItem("Veyra/Physics/Probe BUG-108 Wall Sites")]
        static void Menu() => Debug.Log(Run());

        public static void BatchRun()
        {
            int code = 0;
            try { Debug.Log(Run()); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static string Run()
        {
            if (EditorSceneManager.GetActiveScene().path != "Assets/_Veyra/Scenes/VeyraCity.unity")
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
                EditorSceneManager.OpenScene("Assets/_Veyra/Scenes/VeyraCity.unity");
            }
            Physics.SyncTransforms();
            WorldData.EnsureLoaded();
            RoadProfileEaser.Reset();
            var sb = new StringBuilder($"BUG-108 wall probe {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            foreach (var site in Sites) sb.Append(Probe(site.road, site.s, site.dir, site.label));
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BUG108", "wall_probe.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            return $"[RoadWallProbe] {Sites.Length} sites -> {path}";
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && parts.Count < 4; x = x.parent) parts.Insert(0, x.name.Length > 0 ? x.name : "(unnamed)");
            return string.Join("/", parts);
        }

        static string Probe(string roadId, float s0, int dir, string label)
        {
            var sb = new StringBuilder();
            int r = -1;
            for (int i = 0; i < WorldData.Roads.Count; i++) if (WorldData.Roads[i].Data.id == roadId) { r = i; break; }
            if (r < 0) return $"\n== {roadId} s {s0}: road not found\n";
            var road = WorldData.Roads[r];
            float lane = road.Data.width * 0.25f;
            sb.AppendLine($"\n== {roadId} ({road.Data.cls}, width {road.Data.width} m) s {s0:F0}, dir {dir:+0;-0} - {label}");
            var c0 = road.Path.PositionAt(s0);
            sb.AppendLine($"   centre ({c0.x:F1}, {c0.y:F2}, {c0.z:F1}), eased delta {RoadProfileEaser.Delta(r, s0):F2} m");
            foreach (float latFactor in new[] { 1f, 0f, 2f })
            {
                float lat = lane * latFactor;
                string latName = latFactor == 1f ? "autopilot lane" : latFactor == 0f ? "centre" : "outer lane edge";
                sb.AppendLine($"  -- {latName} (lateral {lat:F1} m right of travel)");
                float prevTop = float.NaN; string prevName = null;
                for (float u = -30f; u <= 12f; u += 0.5f)
                {
                    float s = s0 + dir * u;
                    if (s < 0f || s > road.Path.Length) continue;
                    var c = road.Path.PositionAt(s);
                    var ahead = road.Path.PositionAt(Mathf.Clamp(s + dir, 0f, road.Path.Length)) - road.Path.PositionAt(Mathf.Clamp(s - dir, 0f, road.Path.Length));
                    ahead.y = 0f; ahead.Normalize();
                    var right = new Vector3(ahead.z, 0f, -ahead.x);
                    var p = c + right * lat;
                    float eased = c.y + RoadProfileEaser.Delta(r, s);
                    var hits = Physics.RaycastAll(new Vector3(p.x, eased + 6f, p.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.attachedRigidbody == null).OrderByDescending(h => h.point.y).ToList();
                    // top surface within the driving envelope (eased - 1.5 .. eased + 1.5)
                    var top = hits.FirstOrDefault(h => h.point.y < eased + 1.5f && h.point.y > eased - 1.5f);
                    string Name(RaycastHit h) => h.collider.name.Length > 0 && !(h.collider is BoxCollider) && !(h.collider is CapsuleCollider)
                        ? $"{h.collider.name}<{(h.collider.transform.parent != null ? h.collider.transform.parent.name : "-")}>"
                        : $"[{h.collider.GetType().Name} {HierarchyPath(h.collider.transform)}{(h.collider is TerrainCollider tc ? $" data {AssetDatabase.GetAssetPath(tc.terrainData)} renderData {(tc.GetComponent<Terrain>() != null ? AssetDatabase.GetAssetPath(tc.GetComponent<Terrain>().terrainData) : "none")} at {tc.transform.position}" : "")} size {h.collider.bounds.size.x:F1}x{h.collider.bounds.size.y:F1}x{h.collider.bounds.size.z:F1}]";
                    bool hasTop = top.collider != null;
                    float topY = hasTop ? top.point.y : float.NaN;
                    bool step = hasTop && !float.IsNaN(prevTop) && Mathf.Abs(topY - prevTop) > 0.05f;
                    string forward = "";
                    if (hasTop)
                        foreach (float hgt in new[] { 0.15f, 0.4f, 0.8f })
                            if (Physics.Raycast(new Vector3(p.x, topY + hgt, p.z), ahead, out var fh, 1f, ~0, QueryTriggerInteraction.Ignore) && fh.collider.attachedRigidbody == null)
                                forward += $" FWD@{hgt:F2}:{Name(fh)} n=({fh.normal.x:F2},{fh.normal.y:F2},{fh.normal.z:F2}) d={fh.distance:F2}";
                    if (Mathf.Abs(u) <= 3f || step || forward.Length > 0 || (Mathf.RoundToInt(u * 2) % 10 == 0))
                    {
                        string stack = string.Join(" ", hits.Where(h => h.point.y > eased - 3f && h.point.y < eased + 3f).Take(6).Select(h => $"{Name(h)}@{h.point.y:F2}"));
                        sb.AppendLine($"   u {u,6:F1} s {s,7:F1} eased {eased,7:F2} top {(hasTop ? topY.ToString("F2") : "none"),7} {(step ? $"STEP {topY - prevTop:+0.00;-0.00} ({prevName} -> {Name(top)})" : "")}{forward}  | {stack}");
                    }
                    if (hasTop) { prevTop = topY; prevName = Name(top); }
                }
            }
            return sb.ToString();
        }
    }
}
