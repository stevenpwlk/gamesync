$ErrorActionPreference = "Stop"

Write-Host "=== GameSave Hub Client — état local ===" -ForegroundColor Cyan
$service = Get-Service -Name "GameSaveHubClient" -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service : absent"
} else {
    Write-Host "Service : $($service.Status)"
}

# Depuis le Lot 3, la configuration machine vit hors du dossier d'installation, qui est
# renommé en bloc à chaque mise à jour. L'ancien emplacement reste consulté en repli, le
# temps que tous les postes soient passés par le nouvel installateur.
$config = Join-Path $env:ProgramData "GameSaveHub\appsettings.local.json"
if (-not (Test-Path -LiteralPath $config)) {
    $config = Join-Path $env:ProgramFiles "GameSaveHub\Client\Service\appsettings.local.json"
}
if (Test-Path -LiteralPath $config) {
    Write-Host "Configuration locale : $config"
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

# Present/absent uniquement : jamais son nom logique, qui n'est jamais affiché à l'utilisateur.
$managedSlotPath = Join-Path $env:ProgramData "GameSaveHub\managed-slot.json"
Write-Host "Slot local permanent : $(if (Test-Path -LiteralPath $managedSlotPath) { 'enregistré' } else { 'non configuré' })"

try {
    $health = Invoke-WebRequest -UseBasicParsing -Uri "https://saves.stevenpwlk.fr:18443/healthz"
    Write-Host "NAS /healthz : HTTP $($health.StatusCode) $($health.Content.Trim())"
}
catch {
    Write-Host "NAS /healthz : erreur $($_.Exception.Message)"
}

Write-Host "Ce script est en lecture seule et n'accède pas à WGS."
