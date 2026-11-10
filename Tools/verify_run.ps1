# Card V0: check one autotest run. Prints PASS/FAIL per rule; exit code 1 if any rule fails.
# Usage: powershell -ExecutionPolicy Bypass -File Tools\verify_run.ps1 [-Stats <stats.json>] [-PlayerLog <player.log>]
# Functional rules apply to every build; performance rules apply to release builds only (development builds are slower by design).
param(
    [string]$Stats = "E:\VeyraCity\Logs\stats.json",
    [string]$PlayerLog = "E:\VeyraCity\Logs\player.log"
)

$script:fails = 0
function Rule([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { $tag = 'PASS' } else { $tag = 'FAIL'; $script:fails++ }
    '{0}  {1,-30} {2}' -f $tag, $name, $detail
}
function Info([string]$name, [string]$detail) { '{0}  {1,-30} {2}' -f 'INFO', $name, $detail }

if (-not (Test-Path $Stats)) { "FAIL  stats.json missing: $Stats (did the player run and finish?)"; exit 1 }
$s = Get-Content $Stats -Raw -Encoding UTF8 | ConvertFrom-Json
$log = ''
if (Test-Path $PlayerLog) { $log = Get-Content $PlayerLog -Raw -Encoding UTF8 } else { "WARN  player.log missing: $PlayerLog" }

$logExceptions = ([regex]::Matches($log, '(?m)^[A-Za-z_.]*Exception: ')).Count
$logShaderNull = ([regex]::Matches($log, 'shader is null', 'IgnoreCase')).Count
$stripped = [regex]::Matches($log, "Shader '([^']+)' is not supported or has been stripped") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$v = $s.vehicle

"Run: route {0} | build {1} {2} {3} (git {4}) | {5} | {6} | quality {7} | time of day {8} | {9}" -f $s.route, $s.build.type, $s.build.backend, $s.build.version, $s.build.git, $s.date, $s.resolution, $s.quality, $s.time_of_day, $s.end_reason
''
Rule 'exceptions = 0'              (($s.exceptions -eq 0) -and ($logExceptions -eq 0)) ("stats.json {0}, player.log {1}" -f $s.exceptions, $logExceptions)
Rule '"shader is null" = 0'        (($s.shader_is_null -eq 0) -and ($logShaderNull -eq 0)) ("stats.json {0}, player.log {1}" -f $s.shader_is_null, $logShaderNull)
$firstErr = ''
if ($s.first_errors -and $s.first_errors.Count -gt 0) { $firstErr = " - first: " + $s.first_errors[0] }
Rule 'errors = 0'                  ($s.errors -eq 0) ("{0}{1}" -f $s.errors, $firstErr)
Rule 'spawned_on_road'             ($v.spawned_on_road -eq $true) ("spawn height above surface {0:0.00} m" -f $v.spawn_height_above_surface_m)
Rule 'falls_below_surface = 0'     ($v.falls_below_surface -eq 0) ("{0}" -f $v.falls_below_surface)
Rule 'invisible_wall_events = 0'   ($v.invisible_wall_events -eq 0) ("{0}" -f $v.invisible_wall_events)
Rule 'segments all completed'      (($v.segments_total -gt 0) -and ($v.segments_completed -eq $v.segments_total)) ("{0}/{1}" -f $v.segments_completed, $v.segments_total)
$m = $s.materials
if ($m -and $m.audited) {
    $firstBad = ''
    if ($m.first_offenders -and $m.first_offenders.Count -gt 0) { $firstBad = " - first: " + $m.first_offenders[0] }
    Rule 'no pink or missing materials' (($m.null_materials -eq 0) -and ($m.error_shader -eq 0) -and ($m.unsupported_shader -eq 0)) ("{0} renderers, {1} slots: {2} empty, {3} error shader, {4} unsupported{5}" -f $m.renderers, $m.material_slots, $m.null_materials, $m.error_shader, $m.unsupported_shader, $firstBad)
} else {
    Info 'material audit' 'not in this stats.json (built before card F1)'
}

if ($s.build.type -eq 'release') {
    Rule 'fps_1pct_low >= 60'      ($s.fps_1pct_low -ge 60) ("{0:0.0}" -f $s.fps_1pct_low)
    Rule 'p99 frame time <= 16.6 ms' ($s.frametime_ms.p99 -le 16.6) ("{0:0.00} ms" -f $s.frametime_ms.p99)
    Rule 'hitches_over_50ms = 0'   ($s.hitches_over_50ms -eq 0) ("{0} (max {1:0.0} ms)" -f $s.hitches_over_50ms, $s.frametime_ms.max)
} else {
    Info 'perf rules' ("SKIPPED (development build). Measured: avg {0:0.0} FPS, 1% low {1:0.0}, p99 {2:0.00} ms, max {3:0.0} ms, hitches {4}" -f $s.avg_fps, $s.fps_1pct_low, $s.frametime_ms.p99, $s.frametime_ms.max, $s.hitches_over_50ms)
}
# BUG-090: GPU/CPU frame time and render counters (Appendix C, PC High). Budgets are checked on p95 in release builds only;
# a distribution with 0 samples was not available in that build (older stats.json, or Frame Timing Stats off).
$inv = [Globalization.CultureInfo]::InvariantCulture
function Dist($d, [string]$fmt) {
    if ($null -eq $d -or $d.samples -le 0) { return 'not available' }
    return ("avg {0} | p95 {1} | p99 {2} | max {3} ({4} frames)" -f ([double]$d.avg).ToString($fmt, $inv), ([double]$d.p95).ToString($fmt, $inv), ([double]$d.p99).ToString($fmt, $inv), ([double]$d.max).ToString($fmt, $inv), $d.samples)
}
$r = $s.render
if ($null -eq $r) {
    Info 'render stats' 'not in this stats.json (built before BUG-090)'
} else {
    $budgets = @(
        @{ name = 'GPU frame time p95 <= 13 ms';   d = $r.gpu_ms;        limit = 13;      fmt = '0.00' },
        @{ name = 'CPU main thread p95 <= 10 ms';  d = $r.cpu_main_ms;   limit = 10;      fmt = '0.00' },
        @{ name = 'SetPass calls p95 <= 450';      d = $r.setpass_calls; limit = 450;     fmt = '0' },
        @{ name = 'triangles p95 <= 8 M';          d = $r.triangles;     limit = 8000000; fmt = '0' }
    )
    foreach ($b in $budgets) {
        if ($s.build.type -eq 'release' -and $b.d -and $b.d.samples -gt 0) { Rule $b.name ($b.d.p95 -le $b.limit) (Dist $b.d $b.fmt) }
        else { Info $b.name (Dist $b.d $b.fmt) }
    }
    Info 'CPU render thread (ms)'      (Dist $r.cpu_render_ms '0.00')
    Info 'draw calls'                  (Dist $r.draw_calls '0')
    Info 'batches'                     (Dist $r.batches '0')
    Info 'frame timing stats'          ("{0}" -f $r.frame_timing_enabled)
}
if ($stripped) { Info 'stripped shaders (player.log)' ("{0}: {1}" -f @($stripped).Count, ($stripped -join ', ')) } else { Info 'stripped shaders (player.log)' '0' }
Info 'airborne_events'             ("{0}" -f $v.airborne_events)
Info 'visible_impacts'             ("{0}" -f $v.visible_impacts)
Info 'max_vertical_accel_g'        ("{0:0.00}" -f $v.max_vertical_accel_g)
Info 'distance'                    ("{0:0} m in {1:0.0} s" -f $v.distance_m, $s.duration_s)
Info 'teleport windows'            ("{0} teleports, {1} frames excluded, max {2:0.0} ms, {3} hitches > 50 ms" -f $s.teleport_windows.count, $s.teleport_windows.frames, $s.teleport_windows.max_ms, $s.teleport_windows.hitches_over_50ms)
Info 'memory (MB)'                 ("managed {0:0}, total reserved {1:0}, gfx {2:0}; GC {3:0.00} KB/frame" -f $s.memory_mb.managed, $s.memory_mb.total_reserved, $s.memory_mb.gfx, $s.gc_alloc_kb_per_frame_avg)
foreach ($seg in $s.per_segment) {
    Info ("segment {0}" -f $seg.index) ("{0} {1:0}->{2:0} m @ {3:0}: {4}, {5:0.0} s, avg {6:0} / min {7:0} km/h, lat err {8:0.0} m, air {9}, falls {10}, walls {11}, spawn {12} ({13})" -f $seg.road, $seg.s_start, $seg.s_end, $seg.target_kmh, $seg.result, $seg.time_s, $seg.avg_kmh, $seg.min_kmh, $seg.max_lateral_error_m, $seg.airborne_events, $seg.falls_below_surface, $seg.invisible_wall_events, $seg.spawned_on_road, $seg.spawn_surface)
}
''
if ($script:fails -gt 0) { "RESULT: FAIL ($script:fails rule(s) failed)"; exit 1 } else { "RESULT: PASS"; exit 0 }
