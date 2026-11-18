#!/usr/bin/env bash
# Two headless Godot processes prove the multiplayer path: a dedicated server and a client that joins it, moves,
# chats and leaves. Each keeps its own user:// (XDG_DATA_HOME) so settings, keys and saves stay apart.
# Usage: scripts/net_test.sh            (direct ws://127.0.0.1:7790)
#        scripts/net_test.sh relay URL  (a client against an already-running relay, e.g. ws://127.0.0.1:10000/ws)
#        START_FLOOR=0 scripts/net_test.sh  (the server opens in the tavern; the client joins, strikes the deal and leaves)
#        RIDE=1 scripts/net_test.sh  (the built-in campaign: the server takes the client to floor 12, where the rides' set
#                                     pieces must stand on the client and its props must hash as the server's; then the cart
#                                     rides empty to stop 3 and the client's carts must keep within 1.5 m (2 m for ride 3's
#                                     slag cart) of the server's on a shared server-time grid)
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

if [ "${RIDE:-}" = 1 ]; then
  # the RIDE pass: one server on the built-in campaign, one client; floor 12 after the client is ready
  XDG_DATA_HOME="$TMP/server" run_server --test-net-server --net-test-ride --port=$PORT --bind=127.0.0.1 --campaign=builtin --seed=4242 --lethality=1 --quit-when-empty > "$TMP/server.log" 2>&1 &
  SERVER=$!
  for i in $(seq 1 240); do (exec 3<>/dev/tcp/127.0.0.1/$PORT) 2>/dev/null && break; sleep 0.5; done
  # the built-in floor 1 takes a while to build (the socket is not served meanwhile): wait for the server to say it is built
  for i in $(seq 1 180); do grep -q "\[net-server\] floor 1 built" "$TMP/server.log" 2>/dev/null && break; sleep 0.5; done
  sleep 1
  XDG_DATA_HOME="$TMP/client" "$GODOT" --headless --path . -- --test-net-client --net-test-ride --join="$URL" --name=Rider --class=class.warrior --quit-after=360 > "$TMP/client.log" 2>&1
  for i in $(seq 1 60); do kill -0 $SERVER 2>/dev/null || break; sleep 0.5; done
  kill $SERVER 2>/dev/null; wait $SERVER 2>/dev/null
  echo "--- server"; grep -E "\[net-server\] (ride[:]|RESULT)|Unhandled|ERROR" "$TMP/server.log" | head -20
  echo "--- client"; grep -E "\[net\] (ride[:]|RESULT|prop check)|Unhandled|ERROR" "$TMP/client.log" | head -20
  if ! grep -q "\[net\] RESULT ok" "$TMP/client.log" || ! grep -q "\[net-server\] RESULT ok" "$TMP/server.log"; then echo "NET TEST: FAIL (ride pass)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: floor 12 built with" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never built floor 12's rides)"; exit 1; fi
  # the set pieces stand on the client as on the server, and its props (the rail part too) hash as the server's
  if ! grep -q "\[net\] ride: set pieces on the client:" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's floor 12 set pieces were not checked)"; exit 1; fi
  if ! grep -q "\[net\] ride: the server's thrown switch shows here" "$TMP/client.log"; then echo "NET TEST: FAIL (a switch thrown on the server never showed on the client)"; exit 1; fi
  if ! grep -q "\[net\] ride: prop hash matches the server's" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's floor 12 props differ from the server's)"; exit 1; fi
  # the carts on the wire: both sides print the cart's S on one 0.5 s server-time grid; lines with the same grid t are paired.
  # The party's cart: at least 20 pairs while it rolls, and every pair within 1.5 m. Ride 3's slag cart: paired the same way, within 2 m.
  if ! grep -q "\[net\] ride: the cart followed the server's to stop" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's cart never followed the server's to stop 3)"; exit 1; fi
  PAIRS=$(awk '
    FNR == 1 { file++ }
    function val(k,   i) { for (i = 1; i <= NF; i++) if (index($i, k "=") == 1) return substr($i, length(k) + 2); return "" }
    file == 1 && /^\[net-server\] ride t=/ { srv[val("t")] = val("S"); mode[val("t")] = val("mode") }
    file == 1 && /^\[net-server\] ride cart=/ { if (val("kind") == "Slag" && val("armed") == "3") slag[val("cart") "@" val("t")] = val("S") }
    file == 2 && /^\[net\] ride t=/ { cli[val("t")] = val("S") }
    file == 2 && /^\[net\] ride cart=/ { cslag[val("cart") "@" val("t")] = val("S") }
    END {
      n = 0; rolling = 0; worst = 0; sum = 0; over = 0
      for (t in cli) if (t in srv) {
        d = cli[t] - srv[t]; if (d < 0) d = -d
        n++; sum += d; if (d > worst) worst = d; if (d > 1.5) { over++; print "  party pair over 1.5 m: t=" t " server " srv[t] " client " cli[t] > "/dev/stderr" }
        if (mode[t] == "Rolling") rolling++
      }
      sn = 0; sworst = 0; sover = 0
      for (k in cslag) if (k in slag) {
        d = cslag[k] - slag[k]; if (d < 0) d = -d
        sn++; if (d > sworst) sworst = d; if (d > 2) { sover++; print "  slag pair over 2 m: " k " server " slag[k] " client " cslag[k] > "/dev/stderr" }
      }
      printf "party pairs=%d rolling=%d mean=%.3f max=%.3f over=%d; ride-3 slag pairs=%d max=%.3f over=%d\n", n, rolling, (n ? sum / n : 0), worst, over, sn, sworst, sover
      exit !(rolling >= 20 && over == 0 && sn >= 1 && sover == 0)
    }' "$TMP/server.log" "$TMP/client.log")
  PAIRS_RC=$?
  echo "--- cart S, client vs server: $PAIRS"
  if [ $PAIRS_RC -ne 0 ]; then echo "NET TEST: FAIL (the client's carts did not keep to the server's: $PAIRS)"; exit 1; fi
  echo "NET TEST: ok"; exit 0
fi

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
