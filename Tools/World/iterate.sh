#!/usr/bin/env bash
# World visual iteration loop (user request 2026-11-11: >= 20 check / fix / re-run rounds towards a GTA V day and
# Cyberpunk-style wet neon night). From Git Bash in E:\VeyraCity:
#   Tools/World/iterate.sh <N> [--no-build] [--steps roads,trees,...] [--routes "R_SMOKE ..."] [--filter CAM_05,CAM_06]
# 1. optional world builders (WorldBuildBatch steps) on the authoring scene
# 2. Dev Mono build (runs Distribute To Cells)                                  -> Logs/ITER/<N>/build_unity.log
# 3. review capture of the cameras at noon and at 22:00 wet/rain                 -> Screenshots/<date>_iter<N>/ (2 contact sheets)
# 4. optional autotest routes + verify                                            -> Logs/ITER/<N>/Dev_<route>/
# Prints one line per step. The critique and the change list go to Docs/WORLD_ITERATION_LOG.md (written by hand).
set -u
N="${1:?iteration number}"; shift
BUILD=1; STEPS=""; ROUTES=""; FILTER=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) BUILD=0 ;;
    --steps) STEPS="$2"; shift ;;
    --routes) ROUTES="$2"; shift ;;
    --filter) FILTER="$2"; shift ;;
  esac
  shift
done
P=/e/VeyraCity; cd "$P" || exit 2
U="/e/Unity/Editors/6000.6.8f1/Editor/Unity.exe"
EXE="$P/Builds/DevMono/VeyraCity.exe"
OUT="$P/Logs/ITER/$N"; SHOTS="$P/Screenshots/$(date +%Y-%m-%d)_iter$N"
export TEMP=E:/Temp TMP=E:/Temp
mkdir -p "$OUT" "$SHOTS"
rm -f Temp/UnityLockfile
if [ -n "$STEPS" ]; then
  "$U" -batchmode -quit -projectPath 'E:\VeyraCity' -executeMethod VeyraCity.EditorTools.WorldBuildBatch.Run -veyraSteps "$STEPS" -logFile "$(cygpath -w "$OUT/steps_unity.log")"
  echo "STEPS $STEPS rc=$? errors=$(grep -c 'error CS' "$OUT/steps_unity.log")"
  cp Logs/B1/world_build.txt "$OUT/world_build.txt"
  rm -f Temp/UnityLockfile
fi
if [ "$BUILD" = 1 ]; then
  "$U" -batchmode -quit -projectPath 'E:\VeyraCity' -executeMethod VeyraBuild.BuildDevMono -logFile "$(cygpath -w "$OUT/build_unity.log")"
  rc=$?; rm -f Temp/UnityLockfile
  echo "BUILD rc=$rc errors=$(grep -c 'error CS' "$OUT/build_unity.log") warnings=$(grep 'warning CS' "$OUT/build_unity.log" | sort -u | wc -l)"
  [ "$rc" = 0 ] || { grep "error CS" "$OUT/build_unity.log" | sort -u | head; exit 1; }
fi
FARG=""; [ -n "$FILTER" ] && FARG="-reviewFilter $FILTER"
"$EXE" -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -reviewCapture -reviewCard "iter$N" -reviewDir "$(cygpath -w "$SHOTS")" \
  -reviewTimes 12,22w $FARG -veyraMute -logFile "$(cygpath -w "$OUT/review_player.log")"
echo "REVIEW: $(grep -c '^OK' "$SHOTS/review.txt" 2>/dev/null) OK, $(grep -c '^FAIL' "$SHOTS/review.txt" 2>/dev/null) FAIL; $(grep -o 'materials:.*' "$SHOTS/review.txt" | head -1) -> $SHOTS"
for R in $ROUTES; do
  D="$OUT/Dev_$R"; mkdir -p "$D"
  "$EXE" -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -autotest 0 -route "$R" -statsPath "$(cygpath -w "$D/stats.json")" \
    -logFile "$(cygpath -w "$D/player.log")" -veyraSaveDir "$(cygpath -w "$D/save")" -veyraMute -veyraNoParked -veyraNoTraffic
  powershell -ExecutionPolicy Bypass -File Tools/verify_run.ps1 -Stats "$(cygpath -w "$D/stats.json")" -PlayerLog "$(cygpath -w "$D/player.log")" > "$D/verify.txt" 2>&1
  echo "$R: $(grep RESULT "$D/verify.txt") | $(grep -E 'segments all|perf rules' "$D/verify.txt" | tr -s ' ' | cut -c1-200 | tr '\n' ' ')"
done
