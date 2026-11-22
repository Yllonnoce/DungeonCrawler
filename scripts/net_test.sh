#!/usr/bin/env bash
# Two headless Godot processes prove the multiplayer path: a dedicated server and a client that joins it, moves,
# chats and leaves. Each keeps its own user:// (XDG_DATA_HOME) so settings, keys and saves stay apart.
# Usage: scripts/net_test.sh            (direct ws://127.0.0.1:7790)
#        scripts/net_test.sh relay URL  (a client against an already-running relay, e.g. ws://127.0.0.1:10000/ws)
#        START_FLOOR=0 scripts/net_test.sh  (the server opens in the tavern; the client joins, strikes the deal and leaves)
#        RIDE=1 scripts/net_test.sh  (the built-in campaign: the server takes the client to floor 12, where the rides' set
#                                     pieces must stand on the client and its props must hash as the server's; the client
#                                     boards the cart at the landing through its intent, pumps out of it (the server shows
#                                     the pump), brakes on the way, brakes again for ride 1's barricade and cuts it with
#                                     its blade from the seat, and gets out at stop 1 at the server's landing; stop 1's
#                                     holders die by the test's hand and its gate rises on the client; the client takes the
#                                     orc's keys, opens cage 1 and revives a crewman the server downed in it, each through its
#                                     intent; it boards again, rides on (every departure a real one) and throws ride 3's switch
#                                     from the seat through the seated window (beyond plain reach, no aim; the joiner's quick
#                                     set-back is refused), and the cart crosses on both; ride 2's barricade is smashed (the client sees
#                                     the plating drop 25); across, the client throws ride 3's live switch back from the seat and both
#                                     clients see the late CrossBack bring the cart home (nothing runs off the broken bridge); pressed once more
#                                     from the seat after that, the taken back is ignored quietly (never "Too late to throw it."); on ride 4
#                                     both clients' dark stretch (its start, its lamps out) is the server's, both show the runaway behind the
#                                     train (its pairs within 2 m of the server's), the joiner's seat press in the server's early lead is held
#                                     to the points and thrown as the train passes them straight, the first client's press on its first
#                                     frame past the points leaves it thrown, the runaway takes the spur into the crevasse on every peer and
#                                     nobody shows a ram; in ride 4's cavern (plan 13) the bridge goes behind the train and both clients
#                                     show it all gone within 10 s of stop 4, the joiner's brake held across its call never counts (no
#                                     take), the first client leaps onto r4_slag's deck on its first frame offered and back on its first
#                                     frame after the slag line's call, the joiner leaps across too and back on its last offered frames
#                                     (taken, never a fall), both see both seated on the deck, r4_slag goes over the drop with nobody on it,
#                                     nobody is shown over a gone cell or seated on a freed copy, no "Leap across" shows after the call,
#                                     and both heroes sit in the party's cart at stop 4;
#                                     the client's carts must keep within 1.5 m (2 m
#                                     for ride 3's slag cart) of the server's on a shared server-time grid; the crew sit in their
#                                     wagons on the client as on the server; at the end of the line the client leaves the game seated, and the
#                                     server unseats it quietly beside the lane with its parked pose there; the crew, the gates and the
#                                     set pieces match: the opened wagon couples on the client within 0.5 s of the departure, the downed
#                                     crewman's label counts down there, the beam ducks the client's seated camera, ride 3's buckets pour
#                                     on the server's schedule (0.1 s) and the kiln's (ride 5's) hammer falls within 50 ms of the server's;
#                                     a second client, the Joiner, joins as the cart leaves stop 1: within 1 s of its Ready it holds
#                                     the cart (within 1.5 m), the seats, the train and the props' hash as the server has them, then
#                                     boards at stop 2 and rides ride 3 with the first; 2 s into ride 3 it gets out at speed and is
#                                     thrown onto the line behind the cart (it stands at the server's landing); it then asks for the
#                                     floor again and, rebuilt mid-ride 3, holds the ride as the server has it within 1 s again; put
#                                     beside the cart at stop 3 it boards again and rides ride 4 seated with the first client (every
#                                     departure a real one); out at stop 4, the kiln's, on foot while the train leaves on ride 5 with the
#                                     first client: stop 4's spare, which offered nothing while the train was held there, offers itself
#                                     once it has gone; the joiner takes it through its intent (spare:4), and its chaser runs up behind
#                                     the train (hopping ride 5's gap and the fallen
#                                     trestle) and halts within 3 m of its last body (chase-done on both sides; no relief cart was sent online); the first client leaves
#                                     seated once the chaser has gone; the server then wrecks the cart at the end of the line by a test
#                                     hook, and the joiner sees the Wrecked state and then a spare's takeover (stop 3's; wrecked again
#                                     3 s later, stop 2's, where ride 3 with the heap leaves; only the first is in earshot); it boards that cart
#                                     and rides ride 3 again with its switch straight, into the ore heap: its rendered S never passes
#                                     the heap's face; out of it, the server wrecks the cart until no spare is
#                                     left (the joiner hears that wreck live), and the joiner asks for the floor
#                                     again: its first cart state carries Wrecked and must play nothing there
#                                     (no wreck shown, no cart_wreck, no tumble); then it walks its own hero off the lip of a
#                                     chasm on its own input: the server judges the fall ([crevasse] Joiner fell) and the joiner's
#                                     sheet shows Fallen; the rest of the party is given to the crevasse, and the wipe rises beside
#                                     a fresh cart with the joiner up and set down there by the server's ForcePose, its props hashing as
#                                     the server's with the bridge and the drop standing; then the joiner, alone, rides ride 4 again from
#                                     stop 3 (a test restore), pumped with the split straight: the runaway goes down with the bridge, the
#                                     joiner brakes past the call's reaction time, the bridge takes the train, seen falling, and after the
#                                     rise the joiner's current camera is its own hero's; both clients' slag carts
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
  # 120 s unless told (the pass rides the five rides to the end of the line), and ends anyway once empty. A scripted server deals
  # no objective unless one is named: --objective=jailbreak gives floor 12 the campaign's own (the crew in their cage wagons)
  # preflight: under the ACPI low-power profile the cores clamp to about 600 MHz once the third godot starts (ride 2), and the
  # server's physics falls behind its clock: the clock and pairs checks then fail for the machine, not the code (warn only)
  PROFILE_FILE=/sys/firmware/acpi/platform_profile
  if [ -r "$PROFILE_FILE" ] && [ "$(cat "$PROFILE_FILE" 2>/dev/null)" = low-power ]; then
    echo "WARNING: $PROFILE_FILE reads low-power: the RIDE timing checks (set-piece clocks, party pairs) are unreliable in low-power."
    echo "WARNING: switch to balanced or performance first (Fn+Q, or: echo balanced | sudo tee $PROFILE_FILE), and run this alone."
  fi
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
    XDG_DATA_HOME="$TMP/joiner" "$GODOT" --headless --path . -- --test-net-client --net-test-ride --net-test-ride-join --join="$URL" --name=Joiner --class=class.warrior --quit-after=480 > "$TMP/joiner.log" 2>&1 &
    JOINER=$!
  fi
  wait $CLIENT 2>/dev/null
  [ -n "$JOINER" ] && wait $JOINER 2>/dev/null
  for i in $(seq 1 60); do kill -0 $SERVER 2>/dev/null || break; sleep 0.5; done
  kill $SERVER 2>/dev/null; wait $SERVER 2>/dev/null
  echo "--- server"; grep -E "\[net-server\] (ride[:]|RESULT|.* left while seated)|Unhandled|ERROR" "$TMP/server.log" | head -50
  echo "--- client"; grep -E "\[net\] (ride[:]|RESULT|prop check)|Unhandled|ERROR" "$TMP/client.log" | head -50
  echo "--- joiner"; grep -E "\[net\] (ride[:]|RESULT|prop check)|Unhandled|ERROR" "$TMP/joiner.log" 2>/dev/null | head -60
  # the wipe-rise watch (the server's copy of the joiner beside the cart every tick from the rebuild until it leaves): its line is past the
  # server section's head -50, so it is printed here, and a watch that failed names itself in the ride pass's failure
  RISE_WATCH=$(grep -m1 "\[net-server\] ride: wipe-rise watch: " "$TMP/server.log")
  echo "--- wipe-rise watch: ${RISE_WATCH:-not printed}"
  RISE_WHY=""; case "$RISE_WATCH" in *"(FAIL)") RISE_WHY="; $RISE_WATCH" ;; esac
  if ! grep -q "\[net\] RESULT ok" "$TMP/client.log" || ! grep -q "\[net-server\] RESULT ok" "$TMP/server.log"; then echo "NET TEST: FAIL (ride pass$RISE_WHY)"; exit 1; fi
  # the ride heard where it happens (plan 6 task 5): each client heard the party cart's roll from the synced state, no rail cue came
  # relayed as a plain sfx, and the joiner (who watches the wreck with a spare at the end of the line) heard cart_wreck once: the
  # wreck 3 s later at stop 3 (on to stop 2's spare, for the heap) is shown to it but stands far out of a wreck's 30 m earshot
  echo "--- ride sound"; grep -h "\[net\] ride sound" "$TMP/client.log" "$TMP/joiner.log" 2>/dev/null | tail -4
  for f in client joiner; do
    if ! grep -q "\[net\] ride sound roll=on" "$TMP/$f.log" 2>/dev/null || grep -q "\[net\] ride sound .* railsfx=[1-9]" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f's ride sound: no roll heard, or a rail cue relayed)"; exit 1; fi
  done
  if ! grep "\[net\] ride sound" "$TMP/joiner.log" | tail -1 | grep -q "wreck=1"; then echo "NET TEST: FAIL (the joiner did not hear the wreck with a spare beside it once)"; exit 1; fi
  # a joiner mid-ride (task 8): ready during ride 2 on the server; 1 s after its Ready it traced joined-mid-ride ok; it boarded at stop 2
  if ! grep -q "\[net\] RESULT ok" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (the joiner's pass: $(grep -o "\[net\] RESULT FAIL: .*" "$TMP/joiner.log" 2>/dev/null | head -1 | cut -c1-400))"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner is ready at .*: mid-ride 2" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner was not ready during ride 2)"; exit 1; fi
  if ! grep -q "\[net\] ride: joined-mid-ride ok" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner never traced joined-mid-ride ok)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner boarded at stop 2 through the intent" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner never boarded at stop 2)"; exit 1; fi
  # task 11: the joiner gets out at speed on ride 3 (thrown onto the line, at the server's landing), then has the floor resent mid-ride 3
  # and holds the ride again within 1 s; at the end of the line the server wrecks the cart with a spare left (again, until stop 2's spare
  # stands), and the joiner sees both
  if ! grep -q "Joiner jumps from the moving cart" "$TMP/server.log" || ! grep -q "\[net-server\] ride: Joiner got out of the rolling cart on ride 3 .* on the line behind the cart" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner was not thrown from the rolling cart onto the line on ride 3)"; exit 1; fi
  if ! grep -q "\[net\] ride: thrown at speed: the joiner .* on the line behind the cart" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the thrown joiner did not stand at the server's landing on the line)"; exit 1; fi
  if ! grep -q "\[net\] floor 12 (build [0-9]*) rebuilt from the server's resend" "$TMP/joiner.log" || ! grep -q "\[net\] ride: resynced-mid-ride ok" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's resync mid-ride 3 did not rebuild the floor or did not show the ride as it is)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: wreck: .* took over" "$TMP/server.log" || grep -q "\[net-server\] ride: wreck: .* did NOT take over" "$TMP/server.log" || ! grep -q "\[net\] ride: wreck: the cart showed Wrecked here" "$TMP/joiner.log" || ! grep -q "\[net\] ride: wreck-spare:" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the wreck at the end of the line: no spare took over, or the joiner did not see the Wrecked state and then the takeover)"; exit 1; fi
  # then the joiner rides the spare's cart into ride 3 again with its switch straight: the cart halts at the ore heap, and the joiner's
  # rendered S never passes the face (the follower's clamp at BlockedAt) nor steps back while it rolls
  if ! grep -q "\[net-server\] ride: heap: the cart halted at the ore heap on ride 3" "$TMP/server.log" || ! grep -q "\[net\] ride: heap: halted here at S=" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the spare's cart never halted at the ore heap with the joiner watching)"; exit 1; fi
  # the last wreck (plan 6 final review): the server wrecks the cart until no spare is left; the joiner hears it live, then has the floor
  # resent: a catch-up of a standing wreck shows no wreck, plays no cart_wreck and throws no tumble
  if ! grep -q "\[net-server\] ride: last wreck: the test wrecked the cart [0-9]* time(s) until no spare was left" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never wrecked the cart until no spare was left)"; exit 1; fi
  if ! grep -q "\[net\] ride: last wreck: the cart stands wrecked and hidden here .* cart_wreck played [1-9]" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner did not hear the last wreck live)"; exit 1; fi
  if ! grep -q "\[net\] ride: last-wreck-resync ok: .* wreck=0 shown, cart_wreck played 0 time(s), 0 tumble(s)" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's catch-up of the standing wreck played a wreck: $(grep -o 'last-wreck-resync.*' "$TMP/joiner.log" | head -1))"; exit 1; fi
  # plan 9: put beside the cart at stop 3, the thrown joiner boards again and both clients ride ride 4 seated (a real departure)
  if ! grep -q "\[net-server\] ride: Joiner boarded again at stop 3 through the intent" "$TMP/server.log" || ! grep -q "\[net-server\] ride: the cart departs stop 3 with 2 player(s) aboard" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner never boarded again at stop 3, or the cart never left stop 3 with both clients aboard)"; exit 1; fi
  # the left-behind spare (task 9): the joiner, out at stop 4 (the kiln's), sees stop 4's spare offer nothing while the train is held there;
  # the train leaves on ride 5 with the first client; the joiner takes the spare through its intent, and its chaser halts within 3 m of the train
  if ! grep -q "\[net-server\] ride: the cart departs stop 4 with 1 player(s) aboard" "$TMP/server.log"; then echo "NET TEST: FAIL (the train never left stop 4 with the first client aboard and the joiner on foot)"; exit 1; fi
  if ! grep -q "\[net\] ride: beside stop 4's spare .* with the train held at stop 4 it offers \"\" here" "$TMP/joiner.log"; then echo "NET TEST: FAIL (stop 4's spare was not seen to offer nothing while the train was held there)"; exit 1; fi
  if ! grep -q "\[net\] ride: the train left stop 4 without us .* offers \"Take the spare cart\" .* took it through the intent (spare:4)" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner never took stop 4's spare through its intent)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: Joiner took spare 4 through the intent" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never placed the joiner's chaser)"; exit 1; fi
  if ! grep -q "\[net\] ride: our chaser halted .* behind the train's last body here" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's chaser never halted behind the train)"; exit 1; fi
  if ! grep -q "\[net\] ride: chaser hud \"Spare cart — after the train\"" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner's cart bar did not read the chaser's line while it rolled)"; exit 1; fi
  if ! grep -q "\[net\] ride: chase-done" "$TMP/joiner.log" || ! grep -q "\[net-server\] ride: chase-done: the chaser halted" "$TMP/server.log"; then echo "NET TEST: FAIL (no chase-done on the joiner and the server)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: chase-done: .* it hopped [1-9] hole(s) in the line" "$TMP/server.log"; then echo "NET TEST: FAIL (the chaser never hopped ride 5's gap)"; exit 1; fi
  grep -q "\[net-server\] ride: relief carts sent online: 0" "$TMP/server.log" || { echo "NET TEST: FAIL (a relief cart was sent online)"; exit 1; }
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
  # snapshot's Pumping on the client), brakes (the server's V drops), gets out at stop 1, and leaves seated at the end of the line (a quiet unseat)
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
  # the cage hold (plan 7): at stop 1 (cage 1 locked) the seat offers the hold and a tap still gets out; stops 2 to 4 are left with
  # their cages locked through the held intent (hold:cart:0), stop 3's and stop 4's (the kiln's) by the first client
  if ! grep -q "\[net\] ride: at stop 1 the cart offers \"Get out (hold to leave the prisoners behind)\" here (cage 1 locked)" "$TMP/client.log"; then echo "NET TEST: FAIL (at stop 1 the client's seat did not offer the hold with cage 1 locked)"; exit 1; fi
  if ! grep -hq "\[net\] ride: left the prisoners at stop 2 behind through the intent" "$TMP/client.log" "$TMP/joiner.log"; then echo "NET TEST: FAIL (no client left stop 2's prisoners behind through the held intent)"; exit 1; fi
  if ! grep -q "\[rails\] the prisoners at stop 2 are left behind (" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never left stop 2's prisoners behind)"; exit 1; fi
  if ! grep -q "\[rails\] the prisoners at stop 3 are left behind (Rider)" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never left stop 3's prisoners behind for the first client)"; exit 1; fi
  if ! grep -q "\[rails\] the prisoners at stop 4 are left behind (Rider)" "$TMP/server.log"; then echo "NET TEST: FAIL (the server never left stop 4's prisoners behind for the first client)"; exit 1; fi
  if ! grep -q "\[net\] ride: ride 3's lever offers \"Throw the switch\" in the seated window .* thrown through the intent from the seat" "$TMP/client.log"; then echo "NET TEST: FAIL (the client never threw ride 3's switch from the seat)"; exit 1; fi
  if ! grep -q "\[net-server\] interact switch:[0-9]* from Rider: .* seated window yes" "$TMP/server.log"; then echo "NET TEST: FAIL (the server did not accept ride 3's switch through the seated window)"; exit 1; fi
  if ! grep -q "\[net\] ride: the joiner presses \"Set the switch back\" from the seat" "$TMP/joiner.log"; then echo "NET TEST: FAIL (the joiner never pressed set-back from the seat)"; exit 1; fi
  if ! grep -q "\[net-server\] interact switch:[0-9]* from .*: seated window: set back .* ignored" "$TMP/server.log"; then echo "NET TEST: FAIL (the joiner's quick set-back was not refused)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: the cart takes ride 3's switch .* thrown through the intent" "$TMP/server.log" || ! grep -q "\[net\] ride: the cart crosses here (CrossOut set" "$TMP/client.log"; then echo "NET TEST: FAIL (ride 3's switch thrown by the client did not send the cart across on both)"; exit 1; fi
  # crew, gates and set pieces (task 7): the opened wagon couples here within 0.5 s of stop 1's departure (the cart's Train), the downed
  # crewman's label counts down here from the server's objective state, and the beam ducks our seated camera as our own cart's front passes
  if ! grep -q "\[net\] ride: the cage wagon coupled here .* after the cart left stop 1" "$TMP/client.log"; then echo "NET TEST: FAIL (the coupled wagon never showed on the client at stop 1's departure)"; exit 1; fi
  if ! grep -q "\[net\] ride: .*'s label counts down here" "$TMP/client.log"; then echo "NET TEST: FAIL (the downed crewman's label did not count down on the client)"; exit 1; fi
  if ! grep -q "\[net\] ride: the beam .* our seated camera ducks" "$TMP/client.log"; then echo "NET TEST: FAIL (the beam never ducked the client's seated camera)"; exit 1; fi
  # barricades and the live switch back over the wire (plan 11 task 8), before the clock checks below so a slow server's clocks do not
  # hide them: ride 1's barricade is cut by the client's blade from the seat (its line and the server's, with no smash logged before
  # the cut); ride 2's smash shows on the client as a 25 drop in the plating between the cart states either side of its PropEvt; the
  # client throws ride 3's live switch back from the seat and the server takes it (back from); on both clients the CrossBack, told
  # when it turns from NaN, is the back's points within 0.5 m and the cart is home on its own lane (blend <= 0.01) SwitchStretch + 2 m on;
  # and nothing ever runs off the broken bridge
  echo "--- barricades and the switch back"; grep -h "\[net\] ride: barricade \|\[net\] ride: crossed back here\|\[rails\] barricade r[0-9]_barricade \(cut\|smashed\)\|\[rails\] switch r3_switch_back taken" "$TMP/server.log" "$TMP/client.log" "$TMP/joiner.log" 2>/dev/null
  CUT_AT=$(grep -n "\[rails\] barricade r1_barricade cut by Rider" "$TMP/server.log" | head -1 | cut -d: -f1)
  SMASH_AT=$(grep -n "smashes through the barricade" "$TMP/server.log" | head -1 | cut -d: -f1)
  if [ -z "$CUT_AT" ] || ! grep -q "\[net\] ride: barricade r1_barricade cut here by our blade from the seat (" "$TMP/client.log"; then echo "NET TEST: FAIL (ride 1's barricade was not cut by the client's blade from the seat: no server cut by Rider or no client cut line)"; exit 1; fi
  if [ -n "$SMASH_AT" ] && [ "$SMASH_AT" -lt "$CUT_AT" ]; then echo "NET TEST: FAIL (a barricade was smashed before ride 1's was cut: $(sed -n "${SMASH_AT}p" "$TMP/server.log"))"; exit 1; fi
  SMASH2=$(grep -o "\[net\] ride: barricade r2_barricade smashed here (PropEvt), plating [-0-9.]* -> [-0-9.]*" "$TMP/client.log" | head -1)
  if [ -z "$SMASH2" ] || ! echo "$SMASH2" | awk '{ a = $(NF-2); b = $NF; d = a - b; exit !(a != "" && d >= 24.5 && d <= 25.5) }'; then echo "NET TEST: FAIL (ride 2's smash did not show on the client as a 25 drop in the plating: ${SMASH2:-no line})"; exit 1; fi
  if ! grep -q "\[net\] ride: ride 3's switch_back offers \"Throw the switch\" in the seated window .* thrown through the intent from the seat" "$TMP/client.log" || ! grep -q "\[rails\] switch r3_switch_back taken at S=.*: back from" "$TMP/server.log"; then echo "NET TEST: FAIL (the client's seated throw of ride 3's live switch back, or the server's back from, is missing)"; exit 1; fi
  for f in client joiner; do
    CB=$(grep -o "\[net\] ride: crossed back here: .*" "$TMP/$f.log" 2>/dev/null | head -1)
    if [ -z "$CB" ] || ! echo "$CB" | awk '{ for (i = 1; i <= NF; i++) { if ($i == "CrossBack") cb = $(i+1); if ($i == "points") { p = $(i+1); sub(/\),?$/, "", p) } if ($i == "blend") b = $(i+1) } d = cb - p; if (d < 0) d = -d; exit !(cb != "" && p != "" && cb != "NaN" && p != "NaN" && b != "NaN" && d <= 0.5 && b + 0 <= 0.01) }'; then echo "NET TEST: FAIL (the $f did not show the late switch back: ${CB:-no crossed back line})"; exit 1; fi
  done
  if grep -q "\[rails\] the cart runs off the broken bridge" "$TMP/server.log"; then echo "NET TEST: FAIL (the cart ran off the broken bridge: $(grep -m1 "\[rails\] the cart runs off the broken bridge" "$TMP/server.log"))"; exit 1; fi
  # plan 12 over the wire (task 8). Ride 3: after the server's back from, the first client presses the taken back once more from the seat
  # (a mashed press): the server ignores it quietly (AlreadyThrown, Ruling 21) and never tells it "Too late to throw it."
  echo "--- the mashed back, the dark, the runaway and the split"; grep -h "\[net\] ride: ride 3's switch_back pressed once more\|ignored quietly\|\[net\] ride: dark stretch here\|\[rails\] dark \|\[net\] ride: the runaway \|\[net\] ride: ride 4's split \|\[rails\] split r4_split: the \(train\|runaway\)\|throw held to the points\|\[rails\] .*runaway r4_runaway \|runaway rams" "$TMP/server.log" "$TMP/client.log" "$TMP/joiner.log" 2>/dev/null
  BACK_AT=$(grep -n "\[rails\] switch r3_switch_back taken at S=.*: back from" "$TMP/server.log" | head -1 | cut -d: -f1)
  BACK_REF=$(grep -o "\[net\] ride: ride 3's switch_back pressed once more from the seat after the cart took it (a mashed press, switch:[0-9]*)" "$TMP/client.log" | head -1 | grep -o "switch:[0-9]*")
  QUIET_AT=$([ -n "$BACK_REF" ] && grep -n "\[net-server\] interact $BACK_REF from Rider: the deadly lever was thrown (taken [A-Za-z]*): ignored quietly" "$TMP/server.log" | head -1 | cut -d: -f1)
  if [ -z "$BACK_REF" ]; then echo "NET TEST: FAIL (the client never pressed ride 3's taken switch back once more from the seat)"; exit 1; fi
  if [ -z "$BACK_AT" ] || [ -z "$QUIET_AT" ] || [ "$QUIET_AT" -lt "$BACK_AT" ]; then echo "NET TEST: FAIL (the server did not ignore the mashed press on $BACK_REF quietly after its back from)"; exit 1; fi
  if grep -q "Too late to throw it\." "$TMP/client.log"; then echo "NET TEST: FAIL (the client was told \"Too late to throw it.\": $(grep -m1 "Too late to throw it\." "$TMP/client.log"))"; exit 1; fi
  # the dark: each client's dark stretch (its start and the pocket lamps it put out) is the server's, and every one of those lamps' lights is
  # hidden in its own scene
  SRV_DARK=$(grep -o "\[rails\] dark r4_dark from party S=[0-9.]* to [0-9.]*: [0-9]* pocket lamp(s) out" "$TMP/server.log" | head -1 | sed 's/.*from party S=\([0-9.]*\) to [0-9.]*: \([0-9]*\) .*/\1 \2/')
  for f in client joiner; do
    CLI_DARK=$(grep -o "\[net\] ride: dark stretch here from S=[0-9.]* lamps out [0-9]* ([0-9]* pocket light(s) hidden here)" "$TMP/$f.log" 2>/dev/null | head -1 | sed 's/.*from S=\([0-9.]*\) lamps out \([0-9]*\) (\([0-9]*\) pocket.*/\1 \2 \3/')
    if [ -z "$SRV_DARK" ] || [ "${SRV_DARK#* }" = 0 ] || [ "${CLI_DARK% *}" != "$SRV_DARK" ]; then echo "NET TEST: FAIL (the $f's dark stretch (S and lamps out: $([ -n "$CLI_DARK" ] && echo "${CLI_DARK% *}" || echo none)) is not the server's (${SRV_DARK:-none}))"; exit 1; fi
    if [ -z "$CLI_DARK" ] || [ "${CLI_DARK##* }" != "${SRV_DARK#* }" ]; then echo "NET TEST: FAIL (the $f hid ${CLI_DARK##* } of the dark's ${SRV_DARK#* } pocket lights in its scene)"; exit 1; fi
  done
  # ride 4's runaway: it breaks loose on the server, and both clients show its copy behind the train (the server's cart corrections); on the
  # shared grid every pair on the party line is within 2 m of the server's
  if ! grep -q "\[rails\] t=[0-9.]* runaway r4_runaway breaks loose at party S=" "$TMP/server.log"; then echo "NET TEST: FAIL (ride 4's runaway never broke loose on the server: $(grep -m1 "\[rails\] runaway r4_runaway" "$TMP/server.log"))"; exit 1; fi
  for f in client joiner; do
    RUN_CART=$(grep -o "\[net\] ride: the runaway runs behind the train here (cart [0-9]*, r4_runaway;" "$TMP/$f.log" 2>/dev/null | head -1 | sed 's/.*(cart \([0-9]*\),.*/\1/')
    if [ -z "$RUN_CART" ]; then echo "NET TEST: FAIL (the $f never showed ride 4's runaway behind the train: $(grep -m1 "\[net\] ride: the runaway " "$TMP/$f.log" 2>/dev/null))"; exit 1; fi
    RUNP=$(awk -v id="$RUN_CART" '
      FNR == 1 { file++ }
      function val(k,   i) { for (i = 1; i <= NF; i++) if (index($i, k "=") == 1) return substr($i, length(k) + 2); return "" }
      file == 1 && /^\[net-server\] ride cart=/ && val("cart") == id && val("kind") == "Runaway" && val("armed") == "4" { srv[val("cart") "@" val("t")] = val("S") }
      file == 2 && /^\[net\] ride cart=/ && val("cart") == id && val("kind") == "Runaway" { cli[val("cart") "@" val("t")] = val("S") }
      END {
        n = 0; worst = 0; over = 0
        for (k in cli) if ((k in srv) && srv[k] + 0 >= 100 && cli[k] + 0 >= 100) {
          d = cli[k] - srv[k]; if (d < 0) d = -d
          n++; if (d > worst) worst = d; if (d > 2) { over++; print "  runaway pair over 2 m: " k " server " srv[k] " here " cli[k] > "/dev/stderr" }
        }
        printf "runaway (cart " id ") pairs on the party line=%d max=%.3f over=%d\n", n, worst, over
        exit !(n >= 8 && over == 0)
      }' "$TMP/server.log" "$TMP/$f.log")
    RUNP_RC=$?
    echo "--- the runaway, the $f vs the server: $RUNP"
    if [ $RUNP_RC -ne 0 ]; then echo "NET TEST: FAIL (the $f's runaway did not keep to the server's: $RUNP)"; exit 1; fi
  done
  # ride 4's split from the remote seats: the joiner sends its seat's press with its front in the server's early lead (2.5 m short of the
  # window's start, as a cart running ahead would on its first window frame): the server takes it (seated window yes), holds it to the
  # points with its own front short of its window, passes them straight and throws it behind the train; the first client presses it on its
  # own first frame past the points (a throw, or the set-back the held throw now offers), and the server keeps it thrown; the party never
  # goes down the spur, and the runaway takes the spur and goes into the crevasse; both clients see the throw and the plunge, nobody a ram
  SPLIT_BUILT=$(grep -o "\[rails\] split r4_split: the straight before the points [0-9.]* m, the seated window from S=[0-9.]* ([0-9.]* m short) until the runaway takes the points; throws held from [0-9.]* m short" "$TMP/server.log" | head -1)
  WINDOW=$(echo "$SPLIT_BUILT" | sed -n 's/.*the seated window from S=[0-9.]* (\([0-9.]*\) m short).*/\1/p')
  HOLD_REACH=$(echo "$SPLIT_BUILT" | sed -n 's/.*throws held from \([0-9.]*\) m short$/\1/p')
  EARLY_REF=$(grep -o "\[net\] ride: the joiner presses ride 4's split from the seat [0-9.]* m short of the points here, short of its window .*: switch:[0-9]*" "$TMP/joiner.log" 2>/dev/null | head -1 | grep -o "switch:[0-9]*$")
  SPLIT_LINE=$(grep -o "\[net\] ride: ride 4's split offers \"\(Throw the split behind us\|Set the split back\)\" in the seated window, .* through the intent from the seat ([0-9.]* m past the points here) as switch:[0-9]*" "$TMP/client.log" | head -1)
  SPLIT_REF=$(echo "$SPLIT_LINE" | grep -o "switch:[0-9]*$")
  if [ -z "$EARLY_REF" ]; then echo "NET TEST: FAIL (the joiner never pressed ride 4's split from the seat in the server's early lead: $(grep -m1 "ride 4's split" "$TMP/joiner.log" 2>/dev/null))"; exit 1; fi
  if [ -z "$SPLIT_REF" ] || [ "$SPLIT_REF" != "$EARLY_REF" ]; then echo "NET TEST: FAIL (the client never pressed ride 4's split ($EARLY_REF) from the seat past its points: $(grep -m1 "ride 4's split" "$TMP/client.log"))"; exit 1; fi
  for who in Joiner Rider; do
    if ! grep -q "\[net-server\] interact $SPLIT_REF from $who: .* seated window yes" "$TMP/server.log"; then echo "NET TEST: FAIL (the server did not take $who's press on the split ($SPLIT_REF) through the seated window)"; exit 1; fi
  done
  if grep -q "\[rails\] split r4_split: the train takes the spur\|The cart takes the spur" "$TMP/server.log"; then echo "NET TEST: FAIL (the party went down the split's spur: $(grep -m1 "the train takes the spur\|The cart takes the spur" "$TMP/server.log"))"; exit 1; fi
  HOLD_AT=$(grep -n "\[rails\] Split_r4_split/.*: Joiner's throw held to the points$" "$TMP/server.log" | head -1 | cut -d: -f1)
  PASS_AT=$(grep -n "\[rails\] split r4_split: the train passes straight" "$TMP/server.log" | head -1 | cut -d: -f1)
  # the held throw goes in under its last presser's name (a client ahead re-holds it), short of the points or pressed past them before the branch
  HELD=$(grep -n "\[rails\] split r4_split: [A-Za-z]*'s throw held to the points (\([0-9.]* m short\|pressed -\?[0-9.]* m past them, the branch not yet decided\)): thrown behind the train" "$TMP/server.log" | head -1)
  HELD_AT=$(echo "$HELD" | cut -d: -f1)
  HELD_SHORT=$(grep -o "\[net-server\] ride: ride 4's split held to the points for Joiner with the server's front [-0-9.]* m short of them" "$TMP/server.log" | head -1 | sed 's/.*front \([-0-9.]*\) m short.*/\1/')
  SPUR_AT=$(grep -n "\[rails\] split r4_split: the runaway takes the spur at S=" "$TMP/server.log" | head -1 | cut -d: -f1)
  PLUNGE_AT=$(grep -n "\[rails\] t=[0-9.]* runaway r4_runaway goes off the split's spur into the crevasse" "$TMP/server.log" | head -1 | cut -d: -f1)
  SRV_GONE=$(grep -o "\[rails\] t=[0-9.]* runaway r4_runaway goes off the split's spur into the crevasse at spur S=[0-9.]*" "$TMP/server.log" | head -1 | sed 's/.*at spur S=//')
  if [ -z "$HOLD_AT" ] || [ -z "$PASS_AT" ] || [ -z "$HELD_AT" ] || [ "$PASS_AT" -lt "$HOLD_AT" ] || [ "$HELD_AT" -lt "$PASS_AT" ]; then echo "NET TEST: FAIL (the server did not hold the joiner's press to the points, pass them straight and then throw it behind the train: held $([ -n "$HOLD_AT" ] && echo yes || echo no), passed $([ -n "$PASS_AT" ] && echo yes || echo no), thrown behind $([ -n "$HELD_AT" ] && echo yes || echo no))"; exit 1; fi
  if [ -z "$WINDOW" ] || [ -z "$HOLD_REACH" ] || ! awk -v s="$HELD_SHORT" -v w="$WINDOW" -v h="$HOLD_REACH" 'BEGIN { exit !(s != "" && s + 0 > w + 0 && s + 0 <= h + 0) }'; then echo "NET TEST: FAIL (the joiner's press did not land in the server's early lead: held with the server's front ${HELD_SHORT:-?} m short, the window ${WINDOW:-?} m, throws held from ${HOLD_REACH:-?} m)"; exit 1; fi
  # a held press leaves the lever straight ("now straight"), so only a "now straight" after the held throw went in is a set-back that went through
  UNDONE=$(awk -v from="$HELD_AT" -v r="[net-server] Rider used $SPLIT_REF: now straight" 'NR > from && index($0, r) == 1 { print NR ": " $0; exit }' "$TMP/server.log")
  if [ -n "$UNDONE" ]; then echo "NET TEST: FAIL (the client's press past the points set the thrown split back: $UNDONE)"; exit 1; fi
  if [ -z "$SPUR_AT" ] || [ "$SPUR_AT" -lt "$HELD_AT" ] || [ -z "$PLUNGE_AT" ] || [ "$PLUNGE_AT" -lt "$SPUR_AT" ]; then echo "NET TEST: FAIL (the runaway did not take the thrown split's spur into the crevasse on the server: $(grep -m1 "\[rails\] split r4_split: the runaway" "$TMP/server.log"))"; exit 1; fi
  RIDER_ON=$(grep -m1 "\[rails\] Split_r4_split/.*: Rider's \(throw held to the points (again)\|set-back .* after the held throw went in, ignored\)\|\[net-server\] interact $SPLIT_REF from Rider: \(seated window: set back .* ignored\|the deadly lever was thrown .*\)\|\[net-server\] Rider used $SPLIT_REF: now thrown" "$TMP/server.log")
  echo "--- the split: the joiner's press held ${HELD_SHORT} m short (the window ${WINDOW} m); the client's press past the points: $(echo "$SPLIT_LINE" | grep -o "offers \"[^\"]*\"") $(echo "$SPLIT_LINE" | grep -o "([0-9.]* m past the points here)"), on the server: ${RIDER_ON:-NOTHING}"
  if [ -z "$RIDER_ON" ]; then echo "NET TEST: FAIL (the server's handling of the client's press past the points is not in its log)"; exit 1; fi
  for f in client joiner; do
    if ! grep -q "\[net\] ride: ride 4's split shows thrown here (its PropEvt)" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the server's throw of the split never showed on the $f)"; exit 1; fi
  done
  if grep -q "\[rails\] runaway rams the train" "$TMP/server.log"; then echo "NET TEST: FAIL (the runaway rammed the train: $(grep -m1 "\[rails\] runaway rams the train" "$TMP/server.log"))"; exit 1; fi
  for f in client joiner; do
    PLUNGED=$(grep -o "\[net\] ride: the runaway took the spur here and plunged (it went at spur S=[0-9.]*; our copy at S=[-0-9.]*)" "$TMP/$f.log" 2>/dev/null | head -1)
    if [ -z "$PLUNGED" ]; then echo "NET TEST: FAIL (the $f never saw the runaway take the spur and plunge)"; exit 1; fi
    # the server's spur S where it went, and the client's copy on the spur within 2 m of it
    if ! echo "$PLUNGED" | awk -v srv="$SRV_GONE" '{ g = $0; sub(/.*it went at spur S=/, "", g); sub(/;.*/, "", g); c = $0; sub(/.*our copy at S=/, "", c); sub(/\).*/, "", c); d = c - g; if (d < 0) d = -d; exit !(srv != "" && g == srv && d <= 2) }'; then echo "NET TEST: FAIL (the $f's plunge is not the server's (spur S=${SRV_GONE:-?}) or its copy on the spur was more than 2 m off: $PLUNGED)"; exit 1; fi
    if grep -q "\[net\] ride: the runaway rammed the train here" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f showed a ram the server never made: $(grep -m1 "\[net\] ride: the runaway rammed the train here" "$TMP/$f.log"))"; exit 1; fi
  done
  # ride 4's cavern over the wire (plan 13 task 7). The bridge: built live, it goes behind the train on the server and both clients show
  # every cell gone within 10 s of their arrival at stop 4; the train never falls on the main pass. The joiner holds its brake from its
  # rear's coming onto the bridge across the call and past the reaction time: that press never counts and the train is not taken (the
  # controller's ruling (a)). The leaps: the first client leaps across onto r4_slag's deck seat 0 on its first frame offered it, the joiner
  # onto seat 1; the joiner leaps back on its last offered frames (its copy within 0.5 m of the offer's close), the server takes it (leap
  # accepted, back into the cart; never a fall), and the first client back on its first frame after the slag line's call; both clients see
  # both on the deck (their SeatMsgs). The drop: r4_slag goes over with nobody fallen or hauled, shown on both; nobody seated on a freed copy,
  # nobody shown standing over a gone cell (ruling (d)'s watch; its case, a leaper on a copy as it goes, is not produced here and says
  # so), no "Leap across" offered after the call (ruling (e)); both heroes in the party's cart
  # at stop 4. After the wipe's rise the rebuilt bridge and drop stand, the joiner's props hashing as the server's. Lines after the server's
  # first "ride: take:" belong to the bridge's take at the end of the pass (rulings (b), (c)), so the main pass's are read before it
  echo "--- the cavern"; grep -h "\[rails\] bridge r4_bridge\|\[rails\] t=[0-9.]* bridge r4_bridge\|\[rails\] leap\|\[rails\] t=[0-9.]* leap r4_leap\|goes over the drop\|\[net-server\] ride: \(bridge\|cavern\|take\): \|\[net-server\] interact [a-z]*:[0-9]* from [A-Za-z]*: leap \|\[net\] ride: \(cavern\|take\): \|\[net\] ride: [A-Za-z]* rides slag cart \|\[net\] ride: slag cart [0-9]* went over the drop\|\[net\] ride: the bridge\('s call\| behind us\)\|\[net\] ride: after the wipe's rise\|\[net-server\] ride: wipe-rise: bridge\|runaway r4_runaway goes down with the bridge" "$TMP/server.log" "$TMP/client.log" "$TMP/joiner.log" 2>/dev/null | head -100
  TAKE_AT=$(grep -n "\[net-server\] ride: take: " "$TMP/server.log" | head -1 | cut -d: -f1); TAKE_AT=${TAKE_AT:-999999999}
  BRIDGE_N=$(grep -o "\[rails\] bridge r4_bridge: party S=[0-9.]*\.\.[0-9.]*, [0-9]* deck cell(s), over the crevasse [A-Za-z]* (live)" "$TMP/server.log" | head -1 | sed 's/.*, \([0-9]*\) deck cell(s).*/\1/')
  if [ -z "$BRIDGE_N" ] || [ "$BRIDGE_N" = 0 ]; then echo "NET TEST: FAIL (the server never built ride 4's bridge live: $(grep -m1 "\[rails\] bridge r4_bridge:" "$TMP/server.log"))"; exit 1; fi
  if [ -z "$(awk -v m="$TAKE_AT" 'NR < m && /\[rails\] t=[0-9.]* bridge r4_bridge goes from S=/ { print NR; exit }' "$TMP/server.log")" ]; then echo "NET TEST: FAIL (ride 4's bridge never went behind the train on the server)"; exit 1; fi
  FELL=$(awk -v m="$TAKE_AT" 'NR < m && /\[rails\] the train falls/ { print; exit }' "$TMP/server.log")
  if [ -n "$FELL" ]; then echo "NET TEST: FAIL (the train fell on the server's main pass: $FELL)"; exit 1; fi
  for f in client joiner; do
    BG=$(grep -o "\[net\] ride: the bridge behind us gone [0-9]*/[0-9]* cells here [-+0-9.]* s from our arrival at stop [0-9]*" "$TMP/$f.log" 2>/dev/null | head -1)
    if [ -z "$BG" ] || ! echo "$BG" | awk -v n="$BRIDGE_N" '{ g = $0; sub(/.* gone /, "", g); sub(/ cells.*/, "", g); split(g, c, "/"); d = $0; sub(/.* cells here /, "", d); sub(/ s from.*/, "", d); exit !(c[1] == n && c[2] == n && d + 0 <= 10) }'; then echo "NET TEST: FAIL (the $f did not show ride 4's bridge all gone ($BRIDGE_N cells) within 10 s of its arrival at stop 4: ${BG:-$(grep -m1 "\[net\] ride: the bridge behind us gone" "$TMP/$f.log" 2>/dev/null)})"; exit 1; fi
  done
  BRAKE=$(grep -m1 "\[net-server\] ride: bridge: " "$TMP/server.log")
  case "$BRAKE" in "[net-server] ride: bridge: Joiner held the brake across the call: "*"(ok)") ;; *) echo "NET TEST: FAIL (the joiner's brake held across the bridge's call was not held short of the reaction time, or counted, or the train was taken: ${BRAKE:-no line})"; exit 1 ;; esac
  if grep -q "\[net\] ride: cavern: the joiner lets go of the brake 7 s on with the bridge's call NEVER seen here" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (the joiner never saw the bridge's call while it braked across it)"; exit 1; fi
  R_ACROSS=$(grep -o "\[net\] ride: cavern: \"Leap across\" offered to the rider here .* pressed on our first frame offered it through the intent (slag:[0-9]*)" "$TMP/client.log" | head -1)
  SLAG_ID=$(echo "$R_ACROSS" | sed -n 's/.*(slag:\([0-9]*\))$/\1/p')
  if [ -z "$SLAG_ID" ]; then echo "NET TEST: FAIL (the first client never pressed \"Leap across\" on its first frame offered it: $(grep -m1 "\[net\] ride: cavern: \(FAIL\|\"Leap across\"\)" "$TMP/client.log"))"; exit 1; fi
  for who in Rider:0 Joiner:1; do
    name=${who%:*}; seat=${who#*:}
    if ! grep -q "\[rails\] leap: $name across onto slag cart $SLAG_ID (r4_slag) seat $seat " "$TMP/server.log"; then echo "NET TEST: FAIL (the server never took $name's leap across onto slag cart $SLAG_ID's deck seat $seat: $(grep -m1 "\[rails\] leap: $name \|interact slag:[0-9]* from $name" "$TMP/server.log"))"; exit 1; fi
    for f in client joiner; do
      if ! grep -q "\[net\] ride: $name rides slag cart $SLAG_ID seat $seat here" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f never showed $name on slag cart $SLAG_ID's deck seat $seat from the SeatMsg)"; exit 1; fi
    done
  done
  if ! grep -q "\[net\] ride: cavern: \"Leap across\" offered to the joiner here .* (slag:$SLAG_ID)$" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (the joiner never pressed \"Leap across\" onto slag cart $SLAG_ID: $(grep -m1 "\[net\] ride: cavern: FAIL" "$TMP/joiner.log" 2>/dev/null))"; exit 1; fi
  J_BACK=$(grep -o "\[net\] ride: cavern: the joiner presses \"Leap back\" on its last offered frames: .*, [-0-9.]* m short of the offer's close" "$TMP/joiner.log" 2>/dev/null | head -1)
  J_SHORT=$(echo "$J_BACK" | sed -n "s/.*, \([-0-9.]*\) m short of the offer's close$/\1/p")
  if [ -z "$J_SHORT" ] || ! awk -v d="$J_SHORT" 'BEGIN { exit !(d + 0 >= 0 && d + 0 <= 0.5) }'; then echo "NET TEST: FAIL (the joiner did not press \"Leap back\" with its slag copy within 0.5 m of the offer's close: ${J_BACK:-$(grep -m1 "\[net\] ride: cavern: \(FAIL\|the offer of\)" "$TMP/joiner.log" 2>/dev/null)})"; exit 1; fi
  J_OK_AT=$(grep -n "\[net-server\] interact cart:0 from Joiner: leap accepted" "$TMP/server.log" | head -1 | cut -d: -f1)
  J_IN_AT=$(grep -n "\[rails\] leap: Joiner back into the cart seat " "$TMP/server.log" | head -1 | cut -d: -f1)
  if [ -z "$J_OK_AT" ] || [ -z "$J_IN_AT" ] || [ "$J_IN_AT" -lt "$J_OK_AT" ]; then echo "NET TEST: FAIL (the server did not take the joiner's last-frame \"Leap back\": accepted $([ -n "$J_OK_AT" ] && echo yes || echo no), back into the cart $([ -n "$J_IN_AT" ] && echo yes || echo no))"; exit 1; fi
  if ! grep -q "\[net\] ride: cavern: \"Leap back\" pressed on our first frame after the slag line's call here" "$TMP/client.log" || ! grep -q "\[rails\] leap: Rider back into the cart seat " "$TMP/server.log"; then echo "NET TEST: FAIL (the first client's \"Leap back\" on its first frame after the slag line's call was not pressed or not taken)"; exit 1; fi
  if grep -q "\[rails\] leap: [A-Za-z]* falls with slag cart" "$TMP/server.log"; then echo "NET TEST: FAIL (a hero fell with the slag cart: $(grep -m1 "\[rails\] leap: [A-Za-z]* falls with slag cart" "$TMP/server.log"))"; exit 1; fi
  if [ -z "$(awk -v m="$TAKE_AT" -v id="$SLAG_ID" 'NR < m && index($0, " slag cart " id " (r4_slag) goes over the drop at enemy S=") && / 0 hero\(es\) fell, 0 hauled back$/ { print NR; exit }' "$TMP/server.log")" ]; then echo "NET TEST: FAIL (slag cart $SLAG_ID did not go over the drop with nobody fallen or hauled back: $(grep -m1 "goes over the drop" "$TMP/server.log"))"; exit 1; fi
  if ! grep -q "\[net-server\] ride: cavern: the train arrives at stop [0-9]* with Rider in the party's cart yes and Joiner yes; .* (ok)$" "$TMP/server.log"; then echo "NET TEST: FAIL (the train did not arrive at stop 4 with both players' heroes in the party's cart after two leaps each way and the drop: $(grep -m1 "\[net-server\] ride: cavern: the train arrives" "$TMP/server.log"))"; exit 1; fi
  for f in client joiner; do
    if ! grep -q "\[net\] ride: slag cart $SLAG_ID went over the drop here" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f never showed slag cart $SLAG_ID go over the drop)"; exit 1; fi
    for what in "at stop [0-9]* our hero sits in the party's cart (seat [0-9]*) here" "after the slag line's call no \"Leap across\" offered here .* (ok)" "no hero shown standing over a gone cell here .* (\\(ok\\|ruling (d)'s case not reached here\\))" "after slag cart [0-9, ]* went over the drop no hero sits on a freed copy here (ok)"; do
      if ! grep -q "\[net\] ride: cavern: $what" "$TMP/$f.log" 2>/dev/null; then echo "NET TEST: FAIL (the $f's cavern line \"$what\" was not printed or failed: $(grep "\[net\] ride: cavern: " "$TMP/$f.log" 2>/dev/null | grep "FAIL\|NOT\|NO " | head -1 | cut -c1-300))"; exit 1; fi
    done
    # ruling (d)'s case (a leaper still on a copy as it goes) is not produced by this pass: a note, not a failure (the final review's)
    if grep -q "\[net\] ride: cavern: .*ruling (d)'s case not reached here" "$TMP/$f.log" 2>/dev/null; then echo "NOTE: the $f: ruling (d)'s case not reached (nobody on a slag copy as it went)"; fi
  done
  if ! grep -q "\[net-server\] ride: wipe-rise: bridge r4_bridge 0/[0-9]* gone, the slag line's drop (r4_leap) standing (ok)" "$TMP/server.log"; then echo "NET TEST: FAIL (after the wipe's rise the server's bridge or drop did not stand: $(grep -m1 "\[net-server\] ride: wipe-rise: bridge" "$TMP/server.log"))"; exit 1; fi
  if ! grep -q "\[net\] ride: after the wipe's rise our props hash as the server's: .* BridgeGone 0, SlagLineDropped False (ok)" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (after the wipe's rise the joiner's props did not hash as the server's with the bridge and the drop standing: $(grep -m1 "\[net\] ride: after the wipe's rise" "$TMP/joiner.log" 2>/dev/null))"; exit 1; fi
  # the bridge's take at the end of the pass (rulings (b), (c)): the joiner alone rides ride 4 again from stop 3, pumped, the split straight:
  # the runaway follows it into the cavern and goes down with the bridge (neither rammed nor plunged; no client shows a ram: the checks
  # above), then the joiner brakes past the call's reaction time and the bridge takes the train; risen, its current camera is its own hero's
  echo "--- the bridge's take"; awk -v m="$TAKE_AT" 'NR >= m && (/\[net-server\] ride: take: / || /\[rails\] t=[0-9.]* bridge r4_bridge (goes|takes)/ || /runaway r4_runaway (goes down|breaks loose)/ || /\[rails\] the train falls/)' "$TMP/server.log" | head -20; grep -h "\[net\] ride: take: " "$TMP/joiner.log" 2>/dev/null
  if [ "$TAKE_AT" = 999999999 ]; then echo "NET TEST: FAIL (the bridge's take never began on the server)"; exit 1; fi
  if [ -z "$(awk -v m="$TAKE_AT" 'NR > m && /\[rails\] t=[0-9.]* bridge r4_bridge takes the train at S=/ { print NR; exit }' "$TMP/server.log")" ]; then echo "NET TEST: FAIL (the bridge never took the train at the end of the pass: $(grep -m1 "\[net\] ride: take: FAIL\|\[net\] RESULT FAIL" "$TMP/joiner.log" 2>/dev/null | cut -c1-300))"; exit 1; fi
  if [ -z "$(awk -v m="$TAKE_AT" 'NR > m && /\[net-server\] ride: take: bridge r4_bridge took the train with Joiner aboard .*; the runaway went down with the bridge before it$/ { print NR; exit }' "$TMP/server.log")" ]; then echo "NET TEST: FAIL (the runaway did not go down with the bridge before the take: $(awk -v m="$TAKE_AT" 'NR > m && /ride: take: bridge r4_bridge took the train/ { print; exit }' "$TMP/server.log"))"; exit 1; fi
  TAKE_RUN=$(awk -v m="$TAKE_AT" 'NR > m && /\[net-server\] ride: take: the runaway went down with bridge / { print; exit }' "$TMP/server.log")
  case "$TAKE_RUN" in *"rammed False, plunged False); rams this build 0 (ok)") ;; *) echo "NET TEST: FAIL (the runaway was not taken by the bridge plainly before the take: ${TAKE_RUN:-the bridge never took it})"; exit 1 ;; esac
  if ! grep -q "\[net\] ride: take: the runaway's copy (cart [0-9]*) went here at S=" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (the joiner never saw the runaway's copy go at the bridge)"; exit 1; fi
  TAKE_EYE=$(grep -m1 "\[net\] ride: take: after the bridge's take" "$TMP/joiner.log" 2>/dev/null)
  case "$TAKE_EYE" in *"the seen fall's eye shown here yes;"*) ;; *) echo "NET TEST: FAIL (the taken joiner never saw its own fall (the seen fall's eye was never current on its screen): ${TAKE_EYE:-no line})"; exit 1 ;; esac
  if ! grep -q "\[net\] ride: take: after the bridge's take, risen at stop [0-9]* .* our current camera now our hero's (ok)" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (after the bridge's take the joiner's current camera was not its own hero's: $(grep -m1 "\[net\] ride: take: after the bridge's take" "$TMP/joiner.log" 2>/dev/null))"; exit 1; fi
  # the set pieces' clocks: the kiln's (ride 5's) first hammer enters its fall on both sides (server time; every client fall paired with the nearest
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
  # the wipe's rise (plan 10 task 9): every hero the server holds is given to the crevasse, the party wipes and rises beside the
  # fresh cart at the last stop reached; the joiner, placed there by the server before it is ready again, stands within 6 m of the
  # party's cart once the floor-ready ForcePose has landed
  echo "--- wipe-rise"; grep -h "\[net-server\] ride: wipe-rise: \|\[net\] ride: rose beside the cart" "$TMP/server.log" "$TMP/joiner.log" 2>/dev/null
  RISE_D=$(grep -o "\[net\] ride: rose beside the cart at stop [0-9]* (d=[0-9.]* m)" "$TMP/joiner.log" 2>/dev/null | head -1 | sed 's/.*(d=\([0-9.]*\) m)/\1/')
  if ! grep -q "\[net-server\] ride: wipe-rise: [1-9][0-9]* hero(es) beside the cart at stop [0-9]*$" "$TMP/server.log" || [ -z "$RISE_D" ] || ! awk -v d="$RISE_D" 'BEGIN { exit !(d <= 6.0) }'; then
    echo "NET TEST: FAIL (the wipe did not set the remote player down beside the cart)"; exit 1
  fi
  # the walk-off (plan 10 final review C1): before it says it is ready for the wipe, the joiner's own client walks its hero off the lip
  # of the chasm nearest it on its own input; the server keeps its poses into the drop, so the crevasse watch judges the server's copy
  # of its body ([crevasse] Joiner fell ..., before the wipe asks anything: nothing hands the joiner to the crevasse), and the joiner's
  # sheet shows it Fallen; the wipe's rise then brings it back up, placed beside the cart by the server's ForcePose (the check above)
  echo "--- walk-off"; grep -h "\[net\] ride: walk-off: \|\[net-server\] ride: walk-off: \|\[crevasse\] Joiner fell" "$TMP/server.log" "$TMP/joiner.log" 2>/dev/null
  MARK=$(grep -n "\[net-server\] ride: walk-off: armed" "$TMP/server.log" | head -1 | cut -d: -f1)
  FELL_AT=$(awk -v m="${MARK:-999999999}" 'NR>m && /\[crevasse\] Joiner fell at .*: Dead/ {print NR; exit}' "$TMP/server.log")
  WIPE_AT=$(grep -n "\[net-server\] ride: wipe: the joiner is ready" "$TMP/server.log" | head -1 | cut -d: -f1)
  if [ -z "$MARK" ]; then echo "NET TEST: FAIL (walk-off: no armed marker on the server)"; exit 1; fi
  if ! grep -q "\[net\] ride: walk-off: the joiner steps onto the lip at" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (walk-off: the joiner never stepped onto a lip to walk off)"; exit 1; fi
  if [ -z "$FELL_AT" ] || [ -z "$WIPE_AT" ] || [ "$FELL_AT" -ge "$WIPE_AT" ]; then echo "NET TEST: FAIL (walk-off: the server never judged the joiner's own fall before the wipe: no [crevasse] Joiner fell line ahead of it)"; exit 1; fi
  if ! grep -q "\[net-server\] ride: walk-off: Joiner is Fallen here before the wipe" "$TMP/server.log"; then echo "NET TEST: FAIL (walk-off: the server's copy of the joiner was not Fallen before the wipe)"; exit 1; fi
  if ! grep -q "\[net\] ride: walk-off: the sheet shows the joiner Fallen here" "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (walk-off: the joiner's sheet never showed it Fallen)"; exit 1; fi
  if ! grep -q "\[net\] ride: walk-off: back from the fall here: up " "$TMP/joiner.log" 2>/dev/null; then echo "NET TEST: FAIL (walk-off: the joiner was not back up after the wipe's rise)"; exit 1; fi
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
