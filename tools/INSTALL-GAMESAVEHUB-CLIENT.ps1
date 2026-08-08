param(
    [string]$ServerBaseUrl = "https://saves.stevenpwlk.fr:18443/",

    # Autorise ce PC à écrire dans les sauvegardes du jeu.
    # La valeur par défaut est et doit rester $false : ouvrir ce verrou est une
    # décision explicite, prise campagne de test par campagne de test. Seul le
    # package « PILOTE », produit délibérément par le build, passe ce commutateur.
    [bool]$EnableWgsTransfer = $false
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "L'installation doit être lancée depuis PowerShell en tant qu'administrateur."
    }
}

Assert-Administrator

$serviceName = "GameSaveHubClient"
$installRoot = Join-Path $env:ProgramFiles "GameSaveHub\Client"
$serviceRoot = Join-Path $installRoot "Service"
$appRoot = Join-Path $installRoot "App"
$programDataRoot = Join-Path $env:ProgramData "GameSaveHub"

$interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
if ([string]::IsNullOrWhiteSpace($interactiveUser)) {
    throw "Aucun utilisateur Windows interactif détecté."
}
$account = New-Object Security.Principal.NTAccount($interactiveUser)
$sid = $account.Translate([Security.Principal.SecurityIdentifier]).Value
if ($sid -in @("S-1-5-18", "S-1-5-19", "S-1-5-20")) {
    throw "Le compte joueur ne peut pas être LocalSystem/LocalService/NetworkService."
}

$profileKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
$profile = (Get-ItemProperty -LiteralPath $profileKey -Name ProfileImagePath).ProfileImagePath
$profile = [Environment]::ExpandEnvironmentVariables($profile)
$localAppData = Join-Path $profile "AppData\Local"
if (-not (Test-Path -LiteralPath $localAppData -PathType Container)) {
    throw "AppData\Local introuvable pour $interactiveUser : $localAppData"
}

Write-Host "Utilisateur joueur : $interactiveUser"
Write-Host "SID joueur         : $sid"
Write-Host "Profil AppData      : $localAppData"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Write-Host "Arrêt de l'ancienne version du service..."
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $serviceRoot, $appRoot, $programDataRoot | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "Service\*") -Destination $serviceRoot -Force -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot "App\*") -Destination $appRoot -Force -Recurse

$config = @{
    ClientService = @{
        PipeName = "GameSaveHub.Client"
        RegisteredUserSid = $sid
        RegisteredUserLocalAppData = $localAppData
        ServerBaseUrl = $ServerBaseUrl
        StatePath = "%ProgramData%\GameSaveHub\client-state.json"
        TransferRootPath = "%ProgramData%\GameSaveHub\transfers"
        CngKeyName = "GameSaveHub.DeviceIdentity"
        EnableWgsTransfer = $EnableWgsTransfer
    }
}
$configPath = Join-Path $serviceRoot "appsettings.local.json"
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding UTF8

$serviceExe = Join-Path $serviceRoot "GameSaveHub.Client.Service.exe"
$appExe = Join-Path $appRoot "GameSaveHub.Client.App.exe"
if (-not (Test-Path -LiteralPath $serviceExe)) { throw "EXE service absent : $serviceExe" }
if (-not (Test-Path -LiteralPath $appExe)) { throw "EXE application absent : $appExe" }

New-Service `
    -Name $serviceName `
    -BinaryPathName "`"$serviceExe`"" `
    -DisplayName "GameSave Hub Client" `
    -Description "Service local sécurisé GameSave Hub pour The Planet Crafter." `
    -StartupType Automatic | Out-Null

& sc.exe config $serviceName start= delayed-auto | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/""/0 | Out-Null

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(20))

$startMenu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenu "GameSave Hub.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appExe
$shortcut.WorkingDirectory = $appRoot
$shortcut.Description = "GameSave Hub"
$shortcut.Save()

Write-Host ""
Write-Host "INSTALLATION RÉUSSIE" -ForegroundColor Green
Write-Host "Service : $serviceName / Running"
Write-Host "Application : $appExe"
Write-Host "Raccourci : $shortcutPath"
Write-Host "Écriture WGS locale : DÉSACTIVÉE (EnableWgsTransfer=false)" -ForegroundColor Green
Write-Host "Le prochain test peut enrôler le PC et lire le catalogue NAS sans importer de sauvegarde."
