#!/usr/bin/env bash
# Container entry point: the headless Godot server on 127.0.0.1:7777, then the FastAPI front door on $PORT.
# Saves, keys and the status file live on the persistent disk mounted at /data (XDG_DATA_HOME).
set -euo pipefail
export XDG_DATA_HOME=/data
export STATUS_FILE=/data/status.json
mkdir -p "$XDG_DATA_HOME/godot/app_userdata/DungeonCrawler"
# the server's own keys, from Render secrets, so an AI campaign never needs a key in the image
if [ -n "${CLAUDE_API_KEY:-}" ] || [ -n "${RUNWARE_API_KEY:-}" ]; then
  printf '[keys]\nclaude="%s"\ngemini=""\nrunware="%s"\n' "${CLAUDE_API_KEY:-}" "${RUNWARE_API_KEY:-}" > "$XDG_DATA_HOME/godot/app_userdata/DungeonCrawler/keys.cfg"
fi
CAMPAIGN=${CAMPAIGN:-builtin}
MAX_PLAYERS=${MAX_PLAYERS:-4}
SEED_ARG=""
[ -n "${SEED:-}" ] && SEED_ARG="--seed=$SEED"
PLANNER_ARG=""
[ -n "${RUNWARE_API_KEY:-}" ] && [ -z "${CLAUDE_API_KEY:-}" ] && PLANNER_ARG="--planner=runware"
/app/server/DungeonCrawler.x86_64 --headless -- --server --port=7777 --bind=127.0.0.1 --campaign="$CAMPAIGN" $SEED_ARG $PLANNER_ARG --save=server --max-players="$MAX_PLAYERS" --status-file="$STATUS_FILE" &
GODOT_PID=$!
trap 'kill -TERM $GODOT_PID 2>/dev/null; wait $GODOT_PID' TERM INT
cd /app/deploy
exec uvicorn frontdoor:app --host 0.0.0.0 --port "${PORT:-10000}" --log-level warning
