#!/usr/bin/env bash
# Two headless Godot processes prove the multiplayer path: a dedicated server and a client that joins it, moves,
# chats and leaves. Each keeps its own user:// (XDG_DATA_HOME) so settings, keys and saves stay apart.
# Usage: scripts/net_test.sh            (direct ws://127.0.0.1:7790)
#        scripts/net_test.sh relay URL  (a client against an already-running relay, e.g. ws://127.0.0.1:10000/ws)
set -u
cd "$(dirname "$0")/.."
PORT=${PORT:-7790}
MODE=${1:-direct}
URL=${2:-ws://127.0.0.1:$PORT}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
GODOT=${GODOT:-godot}
SERVER_EXE=${SERVER_BIN:-}   # e.g. build/server/DungeonCrawler.x86_64 to test the exported server
run_server() { if [ -n "$SERVER_EXE" ]; then "$SERVER_EXE" --headless -- "$@"; else "$GODOT" --headless --path . -- "$@"; fi; }

if [ "$MODE" = direct ]; then
  XDG_DATA_HOME="$TMP/server" run_server --test-net-server --port=$PORT --bind=127.0.0.1 --campaign=offline --seed=4242 --quit-when-empty --status-file="$TMP/status.json" > "$TMP/server.log" 2>&1 &
  SERVER=$!
  wait_port() { for i in $(seq 1 240); do (exec 3<>/dev/tcp/127.0.0.1/$PORT) 2>/dev/null && return 0; sleep 0.5; done; echo "server never opened port $PORT"; return 1; }
  wait_port || { echo "--- server"; tail -20 "$TMP/server.log"; echo "NET TEST: FAIL"; exit 1; }
  # the floor takes a moment to build after the socket opens
  for i in $(seq 1 120); do grep -q "\[Adventure\]\|You descend\|floor 1" "$TMP/server.log" 2>/dev/null && break; sleep 0.5; done
  sleep 1
fi

XDG_DATA_HOME="$TMP/client" "$GODOT" --headless --path . -- --test-net-client --join="$URL" --name=Tester --class=class.warrior > "$TMP/client.log" 2>&1
CLIENT_RC=$?

if [ "$MODE" = direct ]; then
  for i in $(seq 1 60); do kill -0 $SERVER 2>/dev/null || break; sleep 0.5; done
  kill $SERVER 2>/dev/null
  wait $SERVER 2>/dev/null
  SERVER_RC=$?
  # let the port close before the resume pass reuses it
  for i in $(seq 1 60); do (exec 3<>/dev/tcp/127.0.0.1/$PORT) 2>/dev/null || break; sleep 0.5; done
  echo "--- server"; grep -E "\[net|\[chat\]|RESULT|Unhandled|ERROR" "$TMP/server.log" | head -40
  [ -f "$TMP/status.json" ] && { echo "--- status.json"; cat "$TMP/status.json"; echo; }
fi
echo "--- client"; grep -E "\[net|RESULT|Unhandled|ERROR" "$TMP/client.log" | head -40
if ! grep -q "\[net\] RESULT ok" "$TMP/client.log" || { [ "$MODE" = direct ] && ! grep -q "\[net-server\] RESULT ok" "$TMP/server.log"; }; then
  echo "NET TEST: FAIL"; exit 1
fi

if [ "$MODE" = direct ]; then
  # second pass: the server restarts on its own save; the same name gets its character, bag and floor back
  XDG_DATA_HOME="$TMP/server" run_server --test-net-server --port=$PORT --bind=127.0.0.1 --campaign=offline --seed=4242 --quit-when-empty > "$TMP/server2.log" 2>&1 &
  SERVER=$!
  wait_port || { echo "--- resumed server"; tail -20 "$TMP/server2.log"; echo "NET TEST: FAIL (resume pass)"; exit 1; }
  for i in $(seq 1 60); do grep -q "resumed save" "$TMP/server2.log" 2>/dev/null && break; sleep 0.5; done
  sleep 3
  XDG_DATA_HOME="$TMP/client" "$GODOT" --headless --path . -- --test-net-rejoin --join="$URL" --name=Tester --class=class.mage > "$TMP/client2.log" 2>&1
  for i in $(seq 1 60); do kill -0 $SERVER 2>/dev/null || break; sleep 0.5; done
  kill $SERVER 2>/dev/null; wait $SERVER 2>/dev/null
  echo "--- resumed server"; grep -E "\[net|RESULT|Unhandled|ERROR" "$TMP/server2.log" | head -20
  echo "--- rejoining client"; grep -E "\[net|RESULT|Unhandled|ERROR" "$TMP/client2.log" | head -20
  if ! grep -q "\[net\] RESULT ok" "$TMP/client2.log" || ! grep -q "\[net-server\] RESULT ok" "$TMP/server2.log"; then
    echo "NET TEST: FAIL (resume pass)"; exit 1
  fi
fi
echo "NET TEST: ok"; exit 0
