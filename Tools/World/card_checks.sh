#!/usr/bin/env bash
# World track card checks (CLAUDE.md "per card" loop), run from Git Bash in E:\VeyraCity:
#   Tools/World/card_checks.sh <CARD> [--no-build] [--no-review] [--routes "R_SMOKE R_BRIDGES R_DOWNTOWN_NIGHT"]
# 1. Dev Mono build (VeyraBuild.BuildDevMono; it runs Veyra/World/Distribute To Cells first) -> Logs/<CARD>/build_unity.log
# 2. each route in Builds/DevMono with -veyraMute -veyraNoParked -veyraNoTraffic, then Tools/verify_run.ps1
#    -> Logs/<CARD>/Dev_<route>/{stats.json, player.log, verify.txt}
# 3. review capture -> Screenshots/<date>_<CARD>/ (+ review.txt, contact_sheet.png)
# Refuses to start while Temp/UnityLockfile exists. Prints one summary line per step; exit 1 if any route fails verify.
set -u
CARD="${1:?card name}"; shift
BUILD=1; REVIEW=1; ROUTES="R_SMOKE R_BRIDGES R_DOWNTOWN_NIGHT"
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) BUILD=0 ;;
    --no-review) REVIEW=0 ;;
    --routes) ROUTES="$2"; shift ;;
  esac
  shift
done
P=/e/VeyraCity
UNITY="/e/Unity/Editors/6000.6.8f1/Editor/Unity.exe"
EXE="$P/Builds/DevMono/VeyraCity.exe"
OUT="$P/Logs/$CARD"
DAY=$(date +%Y-%m-%d)
SHOTS="$P/Screenshots/${DAY}_$CARD"
export TEMP=E:/Temp TMP=E:/Temp
mkdir -p "$OUT"
cd "$P" || exit 2
fail=0
if [ "$BUILD" = 1 ]; then
  if [ -f Temp/UnityLockfile ]; then echo "LOCKED: Temp/UnityLockfile exists"; exit 2; fi
  "$UNITY" -batchmode -quit -projectPath 'E:\VeyraCity' -executeMethod VeyraBuild.BuildDevMono -logFile "E:\\VeyraCity\\Logs\\$CARD\\build_unity.log"
  rc=$?
  rm -f Temp/UnityLockfile
  errs=$(grep -c "error CS" "$OUT/build_unity.log")
  warns=$(grep "warning CS" "$OUT/build_unity.log" | sort -u | wc -l)
  echo "BUILD rc=$rc compile errors=$errs unique warnings=$warns"
  grep "warning CS" "$OUT/build_unity.log" | sort -u > "$OUT/build_warnings.txt"
  grep -m3 "Distribute To Cells\|moved \|HLOD:" "$OUT/build_unity.log"
  [ "$rc" = 0 ] || { grep "error CS\|Exception\|BuildFailed\|Error building" "$OUT/build_unity.log" | sort -u | head -20; exit 1; }
fi
for R in $ROUTES; do
  D="$OUT/Dev_$R"; mkdir -p "$D"
  rm -f "$D/stats.json"
  "$EXE" -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -autotest 0 -route "$R" -statsPath "$(cygpath -w "$D/stats.json")" \
    -logFile "$(cygpath -w "$D/player.log")" -veyraSaveDir "$(cygpath -w "$D/save")" -veyraMute -veyraNoParked -veyraNoTraffic ${CARD_EXTRA:-}
  powershell -ExecutionPolicy Bypass -File Tools/verify_run.ps1 -Stats "$(cygpath -w "$D/stats.json")" -PlayerLog "$(cygpath -w "$D/player.log")" > "$D/verify.txt" 2>&1 || fail=1
  echo "$R: $(grep RESULT "$D/verify.txt") | $(grep -E 'segments all|hitches|perf rules' "$D/verify.txt" | tr -s ' ' | tr '\n' ' ')"
  python -c "import json,sys; s=json.load(open(sys.argv[1],encoding='utf-8')); print('   streaming', s.get('streaming'))" "$D/stats.json" 2>/dev/null
done
if [ "$REVIEW" = 1 ]; then
  mkdir -p "$SHOTS"
  "$EXE" -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -reviewCapture -reviewCard "$CARD" -reviewDir "$(cygpath -w "$SHOTS")" -veyraMute \
    -logFile "$(cygpath -w "$OUT/review_player.log")"
  echo "REVIEW: $(grep -c '^OK' "$SHOTS/review.txt" 2>/dev/null) OK, $(grep -c '^FAIL' "$SHOTS/review.txt" 2>/dev/null) FAIL -> $SHOTS"
fi
exit $fail
