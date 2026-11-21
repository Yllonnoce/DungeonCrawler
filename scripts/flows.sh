#!/usr/bin/env bash
# Runs headless test flows side by side, each in its own user://.
# Usage: scripts/flows.sh [-j N] "<flow args>" "<flow args>" ...
#   e.g. scripts/flows.sh -j 6 "--test-ride" "--test-split=after" "--test-crew --quit-after=570"
# Each argument is one flow's game flags (what follows `godot --headless --path . --`).
#
# - Builds once (dotnet build DungeonCrawler.csproj) and stops if the build fails.
# - Runs up to N flows at a time (default: min(6, number of flows)).
# - Isolation: Godot on Linux puts user:// under $XDG_DATA_HOME/godot/app_userdata/DungeonCrawler, so each flow gets
#   XDG_DATA_HOME=$(mktemp -d): its own settings.cfg, profile.json, saves, voice cache and logs, nothing shared with the
#   other flows or with your real ~/.local/share/godot/app_userdata/DungeonCrawler. A flow starts from an empty user://
#   (default settings, no keys, empty profile, the shipped campaign from res://); none of the headless flows need more.
#   CLAUDE_API_KEY, GEMINI_API_KEY and RUNWARE_API_KEY are removed from each flow's environment: no flow run here spends.
# - Timeout per flow: its last --quit-after=S plus max(120, S/2) seconds; without one FLOWS_TIMEOUT (default 1200).
# - Prints one line per flow: PASS/FAIL, the flags, the flow's own "[tag] RESULT ..." line (or "no RESULT"), its
#   ERROR-line count, its wall time and its log. Exits 1 if any flow lacks a RESULT ok (2 if the build fails).
# - Temp user:// dirs are removed at the end; the logs are kept (FLOWS_LOG_DIR, default /tmp/flows-<date>-<time>).
#   KEEP_TMP=1 keeps the user:// dirs too. GODOT=path picks another Godot binary.
#
# NOT for the RIDE pass: `RIDE=1 scripts/net_test.sh` needs the machine alone (its set-piece clocks and party pairs fail
# on a loaded machine). Run it by itself, never through this script or beside it. Network flows (--test-net-*) are
# refused here too: they share a port and belong to scripts/net_test.sh.
set -u
cd "$(dirname "$0")/.."

usage() { sed -n '2,4p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-2}"; }

JOBS=""
while [ $# -gt 0 ]; do
  case "$1" in
    -j) [ $# -ge 2 ] || usage; JOBS=$2; shift 2 ;;
    -j*) JOBS=${1#-j}; shift ;;
    -h|--help) usage 0 ;;
    --) shift; break ;;
    *) break ;;
  esac
done
[ $# -gt 0 ] || usage
FLOWS=("$@")
N=${#FLOWS[@]}
if [ -z "$JOBS" ]; then JOBS=$(( N < 6 ? N : 6 )); fi
case "$JOBS" in ''|*[!0-9]*) echo "flows.sh: -j wants a number, got '$JOBS'"; exit 2 ;; esac
[ "$JOBS" -ge 1 ] || JOBS=1

for f in "${FLOWS[@]}"; do
  case " $f " in
    *" --test-net"*) echo "flows.sh: refusing '$f': network flows belong to scripts/net_test.sh (and RIDE=1 runs alone)"; exit 2 ;;
  esac
done

GODOT=${GODOT:-godot}
DEFAULT_TIMEOUT=${FLOWS_TIMEOUT:-1200}
LOG_DIR=${FLOWS_LOG_DIR:-/tmp/flows-$(date +%Y%m%d-%H%M%S)}
mkdir -p "$LOG_DIR" || exit 2

echo "flows.sh: building (dotnet build DungeonCrawler.csproj)..."
if ! dotnet build DungeonCrawler.csproj > "$LOG_DIR/build.log" 2>&1; then
  tail -n 30 "$LOG_DIR/build.log"
  echo "flows.sh: build FAILED (full log: $LOG_DIR/build.log)"
  exit 2
fi

declare -a LOG TMPD TOUT START SECS RC
declare -A PID2I=()

cleanup() {
  local pid
  for pid in "${!PID2I[@]}"; do kill -TERM "$pid" 2>/dev/null; done   # the timeout wrappers pass it on to their godot
  if [ -z "${KEEP_TMP:-}" ]; then
    local d; for d in "${TMPD[@]}"; do [ -n "$d" ] && rm -rf "$d"; done
  else
    echo "flows.sh: kept the user:// dirs: ${TMPD[*]}"
  fi
}
trap cleanup EXIT
trap 'echo "flows.sh: interrupted"; exit 130' INT TERM

timeout_for() {   # the last --quit-after=S in the flags, plus a margin; else the default
  local qa="" w
  for w in $1; do case "$w" in --quit-after=*) qa=${w#--quit-after=} ;; esac; done
  qa=${qa%%.*}
  case "$qa" in ''|*[!0-9]*) echo "$DEFAULT_TIMEOUT"; return ;; esac
  local m=$(( qa / 2 )); [ "$m" -lt 120 ] && m=120
  echo $(( qa + m ))
}

start_flow() {
  local i=$1 spec=${FLOWS[$1]} name flags
  read -r -a flags <<< "$spec"
  name=$(printf '%02d-%s' "$((i + 1))" "$(printf '%s' "$spec" | tr -cs 'A-Za-z0-9=._-' '_' | sed 's/^[-_]*//; s/_*$//' | cut -c1-80)")
  LOG[$i]="$LOG_DIR/$name.log"
  TMPD[$i]=$(mktemp -d "${TMPDIR:-/tmp}/flows-xdg.XXXXXX")
  TOUT[$i]=$(timeout_for "$spec")
  START[$i]=$(date +%s)
  XDG_DATA_HOME="${TMPD[$i]}" timeout -k 15 "${TOUT[$i]}" \
    env -u CLAUDE_API_KEY -u GEMINI_API_KEY -u RUNWARE_API_KEY "$GODOT" --headless --path . -- "${flags[@]}" \
    > "${LOG[$i]}" 2>&1 < /dev/null &
  PID2I[$!]=$i
  echo "flows.sh: started [$((i + 1))/$N] $spec (timeout ${TOUT[$i]}s, pid $!)"
}

reap_one() {
  local pid rc i
  wait -n -p pid "${!PID2I[@]}"; rc=$?
  [ -n "${pid:-}" ] || return
  i=${PID2I[$pid]}; unset "PID2I[$pid]"
  RC[$i]=$rc; SECS[$i]=$(( $(date +%s) - START[$i] ))
  echo "flows.sh: finished $((i + 1))/$N ${FLOWS[$i]} (exit $rc, ${SECS[$i]}s)"
}

T0=$(date +%s)
echo "flows.sh: $N flows, $JOBS at a time, logs in $LOG_DIR; loadavg $(cut -d' ' -f1-3 /proc/loadavg)"
for ((i = 0; i < N; i++)); do
  while [ "${#PID2I[@]}" -ge "$JOBS" ]; do reap_one; done
  start_flow "$i"
done
while [ "${#PID2I[@]}" -gt 0 ]; do reap_one; done
WALL=$(( $(date +%s) - T0 ))

# The flow's own RESULT: every floor-12 flow also runs the built-in campaign, whose "[builtin] RESULT ok" comes 2.5 s in,
# so a [builtin] line counts only for a flow that is no test of its own. A failing RESULT line wins over an ok one.
result_of() {
  local log=$1 spec=$2 lines
  lines=$(grep -E '^\[[^]]+\] RESULT' "$log" | grep -v '^\[builtin\] RESULT')
  if [ -z "$lines" ] && [[ " $spec " != *" --test-"* ]]; then lines=$(grep -E '^\[builtin\] RESULT' "$log"); fi
  [ -n "$lines" ] || { echo "no RESULT"; return; }
  local bad; bad=$(printf '%s\n' "$lines" | grep -v ' RESULT ok' | tail -n 1)
  if [ -n "$bad" ]; then echo "$bad"; else printf '%s\n' "$lines" | tail -n 1; fi
}

echo
echo "flows.sh: summary ($N flows, -j $JOBS, wall ${WALL}s; loadavg $(cut -d' ' -f1-3 /proc/loadavg))"
FAILED=0
for ((i = 0; i < N; i++)); do
  res=$(result_of "${LOG[$i]}" "${FLOWS[$i]}" | cut -c1-200)
  errs=$(grep -c 'ERROR' "${LOG[$i]}")
  note=""; [ "${RC[$i]}" = 124 ] || [ "${RC[$i]}" = 137 ] && note=" (timed out after ${TOUT[$i]}s)"
  if [[ "$res" == *" RESULT ok"* ]]; then verdict=PASS; else verdict=FAIL; FAILED=1; fi
  printf '%s  %-32s  %s  errors=%s  %ss%s  %s\n' "$verdict" "${FLOWS[$i]}" "$res" "$errs" "${SECS[$i]}" "$note" "${LOG[$i]}"
done
exit "$FAILED"
