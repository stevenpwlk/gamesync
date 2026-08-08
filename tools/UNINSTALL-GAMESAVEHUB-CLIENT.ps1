$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "La désinstallation doit être lancée en tant qu'administrateur."
}

$serviceName = "GameSaveHubClient"
$installRoot = Join-Path $env:ProgramFiles "GameSaveHub\Client"
$shortcutPath = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\GameSave Hub.lnk"

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}
if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Force -Recurse
}

Write-Host "Application et service supprimés." -ForegroundColor Green
Write-Host "CONSERVÉ volontairement : %ProgramData%\GameSaveHub et la clé CNG machine."
Write-Host "Révoquez d'abord l'appareil côté NAS avant tout nettoyage définitif d'identité."
