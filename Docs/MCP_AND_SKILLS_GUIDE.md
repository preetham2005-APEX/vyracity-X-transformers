# VEYRA CITY — Claude Code Plugins, MCP Servers & Agent Skills

**Installed & Verified for Claude Code (`claude 2.1.319` / Claude Opus 5.5):** 2026-11-11

---

## 1. Installed Claude Code Plugin (User Scope)
- **`unity@unity-agent-plugin`** (`0.1.9-beta`, Status: **enabled**, Scope: **user**)
  - Marketplace: `Unity-Technologies/unity-agent-plugin`
  - Provides **34 official Unity skills** (`unity:craft-game-floors`, `unity:unity-cli`, `unity:unity-pipeline`, `unity:urp-postprocessing`, `unity:validate-urp-render-graph-renderer-feature`, `unity:shader-graph-create-custom-node`, `unity:fix-3d-physics-collisions`, `unity:initialize-ai-navigation`, `unity:asset-transformer-toolkit`, `unity:audio-setup-mixers`, `unity:optimize-audio`, `unity:ui`, `unity:ui-ugui`, `unity:ui-uitk`, `unity:ui-imgui`, etc.).

---

## 2. Configured MCP Servers in Claude Code (`-s user`, All `√ Connected`)

| MCP Server | Command / Package | Role in Veyra City | Target Scope |
|---|---|---|---|
| **`UnityMCP`** | `uvx.exe --from mcpforunityserver==10.3.0 mcp-for-unity --transport stdio --default-instance VeyraCity` (`CoplayDev/unity-mcp`, 15.8k★) + Unity UPM `com.coplaydev.unity-mcp#v10.3.0` | Deep Editor scenes/GameObjects, URP Volumes & RendererFeatures (`manage_graphics`), Materials/Shaders, `batch_execute`, Profiler timing, multi-angle screenshots (`manage_camera` `include_image=True` / `batch="surround"`). | Claude Code (`user`) + Claude Desktop + `E:\VeyraCity\Packages\manifest.json` |
| **`unity`** | `C:\Users\preet\AppData\Local\Unity\bin\unity.exe mcp` (`1.0.0-beta.13`) + `com.unity.pipeline@0.10.0-exp.1` | Official Unity CLI/Pipeline MCP for live `script eval`, `method-reload`, Play Mode & EditMode test runner, builds. | Claude Code (`user`) + Claude Desktop |
| **`blender`** | `uvx.exe mcp-for-blender` (`2.2.7`, `ahujasid/blender-mcp`, 34k★) | Direct Blender 5.2 LTS viewport/scene/geometry/shader scripting on TCP `9876`. | Claude Code (`user`) + Claude Desktop |
| **`context7`** | `npx.cmd -y @upstash/context7-mcp` | Live up-to-date API documentation lookup (`resolve-library-id` + `query-docs`) for Unity 6.6, URP 17, Cinemachine 3, Input System, Blender 5.2. | Claude Code (`user`) + Claude Desktop |

> **Unity Editor Note for `UnityMCP`:** `com.coplaydev.unity-mcp#v10.3.0` is already installed in `E:\VeyraCity\Packages\manifest.json` and compiled cleanly (`0` C# errors in Unity `6000.6.8f1`). Inside Unity Editor, open **`Window -> MCP for Unity`** once and verify the **Unity Bridge** shows **Running** (click **Start Bridge** if stopped).
>
> **Unity CLI Update Note:** When all running `unity.exe` / Claude Desktop processes are closed, you may optionally run `unity self-update` (`1.0.0-beta.13` -> `1.0.0-beta.17`) for `UNITY_PROJECT_PATH` routing support.

---

## 3. Curated User Skills Installed (`C:\Users\preet\.claude\skills\` — `40` Valid Skills + `34` via `unity@unity-agent-plugin` = `74` Total Non-Overlapping)

Exact duplicates covered by `unity@unity-agent-plugin` (`0.1.9-beta`) were removed from `~/.claude/skills/` so Claude Code has zero redundant entries:

| Source (License) | Count | Installed Skills | Purpose in Veyra City |
|---|---|---|---|
| **Official Unity Standalone** (`Unity-Technologies/skills`) | 1 | `project-auditor-fixes` | Project Auditor static performance/allocation fixes (not bundled in `unity-agent-plugin`) |
| **CoplayDev Unity-MCP** (`CoplayDev/unity-mcp`, MIT) | 2 | `unity-mcp-skill`, `blender-to-unity` | Resource-first `batch_execute`, `manage_camera` surround screenshots, FBX/GLB Blender-to-Unity bridge |
| **Unity Shader Agent Skills** (`adevra/unity-shader-agent-skills`, MIT) | 6 | `urp-hlsl-templates`, `texture-packing-variant-stripping`, `shader-graph-best-practices`, `water-fluid-shaders`, `mobile-post-processing`, `mobile-shader-optimization` | SRP Batcher `CBUFFER_START(UnityPerMaterial)`, `Metallic(R)/AO(G)/Detail(B)/Smoothness(A)`, `IPreprocessShaders` stripping, URP water waves/foam |
| **Blender 5.2 Agent Skills** (`EL4CTEO/blender-skills`, MIT) | 14 | `blender-core`, `blender-scene-setup`, `blender-environment-kits`, `blender-hardsurface-modeling`, `blender-game-optimization`, `blender-retopology-uv`, `blender-pbr-materials`, `blender-texturing`, `blender-texture-baking`, `blender-export-pipelines`, `blender-asset-qa`, `blender-lighting-lookdev`, `blender-time-of-day`, `blender-weather-fx` | Tested Blender 5.2 LTS Python scripts (`terrain.py`, `scatter.py`, `make_lods.py`, `mesh_stats.py`, `bake_maps.py`, `texel_density.py`, `qa_check.py`) |
| **Blender Specialist Skills** (`arjun988/blender-skills`, MIT) | 6 | `vehicle-artist`, `neon-retrofuturism`, `collision-proxy`, `lod-pipeline`, `vegetation-artist`, `geometry-nodes` | Car body panels & wheel wells (`A8`/`A9`), D6 Neon Mile mood, convex/box collision proxies, LOD ratios, foliage & Geometry Nodes scatter |
| **Awesome GameDev Skills** (`gamedev-skills/awesome-gamedev-agent-skills`, Apache-2.0) | 10 | `camera-systems`, `physics-tuning`, `game-feel`, `performance-optimization`, `level-design`, `procedural-gen`, `unity-physics`, `unity-scriptableobjects`, `unity-input-system`, `unity-build-pipeline` | Cinemachine 3 chase camera & lag, 50/100/120 Hz damping/CCD, nitro/drift feel, ScriptableObject tuning |
| **Project Override** (local) | 1 | `veyra-pipeline-bridge` | Enforces `CLAUDE.md` rules: `BLENDER_EXE="D:\New folder\blender.exe"`, `vc_tools.py` S4 presets, `VeyraMaterialLibrary` (`F1`), and `UnityMCP`/`unity` usage |

---

## 4. Maintenance Command

```bat
:: Re-sync or update all curated GitHub skills into C:\Users\preet\.claude\skills anytime
python E:\VeyraCity\Tools\install_best_skills.py
```
