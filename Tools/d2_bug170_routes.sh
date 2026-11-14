#!/usr/bin/env bash
# D2 / BUG-170: Release IL2CPP build, then the three V1 routes WITH traffic (vsync off, muted), same recipe as the
# audit's Phase E baseline (Logs/PhaseE/Release_<route>_vsync0). Results in Logs/D2_BUG170/Release_<route>_vsync0_traffic.
# Usage: bash Tools/d2_bug170_routes.sh [--nobuild]
set -u
cd /e/VeyraCity || exit 1
export TEMP=E:/Temp/VeyraCity TMP=E:/Temp/VeyraCity
OUT=Logs/D2_BUG170
mkdir -p "$OUT"
if [ "${1:-}" != "--nobuild" ]; then
  [ -f Temp/UnityLockfile ] && { echo "Unity lock is held: not building"; exit 3; }
  cmd //c "Tools\\build_release.cmd" > "$OUT/build_release.txt" 2>&1
  rc=$?
  cp -f Logs/unity_build_release.log "$OUT/unity_build_release.log" 2>/dev/null
  echo "build exit $rc"; grep -E "error CS|BUILD OK|BUILD FAILED" "$OUT/build_release.txt" | head
  [ $rc -ne 0 ] && exit 4
fi
for R in R_SMOKE R_BRIDGES R_DOWNTOWN_NIGHT; do
  D="$OUT/Release_${R}_vsync0_traffic"
  mkdir -p "$D"
  cmd //c "Tools\\run_autotest.cmd $R 0 Release -vsync 0 -veyraMute" > "$D/run.txt" 2>&1
  cp -f Logs/stats.json "$D/stats.json"; cp -f Logs/player.log "$D/player.log"
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/verify_run.ps1 > "$D/verify.txt" 2>&1
  echo "== $R"; grep -E "^Run:|segments|CPU main|hitches|p99" "$D/verify.txt" | head -6
  grep -E "\[AutoTest\] segment" "$D/player.log" | sed 's/^.*segment/  segment/' | cut -c1-200
done
