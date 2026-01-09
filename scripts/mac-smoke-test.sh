#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:43121}"
TOKEN="${TOKEN:-dev-token}"

hdr=(-H "X-Agent-Token: $TOKEN" -H "Content-Type: application/json")

echo "GET /version"
curl -s "${hdr[@]}" "$BASE_URL/version" | jq .

echo "GET /health"
curl -s "${hdr[@]}" "$BASE_URL/health" | jq .

echo "GET /diag (before)"
curl -s "${hdr[@]}" "$BASE_URL/diag" | jq .

echo "POST /events/idle"
curl -s "${hdr[@]}" -X POST "$BASE_URL/events/idle" \
  -d "{\"idleSeconds\": 3, \"timestampUtc\": \"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\"}" | jq .

echo "POST /events/app-focus"
curl -s "${hdr[@]}" -X POST "$BASE_URL/events/app-focus" \
  -d "{\"appName\":\"Finder\",\"windowTitle\":\"SmokeTest\",\"timestampUtc\":\"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\"}" | jq .

echo "GET /diag (after)"
curl -s "${hdr[@]}" "$BASE_URL/diag" | jq .

echo "Done."
