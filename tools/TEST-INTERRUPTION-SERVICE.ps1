#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Coupe le service GameSave Hub à un instant choisi, puis le relance.

.DESCRIPTION
    Sert à produire la preuve « reprise après interruption » du go/no-go, qui exige
    de couper le service PENDANT une phase précise — l'envoi ou l'import — et non
    entre deux étapes.

    Le script doit être lancé en administrateur, puis il attend. L'arrêt est
    déclenché par la simple apparition d'un fichier signal, ce qui permet de choisir
    le moment sans disposer de droits d'élévation :

        %ProgramData%\GameSaveHub\interrupt.trigger

    Il enregistre les horodatages exacts de l'arrêt et du redémarrage, ainsi que
    l'état de la session locale avant et après. Ces relevés sont la preuve, pas le
    ressenti.

.PARAMETER DowntimeSeconds
    Durée pendant laquelle le service reste arrêté. 25 s par défaut : au-delà des
    90 s du watchdog serveur la session passerait en Interrupted côté serveur aussi,
    ce qui est un autre scénario.

.PARAMETER TimeoutMinutes
    Abandon de l'attente si aucun signal n'arrive.
#>
param(
    [int]$DowntimeSeconds = 25,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'

$serviceName = 'GameSaveHubClient'
$root = Join-Path $env:ProgramData 'GameSaveHub'
$trigger = Join-Path $root 'interrupt.trigger'
$transcript = Join-Path $root ("interruption-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))

function Get-LocalSessions {
    $transfers = Join-Path $root 'transfers'
    if (-not (Test-Path -LiteralPath $transfers)) { return @() }
    Get-ChildItem -LiteralPath $transfers -Directory | ForEach-Object {
        $file = Join-Path $_.FullName 'session.json'
        if (Test-Path -LiteralPath $file) {
            $s = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Session  = $s.localSessionId
                Etape    = $s.stage
                Revision = $s.revision
                Upload   = $s.uploadId
                Chunks   = ($s.confirmedChunks -join ',')
                Version  = $s.resultVersionId
            }
        }
    }
}

function Write-Both([string]$text) {
    Write-Host $text
    Add-Content -LiteralPath $transcript -Value $text -Encoding UTF8
}

New-Item -ItemType Directory -Force -Path $root | Out-Null
if (Test-Path -LiteralPath $trigger) { Remove-Item -LiteralPath $trigger -Force }

Write-Both "=== Essai d'interruption GameSave Hub ==="
Write-Both "Journal : $transcript"
Write-Both ""
Write-Both "Le service sera arrete des l'apparition du fichier signal, puis relance"
Write-Both "apres $DowntimeSeconds secondes."
Write-Both ""
Write-Both "EN ATTENTE DU SIGNAL. Laissez cette fenetre ouverte et poursuivez le transfert."
Write-Both ""

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path -LiteralPath $trigger)) {
    if ((Get-Date) -gt $deadline) {
        Write-Both "Aucun signal recu avant expiration. Rien n'a ete touche."
        exit 2
    }
    Start-Sleep -Milliseconds 400
}
Remove-Item -LiteralPath $trigger -Force -ErrorAction SilentlyContinue

Write-Both "--- Etat AVANT interruption ---"
Get-LocalSessions | Format-List | Out-String | ForEach-Object { Write-Both $_ }

$stopAt = Get-Date
Write-Both ("Arret du service a {0:HH:mm:ss.fff}" -f $stopAt)
Stop-Service -Name $serviceName -Force
(Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
Write-Both ("Service arrete a {0:HH:mm:ss.fff}" -f (Get-Date))

Start-Sleep -Seconds $DowntimeSeconds

Write-Both "--- Etat PENDANT l'arret (checkpoint sur disque) ---"
Get-LocalSessions | Format-List | Out-String | ForEach-Object { Write-Both $_ }

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(40))
$startAt = Get-Date
Write-Both ("Service redemarre a {0:HH:mm:ss.fff} (indisponible {1:n1} s)" -f $startAt, ($startAt - $stopAt).TotalSeconds)

Start-Sleep -Seconds 10
Write-Both "--- Etat APRES redemarrage ---"
Get-LocalSessions | Format-List | Out-String | ForEach-Object { Write-Both $_ }

Write-Both "=== Essai termine ==="
Write-Both "Envoyez ce journal, puis generez un rapport depuis l'application."
