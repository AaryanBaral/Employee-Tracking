# Agent.Windows (External Collector)

This collector posts idle and foreground app samples to the Local API v1 exposed by `Agent.Service`.

Prerequisites:
- `Agent.Service` is installed and running on the same machine.
- Local API token is configured and known.

## Build
From the repo root:
```
dotnet publish Agent.Windows/Agent.Windows.csproj -c Release -r win-x64 --self-contained false
```

## Run manually (PowerShell)
Use per-session env vars for immediate runs:
```
$env:AGENT_LOCAL_API_URL = "http://127.0.0.1:43121"
$env:AGENT_LOCAL_API_TOKEN = "dev-token"
$env:AGENT_POLL_SECONDS = "1"
$env:AGENT_FAILURE_EXIT_SECONDS = "60"

dotnet run -p Agent.Windows
```

Persist env vars for scheduled tasks (applies to future shells):
```
setx AGENT_LOCAL_API_URL "http://127.0.0.1:43121"
setx AGENT_LOCAL_API_TOKEN "dev-token"
setx AGENT_POLL_SECONDS "1"
setx AGENT_FAILURE_EXIT_SECONDS "60"
```

## Install as Scheduled Task (PowerShell)
```
$exe = "C:\EmployeeTracker\Agent.Windows\Agent.Windows.exe"

schtasks /Create /TN "EmployeeTrackerAgentWindows" /TR "`"$exe`"" /SC ONLOGON /RL HIGHEST /F
schtasks /Run /TN "EmployeeTrackerAgentWindows"
schtasks /Delete /TN "EmployeeTrackerAgentWindows" /F
```

Notes:
- Ensure `Agent.Service` is installed and running before starting this task.
- Set environment variables in the user or system scope before launching the task.

## Manual API smoke test (PowerShell)
```
$headers = @{ "X-Agent-Token" = "dev-token" }
Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:43121/health" -Headers $headers
Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:43121/version" -Headers $headers
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:43121/events/idle" -Headers $headers -Body (@{
  idleSeconds = 5
  timestampUtc = (Get-Date).ToUniversalTime()
} | ConvertTo-Json) -ContentType "application/json"
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:43121/events/app-focus" -Headers $headers -Body (@{
  appName = "Explorer"
  windowTitle = "Test Window"
  timestampUtc = (Get-Date).ToUniversalTime()
} | ConvertTo-Json) -ContentType "application/json"
```

## Testing checklist
- `/health` and `/version` succeed with `X-Agent-Token`.
- Agent.Windows logs show successful POSTs.
- Agent.Service logs show outbox enqueue + sender flush.
