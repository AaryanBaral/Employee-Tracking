param(
  [string]$BaseUrl = "http://127.0.0.1:43121",
  [Parameter(Mandatory=$true)][string]$Token
)

$headers = @{ "X-Agent-Token" = $Token }

Write-Host "GET /version"
$ver = Invoke-RestMethod -Method Get -Uri "$BaseUrl/version" -Headers $headers
if ($ver.contract -ne "local-api-v1") { throw "Contract mismatch: $($ver.contract)" }
Write-Host "OK contract=$($ver.contract) deviceId=$($ver.deviceId) agentVersion=$($ver.agentVersion)"

Write-Host "GET /health"
$health = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health" -Headers $headers
if (-not $health.ok) { throw "/health ok=false" }
Write-Host "OK lastFlushAtUtc=$($health.lastFlushAtUtc)"

Write-Host "GET /diag (before)"
$diag1 = Invoke-RestMethod -Method Get -Uri "$BaseUrl/diag" -Headers $headers
$diag1 | ConvertTo-Json -Depth 6

Write-Host "POST /events/idle"
Invoke-RestMethod -Method Post -Uri "$BaseUrl/events/idle" -Headers $headers `
  -ContentType "application/json" `
  -Body (@{ idleSeconds = 3; timestampUtc = (Get-Date).ToUniversalTime() } | ConvertTo-Json)

Write-Host "POST /events/app-focus"
Invoke-RestMethod -Method Post -Uri "$BaseUrl/events/app-focus" -Headers $headers `
  -ContentType "application/json" `
  -Body (@{ appName = "Explorer"; windowTitle = "SmokeTest"; timestampUtc = (Get-Date).ToUniversalTime() } | ConvertTo-Json)

Start-Sleep -Seconds 1

Write-Host "GET /diag (after)"
$diag2 = Invoke-RestMethod -Method Get -Uri "$BaseUrl/diag" -Headers $headers
$diag2 | ConvertTo-Json -Depth 6

Write-Host "Done."
