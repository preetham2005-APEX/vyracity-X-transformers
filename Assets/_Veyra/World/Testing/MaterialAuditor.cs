using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card F1: checks every enabled renderer of the loaded scenes for the things that draw pink or nothing in a player: an empty
    /// material slot, no shader, the error/fallback shader, or a shader this build/GPU does not support (stripped).
    /// </summary>
    public static class MaterialAuditor
    {
        const int MaxOffenders = 12;

        public static AutoTestReport.MaterialAudit Run()
        {
            var a = new AutoTestReport.MaterialAudit { audited = true };
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled) continue;
                a.renderers++;
                if (r is ParticleSystemRenderer psr)
                {
                    // Slot 1 is the trail material, which is only used when the Trails module is on.
                    Check(a, r, 0, psr.sharedMaterial);
                    var ps = r.GetComponent<ParticleSystem>();
                    if (ps != null && ps.trails.enabled) Check(a, r, 1, psr.trailMaterial);
                    continue;
                }
                var mats = r.sharedMaterials;
                if (mats.Length == 0) { a.null_materials++; Offender(a, r, -1, "no material slots"); continue; }
                for (int i = 0; i < mats.Length; i++) Check(a, r, i, mats[i]);
            }
            return a;
        }

        static void Check(AutoTestReport.MaterialAudit a, Renderer r, int slot, Material m)
        {
            a.material_slots++;
            if (m == null) { a.null_materials++; Offender(a, r, slot, "empty slot"); return; }
            var s = m.shader;
            if (s == null) { a.error_shader++; Offender(a, r, slot, $"material '{m.name}' has no shader"); return; }
            if (s.name.Contains("InternalErrorShader") || s.name.Contains("FallbackError")) { a.error_shader++; Offender(a, r, slot, $"material '{m.name}' uses {s.name}"); return; }
            if (!s.isSupported) { a.unsupported_shader++; Offender(a, r, slot, $"material '{m.name}': shader {s.name} not supported"); }
        }

        static void Offender(AutoTestReport.MaterialAudit a, Renderer r, int slot, string what)
        {
            if (a.first_offenders.Count >= MaxOffenders) return;
            a.first_offenders.Add($"{Path(r.transform)} [{slot}]: {what}");
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 4 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        public static string Summary(AutoTestReport.MaterialAudit a) =>
            $"materials: {a.renderers} renderers, {a.material_slots} slots, {a.null_materials} empty, {a.error_shader} error shader, {a.unsupported_shader} unsupported" +
            (a.first_offenders.Count > 0 ? " | first: " + string.Join(" ; ", a.first_offenders) : "");
    }
}
