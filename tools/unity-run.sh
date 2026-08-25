#!/usr/bin/env bash
# Unity batchmode cagrilarini serilestirir: ayni proje uzerinde iki instance calisamaz.
# macOS'ta `flock` YOKTUR — mkdir tabanli atomik kilit kullaniyoruz.
# Kullanim: tools/unity-run.sh <unity-args...>   (-projectPath'i bu script ekler)
set -uo pipefail
UNITY="/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCKDIR="$PROJ/.plan-build/unity.lock.d"
mkdir -p "$PROJ/.plan-build/logs"

acquired=0
for i in $(seq 1 900); do            # en fazla ~30 dk bekle
  if mkdir "$LOCKDIR" 2>/dev/null; then
    echo $$ > "$LOCKDIR/pid"; acquired=1; break
  fi
  # bayat kilit: sahibi olmeyse temizle
  owner="$(cat "$LOCKDIR/pid" 2>/dev/null || echo)"
  if [ -n "$owner" ] && ! kill -0 "$owner" 2>/dev/null; then
    echo "[unity-run] bayat kilit temizleniyor (pid=$owner)" >&2
    rm -rf "$LOCKDIR"; continue
  fi
  [ $((i % 15)) -eq 1 ] && echo "[unity-run] kilit mesgul (sahip=$owner), bekleniyor..." >&2
  sleep 2
done
if [ "$acquired" -ne 1 ]; then echo "[unity-run] HATA: kilit alinamadi" >&2; exit 99; fi
cleanup(){ rm -rf "$LOCKDIR"; }
trap cleanup EXIT INT TERM

# ekstra guvenlik: bu proje uzerinde baska bir Unity kosuyorsa bekle
busy=1
for i in $(seq 1 30); do
  pgrep -if "Unity.app/Contents/MacOS/Unity .*-projectpath $PROJ" >/dev/null 2>&1 || { busy=0; break; }
  [ $((i % 15)) -eq 1 ] && echo "[unity-run] bu proje uzerinde baska bir Unity kosuyor, bekleniyor..." >&2
  sleep 2
done
# ABORT, do not fall through: launching a second Unity against an already-open
# project is the exact collision this guard exists to prevent. Falling through
# silently after the timeout turned the guard into a 10-minute delay.
if [ "$busy" -ne 0 ]; then
  echo "[unity-run] HATA: bu proje zaten acik (Editor pid: $(pgrep -if "Unity.app/Contents/MacOS/Unity .*-projectpath $PROJ" | tr '\n' ' '))" >&2
  echo "[unity-run] Editor'u kapatip tekrar deneyin." >&2
  exit 98
fi

echo "[unity-run] calisiyor: $*" >&2
"$UNITY" -projectPath "$PROJ" "$@"
rc=$?
echo "[unity-run] bitti rc=$rc" >&2
exit $rc
