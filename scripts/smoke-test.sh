#!/usr/bin/env bash
#
# smoke-test.sh - plays a whole game against a running Cats & Mouse API, calling every hub method
# through SignalR's long-polling transport with nothing but bash and curl.
#
# CI runs it against the Native AOT executable it has just published, before anything is
# deployed. The unit tests run on the JIT, and the AOT build fails differently: a type missing
# from the source-generated JSON metadata, or code the trimmer removed, only breaks at runtime,
# in exactly that build. This drives the executable the server will run, through the same
# protocol the browser speaks.
#
# Usage: scripts/smoke-test.sh <base-url> [host-header]
#   scripts/smoke-test.sh http://127.0.0.1:53000 cats-and-mouse-api.marcosgimeno.com
#
# The host header is needed for a Production build, whose AllowedHosts rejects 127.0.0.1.
#
# Exit status: 0 when every check passes, 1 at the first one that does not.

set -euo pipefail

BASE_URL="${1:?usage: $0 <base-url> [host-header]}"
BASE_URL="${BASE_URL%/}"
HOST_HEADER="${2:-}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

CURL=(curl --silent --show-error --fail --max-time 10)
if [[ -n "$HOST_HEADER" ]]; then
  CURL+=(--header "Host: $HOST_HEADER")
fi

INVOCATION_ID=0
LAST_RESULT=""

fail() {
  printf 'FAIL %s\n' "$*" >&2
  exit 1
}

pass() {
  printf 'ok   %s\n' "$*"
}

token_of() {
  cat "$WORK_DIR/$1.token"
}

# One long poll for the connection. Every record it returns is appended to the connection's log,
# one per line (the JSON protocol ends each record with 0x1e). A poll with nothing to deliver
# blocks, so this is only called when a record is due; the timeout is the safety net.
poll() {
  local name="$1"
  "${CURL[@]}" --max-time 5 "$BASE_URL/gameHub?id=$(token_of "$name")" | tr '\036' '\n' >> "$WORK_DIR/$name.log" || true
}

send() {
  local name="$1" record="$2"
  printf '%s\036' "$record" |
    "${CURL[@]}" --request POST --header 'Content-Type: text/plain;charset=UTF-8' --data-binary @- \
      "$BASE_URL/gameHub?id=$(token_of "$name")" > /dev/null ||
    fail "$name could not send: $record"
}

# Waits until a record received by the connection matches the extended regex.
expect() {
  local name="$1" pattern="$2" description="$3" attempt
  for attempt in 1 2 3 4 5; do
    if grep -qE -- "$pattern" "$WORK_DIR/$name.log"; then
      return 0
    fi
    poll "$name"
  done

  if ! grep -qE -- "$pattern" "$WORK_DIR/$name.log"; then
    printf -- '--- received by %s:\n' "$name" >&2
    cat "$WORK_DIR/$name.log" >&2
    fail "$name never received $description"
  fi
}

# Forgets what the connections received so far, so the next expectations only see new records.
clear_received() {
  local name
  for name in "$@"; do
    : > "$WORK_DIR/$name.log"
  done
}

connect() {
  local name="$1" negotiation token
  negotiation="$("${CURL[@]}" --request POST "$BASE_URL/gameHub/negotiate?negotiateVersion=1")" ||
    fail "negotiation failed for $name"
  grep -q '"LongPolling"' <<<"$negotiation" || fail "the hub does not offer long polling: $negotiation"
  token="$(grep -oE '"connectionToken":"[^"]+"' <<<"$negotiation" | cut -d'"' -f4)"
  [[ -n "$token" ]] || fail "negotiation returned no connection token: $negotiation"

  printf '%s' "$token" > "$WORK_DIR/$name.token"
  : > "$WORK_DIR/$name.log"

  # The first poll only opens the connection, and returns at once with no content.
  poll "$name"
  send "$name" '{"protocol":"json","version":1}'
  expect "$name" '^\{\}$' "the handshake response"
}

disconnect() {
  "${CURL[@]}" --request DELETE "$BASE_URL/gameHub?id=$(token_of "$1")" > /dev/null || true
}

# Calls a hub method and waits for its completion, which must not be an error. The completion
# record is left in LAST_RESULT.
invoke() {
  local name="$1" target="$2" arguments="$3" id completion
  INVOCATION_ID=$((INVOCATION_ID + 1))
  id="$INVOCATION_ID"

  send "$name" "{\"type\":1,\"invocationId\":\"$id\",\"target\":\"$target\",\"arguments\":$arguments}"
  expect "$name" "^\{\"type\":3,\"invocationId\":\"$id\"" "the completion of $target"

  completion="$(grep -E "^\{\"type\":3,\"invocationId\":\"$id\"" "$WORK_DIR/$name.log" | tail -n 1)"
  if grep -q '"error"' <<<"$completion"; then
    fail "$target failed: $completion"
  fi

  LAST_RESULT="$completion"
}

# Calls a hub method that must fail with the given error text.
invoke_rejected() {
  local name="$1" target="$2" arguments="$3" error_text="$4" id completion
  INVOCATION_ID=$((INVOCATION_ID + 1))
  id="$INVOCATION_ID"

  send "$name" "{\"type\":1,\"invocationId\":\"$id\",\"target\":\"$target\",\"arguments\":$arguments}"
  expect "$name" "^\{\"type\":3,\"invocationId\":\"$id\"" "the completion of $target"

  completion="$(grep -E "^\{\"type\":3,\"invocationId\":\"$id\"" "$WORK_DIR/$name.log" | tail -n 1)"
  grep -qF "$error_text" <<<"$completion" || fail "$target should have failed with '$error_text': $completion"
}

game_id_in() {
  grep -oE '"gameId":"[0-9a-f]+"' <<<"$1" | head -n 1 | cut -d'"' -f4
}

status="$("${CURL[@]}" "$BASE_URL/status")" || fail "GET /status failed"
grep -q 'is ready' <<<"$status" || fail "GET /status did not report ready: $status"
pass "status endpoint"

CATS_USER="smoke-cats-$$-$RANDOM"
MOUSE_USER="smoke-mouse-$$-$RANDOM"

connect cats
connect mouse
pass "negotiation and handshake over long polling"

invoke cats RegisterConnection "[\"$CATS_USER\"]"
expect cats '"target":"HasInProgressGame","arguments":\[\{"hasInProgressGame":false' "HasInProgressGame"
expect cats '"target":"GameList"' "the game list"
invoke mouse RegisterConnection "[\"$MOUSE_USER\"]"
pass "RegisterConnection"

clear_received cats mouse
invoke cats CreateGame '[{"userName":"Smoke cats","teamId":1}]'
GAME_ID="$(game_id_in "$LAST_RESULT")"
[[ -n "$GAME_ID" ]] || fail "CreateGame returned no game id: $LAST_RESULT"
grep -qF "\"userId\":\"$CATS_USER\"" <<<"$LAST_RESULT" || fail "CreateGame did not give the creator their own id: $LAST_RESULT"
expect mouse "\"gameId\":\"$GAME_ID\",\"userId\":\"\",\"userName\":\"Smoke cats\",\"teamId\":1,\"isPasswordProtected\":false" "the new game in the list"
if grep -qF "$CATS_USER" "$WORK_DIR/mouse.log"; then
  fail "another player received the creator's user id"
fi
pass "CreateGame, with the creator's id kept from everyone else"

clear_received cats mouse
invoke mouse JoinGame "[{\"gameId\":\"$GAME_ID\",\"userName\":\"Smoke mouse\"}]"
expect mouse '"target":"GameStart"' "GameStart"
expect mouse '"myPlayerIndex":1' "its own game status"
expect cats '"myPlayerIndex":0' "its own game status"
if grep -qE "$CATS_USER|$MOUSE_USER" "$WORK_DIR/cats.log" "$WORK_DIR/mouse.log"; then
  fail "a game status carried a user id"
fi
pass "JoinGame"

clear_received cats mouse
invoke mouse Move '[{"figureId":0,"rowIndex":6,"columnIndex":3}]'
expect cats '"position":\{"rowIndex":6,"columnIndex":3\}' "the mouse's move"
invoke_rejected mouse Move '[{"figureId":0,"rowIndex":5,"columnIndex":2}]' "It's not your turn"
invoke cats Move '[{"figureId":1,"rowIndex":1,"columnIndex":0}]'
pass "Move, and a move out of turn rejected"

clear_received cats mouse
invoke cats SendChatMessage "[{\"gameId\":\"$GAME_ID\",\"message\":\"  smoke test  \"}]"
expect mouse '"chatLine":\{"userName":"Smoke cats","teamId":1,"message":"smoke test"\}' "the chat line"
pass "SendChatMessage"

clear_received cats mouse
invoke mouse SendInProgressGameStatusToCaller '[]'
expect mouse "\"gameStatus\":\\{\"gameId\":\"$GAME_ID\"" "its game status"
invoke cats SendWhetherHasInProgressGameToCaller '[]'
expect cats '"hasInProgressGame":true' "HasInProgressGame"
pass "SendInProgressGameStatusToCaller, SendWhetherHasInProgressGameToCaller"

clear_received cats mouse
invoke mouse Surrender '[]'
expect cats '"target":"PlayerHasSurrendered"' "the surrender"
expect cats '"isWinner":true' "a winner"
pass "Surrender"

clear_received cats mouse
invoke cats PlayerWantsToRematch "[{\"gameId\":\"$GAME_ID\"}]"
expect mouse '"target":"PlayerWantsRematch"' "the rematch request"
invoke mouse PlayerWantsToRematch "[{\"gameId\":\"$GAME_ID\"}]"
expect cats '"target":"GameStatus"' "the rematch game"
REMATCH_ID="$(grep -E '"target":"GameStatus"' "$WORK_DIR/cats.log" | tail -n 1 | grep -oE '"gameId":"[0-9a-f]+"' | head -n 1 | cut -d'"' -f4)"
[[ -n "$REMATCH_ID" && "$REMATCH_ID" != "$GAME_ID" ]] || fail "the rematch did not start a new game"
pass "PlayerWantsToRematch"

clear_received cats mouse
invoke cats ExitGame "[{\"gameId\":\"$REMATCH_ID\"}]"
expect mouse '"target":"PlayerHasLeftGame"' "the opponent leaving"
pass "ExitGame"

clear_received cats mouse
invoke cats CreateGame '[{"userName":"Smoke cats","teamId":2,"gamePassword":"secret"}]'
SECOND_GAME_ID="$(game_id_in "$LAST_RESULT")"
grep -q '"isPasswordProtected":true' <<<"$LAST_RESULT" || fail "the game is not password protected: $LAST_RESULT"
invoke_rejected mouse JoinGame "[{\"gameId\":\"$SECOND_GAME_ID\",\"userName\":\"Smoke mouse\",\"gamePassword\":\"wrong\"}]" "Game password is invalid"
invoke cats CancelGameThatHasNotStarted "[{\"gameId\":\"$SECOND_GAME_ID\",\"userId\":\"$CATS_USER\",\"sendAwaitingGamesToAllClients\":true}]"
clear_received mouse
invoke mouse SendGamesAwaitingForSecondPlayerToCallerAsync '[]'
expect mouse '"target":"GameList"' "the game list"
if grep -qF "\"gameId\":\"$SECOND_GAME_ID\"" "$WORK_DIR/mouse.log"; then
  fail "a cancelled game is still listed"
fi
pass "password-protected game, CancelGameThatHasNotStarted, SendGamesAwaitingForSecondPlayerToCallerAsync"

invoke cats Ping '[]'
pass "Ping"

disconnect cats
disconnect mouse
pass "all hub methods answered correctly"
