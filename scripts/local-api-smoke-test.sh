#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: ./scripts/local-api-smoke-test.sh --token <token> [--base-url <url>]

Options:
  --token, -t     X-Agent-Token value (required)
  --base-url, -u  Base URL (default: http://127.0.0.1:43121)
  --help, -h      Show help

Environment:
  AGENT_LOCAL_API_TOKEN, TOKEN
  AGENT_LOCAL_API_URL, BASE_URL
USAGE
}

BASE_URL="${AGENT_LOCAL_API_URL:-${BASE_URL:-http://127.0.0.1:43121}}"
TOKEN="${AGENT_LOCAL_API_TOKEN:-${TOKEN:-}}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --token|-t)
      TOKEN="${2:-}"
      shift 2
      ;;
    --base-url|-u)
      BASE_URL="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$TOKEN" ]]; then
  echo "Missing token. Provide --token or set TOKEN." >&2
  exit 1
fi

json_get() {
  local key="$1"
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$key" <<'PY'
import json,sys
data = json.load(sys.stdin)
key = sys.argv[1]
value = data.get(key, "")
print("" if value is None else value)
PY
  else
    return 1
  fi
}

headers=(-H "X-Agent-Token: $TOKEN" -H "Content-Type: application/json")
timestamp="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

echo "GET /version"
version_json="$(curl -fsS "${headers[@]}" "$BASE_URL/version")"
printf '%s\n' "$version_json"
if contract="$(printf '%s' "$version_json" | json_get contract 2>/dev/null)"; then
  if [[ -n "$contract" && "$contract" != "local-api-v1" ]]; then
    echo "Contract mismatch: $contract" >&2
    exit 1
  fi
fi

echo "GET /health"
health_json="$(curl -fsS "${headers[@]}" "$BASE_URL/health")"
printf '%s\n' "$health_json"
if ok="$(printf '%s' "$health_json" | json_get ok 2>/dev/null)"; then
  if [[ "$ok" != "True" && "$ok" != "true" ]]; then
    echo "/health ok=false" >&2
    exit 1
  fi
fi

echo "GET /diag (before)"
diag_before="$(curl -fsS "${headers[@]}" "$BASE_URL/diag")"
printf '%s\n' "$diag_before"

echo "POST /events/idle"
idle_payload=$(cat <<JSON
{ "idleSeconds": 3, "timestampUtc": "$timestamp" }
JSON
)
curl -fsS -X POST "${headers[@]}" -d "$idle_payload" "$BASE_URL/events/idle" >/dev/null

echo "POST /events/app-focus"
app_payload=$(cat <<JSON
{ "appName": "Finder", "windowTitle": "SmokeTest", "timestampUtc": "$timestamp" }
JSON
)
curl -fsS -X POST "${headers[@]}" -d "$app_payload" "$BASE_URL/events/app-focus" >/dev/null

echo "POST /events/web"
if command -v uuidgen >/dev/null 2>&1; then
  event_id="$(uuidgen)"
elif command -v python3 >/dev/null 2>&1; then
  event_id="$(python3 - <<'PY'
import uuid
print(uuid.uuid4())
PY
)"
else
  echo "Missing uuid generator (need uuidgen or python3)." >&2
  exit 1
fi
web_payload=$(cat <<JSON
{
  "eventId": "$event_id",
  "domain": "example.com",
  "title": "Example",
  "url": "https://example.com",
  "timestampUtc": "$timestamp",
  "browser": "safari"
}
JSON
)
curl -fsS -X POST "${headers[@]}" -d "$web_payload" "$BASE_URL/events/web" >/dev/null

echo "GET /local/outbox/stats"
outbox_stats="$(curl -fsS "${headers[@]}" "$BASE_URL/local/outbox/stats")"
printf '%s\n' "$outbox_stats"

echo "GET /diag (after)"
diag_after="$(curl -fsS "${headers[@]}" "$BASE_URL/diag")"
printf '%s\n' "$diag_after"

echo "Done."
