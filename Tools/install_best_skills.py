"""Download and install curated best Unity 6, URP Shader, Blender 5.2, and Game-Dev Agent Skills.

Uses shallow `git clone --depth 1` so GitHub API rate limits never block downloads,
then copies only the curated skill folders into Claude Code's skills directory.

Sources (all audited MIT / Apache-2.0 / Unity Official):
  1. Unity-Technologies/skills                 (Official Project Auditor fixes; other 34 Unity skills come from unity@unity-agent-plugin)
  2. CoplayDev/unity-mcp                     (MIT - Unity MCP Editor tool workflows & Blender-to-Unity bridge)
  3. adevra/unity-shader-agent-skills          (MIT - URP HLSL templates, channel packing, variant stripping)
  4. EL4CTEO/blender-skills                    (MIT - Blender 5.2+ terrain, scatter, LODs, baking, UV, QA)
  5. arjun988/blender-skills                   (MIT - Vehicle, neon night, collision, LODs, vegetation, geo nodes)
  6. gamedev-skills/awesome-gamedev-agent-skills (Apache-2.0 - Physics tuning, camera systems, game feel, Unity)

Usage:
    python Tools/install_best_skills.py
    python Tools/install_best_skills.py --dest C:/Users/preet/.claude/skills
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

SKILL_SOURCES = [
    (
        "https://github.com/Unity-Technologies/skills.git",
        "skills",
        [
            "project-auditor-fixes",
        ],
    ),
    (
        "https://github.com/CoplayDev/unity-mcp.git",
        ".claude/skills",
        [
            "unity-mcp-skill",
            "blender-to-unity",
        ],
    ),
    (
        "https://github.com/adevra/unity-shader-agent-skills.git",
        "skills",
        [
            "urp-hlsl-templates",
            "texture-packing-variant-stripping",
            "shader-graph-best-practices",
            "water-fluid-shaders",
            "mobile-post-processing",
            "mobile-shader-optimization",
        ],
    ),
    (
        "https://github.com/EL4CTEO/blender-skills.git",
        "skills",
        [
            "blender-core",
            "blender-scene-setup",
            "blender-environment-kits",
            "blender-hardsurface-modeling",
            "blender-game-optimization",
            "blender-retopology-uv",
            "blender-pbr-materials",
            "blender-texturing",
            "blender-texture-baking",
            "blender-export-pipelines",
            "blender-asset-qa",
            "blender-lighting-lookdev",
            "blender-time-of-day",
            "blender-weather-fx",
        ],
    ),
    (
        "https://github.com/arjun988/blender-skills.git",
        ".claude/skills",
        [
            "vehicle-artist",
            "neon-retrofuturism",
            "collision-proxy",
            "lod-pipeline",
            "vegetation-artist",
            "geometry-nodes",
        ],
    ),
    (
        "https://github.com/gamedev-skills/awesome-gamedev-agent-skills.git",
        "skills/disciplines",
        [
            "camera-systems",
            "physics-tuning",
            "game-feel",
            "performance-optimization",
            "level-design",
            "procedural-gen",
        ],
    ),
    (
        "https://github.com/gamedev-skills/awesome-gamedev-agent-skills.git",
        "skills/unity",
        [
            "unity-physics",
            "unity-scriptableobjects",
            "unity-input-system",
            "unity-build-pipeline",
        ],
    ),
]


def clone_repo(url: str, dest: Path) -> None:
    subprocess.run(
        ["git", "clone", "--depth", "1", "--quiet", url, str(dest)],
        check=True,
    )


def install_all(dest_root: Path) -> tuple[int, int]:
    dest_root.mkdir(parents=True, exist_ok=True)
    temp_base = Path("E:/Temp")
    temp_base.mkdir(parents=True, exist_ok=True)

    cloned: dict[str, Path] = {}
    installed_skills = 0
    total_files = 0

    with tempfile.TemporaryDirectory(prefix="veyra_skills_", dir=str(temp_base)) as tmp:
        tmp_path = Path(tmp)
        for repo_url, prefix, skill_names in SKILL_SOURCES:
            if repo_url not in cloned:
                repo_dir = tmp_path / f"repo_{len(cloned)}"
                print(f"Cloning {repo_url} ...")
                clone_repo(repo_url, repo_dir)
                cloned[repo_url] = repo_dir
            base_dir = cloned[repo_url] / prefix

            for skill in skill_names:
                src = base_dir / skill
                if not (src / "SKILL.md").is_file():
                    print(f"[WARN] Missing SKILL.md at {src}")
                    continue
                dst = dest_root / skill
                if dst.exists():
                    shutil.rmtree(dst)
                shutil.copytree(
                    src,
                    dst,
                    ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
                )
                n_files = sum(1 for p in dst.rglob("*") if p.is_file())
                total_files += n_files
                installed_skills += 1
                print(f"[OK] {skill:44s} ({n_files} files)")

    return installed_skills, total_files


def main() -> int:
    default_dest = Path.home() / ".claude" / "skills"
    parser = argparse.ArgumentParser(description="Install curated Unity/Blender/Shader skills for Claude Code.")
    parser.add_argument("--dest", type=Path, default=default_dest, help="Destination skills root directory.")
    args = parser.parse_args()

    skills_ok, files_ok = install_all(args.dest)
    print(f"\nInstalled {skills_ok} skills ({files_ok} files) into {args.dest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
