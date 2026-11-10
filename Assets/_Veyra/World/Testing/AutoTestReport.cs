using System;
using System.Collections.Generic;

namespace VeyraCity.Testing
{
    /// <summary>stats.json written by <see cref="VeyraAutoTest"/> (card V0). Field names are the card's keys.</summary>
    [Serializable]
    public sealed class AutoTestReport
    {
        public BuildBlock build = new BuildBlock();
        public string date;
        public string gpu;
        public string graphics_api;
        public string resolution;
        public string quality;
        public int vsync_count;
        public string route;
        public float time_of_day;
        public float time_limit_s;
        public bool route_complete;
        public string end_reason;
        public float duration_s;
        public int frames;
        public float avg_fps;
        public float fps_1pct_low;
        public FrameTimes frametime_ms = new FrameTimes();
        public int hitches_over_50ms;
        /// <summary>Frames in the 2 s after each teleport are excluded from the numbers above and reported here.</summary>
        public TeleportWindow teleport_windows = new TeleportWindow();
        /// <summary>-1 when the "GC Allocated In Frame" counter is not available in this build.</summary>
        public float gc_alloc_kb_per_frame_avg = -1f;
        public MemoryBlock memory_mb = new MemoryBlock();
        /// <summary>
        /// BUG-090: GPU and CPU frame times (FrameTimingManager; release players need Player Settings > Frame Timing Stats)
        /// and render counters (ProfilerRecorder), over the same driving frames as <see cref="frametime_ms"/>.
        /// A distribution with samples = 0 (values -1) was not available in this build.
        /// </summary>
        public RenderStats render = new RenderStats();
        public int exceptions;
        public int errors;
        public int warnings;
        public int shader_is_null;
        public List<string> first_errors = new List<string>();
        public VehicleBlock vehicle = new VehicleBlock();
        public List<SegmentReport> per_segment = new List<SegmentReport>();
        public List<EventRecord> events = new List<EventRecord>();
        public string review_capture = "not requested";
        /// <summary>Card F1: material audit of every enabled renderer at the end of the route (no pink, no missing material).</summary>
        public MaterialAudit materials = new MaterialAudit();

        [Serializable] public sealed class BuildBlock { public string type, backend, version, git, unity; }
        [Serializable] public sealed class FrameTimes { public float p50, p95, p99, max; }
        [Serializable] public sealed class TeleportWindow { public int count; public float excluded_s; public int frames; public float max_ms; public int hitches_over_50ms; }
        /// <summary>-1 = not available in this build type.</summary>
        [Serializable] public sealed class MemoryBlock { public float managed = -1f, total_reserved = -1f, gfx = -1f; }

        /// <summary>Average, nearest-rank p95/p99 and maximum of one per-frame value; -1 when there were no samples.</summary>
        [Serializable] public sealed class Distribution { public int samples; public float avg = -1f, p95 = -1f, p99 = -1f, max = -1f; }

        /// <summary>
        /// BUG-090. cpu_main_ms is the main thread's busy time (frame time minus the wait for Present/VSync);
        /// triangles counts every rendered pass (shadow maps included), so it is an upper bound for visible triangles.
        /// </summary>
        [Serializable]
        public sealed class RenderStats
        {
            public bool frame_timing_enabled;
            public Distribution gpu_ms = new Distribution();
            public Distribution cpu_main_ms = new Distribution();
            public Distribution cpu_render_ms = new Distribution();
            public Distribution setpass_calls = new Distribution();
            public Distribution draw_calls = new Distribution();
            public Distribution batches = new Distribution();
            public Distribution triangles = new Distribution();
        }

        /// <summary>
        /// Card F1. renderers/material_slots audited; null_materials = empty slots (draw pink or nothing); error_shader = the
        /// error/fallback shader or no shader (pink); unsupported_shader = a shader this GPU/build cannot run.
        /// </summary>
        [Serializable]
        public sealed class MaterialAudit
        {
            public bool audited;
            public int renderers, material_slots, null_materials, error_shader, unsupported_shader;
            public List<string> first_offenders = new List<string>();
        }

        [Serializable]
        public sealed class VehicleBlock
        {
            public string car;
            public bool spawned_on_road;
            public float spawn_height_above_surface_m;
            public int falls_below_surface;
            public int airborne_events;
            /// <summary>Speed drop &gt; 30 km/h within 0.2 s with no collision against visible geometry (road steps, collider seams).</summary>
            public int invisible_wall_events;
            /// <summary>Same speed drop, but caused by hitting visible geometry (buildings, props, cars).</summary>
            public int visible_impacts;
            public float max_vertical_accel_g;
            public float distance_m;
            public int segments_completed;
            public int segments_total;
        }

        [Serializable]
        public sealed class SegmentReport
        {
            public int index;
            public string road;
            public float s_start, s_end, target_kmh;
            public string note;
            public bool reached;
            public bool completed;
            public string result;
            public float time_s, distance_m, avg_kmh, min_kmh;
            public bool spawned_on_road;
            public string spawn_surface;
            public float spawn_height_above_surface_m;
            public float max_lateral_error_m;
            public int airborne_events, falls_below_surface, invisible_wall_events, visible_impacts;
            public float max_vertical_accel_g;
        }

        [Serializable]
        public sealed class EventRecord
        {
            public string type;
            public int segment;
            public string road;
            public float s;
            public float t;
            public float x, y, z;
            public string detail;
        }
    }
}
