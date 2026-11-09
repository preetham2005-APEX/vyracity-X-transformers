# BUG_TRACKER — VEYRA CITY (Unity project)

Bugs before 2026-11-09 evening are in the design repo, `E:\PROJECT BANGERRR\Docs\BUG_TRACKER.md` (read-only); the open ones are carried over below.

| ID | Severity | Status | Summary | Fix / evidence |
|---|---|---|---|---|
| BUG-002 | Critical | Fixed | Player builds: `ArgumentNullException: shader is null` (RuntimeMaterials, `GameFlow.BuildMarkers`) | Material templates from `GameDatabase`; set in `GameFlow.Awake` with execution order −1000; null guards. Visual-pass runs: 0 exceptions (`Logs/visual*/player.log`). The runtime `Shader.Find` fallback remains: card F1 replaces it with `VeyraMaterialLibrary` |
| BUG-026 | High | Open | RACE-07 Halcyon Ascent: cars block about 200 m in on A18 | Design-repo history |
| BUG-028 | Medium | Open | AI field spread too wide; the stock Kite on autopilot needs many respawns on the motorway and gravel | |
| BUG-029 | Medium | Open | Hitches > 50 ms on spawns, teleports and time-of-day jumps (12 in the 107 s review run) | Pre-warm and pooling |
| BUG-031 | High | Fixed | Daylight paving washed out to white: bloom threshold 0.9 is below sunlit light-surface luminance | Threshold 1.25 by day → 0.9 at night; lower night exposure; darker paving |
| BUG-032 | Low | Fixed | The welcome dialog covered automated review screenshots | Not shown in automated runs |
| BUG-033 | Low | Open | `process_city_map.py` splits buildings that cross chunk lines into several UBX boxes | Merge per original part |
| SEC-002 | High | Mitigated (user action pending) | A Sketchfab API token was hard-coded in 7 git-tracked `Tools/*.py` scripts (committed in `e65856d`; no git remote, never pushed) | Scripts now read `SKETCHFAB_API_TOKEN` from the environment and exit if it is missing. **User:** regenerate the token at sketchfab.com → Settings → Password & API; the old value stays in local history only |
