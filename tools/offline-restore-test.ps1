[CmdletBinding()]
param(
    [string]$TestWorld = 'Shlags1',
    [string]$SourceSnapshot = 'snapshots\20260802T220955Z-add8fe0bb7fb48e2a480682eb47fe4f8',
    [string]$SnapshotRoot = 'snapshots'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$diagnostics = Join-Path $repoRoot 'src\GameSaveHub.Diagnostics\bin\Release\net10.0\GameSaveHub.Diagnostics.dll'
$source = [IO.Path]::GetFullPath((Join-Path $repoRoot $SourceSnapshot))
$snapshotOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $SnapshotRoot))
$reportRoot = Join-Path $repoRoot 'diagnostics-output'

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "Runtime .NET introuvable : $dotnet"
}
if (-not (Test-Path -LiteralPath $diagnostics -PathType Leaf)) {
    throw "Outil compile introuvable : $diagnostics"
}
if (-not (Test-Path -LiteralPath (Join-Path $source 'snapshot-manifest.json') -PathType Leaf)) {
    throw "Snapshot source introuvable ou incomplet : $source"
}

function Invoke-Diagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $dotnet $diagnostics @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "La commande de diagnostic a echoue (code $LASTEXITCODE) : $($Arguments -join ' ')"
    }
}

Write-Host ''
Write-Host '=== GameSave Hub : essai de restauration hors ligne ===' -ForegroundColor Cyan
Write-Host 'Ne reactivez pas Internet avant la fin complete de ce script.' -ForegroundColor Yellow
Write-Host ''

Invoke-Diagnostics -Arguments @('safety-status')

$beforeRestore = @(Get-ChildItem -LiteralPath $snapshotOutput -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
Invoke-Diagnostics -Arguments @(
    'restore-test-world',
    '--from-snapshot', $source,
    '--test-world', $TestWorld,
    '--backup-output', $snapshotOutput,
    '--acknowledge-test-world',
    '--acknowledge-offline'
)

$preRestoreSnapshot = Get-ChildItem -LiteralPath $snapshotOutput -Directory |
    Where-Object { $_.FullName -notin $beforeRestore } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

Write-Host ''
Write-Host 'Restauration locale terminee.' -ForegroundColor Green
Write-Host '1. Sans reactiver Internet, lancez The Planet Crafter.'
Write-Host "2. Chargez uniquement '$TestWorld'."
Write-Host '3. Verifiez si la position precedente est revenue et si les deux glaces ajoutees ont disparu.'
Write-Host '4. Quittez completement le jeu.'
Write-Host '5. Revenez dans cette fenetre PowerShell.'
Write-Host ''

$gameLaunched = Read-Host 'Apres fermeture du jeu : a-t-il demarre hors ligne ? (oui/non)'
$positionRestored = Read-Host 'La position precedente a-t-elle ete restauree ? (oui/non/inconnu)'
$iceRemoved = Read-Host 'Les deux glaces ajoutees ont-elles disparu ? (oui/non/inconnu)'

Invoke-Diagnostics -Arguments @('safety-status')

$beforeFinalSnapshot = @(Get-ChildItem -LiteralPath $snapshotOutput -Directory | Select-Object -ExpandProperty FullName)
Invoke-Diagnostics -Arguments @(
    'snapshot',
    '--output', $snapshotOutput,
    '--test-world', $TestWorld,
    '--acknowledge-test-world'
)

$finalSnapshot = Get-ChildItem -LiteralPath $snapshotOutput -Directory |
    Where-Object { $_.FullName -notin $beforeFinalSnapshot } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $finalSnapshot) {
    throw 'Le snapshot final est introuvable.'
}

Invoke-Diagnostics -Arguments @('validate-snapshot', $finalSnapshot.FullName)

New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$reportPath = Join-Path $reportRoot ("offline-restore-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
[ordered]@{
    schemaVersion = 1
    completedAt = (Get-Date).ToUniversalTime().ToString('o')
    testWorld = $TestWorld
    sourceSnapshot = $source
    preRestoreSnapshot = $preRestoreSnapshot.FullName
    finalSnapshot = $finalSnapshot.FullName
    gameLaunchedOffline = $gameLaunched
    positionRestored = $positionRestored
    addedIceRemoved = $iceRemoved
} | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host ''
Write-Host '=== Essai hors ligne termine et snapshot final valide ===' -ForegroundColor Green
Write-Host "Rapport : $reportPath"
Write-Host "Snapshot final : $($finalSnapshot.FullName)"
Write-Host "Vous pouvez maintenant reactiver Internet, mais ne relancez pas le jeu ni l'application Xbox avant analyse." -ForegroundColor Yellow
