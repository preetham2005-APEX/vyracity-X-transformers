#!/usr/bin/env bash
# D2 / BUG-170: Release IL2CPP build, then the three V1 routes WITH traffic (vsync off, muted), each run twice back to back:
# the D2 trims ("new") and the same build with -veyraD2Legacy ("legacy" = pre-trim behaviour), alternating the order per
# route so drift in machine state does not favour one side. Then short R_SMOKE runs at 50 and 120 Hz (-veyraFixedHz).
# Results: Logs/D2_BUG170/Release_<route>_<mode>. Refuses to run while another Unity editor or VeyraCity player is up.
# Usage: bash Tools/d2_bug170_routes.sh [--nobuild]
set -u
cd /e/VeyraCity || exit 1
export TEMP=E:/Temp/VeyraCity TMP=E:/Temp/VeyraCity
OUT=Logs/D2_BUG170
mkdir -p "$OUT"
busy() { tasklist 2>/dev/null | grep -qiE "^VeyraCity\.exe" && return 0; powershell -NoProfile -Command "@(Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | Where-Object { \$_.CommandLine -match 'VeyraCity' }).Count" | grep -qv '^0'; }
if busy; then echo "another Unity editor or VeyraCity player is running: not starting (quiet machine needed)"; exit 3; fi
if [ "${1:-}" != "--nobuild" ]; then
  [ -f Temp/UnityLockfile ] && { echo "Unity lock is held: not building"; exit 3; }
  cmd //c "Tools\\build_release.cmd" > "$OUT/build_release.txt" 2>&1
  rc=$?
  cp -f Logs/unity_build_release.log "$OUT/unity_build_release.log" 2>/dev/null
  echo "build exit $rc"; grep -E "error CS|BUILD OK|BUILD FAILED" "$OUT/build_release.txt" | head
  [ $rc -ne 0 ] && exit 4
fi
run() { # <route> <tag> [extra args]
  local R=$1 TAG=$2; shift 2
  local D="$OUT/Release_${R}_${TAG}"
  mkdir -p "$D"
  if busy; then echo "skipping $R $TAG: machine busy"; return; fi
  cmd //c "Tools\\run_autotest.cmd $R 0 Release -vsync 0 -veyraMute $*" > "$D/run.txt" 2>&1
  cp -f Logs/stats.json "$D/stats.json"; cp -f Logs/player.log "$D/player.log"
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/verify_run.ps1 > "$D/verify.txt" 2>&1
  echo "== $R $TAG"; grep -E "segments|CPU main|hitches|p99" "$D/verify.txt" | head -5
  grep -E "\[AutoTest\] segment" "$D/player.log" | sed 's/^.*segment/  segment/' | cut -c1-220
  grep -E "physics steps per frame" "$D/player.log" | sed 's/^.*physics/  physics/'
}
run R_SMOKE new;              run R_SMOKE legacy -veyraD2Legacy
run R_BRIDGES legacy -veyraD2Legacy; run R_BRIDGES new
run R_DOWNTOWN_NIGHT new;     run R_DOWNTOWN_NIGHT legacy -veyraD2Legacy
run R_SMOKE hz50 -veyraFixedHz 50
run R_SMOKE hz120 -veyraFixedHz 120
python -I Tools/d2_bug170_summary.py "$OUT" | tee "$OUT/summary.txt"
