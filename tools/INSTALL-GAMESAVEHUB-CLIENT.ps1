param(
    [string]$ServerBaseUrl = "https://saves.stevenpwlk.fr:18443/",

    # Autorise ce PC à écrire dans les sauvegardes du jeu.
    # Commutateur, donc absent par défaut : ouvrir ce verrou est une décision
    # explicite, prise campagne de test par campagne de test. Seul le package
    # « PILOTE », produit délibérément par le build, le passe.
    # Un [bool] serait ici un piège : appelé depuis un .cmd, « -EnableWgsTransfer $true »
    # transmet la chaîne littérale « $true », que PowerShell refuse de convertir.
    [switch]$EnableWgsTransfer
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

# État du verrou d'écriture AVANT cette installation. Deux packages coexistent, aux
# noms très proches : installer le standard par-dessus le pilote referme le verrou
# sans que rien ne le signale, et le blocage n'apparaît qu'une fois le joueur engagé
# dans un transfert. On le relève ici pour pouvoir l'annoncer explicitement.
$configPath = Join-Path $serviceRoot "appsettings.local.json"
$previousGate = $null
if (Test-Path -LiteralPath $configPath) {
    try {
        $previousGate = [bool](Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).ClientService.EnableWgsTransfer
    }
    catch {
        $previousGate = $null
    }
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
        EnableWgsTransfer = [bool]$EnableWgsTransfer
    }
}
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
if ($EnableWgsTransfer) {
    Write-Host "Écriture des sauvegardes : ACTIVÉE sur ce PC (EnableWgsTransfer=true)" -ForegroundColor Yellow
    Write-Host "Ce PC pourra écrire dans un monde neuf que vous créerez vous-même. Suivez l'assistant sans sauter d'étape."
}
else {
    Write-Host "Écriture des sauvegardes : DÉSACTIVÉE (EnableWgsTransfer=false)" -ForegroundColor Green
    Write-Host "Ce PC peut enrôler, lire le catalogue et vérifier la compatibilité, sans rien écrire."
}

# Un changement d'état du verrou n'est jamais anodin : il décide si ce PC peut, ou
# non, écrire dans les sauvegardes du joueur. Le refermer par inadvertance bloque
# un transfert en cours de campagne, et le message ci-dessus passe inaperçu quand
# on ne le cherche pas.
if ($null -ne $previousGate -and $previousGate -ne [bool]$EnableWgsTransfer) {
    Write-Host ""
    if ($previousGate) {
        Write-Host "ATTENTION : L'ÉCRITURE VIENT D'ÊTRE REFERMÉE SUR CE PC." -ForegroundColor Red
        Write-Host "Elle était autorisée avant cette installation. Vous venez d'installer le"
        Write-Host "package STANDARD par-dessus le package PILOTE."
        Write-Host "Si vous vouliez la conserver, relancez INSTALLER-GAMESAVEHUB-PILOTE.cmd."
    }
    else {
        Write-Host "L'écriture des sauvegardes vient d'être AUTORISÉE sur ce PC." -ForegroundColor Yellow
        Write-Host "Elle était fermée avant cette installation."
    }
}
