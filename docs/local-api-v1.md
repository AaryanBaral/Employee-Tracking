# Local API v1

Base URL: `http://127.0.0.1:<port>`

Authentication:
- Header `X-Agent-Token` is required for all requests.

Endpoints:
- `GET /health`
- `GET /version`
- `POST /events/app-focus`
- `POST /events/idle`
- `POST /events/web`
- `POST /events/web-session` (alias for `/events/web`)
- `GET /local/outbox/stats` (requires auth)

Responses:
- `200 OK` accepted
- `401 Unauthorized` missing `X-Agent-Token`
- `403 Forbidden` token mismatch
- `429 Too Many Requests` back off and retry later

Notes:
- Routes and payloads are treated as stable for v1.
- `/events/web-session` is a temporary alias for `/events/web` and will be removed after clients migrate.

Example `/version` response:
```json
{
  "contract": "local-api-v1",
  "deviceId": "6d9de0a3-46b2-4fb7-9d4c-7f5d4270a0f1",
  "agentVersion": "1.0.0",
  "port": 43121,
  "routes": [
    "GET /health",
    "GET /version",
    "GET /local/outbox/stats",
    "POST /events/app-focus",
    "POST /events/idle",
    "POST /events/web",
    "POST /events/web-session (alias)"
  ],
  "auth": {
    "header": "X-Agent-Token"
  },
  "serverTimeUtc": "2026-01-07T12:34:56Z"
}
```
