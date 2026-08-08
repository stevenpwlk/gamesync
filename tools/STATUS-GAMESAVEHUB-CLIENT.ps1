$ErrorActionPreference = "Stop"

Write-Host "=== GameSave Hub Client — état local ===" -ForegroundColor Cyan
$service = Get-Service -Name "GameSaveHubClient" -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service : absent"
} else {
    Write-Host "Service : $($service.Status)"
}

$config = Join-Path $env:ProgramFiles "GameSaveHub\Client\Service\appsettings.local.json"
if (Test-Path -LiteralPath $config) {
    $settings = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    Write-Host "SID configuré : $($settings.ClientService.RegisteredUserSid)"
    Write-Host "Serveur : $($settings.ClientService.ServerBaseUrl)"
    Write-Host "EnableWgsTransfer : $($settings.ClientService.EnableWgsTransfer)"
} else {
    Write-Host "Configuration locale : absente"
}

$statePath = Join-Path $env:ProgramData "GameSaveHub\client-state.json"
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Write-Host "DeviceId : $($state.deviceId)"
    Write-Host "Pseudo Planet Crafter : $($state.registeredPlayerName)"
    Write-Host "Session active : $($state.activeSessionId)"
} else {
    Write-Host "État persistant : pas encore créé"
}

try {
    $health = Invoke-WebRequest -UseBasicParsing -Uri "https://saves.stevenpwlk.fr:18443/healthz"
    Write-Host "NAS /healthz : HTTP $($health.StatusCode) $($health.Content.Trim())"
}
catch {
    Write-Host "NAS /healthz : erreur $($_.Exception.Message)"
}

Write-Host "Ce script est en lecture seule et n'accède pas à WGS."
