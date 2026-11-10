#!/usr/bin/env bash
# Visual iteration capture (Docs/VISUAL_ITERATION_LOG.md): optional car reimport and Dev Mono build, then the scripted
# scenes (GameFlow.IterationShots) into Screenshots/<date>_iterNN, a contact sheet and the [Iter]/[IterPerf] lines.
#
#   Tools/visual_iteration.sh <NN> [--import] [--method Ns.Class.Method]... [--build] [--ab "trees;probe,parked"] [--tests]
#
# --method runs an editor method in batch mode before the build (e.g. VeyraCity.EditorTools.SkyDomeMaterial.Build).
# --ab runs extra captures with -veyraIterOff <list> (one per ';' entry) for frame-time A/B tests.
# --tests runs the EditMode tests after the captures (never in parallel with a player run: it skews the frame times).
# Waits until no Unity editor holds Temp/UnityLockfile (shared project). Everything stays on E:.
set -u
cd /e/VeyraCity || exit 1
export TEMP=E:/Temp/VeyraCity TMP=E:/Temp/VeyraCity
U="/e/Unity/Editors/6000.6.8f1/Editor/Unity.exe"
BLENDER="/d/New folder/blender.exe"
N=$1; shift
IMPORT=0; BUILD=0; AB=""; TESTS=0; METHODS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --import) IMPORT=1 ;;
    --build) BUILD=1 ;;
    --ab) AB=$2; shift ;;
    --method) METHODS+=("$2"); shift ;;
    --tests) TESTS=1 ;;
  esac
  shift
done
DIR="Screenshots/$(date +%Y-%m-%d)_iter$N"
mkdir -p Logs/A8 "$DIR"

if [ $IMPORT -eq 1 ] || [ $BUILD -eq 1 ] || [ ${#METHODS[@]} -gt 0 ]; then
  while [ -f Temp/UnityLockfile ]; do sleep 20; done
  sleep 30
  [ -f Temp/UnityLockfile ] && { echo "LOCK RETAKEN"; exit 3; }
fi
if [ $IMPORT -eq 1 ]; then
  "$U" -batchmode -nographics -projectPath "E:/VeyraCity" -executeMethod VeyraCity.EditorTools.CarImportTools.ImportBatch -logFile "E:/VeyraCity/Logs/A8/unity_import.log"
  echo "import exit $?"; grep -E "error CS|\[CarImport\] done" Logs/A8/unity_import.log | head -20
fi
for meth in "${METHODS[@]}"; do
  "$U" -batchmode -nographics -projectPath "E:/VeyraCity" -executeMethod "$meth" -quit -logFile "E:/VeyraCity/Logs/A8/iter${N}_method.log"
  echo "method $meth exit $?"; grep -E "error CS|\[SkyDome\]|Exception" "Logs/A8/iter${N}_method.log" | head -10
done
if [ $BUILD -eq 1 ]; then
  "$U" -batchmode -nographics -projectPath "E:/VeyraCity" -executeMethod VeyraBuild.BuildDevMono -logFile "E:/VeyraCity/Logs/A8/iter${N}_build.log"
  rc=$?
  echo "build exit $rc"; grep -E "error CS|Build Finished" "Logs/A8/iter${N}_build.log" | head -20
  [ $rc -ne 0 ] && exit 4
fi

run() { # <outdir> <log> [off-list]
  if [ -n "${3:-}" ]; then
    Builds/DevMono/VeyraCity.exe -veyraIterShots "E:/VeyraCity/$1" -veyraIterOff "$3" -logFile "E:/VeyraCity/$2" >/dev/null 2>&1
  else
    Builds/DevMono/VeyraCity.exe -veyraIterShots "E:/VeyraCity/$1" -logFile "E:/VeyraCity/$2" >/dev/null 2>&1
  fi
  grep -E "^\[Iter|Frame stats|Exception" "$2" | head -12
}
run "$DIR" "Logs/A8/iter${N}_player.log"
S="E:/VeyraCity/$DIR"
"$BLENDER" -b --factory-startup -P E:/VeyraCity/Tools/Blender/contact_sheet.py -- "$S/sheet.png" 4 4 \
  "$S/it1_noon_city.png" "$S/it2_golden_hour.png" "$S/it3_neon_night.png" "$S/it4_rain_night.png" \
  "$S/it5_nitro_speed.png" "$S/it6_drift.png" "$S/it7_traffic_evening.png" "$S/it8_car_closeup.png" 2>&1 | grep SHEET
"$BLENDER" -b --factory-startup -P E:/VeyraCity/Tools/Blender/contact_sheet.py -- "$S/sheet_crash.png" 2 1280x720 \
  "$S/it9_crash.png" "$S/it9_crash_closeup.png" 2>&1 | grep SHEET

if [ -n "$AB" ]; then
  IFS=';' read -ra SETS <<< "$AB"
  for k in "${SETS[@]}"; do
    tag=$(echo "$k" | tr ',' '+')
    echo "== A/B off=$k"
    run "$DIR/ab_$tag" "Logs/A8/iter${N}_ab_${tag}.log" "$k"
  done
fi
if [ $TESTS -eq 1 ]; then
  while [ -f Temp/UnityLockfile ]; do sleep 20; done
  "$U" -batchmode -nographics -projectPath "E:/VeyraCity" -runTests -testPlatform EditMode -testResults "E:/VeyraCity/Logs/A8/editmode_results.xml" -logFile "E:/VeyraCity/Logs/A8/editmode.log"
  echo "tests exit $?"
  python -I -c "
import xml.etree.ElementTree as E
r=E.parse('E:/VeyraCity/Logs/A8/editmode_results.xml').getroot()
print(r.get('result'),r.get('total'),'passed',r.get('passed'),'failed',r.get('failed'))
for t in r.iter('test-case'):
    if t.get('result')!='Passed':
        m=t.find('.//message'); print('FAIL',t.get('fullname'),(m.text or '')[:2500] if m is not None else '')
"
  cat Logs/A8/handling_benchmark.txt 2>/dev/null
fi
echo "iteration $N done $(date +%H:%M:%S)"
