#!/usr/bin/env bash
# Two headless Godot processes prove the multiplayer path: a dedicated server and a client that joins it, moves,
# chats and leaves. Each keeps its own user:// (XDG_DATA_HOME) so settings, keys and saves stay apart.
# Usage: scripts/net_test.sh            (direct ws://127.0.0.1:7790)
#        scripts/net_test.sh relay URL  (a client against an already-running relay, e.g. ws://127.0.0.1:10000/ws)
#        START_FLOOR=0 scripts/net_test.sh  (the server opens in the tavern; the client joins, strikes the deal and leaves)
set -u
cd "$(dirname "$0")/.."
PORT=${PORT:-7790}
MODE=${1:-direct}
URL=${2:-ws://127.0.0.1:$PORT}
START_FLOOR=${START_FLOOR:-1}
JOIN_ONLY=""; [ "$START_FLOOR" = 0 ] && JOIN_ONLY="--net-test-join-only"
TMP=$(mktemp -d)
trap '[ -n "${KEEP_TMP:-}" ] && echo "kept $TMP" || rm -rf "$TMP"' EXIT
GODOT=${GODOT:-godot}
SERVER_EXE=${SERVER_BIN:-}   # e.g. build/server/DungeonCrawler.x86_64 to test the exported server
run_server() { if [ -n "$SERVER_EXE" ]; then "$SERVER_EXE" --headless -- "$@"; else "$GODOT" --headless --path . -- "$@"; fi; }

if [ "$MODE" = direct ]; then
  XDG_DATA_HOME="$TMP/server" run_server --test-net-server --floor=$START_FLOOR --port=$PORT --bind=127.0.0.1 --campaign=offline --seed=4242 --lethality=1 --hazard=press@2 --quit-when-empty --status-file="$TMP/status.json" > "$TMP/server.log" 2>&1 &
  SERVER=$!
  wait_port() { for i in $(seq 1 240); do (exec 3<>/dev/tcp/127.0.0.1/$PORT) 2>/dev/null && return 0; sleep 0.5; done; echo "server never opened port $PORT"; return 1; }
  wait_port || { echo "--- server"; tail -20 "$TMP/server.log"; echo "NET TEST: FAIL"; exit 1; }
  # the floor takes a moment to build after the socket opens
  for i in $(seq 1 120); do grep -q "\[Adventure\]\|You descend\|floor 1\|The tavern\.\|floor 0" "$TMP/server.log" 2>/dev/null && break; sleep 0.5; done
  sleep 1
fi

XDG_DATA_HOME="$TMP/client" "$GODOT" --headless --path . -- --test-net-client $JOIN_ONLY --join="$URL" --name=Tester --class=class.warrior --hazard=press@2 > "$TMP/client.log" 2>&1
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
if [ -n "$JOIN_ONLY" ]; then
  # the tavern run stops after the join: the floor, the merchant and the deal paid once through the server
  if ! grep -q "\[net-client\] tavern join OK" "$TMP/client.log"; then echo "NET TEST: FAIL (no tavern join)"; exit 1; fi
  echo "NET TEST: ok"; exit 0
fi
# the safe room on floor 2: the client stepped in and its HUD showed it (the server's objective line)
if ! grep -q "\[net\] safe room: HUD shows" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's HUD never showed the safe room)"; exit 1; fi
# the merchant in that safe room: a purchase and a sale through the server, each answered with a shop sync
if ! grep -q "\[net\] bought and sold at the merchant" "$TMP/client.log"; then echo "NET TEST: FAIL (no purchase and sale at the merchant)"; exit 1; fi
# co-op: the client's ping came back through the server with its name, and one player online took the stairs at once (no ready-check)
if ! grep -q "\[net\] ping relayed" "$TMP/client.log"; then echo "NET TEST: FAIL (the ping never came back to the client)"; exit 1; fi
# the bag guard (v14): a destroy aimed at an item that moved was refused, and a stat point for someone else's hero was refused out loud
if ! grep -q "\[net\] bag guard: a destroy aimed" "$TMP/client.log" || ! grep -q "\[net\] bag guard: a stat point" "$TMP/client.log"; then echo "NET TEST: FAIL (the bag guard step did not run or did not pass)"; exit 1; fi
# the effect sync (v14): a buff the server gave us reached our sheet and so did its end; a fight did not send the sheet per blow
if ! grep -q "\[net\] effect sync: its end reached us" "$TMP/client.log" || ! grep -q "\[net\] fight traffic:" "$TMP/client.log"; then echo "NET TEST: FAIL (the effect sync or fight traffic step did not run or did not pass)"; exit 1; fi
# the prop check (v14): a fake party trap, a door, a floor trap and a lever spoiled on the client were put back by the server's answer
if ! grep -q "\[net\] prop check: the spoiled props were repaired" "$TMP/client.log"; then echo "NET TEST: FAIL (the prop check step did not run or did not pass)"; exit 1; fi
# the enemy check (v14): a ghost creature only the client showed was freed and a creature it lost came back (and a miscounted item was put right)
if ! grep -q "\[net\] enemy check: the ghost creature went" "$TMP/client.log"; then echo "NET TEST: FAIL (the enemy check step did not run or did not pass)"; exit 1; fi
# the floor watchdog (v14): the client lost the FloorChange to floor 2 on purpose, asked for the floor again and built it
if ! grep -q "\[net\] floor watchdog: the lost floor change was asked for and built" "$TMP/client.log"; then echo "NET TEST: FAIL (the floor watchdog did not recover a lost floor change)"; exit 1; fi
if [ "$MODE" = direct ] && ! grep -q "stairs:immediate" "$TMP/server.log"; then echo "NET TEST: FAIL (one player online did not take the stairs at once)"; exit 1; fi
# the elite roster of a first-visit floor is the server's: both sides name the same spawn indices and elite names on floor 2
if [ "$MODE" = direct ]; then
  SERVER_ELITES=$(grep -m1 "\[net-server\] elites on floor 2:" "$TMP/server.log" | sed 's/.*elites on floor 2: //')
  CLIENT_ELITES=$(grep -m1 "\[net\] elites on floor 2:" "$TMP/client.log" | sed 's/.*elites on floor 2: //')
  echo "--- elites on floor 2: server $SERVER_ELITES client $CLIENT_ELITES"
  if [ -z "$SERVER_ELITES" ] || [ "$SERVER_ELITES" != "$CLIENT_ELITES" ]; then echo "NET TEST: FAIL (the client's elite roster differs from the server's)"; exit 1; fi
  if grep -q "elites MISMATCH" "$TMP/client.log"; then echo "NET TEST: FAIL (the client built a roster other than the FloorChange named)"; exit 1; fi
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
