#!/usr/bin/env bash
# Case 1 tray idempotence assertion.
#
# Asserts that every log given on the command line reports the SAME tray ground plane, i.e. that
# `Case1SceneSetup.Build` converges instead of walking the plane down a little further on each run.
# Reads the value Build already prints; adds nothing to any measured path.
#
#   tools/case1-tray-idempotence.sh .plan-build/logs/idem1.log ... 
#
# rc=0 GREEN (all equal), rc=1 RED (they drift), rc=2 a log had no TRAY_GROUND line.
#
# NEGATIVE CONTROL - this assertion is known to go RED. Run it against the five consecutive Builds
# of the pre-fix code that are still in the repo:
#   tools/case1-tray-idempotence.sh .plan-build/logs/{c1_build4,c1_build5,build_after,playgate,selgate}.log
# -> RED, 1.754 / 1.148 / 0.513 / -0.153 / -0.852.
set -uo pipefail
[ $# -ge 2 ] || { echo "usage: $0 <log> <log> [log...]" >&2; exit 2; }

vals=(); rc=0
for f in "$@"; do
  v="$(grep -o 'TRAY_GROUND y=[-0-9.]*' "$f" 2>/dev/null | tail -1 | cut -d= -f2)"
  if [ -z "$v" ]; then echo "TRAY_IDEM MISSING $f (no TRAY_GROUND line)" >&2; exit 2; fi
  printf 'TRAY_IDEM %-44s y=%s\n' "$f" "$v"
  vals+=("$v")
done
for v in "${vals[@]}"; do [ "$v" = "${vals[0]}" ] || rc=1; done

if [ $rc -eq 0 ]; then
  echo "TRAY_IDEM GREEN runs=$# y=${vals[0]} spread=0"
else
  echo "TRAY_IDEM RED runs=$# values: ${vals[*]}"
fi
exit $rc
