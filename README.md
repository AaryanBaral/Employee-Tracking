# Employee Tracker

End-to-end employee activity tracking with an agent, a local browser extension bridge, and a backend API. The system collects web/app/idle signals on a device, sessionizes them locally, and uploads durable batches to the server for reporting.

## What this repo contains

- `Agent.Service` – main background agent process (outbox, sessionizers, local API)
- `Agent.Mac` / `Agent.Windows` – platform collectors (foreground app + idle)
- `Agent.Shared` – shared contracts, models, and config
- `Tracker.Api` – backend API for ingest + device reporting
- `Extensions` – Chromium extension to emit tab/url/title events

## Architecture at a glance

1) **Extension** posts `WebEvent` payloads to the local agent API.
2) **Agent.Service**:
   - records raw events to an outbox
   - sessionizes web/app/idle into durable sessions
   - uploads batches to `Tracker.Api`
3) **Tracker.Api** persists events + sessions and serves device reports.

```
Extension -> Local API (Agent.Service) -> Outbox -> Tracker.Api
                       |                         |
                       |-- Sessionizers ---------|
```

## Key concepts

- **WebEvent**: raw browser event (domain/url/title/timestamp/browser)
- **WebSession**: time-bounded activity on a specific URL/domain (sessionized on the agent)
- **AppSession**: foreground app usage session
- **IdleSession**: idle periods
- **Device**: unique machine identity, last seen, reviewed state

## Getting started (local dev)

### Prereqs

- .NET SDK 8.x
- PostgreSQL (for `Tracker.Api`)
- Chromium-based browser (Chrome/Brave/Edge) for the extension

### Build

```bash
dotnet build
```

### Run Tracker.Api

1) Configure the connection string (default is `Tracker.Api/appsettings.json`).
2) Apply migrations (EF Core) if needed.
3) Start the API.

```bash
dotnet run --project Tracker.Api/Tracker.Api.csproj
```

### Run Agent.Service

```bash
dotnet run --project Agent.Service/Agent.Service.csproj
```

By default, the agent exposes a local API on `http://127.0.0.1:43121` and will attempt to send data to `http://localhost:5000`.

### Install the browser extension

Load `Extensions/` as an unpacked extension in your Chromium browser. The extension sends events to the agent local API. Update the endpoint/token in `Extensions/background.js` if you change them in the agent config.

## Configuration

`Agent.Service/appsettings.json`:

- `LocalApi:Token` – required token header for extension requests
- `Agent:LocalApiPort` – local API port (default 43121)
- `Agent:CollectorPollSeconds` – app/idle sampling interval
- `Agent:WebEventIngestEndpoint` – Tracker.Api web event endpoint
- `Agent:WebSessionIngestEndpoint` – Tracker.Api web session endpoint
- `Agent:AppSessionIngestEndpoint` – Tracker.Api app session endpoint
- `Agent:IdleSessionIngestEndpoint` – Tracker.Api idle session endpoint
- `Agent:DeviceHeartbeatEndpoint` – Tracker.Api device heartbeat

## Backend API overview

### Ingest endpoints

- `POST /events/web` – ingest a single web event
- `POST /events/web/batch` – ingest multiple web events
- `POST /ingest/web-sessions` – ingest web sessions
- `POST /ingest/app-sessions` – ingest app sessions
- `POST /ingest/idle-sessions` – ingest idle sessions
- `POST /devices/heartbeat` – device heartbeat

### Device reporting endpoints

- `GET /devices` – list devices with pagination and optional `query`, `seen`, `activeSince`
- `PATCH /devices/{deviceId}` – update `DisplayName` or mark as seen
- `GET /devices/{deviceId}/summary?date=YYYY-MM-DD` – daily summary
- `GET /devices/{deviceId}/web-sessions` – list web sessions with filters
- `GET /devices/{deviceId}/app-sessions` – list app sessions with filters
- `GET /devices/{deviceId}/idle-sessions` – list idle sessions with filters

## Data accuracy notes

Web time is gated by browser foreground + user activity. The agent will pause web sessions when the browser is not frontmost or the user is idle, which prevents inflated time reporting.

## Troubleshooting

- **No web events received**: check extension token and local API port.
- **Sessions not uploading**: check outbox stats via `GET /local/outbox/stats` on the agent local API.
- **API not receiving**: verify agent endpoints match `Tracker.Api` base URL.

## Repo layout

```
Agent.Mac/
Agent.Service/
Agent.Shared/
Agent.Windows/
Extensions/
Tracker.Api/
```

## License

Add your license here.
