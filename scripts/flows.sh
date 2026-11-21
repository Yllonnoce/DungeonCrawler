#!/usr/bin/env bash
# Runs headless test flows side by side, each in its own user://.
# Usage: scripts/flows.sh [-j N] "<flow args>" "<flow args>" ...
#   e.g. scripts/flows.sh -j 6 "--test-crew --quit-after=570" "--test-ride" "--test-split=after"
# Each argument is one flow's game flags (what follows `godot --headless --path . --`), split on whitespace only (no
# quoting inside a spec: a flag value cannot hold a space). Put --quit-after=S after the test flag: a --test-* flag sets
# its own budget and overrides an earlier one. List the longest flow (e.g. --test-crew) first.
#
# - Needs bash 5.1 or later (wait -n -p).
# - Builds once (dotnet build DungeonCrawler.csproj) and stops if the build fails.
# - Runs up to N flows at a time (default: min(6, number of flows, half the cores)).
# - Isolation: Godot on Linux puts user:// under $XDG_DATA_HOME/godot/app_userdata/DungeonCrawler, so each flow gets
#   XDG_DATA_HOME=$(mktemp -d) (absolute, made before any flow starts; the run aborts if one cannot be made): its own
#   settings.cfg, profile.json, saves, voice cache and logs, nothing shared with the other flows or with your real
#   ~/.local/share/godot/app_userdata/DungeonCrawler. A flow starts from an empty user:// (default settings, no keys,
#   empty profile, the shipped campaign from res://); none of the headless flows need more.
#   CLAUDE_API_KEY, GEMINI_API_KEY and RUNWARE_API_KEY are removed from each flow's environment: no flow run here spends.
# - Timeout per flow: its --quit-after=S (the last one after its last --test-* flag) plus max(120, S/2) seconds; without
#   one FLOWS_TIMEOUT (default 1200).
# - Prints one line per flow: PASS/FAIL, the flags, the flow's own "[tag] RESULT ..." line (or "no RESULT"), its exit
#   status, its ERROR-line count (marked "!" when not 0), its wall time and its log. A flow passes only if it exited 0 AND
#   printed its own RESULT ok (and no failing RESULT line); a timeout or a crash after the RESULT line is a FAIL. The early
#   "[builtin] RESULT ok" that every --test-* flow on the built-in campaign prints never counts for a --test-* spec.
# - Exit status: 0 all PASS, 1 any FAIL, 2 usage, refusal, temp dir or build failure, 130 interrupted.
# - Temp user:// dirs are removed at the end (after the flows have stopped); the logs are kept (FLOWS_LOG_DIR, default a
#   fresh /tmp/flows-<date>-<time>-XXXX). KEEP_TMP=1 keeps the user:// dirs too. GODOT=path picks another Godot binary.
#
# NOT for the RIDE pass: `RIDE=1 scripts/net_test.sh` needs the machine alone (its set-piece clocks and party pairs fail
# on a loaded machine). Run it by itself, never through this script or beside it. Network flows (--test-net-*, --server,
# --join=) are refused here too: they share a port and belong to scripts/net_test.sh.
set -u
if (( BASH_VERSINFO[0] < 5 || (BASH_VERSINFO[0] == 5 && BASH_VERSINFO[1] < 1) )); then
  echo "flows.sh: needs bash 5.1 or later (wait -n -p); this is $BASH_VERSION"; exit 2
fi
SELF=$(realpath "$0")
cd "$(dirname "$SELF")/.." || exit 2

usage() { sed -n '2,7p' "$SELF" | sed 's/^# \{0,1\}//'; exit "${1:-2}"; }

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
if [ -z "$JOBS" ]; then
  half=$(( $(nproc 2>/dev/null || echo 2) / 2 )); [ "$half" -ge 1 ] || half=1
  JOBS=$N; [ "$JOBS" -gt 6 ] && JOBS=6; [ "$JOBS" -gt "$half" ] && JOBS=$half
fi
case "$JOBS" in ''|*[!0-9]*) echo "flows.sh: -j wants a number, got '$JOBS'"; exit 2 ;; esac
[ "$JOBS" -ge 1 ] || JOBS=1

# split a spec into WORDS on any whitespace (space, tab, newline), with no globbing
split_words() { WORDS=(); read -r -d '' -a WORDS <<< "$1" || true; }

for f in "${FLOWS[@]}"; do
  split_words "$f"
  [ "${#WORDS[@]}" -gt 0 ] || { echo "flows.sh: refusing an empty flow spec"; exit 2; }
  for w in "${WORDS[@]}"; do
    case "$w" in
      --test-net*|--server|--server=*|--join=*)
        echo "flows.sh: refusing '$f' ($w): network flows belong to scripts/net_test.sh (and RIDE=1 runs alone)"; exit 2 ;;
    esac
  done
done

GODOT=${GODOT:-godot}
command -v "$GODOT" > /dev/null || { echo "flows.sh: no Godot binary '$GODOT' (set GODOT=path)"; exit 2; }
DEFAULT_TIMEOUT=${FLOWS_TIMEOUT:-1200}
case "$DEFAULT_TIMEOUT" in ''|*[!0-9]*) echo "flows.sh: FLOWS_TIMEOUT wants whole seconds, got '$DEFAULT_TIMEOUT'"; exit 2 ;; esac
if [ -n "${FLOWS_LOG_DIR:-}" ]; then LOG_DIR=$FLOWS_LOG_DIR; mkdir -p "$LOG_DIR" || exit 2
else LOG_DIR=$(mktemp -d "/tmp/flows-$(date +%Y%m%d-%H%M%S)-XXXX") || { echo "flows.sh: cannot make a log dir"; exit 2; }; fi

declare -a LOG TMPD TOUT START SECS RC
declare -A PID2I=()

cleanup() {
  trap - INT TERM
  local pid
  for pid in "${!PID2I[@]}"; do kill -TERM "$pid" 2>/dev/null; done   # the timeout wrappers pass it on to their godot
  [ "${#PID2I[@]}" -gt 0 ] && wait "${!PID2I[@]}" 2>/dev/null           # timeout -k 15 bounds this
  if [ -z "${KEEP_TMP:-}" ]; then
    local d; for d in "${TMPD[@]}"; do [ -n "$d" ] && rm -rf "$d"; done
  else
    echo "flows.sh: kept the user:// dirs: ${TMPD[*]}"
  fi
}
trap cleanup EXIT
trap 'echo "flows.sh: interrupted"; exit 130' INT TERM

# Every flow's user:// first, absolute and present, or nothing runs: an empty or relative XDG_DATA_HOME makes Godot fall
# back to the real ~/.local/share, the clash this script exists to stop.
TMP_BASE=$(realpath -e "${TMPDIR:-/tmp}" 2>/dev/null) || { echo "flows.sh: temp base '${TMPDIR:-/tmp}' does not exist"; exit 2; }
for ((i = 0; i < N; i++)); do
  TMPD[$i]=$(mktemp -d "$TMP_BASE/flows-xdg.XXXXXX") || { echo "flows.sh: mktemp failed in $TMP_BASE; nothing started"; exit 2; }
  case "${TMPD[$i]}" in /*) ;; *) echo "flows.sh: temp dir '${TMPD[$i]}' is not absolute; nothing started"; exit 2 ;; esac
  [ -d "${TMPD[$i]}" ] || { echo "flows.sh: temp dir '${TMPD[$i]}' is missing; nothing started"; exit 2; }
done

echo "flows.sh: building (dotnet build DungeonCrawler.csproj)..."
if ! dotnet build DungeonCrawler.csproj > "$LOG_DIR/build.log" 2>&1; then
  tail -n 30 "$LOG_DIR/build.log"
  echo "flows.sh: build FAILED (full log: $LOG_DIR/build.log)"
  exit 2
fi

timeout_for() {   # the flow's --quit-after=S (a later --test-* flag sets its own and drops it), plus a margin; else the default
  local qa="" w
  for w in "${WORDS[@]}"; do
    case "$w" in
      --quit-after=*) qa=${w#--quit-after=} ;;
      --test-*) qa="" ;;
    esac
  done
  [ -n "$qa" ] || { echo "$DEFAULT_TIMEOUT"; return; }
  qa=${qa%%.*}
  case "$qa" in ''|*[!0-9]*) echo "flows.sh: cannot read --quit-after in '$*'; timeout ${DEFAULT_TIMEOUT}s" >&2; echo "$DEFAULT_TIMEOUT"; return ;; esac
  [ "${#qa}" -le 6 ] || qa=999999
  qa=$(( 10#$qa ))
  local m=$(( qa / 2 )); [ "$m" -lt 120 ] && m=120
  echo $(( qa + m ))
}

start_flow() {
  local i=$1 spec=${FLOWS[$1]} name
  split_words "$spec"
  name=$(printf '%02d-%s' "$((i + 1))" "$(printf '%s' "$spec" | tr -cs 'A-Za-z0-9=._-' '_' | sed 's/^[-_]*//; s/_*$//' | cut -c1-80)")
  LOG[$i]="$LOG_DIR/$name.log"
  case "${TMPD[$i]}" in /*) [ -d "${TMPD[$i]}" ] || exit 2 ;; *) exit 2 ;; esac
  TOUT[$i]=$(timeout_for "$spec")
  START[$i]=$(date +%s)
  XDG_DATA_HOME="${TMPD[$i]}" timeout -k 15 "${TOUT[$i]}" \
    env -u CLAUDE_API_KEY -u GEMINI_API_KEY -u RUNWARE_API_KEY "$GODOT" --headless --path . -- "${WORDS[@]}" \
    > "${LOG[$i]}" 2>&1 < /dev/null &
  PID2I[$!]=$i
  echo "flows.sh: started [$((i + 1))/$N] $spec (timeout ${TOUT[$i]}s, pid $!)"
}

reap_one() {
  local pid rc i
  wait -n -p pid "${!PID2I[@]}"; rc=$?
  [ -n "${pid:-}" ] || { echo "flows.sh: wait failed (status $rc)"; exit 2; }
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

# The flow's own RESULT: every flow on the built-in campaign also prints "[builtin] RESULT ok" 2.5 s in, so a [builtin]
# line counts only for a spec with no --test-* flag (and, like any RESULT, only with exit 0). A failing line wins.
result_of() {
  local log=$1 lines has_test=0 w
  for w in "${WORDS[@]}"; do [[ $w == --test-* ]] && has_test=1; done
  lines=$(grep -E '^\[[^]]+\] RESULT' "$log" | grep -v '^\[builtin\] RESULT')
  if [ -z "$lines" ] && [ "$has_test" = 0 ]; then lines=$(grep -E '^\[builtin\] RESULT' "$log"); fi
  [ -n "$lines" ] || { echo "no RESULT"; return; }
  local bad; bad=$(printf '%s\n' "$lines" | grep -v ' RESULT ok' | tail -n 1)
  if [ -n "$bad" ]; then echo "$bad"; else printf '%s\n' "$lines" | tail -n 1; fi
}

echo
echo "flows.sh: summary ($N flows, -j $JOBS, wall ${WALL}s; loadavg $(cut -d' ' -f1-3 /proc/loadavg))"
FAILED=0
for ((i = 0; i < N; i++)); do
  split_words "${FLOWS[$i]}"
  res=$(result_of "${LOG[$i]}" | cut -c1-200)
  errs=$(grep -c 'ERROR' "${LOG[$i]}"); [ "$errs" = 0 ] || errs="$errs !"
  rc=${RC[$i]:-255}
  note=""
  if { [ "$rc" = 124 ] || [ "$rc" = 137 ]; } && [ "${SECS[$i]}" -ge "${TOUT[$i]}" ]; then note=" (timed out after ${TOUT[$i]}s)"; fi
  if [[ "$res" == *" RESULT ok"* ]] && [ "$rc" = 0 ]; then verdict=PASS; else verdict=FAIL; FAILED=1; fi
  printf '%s  %-32s  %s  exit=%s%s  errors=%s  %ss  %s\n' "$verdict" "${FLOWS[$i]}" "$res" "$rc" "$note" "$errs" "${SECS[$i]}" "${LOG[$i]}"
done
exit "$FAILED"
