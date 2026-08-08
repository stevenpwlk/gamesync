$ErrorActionPreference = "Stop"
$hostName = "saves.stevenpwlk.fr"
$port = 18443
$base = "https://${hostName}:$port"
$fakeWorld = [guid]::NewGuid().ToString("D")

Write-Host "=== GameSave Hub NAS Phase 3 - validation lecture seule ===" -ForegroundColor Cyan

Write-Host "`n1/4 DNS et port..."
$dns = [System.Net.Dns]::GetHostAddresses($hostName) | Select-Object -First 1
Write-Host "DNS : $dns"
$tcp = Test-NetConnection -ComputerName $hostName -Port $port -WarningAction SilentlyContinue
if (-not $tcp.TcpTestSucceeded) { throw "TCP $port inaccessible." }
Write-Host "TCP $port : OK"

Write-Host "`n2/4 Health..."
$health = Invoke-RestMethod -Uri "$base/healthz" -Method Get
if ($health -ne "Healthy") { throw "Health inattendu : $health" }
Write-Host "Health : Healthy"

function Assert-Unauthorized([string]$Uri, [string]$Label) {
    try {
        Invoke-WebRequest -Uri $Uri -Method Get -UseBasicParsing | Out-Null
        throw "$Label : la route n'a pas exige d'authentification."
    }
    catch {
        $status = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        if ($status -ne 401) {
            throw "$Label : HTTP $status au lieu de 401."
        }
        Write-Host "$Label : 401 attendu sans authentification -> OK"
    }
}

Write-Host "`n3/4 Catalogue protege..."
Assert-Unauthorized "$base/api/v1/worlds" "/api/v1/worlds"

Write-Host "`n4/4 Preview protege..."
Assert-Unauthorized "$base/api/v1/worlds/$fakeWorld/preview" "/api/v1/worlds/{id}/preview"

Write-Host ""
Write-Host "VALIDATION REUSSIE : API Phase 3 joignable et nouvelles routes protegees." -ForegroundColor Green
Write-Host "Ce script ne modifie ni SQLite, ni les artefacts, ni WGS."
