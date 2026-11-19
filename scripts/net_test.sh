#!/usr/bin/env bash
# Two headless Godot processes prove the multiplayer path: a dedicated server and a client that joins it, moves,
# chats and leaves. Each keeps its own user:// (XDG_DATA_HOME) so settings, keys and saves stay apart.
# Usage: scripts/net_test.sh            (direct ws://127.0.0.1:7790)
#        scripts/net_test.sh relay URL  (a client against an already-running relay, e.g. ws://127.0.0.1:10000/ws)
#        START_FLOOR=0 scripts/net_test.sh  (the server opens in the tavern; the client joins, strikes the deal and leaves)
#        RIDE=1 scripts/net_test.sh  (the built-in campaign: the server takes the client to floor 12, where the rides' set
#                                     pieces must stand on the client and its props must hash as the server's; the client
#                                     boards the cart at the landing through its intent, pumps out of it (the server shows
#                                     the pump), brakes on the way and gets out at stop 1 at the server's landing; stop 1's
#                                     holders die by the test's hand and its gate rises on the client; the client takes the
#                                     orc's keys, opens cage 1 and revives a crewman the server downed in it, each through its
#                                     intent; it boards again, rides on (every departure a real one) and throws ride 3's switch
#                                     from the seat, and the cart crosses on both; the client's carts must keep within 1.5 m (2 m
#                                     for ride 3's slag cart) of the server's on a shared server-time grid; the crew sit in their
#                                     wagons on the client as on the server; at stop 3 the client leaves the game seated, and the
#                                     server unseats it quietly beside the lane with its parked pose there; the crew, the gates and the
#                                     set pieces match: the opened wagon couples on the client within 0.5 s of the departure, the downed
#                                     crewman's label counts down there, the beam ducks the client's seated camera, ride 3's buckets pour
#                                     on the server's schedule (0.1 s) and ride 4's hammer falls within 50 ms of the server's;
#                                     a second client, the Joiner, joins as the cart leaves stop 1: within 1 s of its Ready it holds
#                                     the cart (within 1.5 m), the seats, the train and the props' hash as the server has them, then
#                                     boards at stop 2 and rides ride 3 with the first; 2 s into ride 3 it gets out at speed and is
#                                     thrown onto the line behind the cart (it stands at the server's landing); it then asks for the
#                                     floor again and, rebuilt mid-ride 3, holds the ride as the server has it within 1 s again; on foot
#                                     at stop 3 while the train leaves on ride 4 with the first client: stop 3's spare, which offered
#                                     nothing while the train was held there, offers itself once it has gone; the joiner takes it through
#                                     its intent (spare:3), and its chaser runs up behind the train (hopping ride 4's gap and the fallen
#                                     trestle) and halts within 3 m of its last body (chase-done on both sides); the first client leaves
#                                     seated once the chaser has gone; the server then wrecks the cart at the end of the line by a test
#                                     hook, and the joiner sees the Wrecked state and then a spare's takeover; it boards that cart
#                                     and rides ride 3 again with its switch straight, into the ore heap: its rendered S never passes
#                                     the heap's face; both clients' slag carts
#                                     (and their riders) and boarding hounds must match the server's on the grid as well)
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
  # the RIDE pass: run it alone (its set-piece clocks fail on a loaded machine: a slow server's floor clock falls behind its own
  # wall clock). One server on the built-in campaign, one client; floor 12 after the client is ready. A test server quits after
  # 120 s unless told (the pass now rides to the end of the line: about 150 s), and ends anyway once empty. A scripted server deals
  # no objective unless one is named: --objective=jailbreak gives floor 12 the campaign's own (the crew in their cage wagons)
  XDG_DATA_HOME="$TMP/server" run_server --test-net-server --net-test-ride --port=$PORT --bind=127.0.0.1 --campaign=builtin --objective=jailbreak --seed=4242 --lethality=1 --quit-when-empty --quit-after=600 > "$TMP/server.log" 2>&1 &
  SERVER=$!
  for i in $(seq 1 240); do (exec 3<>/dev/tcp/127.0.0.1/$PORT) 2>/dev/null && break; sleep 0.5; done
  # the built-in floor 1 takes a while to build (the socket is not served meanwhile): wait for the server to say it is built
  for i in $(seq 1 180); do grep -q "\[net-server\] floor 1 built" "$TMP/server.log" 2>/dev/null && break; sleep 0.5; done
  sleep 1
  XDG_DATA_HOME="$TMP/client" "$GODOT" --headless --path . -- --test-net-client --net-test-ride --join="$URL" --name=Rider --class=class.warrior --quit-after=480 > "$TMP/client.log" 2>&1 &
  CLIENT=$!
  # the joiner: a second client starts as the cart leaves stop 1, so its Ready falls during ride 2 (it boards at stop 2 and rides ride 3)
  JOINER=""
  for i in $(seq 1 720); do
    grep -q "\[net-server\] ride: the cart departs stop 1 with" "$TMP/server.log" 2>/dev/null && break
    kill -0 $CLIENT 2>/dev/null || break
    sleep 0.5
  done
  if grep -q "\[net-server\] ride: the cart departs stop 1 with" "$TMP/server.log" 2>/dev/null; then
    XDG_DATA_HOME="$TMP/joiner" "$GODOT" --headless --path . -- --test-net-client --net-test-ride --net-test-ride-join --join="$URL" --name=Joiner --class=class.warrior --quit-after=360 > "$TMP/joiner.log" 2>&1 &
    JOINER=$!
  fi
  wait $CLIENT 2>/dev/null
  [ -n "$JOINER" ] && wait $JOINER 2>/dev/null
  for i in $(seq 1 60); do kill -0 $SERVER 2>/dev/null || break; sleep 0.5; done
  kill $SERVER 2>/dev/null; wait $SERVER 2>/dev/null
  echo "--- server"; grep -E "\[net-server\] (ride[:]|RESULT|.* left while seated)|Unhandled|ERROR" "$TMP/server.log" | head -50
  echo "--- client"; grep -E "\[net\] (ride[:]|RESULT|prop check)|Unhandled|ERROR" "$TMP/client.log" | head -50
  echo "--- joiner"; grep -E "\[net\] (ride[:]|RESULT|prop check)|Unhandled|ERROR" "$TMP/joiner.log" 2>/dev/null | head -40
  if ! grep -q "\[net\] RESULT ok" "$TMP/client.log" || ! grep -q "\[net-server\] RESULT ok" "$TMP/server.log"; then echo "NET TEST: FAIL (ride pass)"; exit 1; fi
  # the ride heard where it happens (plan 6 task 5): each client heard the party cart's roll from the synced state, no rail cue came
  # relayed as a plain sfx, and the joiner (who watches the wreck with a spare) heard cart_wreck once
  echo "--- ride sound"; grep -h "\[net\] ride sound" "$TMP/client.log" "$TMP/joiner.log" 2>/dev/null | tail -4
  for f in client joiner; do
    if ! grep -q "\[net\] ride sound roll=on" "$TMP/$f.log" 2>/dev/null || grep -q "\[net\] ride sound .* railsfx=[1-9]" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f's ride sound: no roll heard, or a rail cue relayed)"; exit 1; fi
  done
  if ! grep "\[net\] ride sound" "$TMP/joiner.log" | tail -1 | grep -q "wreck=1"; then echo "NET TEST: FAIL (the joiner did not hear the wreck with a spare once)"; exit 1; fi
  # a joiner mid-ride (task 8): ready during ride 2 on the server; 1 s after its Ready it traced joined-mid-ride ok; it boarded at stop 2
  if ! grep -q "\[net\] RESULT ok" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (the joiner's pass)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner is ready at .*: mid-ride 2" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner was not ready during ride 2)"; exit 1; fi
  if ! grep -q "\[net\] ride: joined-mid-ride ok" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner never traced joined-mid-ride ok)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner boarded at stop 2 through the intent" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner never boarded at stop 2)"; exit 1; fi
  # task 11: the joiner gets out at speed on ride 3 (thrown onto the line, at the server's landing), then has the floor resent mid-ride 3
  # and holds the ride again within 1 s; at the end of the line the server wrecks the cart with a spare left, and the joiner sees both
  if ! grep -q "Joiner jumps from the moving cart" "$TMP/server.log" || ! grep -q "\[net-server\] ride: Joiner got out of the rolling cart on ride 3 .* on the line behind the cart" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner was not thrown from the rolling cart onto the line on ride 3)"; exit 1; fi
  if ! grep -q "\[net\] ride: thrown at speed: the joiner .* on the line behind the cart" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the thrown joiner did not stand at the server's landing on the line)"; exit 1; fi
  if ! grep -q "\[net\] floor 12 (build [0-9]*) rebuilt from the server's resend" "$TMP/joiner.log" || ! grep -q "\[net\] ride: resynced-mid-ride ok" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's resync mid-ride 3 did not rebuild the floor or did not show the ride as it is)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: wreck: .* took over" "$TMP/server.log" || ! grep -q "\[net\] ride: wreck: the cart showed Wrecked here" "$TMP/joiner.log" || ! grep -q "\[net\] ride: wreck-spare:" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the wreck at the end of the line: no spare took over, or the joiner did not see the Wrecked state and then the takeover)"; exit 1; fi
  # then the joiner rides the spare's cart into ride 3 again with its switch straight: the cart halts at the ore heap, and the joiner's
  # rendered S never passes the face (the follower's clamp at BlockedAt) nor steps back while it rolls
  if ! grep -q "\[net-server\] ride: heap: the cart halted at the ore heap on ride 3" "$TMP/server.log" || ! grep -q "\[net\] ride: heap: halted here at S=" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the spare's cart never halted at the ore heap with the joiner watching)"; exit 1; fi
  # the left-behind spare (task 9): the joiner, out at stop 3, sees stop 3's spare offer nothing while the train is held there; the train
  # leaves on ride 4 with the first client; the joiner takes the spare through its intent, and its chaser halts within 3 m of the train
  if ! grep -q "\[net-server\] ride: the cart departs stop 3 with 1 player(s) aboard" "$TMP/server.log"; then echo "NET TEST: FAIL (the train never left stop 3 with the first client aboard and the joiner on foot)"; exit 1; fi
  if ! grep -q "\[net\] ride: beside stop 3's spare .* with the train held at stop 3 it offers \"\" here" "$TMP/joiner.log"; then echo "NET TEST: FAIL (stop 3's spare was not seen to offer nothing while the train was held there)"; exit 1; fi
  if ! grep -q "\[net\] ride: the train left stop 3 without us .* offers \"Take the spare cart\" .* took it through the intent (spare:3)" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner never took stop 3's spare through its intent)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner took spare 3 through the intent" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never placed the joiner's chaser)"; exit 1; fi
  if ! grep -q "\[net\] ride: our chaser halted .* behind the train's last body here" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's chaser never halted behind the train)"; exit 1; fi
  if ! grep -q "\[net\] ride: chaser hud \"Spare cart — after the train\"" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's cart bar did not read the chaser's line while it rolled)"; exit 1; fi
  if ! grep -q "\[net\] ride: chase-done" "$TMP/joiner.log" || ! grep -q "\[net-server\] ride: chase-done: the chaser halted" "$TMP/server.log"; then echo "NET TEST: FAIL (no chase-done on the joiner and the server)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: chase-done: .* it hopped [1-9] hole(s) in the line" "$TMP/server.log"; then echo "NET TEST: FAIL (the chaser never hopped ride 4's gap)"; exit 1; fi
  if ! grep -q "\[net\] ride: .*the joiner's chaser came and went" "$TMP/client.log"; then echo "NET TEST: FAIL (the first client never saw the joiner's chaser come and go)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: floor 12 built with" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never built floor 12's rides)"; exit 1; fi
  # the set pieces stand on the client as on the server, and its props (the rail part too) hash as the server's
  if ! grep -q "\[net\] ride: set pieces on the client:" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's floor 12 set pieces were not checked)"; exit 1; fi
  if ! grep -q "\[net\] ride: the server's thrown switch shows here" "$TMP/client.log"; then echo "NET TEST: FAIL (a switch thrown on the server never showed on the client)"; exit 1; fi
  if ! grep -q "\[net\] ride: prop hash matches the server's" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's floor 12 props differ from the server's)"; exit 1; fi
  # the carts on the wire: both sides print the cart's S on one 0.5 s server-time grid; lines with the same grid t are paired.
  # The party's cart: at least 20 pairs while it rolls, and every pair within 1.5 m. Ride 3's slag cart: paired the same way, within 2 m.
  if ! grep -q "\[net\] ride: the cart followed the server's to stop" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's cart never followed the server's to stop 3)"; exit 1; fi
  # seats on the wire: the crew sit in their wagons on the client as on the server (the catch-up's SeatMsgs); the client's hero, seated
  # on the server as the cart rolls, sits in a hero seat of the client's cart; set down at the next stop, it stands at the server's landing
  SRV_CREW=$(grep -o "\[net-server\] ride: crew seated in wagons: [0-9]*" "$TMP/server.log" | head -1 | grep -o "[0-9]*$")
  CLI_CREW=$(grep -o "\[net\] ride: crew seated in their wagons here: [0-9]*" "$TMP/client.log" | head -1 | grep -o "[0-9]*$")
  if [ -z "$SRV_CREW" ] || [ "$SRV_CREW" != "$CLI_CREW" ]; then echo "NET TEST: FAIL (crew seated in wagons: server ${SRV_CREW:-?}, client ${CLI_CREW:-?})"; exit 1; fi
  if ! grep -q "\[net\] ride: seated here in seat [0-3] (the party's cart)" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's hero never sat in a hero seat of its cart)"; exit 1; fi
  if ! grep -q "\[net\] ride: set down within" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's hero was not set down at the server's landing)"; exit 1; fi
  # riding: the client boards through its intent (the server traces aboard:), pumps out of the landing (the server's pumpers and the
  # snapshot's Pumping on the client), brakes (the server's V drops), gets out at stop 1, and leaves seated at stop 3 (a quiet unseat)
  if ! grep -q "\[net\] ride: the cart offers \"Board the cart\" here at the landing" "$TMP/client.log"; then echo "NET TEST: FAIL (the client's cart never offered to board)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Rider boarded through the intent" "$TMP/server.log"; then echo "NET TEST: FAIL (the client never boarded through its intent)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the cart left the landing with [1-9] pumper(s)" "$TMP/server.log"; then echo "NET TEST: FAIL (the client's pump did not reach the server's cart)"; exit 1; fi
  if ! grep -q "\[net\] ride: the server shows our hero pumping" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never saw the server's Pumping on its hero)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the client's brake took V from .* (ok)" "$TMP/server.log"; then echo "NET TEST: FAIL (the client's brake did not slow the server's cart)"; exit 1; fi
  if ! grep -q "\[net\] ride: at stop 1; asked to get out through the intent" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never got out at stop 1)"; exit 1; fi
  if ! grep -q "\[net-server\] Rider left while seated: unseated quietly" "$TMP/server.log"; then echo "NET TEST: FAIL (the client left seated but was not unseated quietly)"; exit 1; fi
  if grep -q "Rider jumps from the moving cart" "$TMP/server.log"; then echo "NET TEST: FAIL (the client was thrown from the cart)"; exit 1; fi
  # the props: stop 1's gate rises on the client (its PropEvt); the keys (condemned branch), cage 1 and a revive through the client's
  # intents; every departure from stops 1 and 2 a real one with the client aboard; ride 3's switch thrown from the seat, crossed on both
  if ! grep -q "\[net\] ride: gate 1 is up here (its PropEvt)" "$TMP/client.log"; then echo "NET TEST: FAIL (stop 1's gate never rose on the client)"; exit 1; fi
  if grep -q "\[net-server\] ride: stop 1's .* keyring: at Grisel's belt" "$TMP/server.log" && ! grep -q "\[net\] ride: took the orc's keys through the intent" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never took the orc's keys)"; exit 1; fi
  if ! grep -q "\[net\] ride: opened cage 1 through the intent" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never opened cage 1)"; exit 1; fi
  if ! grep -q "\[net\] ride: revived .* through the intent" "$TMP/client.log" || ! grep -q "\[net-server\] ride: .* is up again" "$TMP/server.log"; then echo "NET TEST: FAIL (the downed crewman was not revived through the client's intent)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the cart departs stop 1 with 1 player(s) aboard" "$TMP/server.log"; then echo "NET TEST: FAIL (the cart never left stop 1 for real with the client aboard)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the cart departs stop 2 with 2 player(s) aboard" "$TMP/server.log"; then echo "NET TEST: FAIL (the cart never left stop 2 for real with both clients aboard)"; exit 1; fi
  if ! grep -q "\[net\] ride: ride 3's lever offers \"Throw the switch\" .* thrown through the intent" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never threw ride 3's switch from the seat)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the cart takes ride 3's switch .* thrown through the intent" "$TMP/server.log" || ! grep -q "\[net\] ride: the cart crosses here (CrossOut set" "$TMP/client.log"; then echo "NET TEST: FAIL (ride 3's switch thrown by the client did not send the cart across on both)"; exit 1; fi
  # crew, gates and set pieces (task 7): the opened wagon couples here within 0.5 s of stop 1's departure (the cart's Train), the downed
  # crewman's label counts down here from the server's objective state, and the beam ducks our seated camera as our own cart's front passes
  if ! grep -q "\[net\] ride: the cage wagon coupled here .* after the cart left stop 1" "$TMP/client.log"; then echo "NET TEST: FAIL (the coupled wagon never showed on the client at stop 1's departure)"; exit 1; fi
  if ! grep -q "\[net\] ride: .*'s label counts down here" "$TMP/client.log"; then echo "NET TEST: FAIL (the downed crewman's label did not count down on the client)"; exit 1; fi
  if ! grep -q "\[net\] ride: the beam .* our seated camera ducks" "$TMP/client.log"; then echo "NET TEST: FAIL (the beam never ducked the client's seated camera)"; exit 1; fi
  # the set pieces' clocks: ride 4's first hammer enters its fall on both sides (server time; every client fall paired with the nearest
  # server fall within 1 s, all within 50 ms, at least 10); ride 3's buckets pour on both sides from the ride's arm time (by bucket, within 0.1 s)
  CLOCKS=$(awk '
    FNR == 1 { file++ }
    function val(k,   i) { for (i = 1; i <= NF; i++) if (index($i, k "=") == 1) return substr($i, length(k) + 2); return "" }
    file == 1 && /^\[net-server\] ride hammer=/ { sh[++nsh] = val("t") }
    file == 2 && /^\[net\] ride hammer=/ { ch[++nch] = val("t") }
    file == 1 && /^\[net-server\] ride bucket pour / { if (!(val("n") in sb)) sb[val("n")] = val("t") }
    file == 2 && /^\[net\] ride bucket pour / { if (!(val("n") in cb)) cb[val("n")] = val("t") }
    END {
      hp = 0; hworst = 0; hover = 0
      for (i = 1; i <= nch; i++) {
        best = 99
        for (j = 1; j <= nsh; j++) { d = ch[i] - sh[j]; if (d < 0) d = -d; if (d < best) best = d }
        if (best > 1) continue
        hp++; if (best > hworst) hworst = best
        if (best > 0.05) { hover++; print "  hammer fall over 50 ms: client t=" ch[i] " (" best " s from the server s nearest)" > "/dev/stderr" }
      }
      bp = 0; bworst = 0; bover = 0
      for (n in cb) if (n in sb) {
        d = cb[n] - sb[n]; if (d < 0) d = -d
        bp++; if (d > bworst) bworst = d
        if (d > 0.1) { bover++; print "  bucket pour over 0.1 s: n=" n " server " sb[n] " client " cb[n] > "/dev/stderr" }
      }
      printf "hammer falls paired=%d (server %d, client %d) max=%.3f over=%d; bucket pours paired=%d max=%.3f over=%d\n", hp, nsh, nch, hworst, hover, bp, bworst, bover
      exit !(hp >= 10 && hover == 0 && bp >= 3 && bover == 0)
    }' "$TMP/server.log" "$TMP/client.log")
  CLOCKS_RC=$?
  echo "--- set-piece clocks, client vs server: $CLOCKS"
  if [ $CLOCKS_RC -ne 0 ]; then echo "NET TEST: FAIL (the client's set pieces did not keep the server's clocks: $CLOCKS)"; exit 1; fi
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
  # the joiner and the first client against the server on the grid: their seats (both carts' books, seat by seat), the train and the
  # props' hash must match the server's within 1 s of the joiner's Ready (with its cart within 1.5 m), the joiner's cart pairs all within
  # 1.5 m, and after that no book differs except next to a change on the server (a grid point whose neighbours both agree with it)
  BOOKS=$(awk '
    FNR == 1 { file++ }
    function val(k,   i) { for (i = 1; i <= NF; i++) if (index($i, k "=") == 1) return substr($i, length(k) + 2); return "" }
    function key(t) { return sprintf("%.1f", t) }
    file == 1 && /^\[net-server\] ride t=/ { srv[val("t")] = val("S") }
    file == 1 && /^\[net-server\] ride book / { sb[val("t")] = val("seats") " " val("train") " " val("props"); sc[val("t")] = val("carts") " " val("boarders") }
    file == 2 && /^\[net\] ride book / { cb[val("t")] = val("seats") " " val("train") " " val("props"); cc[val("t")] = val("carts") " " val("boarders") }
    file == 3 && /^\[net\] ride: joined mid-ride; ready at t=/ { split($0, a, "ready at t="); split(a[2], b, " "); ready = b[1] + 0 }
    file == 3 && /^\[net\] ride: resynced; ready at t=/ { split($0, a, "ready at t="); split(a[2], b, " "); rready = b[1] + 0 }
    file == 3 && /^\[net\] ride t=/ { js[val("t")] = val("S") }
    file == 3 && /^\[net\] ride book / { n3++; jt[n3] = val("t"); jnow[n3] = val("now") + 0; jb[val("t")] = val("seats") " " val("train") " " val("props"); jc[val("t")] = val("carts") " " val("boarders") }
    function stable(t) { return (key(t - 0.5) in sb) && (key(t + 0.5) in sb) && sb[key(t - 0.5)] == sb[key(t)] && sb[key(t + 0.5)] == sb[key(t)] }
    function cstable(t) { return (key(t - 0.5) in sc) && (key(t + 0.5) in sc) && sc[key(t - 0.5)] == sc[key(t)] && sc[key(t + 0.5)] == sc[key(t)] }
    # the first grid point at or after a Ready (the join, the resync) where the joiner holds the seats, train and props of the server and its cart within 1.5 m
    function matched(from,   i, t, d) {
      for (i = 1; i <= n3; i++) {
        t = jt[i]; if (jnow[i] < from) continue
        if (!(t in sb) || !(t in srv) || !(t in js)) continue
        d = js[t] - srv[t]; if (d < 0) d = -d
        if (jb[t] == sb[t] && d <= 1.5) { mt = t; return jnow[i] - from }
      }
      return -1
    }
    # a slag cart (a "Slag[" word) and a living boarder (a hound id other than -1 in P[...] or W<n>[...]) in a carts-and-boarders word
    function hasslag(w) { return index(w, "Slag[") > 0 }
    function hasboarder(w,   p, n, b) { n = split(w, p, " "); b = p[n]; gsub(/W[0-9]+\[/, "[", b); gsub(/-1/, "", b); return b ~ /[0-9]/ }
    END {
      match_at = matched(ready); match_t = mt
      rmatch_at = -1; rmatch_t = ""
      if (rready > 0) { rmatch_at = matched(rready); rmatch_t = mt }
      jp = 0; jover = 0; jworst = 0
      for (t in js) if (t in srv) { d = js[t] - srv[t]; if (d < 0) d = -d; jp++; if (d > jworst) jworst = d; if (d > 1.5) { jover++; print "  joiner pair over 1.5 m: t=" t " server " srv[t] " joiner " js[t] > "/dev/stderr" } }
      jm = 0; jbad = 0
      for (t in jb) if ((t in sb) && match_at >= 0 && t + 0 >= match_t + 0) { jm++; if (jb[t] != sb[t] && stable(t + 0)) { jbad++; print "  joiner book differs: t=" t " server " sb[t] " joiner " jb[t] > "/dev/stderr" } }
      cm = 0; cbad = 0
      for (t in cb) if (t in sb) { cm++; if (cb[t] != sb[t] && stable(t + 0)) { cbad++; print "  client book differs: t=" t " server " sb[t] " client " cb[t] > "/dev/stderr" } }
      # the other carts (their riders) and the boarders: the first client all along; the joiner from its match on, except in the second after a Ready
      ccm = 0; ccbad = 0; ccslag = 0; ccboard = 0
      for (t in cc) if ((t in sc) && cstable(t + 0)) { ccm++; if (hasslag(sc[t])) ccslag++; if (hasboarder(sc[t])) ccboard++; if (cc[t] != sc[t]) { ccbad++; print "  client carts/boarders differ: t=" t " server " sc[t] " client " cc[t] > "/dev/stderr" } }
      jcm = 0; jcbad = 0; jcslag = 0; jcboard = 0
      for (i = 1; i <= n3; i++) {
        t = jt[i]; if (!(t in jc) || !(t in sc) || match_at < 0 || t + 0 < match_t + 0) continue
        if (jnow[i] - ready < 1.0 || (rready > 0 && jnow[i] >= rready && jnow[i] - rready < 1.0) || !cstable(t + 0)) continue
        jcm++; if (hasslag(sc[t])) jcslag++; if (hasboarder(sc[t])) jcboard++
        if (jc[t] != sc[t]) { jcbad++; print "  joiner carts/boarders differ: t=" t " server " sc[t] " joiner " jc[t] > "/dev/stderr" }
      }
      printf "joiner ready t=%.3f, matched the server (seats, train, props, cart within 1.5 m) %.3f s after its Ready (grid t=%s); resynced t=%.3f, matched %.3f s after that Ready (grid t=%s); joiner cart pairs=%d max=%.3f over=%d; books compared: joiner %d (%d differ), client %d (%d differ); carts and boarders compared: joiner %d (%d with a slag cart, %d with a boarder; %d differ), client %d (%d, %d; %d differ)\n", ready, match_at, match_t, rready, rmatch_at, rmatch_t, jp, jworst, jover, jm, jbad, cm, cbad, jcm, jcslag, jcboard, jcbad, ccm, ccslag, ccboard, ccbad
      exit !(ready > 0 && match_at >= 0 && match_at <= 1.0 && rready > 0 && rmatch_at >= 0 && rmatch_at <= 1.0 && jp >= 10 && jover == 0 && jm >= 10 && jbad == 0 && cm >= 20 && cbad == 0 && jcm >= 10 && jcslag >= 1 && jcbad == 0 && ccm >= 20 && ccslag >= 1 && ccbad == 0)
    }' "$TMP/server.log" "$TMP/client.log" "$TMP/joiner.log")
  BOOKS_RC=$?
  echo "--- the joiner and the books, client vs server: $BOOKS"
  if [ $BOOKS_RC -ne 0 ]; then echo "NET TEST: FAIL (the joiner or the client did not see the ride as the server has it: $BOOKS)"; exit 1; fi
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
