$ErrorActionPreference = "Stop"

$base = "https://saves.stevenpwlk.fr:18443"
Write-Host "=== GameSave Hub NAS Phase 2 r2 - validation lecture seule ===" -ForegroundColor Cyan

Write-Host "`n1/3 DNS et port..."
$dns = Resolve-DnsName saves.stevenpwlk.fr -Type A -ErrorAction Stop | Select-Object -First 1
Write-Host ("DNS : " + $dns.IPAddress)
$tcp = Test-NetConnection saves.stevenpwlk.fr -Port 18443 -WarningAction SilentlyContinue
if (-not $tcp.TcpTestSucceeded) { throw "TCP 18443 inaccessible." }
Write-Host "TCP 18443 : OK" -ForegroundColor Green

Write-Host "`n2/3 Health..."
$health = Invoke-WebRequest -UseBasicParsing -Uri "$base/healthz" -Method Get
if ($health.StatusCode -ne 200 -or $health.Content.Trim() -ne "Healthy") {
    throw "Health inattendu : HTTP $($health.StatusCode) / '$($health.Content)'"
}
Write-Host "Health : Healthy" -ForegroundColor Green

Write-Host "`n3/3 Route API protegee..."
$world = "00000000-0000-0000-0000-000000000000"
try {
    Invoke-WebRequest -UseBasicParsing -Uri "$base/api/v1/worlds/$world/status" -Method Get | Out-Null
    throw "La route protegee a accepte une requete sans authentification."
}
catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { throw }
    $status = [int]$response.StatusCode
    if ($status -ne 401) {
        throw "La route API repond HTTP $status au lieu de 401."
    }
    Write-Host "Route /api/v1 : 401 attendu sans authentification -> OK" -ForegroundColor Green
}

Write-Host "`nVALIDATION REUSSIE : transport TLS, health et routage API sont operationnels." -ForegroundColor Green
Write-Host "Ce script n'ecrit rien dans la base et ne lance aucun transfert."
