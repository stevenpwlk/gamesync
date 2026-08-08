param(
    # Produit EN PLUS du package standard une variante PILOTE dont l installateur
    # ouvre le verrou d ecriture WGS. Le package standard reste inchange et ferme.
    # Ouvrir le verrou doit rester un acte volontaire : d ou un commutateur
    # explicite, jamais une valeur par defaut.
    [switch]$PilotTransfer
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Invoke-DotnetStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    Write-Host "`n=== $Label ===" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "La commande dotnet a echoue pendant : $Label"
    }
}

function Test-SourceManifest {
    $manifestPath = Join-Path $repo 'SOURCE-SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw 'SOURCE-SHA256SUMS.txt est absent.'
    }

    $count = 0
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-fA-F]{64}) \*(.+)$') {
            throw "Ligne de manifeste invalide : $line"
        }
        $expected = $Matches[1].ToLowerInvariant()
        $relative = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
        $path = Join-Path $repo $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Fichier source absent : $relative"
        }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "Hash source invalide : $relative"
        }
        $count++
    }
    Write-Host "$count fichiers source : hashes valides"
}

function Test-Phase3Guards {
    Write-Host "`n=== Garde-fous statiques Phase 3 ===" -ForegroundColor Cyan

    $unitFiles = Get-ChildItem -LiteralPath (Join-Path $repo 'tests\Unit') -Filter '*.cs' -File
    $facts = 0
    $inline = 0
    foreach ($file in $unitFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $facts += ([regex]::Matches($text, '\[Fact\]')).Count
        $inline += ([regex]::Matches($text, '\[InlineData')).Count
    }
    $cases = $facts + $inline
    # Plancher de non-regression : le nombre de cas peut augmenter, jamais diminuer.
    $minimumCases = 70
    if ($cases -lt $minimumCases) {
        throw "Regression de couverture : $cases cas pour un plancher de $minimumCases."
    }
    Write-Host "$cases cas de test unitaires declares (plancher $minimumCases)"

    $apiSettings = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Server.Api\appsettings.json') -Raw
    $portainer = Get-Content -LiteralPath (Join-Path $repo 'deploy\compose.portainer.yml') -Raw
    $compose = Get-Content -LiteralPath (Join-Path $repo 'deploy\compose.yml') -Raw
    $serviceSettings = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.Service\appsettings.json') -Raw
    $installer = Get-Content -LiteralPath (Join-Path $repo 'tools\INSTALL-GAMESAVEHUB-CLIENT.ps1') -Raw

    if ($apiSettings -notmatch '"AllowHostTransfer"\s*:\s*false') { throw 'Feature gate serveur API ouvert par erreur.' }
    if ($portainer -notmatch 'FeatureGates__AllowHostTransfer:\s*"false"') { throw 'Feature gate serveur Portainer ouvert par erreur.' }
    if ($compose -notmatch 'GSH_ALLOW_HOST_TRANSFER:-false') { throw 'Valeur par defaut du feature gate Docker modifiee.' }
    if ($serviceSettings -notmatch '"EnableWgsTransfer"\s*:\s*false') { throw 'Gate WGS local ouvert dans appsettings.' }
    # Le verrou local doit venir du commutateur, jamais d une valeur en dur.
    # Un [switch] est absent par defaut : c est ce qui garantit un package standard ferme.
    if ($installer -notmatch '\[switch\]\$EnableWgsTransfer') {
        throw 'Le verrou WGS local n est plus un commutateur : le defaut ferme n est plus garanti.'
    }
    if ($installer -notmatch 'EnableWgsTransfer = \[bool\]\$EnableWgsTransfer') {
        throw 'Le verrou WGS local n est pas alimente par le parametre.'
    }
    if ($installer -match 'EnableWgsTransfer\s*=\s*\$true') {
        throw 'Verrou WGS local ouvert en dur dans l installateur.'
    }
    Write-Host 'Double gate : serveur=false et client WGS=false'

    $contracts = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Contracts\ApiContracts.cs') -Raw
    $compatibility = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Contracts\PlayerCompatibilityRules.cs') -Raw
    $server = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Server.Api\Program.cs') -Raw
    $validator = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Server.Infrastructure\ArtifactEnvelopeValidator.cs') -Raw
    $pipe = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.Service\PipeServerWorker.cs') -Raw
    $serviceProgram = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.Service\Program.cs') -Raw
    $identity = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.Service\DeviceIdentity.cs') -Raw
    $httpClient = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.Service\AuthenticatedTransferServerClient.cs') -Raw

    foreach ($required in @('WorldCatalogItemResponse', 'WorldPreviewResponse', 'WorldPreviewPlayerResponse')) {
        if ($contracts -notmatch $required) { throw "Contrat Phase 3 absent : $required" }
    }
    if ($compatibility -notmatch 'PlayerNotFound' -or $compatibility -notmatch 'PlayerAmbiguous') {
        throw 'Garde-fou pseudo absent/ambigu absent.'
    }
    if ($server -notmatch 'MapGet\("/worlds"' -or $server -notmatch 'MapGet\("/worlds/\{id:guid\}/preview"') {
        throw 'Endpoints catalogue/preview absents.'
    }
    if ($validator -notmatch 'ReadSummaryAsync' -or $validator -notmatch 'ArtifactEnvelopePlayerSummary') {
        throw 'Lecture securisee du manifeste serveur absente.'
    }
    if ($pipe -notmatch '"preflight"' -or $pipe -notmatch 'local_transfer_gate_closed') {
        throw 'Preflight ou gate WGS local absent du service.'
    }
    if ($pipe -notmatch 'PlayerCompatibilityRules\.Evaluate') {
        throw 'Validation du pseudo serveur absente.'
    }
    # Windows refuse ImpersonateNamedPipeClient tant qu aucune donnee n a ete lue sur le
    # canal. Verifier le SID avant la lecture faisait echouer toutes les connexions.
    # L ordre lecture -> controle du SID -> execution doit donc etre preserve.
    $readIndex = $pipe.IndexOf('ReadLineAsync')
    $sidIndex = $pipe.IndexOf('GetConnectedSid(pipe)')
    $dispatchIndex = $pipe.IndexOf('DispatchAsync(request')
    if ($readIndex -lt 0 -or $sidIndex -lt 0 -or $dispatchIndex -lt 0) {
        throw 'Sequence de traitement du named pipe introuvable.'
    }
    if ($sidIndex -lt $readIndex) {
        throw 'Le controle du SID precede la lecture du canal : toutes les connexions echoueront.'
    }
    if ($dispatchIndex -lt $sidIndex) {
        throw 'Une commande serait executee avant le controle du SID appelant.'
    }
    # Le service est publie en executable mono-fichier. Une assembly chargee pour la
    # premiere fois A L INTERIEUR de RunAsClient echoue (FileNotFoundException sur
    # System.Security.Claims) : le prechargement hors usurpation est obligatoire.
    $preloadIndex = $pipe.IndexOf('PreloadIdentityAssemblies();')
    if ($preloadIndex -lt 0) {
        throw 'Prechargement des assemblies d identite absent : le controle du SID echouera en mono-fichier.'
    }
    if ($preloadIndex -gt $sidIndex) {
        throw 'Le prechargement des assemblies d identite doit preceder tout controle du SID.'
    }
    if ($serviceProgram -notmatch 'appsettings\.local\.json') {
        throw 'Chargement de la configuration locale installateur absent.'
    }
    if ($identity -notmatch 'CngExportPolicies\.None' -or $identity -notmatch 'CngKeyCreationOptions\.MachineKey') {
        throw 'Identite CNG persistante non exportable absente.'
    }
    if ($httpClient -notmatch 'ListWorldsAsync' -or $httpClient -notmatch 'GetWorldPreviewAsync') {
        throw 'Client HTTP catalogue/preview absent.'
    }
    Write-Host 'Catalogue NAS, preview, pseudo obligatoire et identite CNG : presents'

    $migrations = Get-ChildItem -LiteralPath (Join-Path $repo 'src\GameSaveHub.Server.Infrastructure\Migrations') -Filter '*.cs' -File |
        Where-Object { $_.Name -notmatch '\.Designer\.cs$' -and $_.Name -ne 'GameSaveHubDbContextModelSnapshot.cs' }
    if ($migrations.Count -ne 4) {
        throw "Nombre de migrations EF inattendu : $($migrations.Count) (4 attendu, aucune migration Phase 3)."
    }
    Write-Host 'Base SQLite : aucune nouvelle migration Phase 3'

    foreach ($bad in @('DangerousAcceptAnyServerCertificateValidator', 'ServerCertificateCustomValidationCallback')) {
        if ($httpClient -match $bad) { throw "Bypass TLS interdit detecte : $bad" }
    }
    if ($httpClient -notmatch 'EnsureMutationIdempotency\(request\)') {
        throw 'Idempotence HTTP r3 absente.'
    }

    $badTestNames = @()
    foreach ($file in $unitFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, 'public\s+(?:async\s+)?(?:Task|void)\s+([A-Za-z0-9]+_[A-Za-z0-9_]+)\s*\(')) {
            $badTestNames += $match.Groups[1].Value
        }
    }
    if ($badTestNames.Count -gt 0) {
        throw "Noms de tests incompatibles avec CA1707 : $($badTestNames -join ', ')"
    }

    if ($installer -notmatch 'ProfileList' -or $installer -notmatch 'RegisteredUserSid' -or $installer -notmatch 'GameSaveHubClient') {
        throw 'Installateur service/SID incomplet.'
    }

    $clientMainWindow = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.App\MainWindow.xaml.cs') -Raw
    if ($clientMainWindow -match 'GetInt64\(\)\.ToString\(\)' -or $clientMainWindow -match 'GetInt32\(\)\.ToString\(\)') {
        throw 'CA1305 : valeur numerique formatee sans culture explicite dans MainWindow.'
    }
    if ($clientMainWindow -notmatch 'CultureInfo\.InvariantCulture') {
        throw 'Formatage invariant absent dans MainWindow.'
    }

    # Regression constatee en Phase 3 : l'IHM ne cablait plus qu'une des six commandes de
    # transfert, rendant impossible d'aller au bout d'un import. Le presentateur porte
    # desormais ces commandes et l'ecran doit s'appuyer sur lui.
    $presenterPath = Join-Path $repo 'src\GameSaveHub.Client.Orchestration\TransferWizardPresenter.cs'
    if (-not (Test-Path -LiteralPath $presenterPath)) {
        throw 'TransferWizardPresenter absent : l assistant de transfert ne peut pas etre rendu.'
    }
    $presenter = Get-Content -LiteralPath $presenterPath -Raw
    foreach ($command in @(
        'transfer-start',
        'transfer-placeholder-ready',
        'transfer-play-started',
        'transfer-play-complete',
        'transfer-resume',
        'transfer-abort')) {
        if ($presenter -notmatch [regex]::Escape($command)) {
            throw "Commande de transfert absente de l assistant : $command"
        }
    }
    if ($clientMainWindow -notmatch 'TransferWizardPresenter') {
        throw 'MainWindow n utilise pas TransferWizardPresenter : les etapes de transfert ne seraient pas pilotables.'
    }

    # Une exception non geree dans un gestionnaire async void fermait l application sans message.
    if ($clientMainWindow -notmatch 'GuardAsync') {
        throw 'Gestionnaires IHM sans enveloppe de gestion d erreur.'
    }
    $appXaml = Get-Content -LiteralPath (Join-Path $repo 'src\GameSaveHub.Client.App\App.xaml') -Raw
    if ($appXaml -notmatch 'DispatcherUnhandledException') {
        throw 'Filet de securite global absent de App.xaml.'
    }

    # Windows PowerShell 5.1 lit un .ps1 sans BOM comme de l ANSI : tout accent UTF-8
    # y devient illisible. Les scripts destines a l utilisateur final doivent donc
    # porter un BOM, sans quoi INSTALLATION REUSSIE s affiche mutile a distance.
    # Ce script-ci reste volontairement en ASCII pur, comme le reste de ses messages.
    $bom = [byte[]](0xEF, 0xBB, 0xBF)
    foreach ($script in Get-ChildItem -LiteralPath (Join-Path $repo 'tools') -Filter '*.ps1' -File) {
        $bytes = [IO.File]::ReadAllBytes($script.FullName)
        $hasNonAscii = $bytes | Where-Object { $_ -gt 127 } | Select-Object -First 1
        if (-not $hasNonAscii) { continue }
        $startsWithBom = $bytes.Length -ge 3 -and
                         $bytes[0] -eq $bom[0] -and $bytes[1] -eq $bom[1] -and $bytes[2] -eq $bom[2]
        if (-not $startsWithBom) {
            throw "Script accentue sans BOM UTF-8 : $($script.Name). Les accents seraient illisibles sous PowerShell 5.1."
        }
    }

    Write-Host 'Assistant de transfert complet, gestion d erreur, encodage des scripts et culture numerique : valides'
}

Write-Host "`n=== GameSave Hub - Integrated Client Phase 3 / 0.3.0 r2 ===" -ForegroundColor Cyan
$version = (& dotnet --version).Trim()
if (-not $version.StartsWith('10.')) {
    throw "SDK .NET 10 requis. Version detectee : $version"
}
Write-Host "SDK detecte : $version"

Test-SourceManifest
Test-Phase3Guards

Invoke-DotnetStep '1/6 Restauration' @('restore', '.\GameSaveHub.slnx')
Invoke-DotnetStep '2/6 Compilation complete' @('build', '.\GameSaveHub.slnx', '--configuration', 'Release', '--no-restore')
Invoke-DotnetStep '3/6 Tests unitaires' @('test', '.\tests\Unit\GameSaveHub.UnitTests.csproj', '--configuration', 'Release', '--no-build', '--verbosity', 'normal')
Invoke-DotnetStep '4/6 Capacites adapter' @('run', '--project', '.\src\GameSaveHub.Diagnostics\GameSaveHub.Diagnostics.csproj', '--configuration', 'Release', '--no-build', '--', 'capabilities')

Write-Host "`n=== 5/6 Publication client Windows ===" -ForegroundColor Cyan
$artifactRoot = Join-Path $repo 'artifacts'
$clientPackage = Join-Path $artifactRoot 'GameSaveHub-Client-Phase3-0.3.0'
$serviceOut = Join-Path $clientPackage 'Service'
$appOut = Join-Path $clientPackage 'App'
if (Test-Path -LiteralPath $clientPackage) { Remove-Item -LiteralPath $clientPackage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $serviceOut, $appOut | Out-Null

& dotnet publish '.\src\GameSaveHub.Client.Service\GameSaveHub.Client.Service.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $serviceOut
if ($LASTEXITCODE -ne 0) { throw 'Publication du service Windows echouee.' }
& dotnet publish '.\src\GameSaveHub.Client.App\GameSaveHub.Client.App.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'Publication de l application WPF echouee.' }

Copy-Item -LiteralPath '.\src\GameSaveHub.Client.Service\appsettings.json' -Destination (Join-Path $serviceOut 'appsettings.json') -Force
Copy-Item -LiteralPath '.\tools\INSTALLER-GAMESAVEHUB.cmd' -Destination $clientPackage -Force
Copy-Item -LiteralPath '.\tools\LISEZ-MOI-DABORD.txt' -Destination $clientPackage -Force
Copy-Item -LiteralPath '.\tools\INSTALL-GAMESAVEHUB-CLIENT.ps1' -Destination $clientPackage -Force
Copy-Item -LiteralPath '.\tools\UNINSTALL-GAMESAVEHUB-CLIENT.ps1' -Destination $clientPackage -Force
Copy-Item -LiteralPath '.\tools\STATUS-GAMESAVEHUB-CLIENT.ps1' -Destination $clientPackage -Force
Copy-Item -LiteralPath '.\docs\operations\PHASE3-INTEGRATED-CLIENT.md' -Destination (Join-Path $clientPackage 'README-PHASE3.md') -Force

$clientZip = Join-Path $artifactRoot 'GameSaveHub-Client-Phase3-0.3.0-win-x64.zip'
if (Test-Path -LiteralPath $clientZip) { Remove-Item -LiteralPath $clientZip -Force }
Compress-Archive -Path (Join-Path $clientPackage '*') -DestinationPath $clientZip -CompressionLevel Optimal
$clientHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $clientZip).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($clientZip + '.sha256') -Encoding ASCII -Value "$clientHash *$(Split-Path -Leaf $clientZip)"
Write-Host "Client ZIP : $clientZip"
Write-Host "SHA-256    : $clientHash"

if ($PilotTransfer) {
    Write-Host "`n=== Variante PILOTE (verrou d ecriture ouvert) ===" -ForegroundColor Yellow

    $pilotPackage = Join-Path $artifactRoot 'GameSaveHub-Client-Phase3-0.3.0-PILOTE'
    if (Test-Path -LiteralPath $pilotPackage) { Remove-Item -LiteralPath $pilotPackage -Recurse -Force }
    Copy-Item -LiteralPath $clientPackage -Destination $pilotPackage -Recurse -Force

    # Le lanceur standard ne doit pas subsister dans la variante pilote : deux
    # installateurs cote a cote inviteraient a se tromper de fichier.
    Remove-Item -LiteralPath (Join-Path $pilotPackage 'INSTALLER-GAMESAVEHUB.cmd') -Force
    Copy-Item -LiteralPath '.\tools\INSTALLER-GAMESAVEHUB-PILOTE.cmd' -Destination $pilotPackage -Force
    Copy-Item -LiteralPath '.\tools\LISEZ-MOI-PILOTE.txt' -Destination (Join-Path $pilotPackage 'LISEZ-MOI-DABORD.txt') -Force

    $pilotZip = Join-Path $artifactRoot 'GameSaveHub-Client-Phase3-0.3.0-PILOTE-win-x64.zip'
    if (Test-Path -LiteralPath $pilotZip) { Remove-Item -LiteralPath $pilotZip -Force }
    Compress-Archive -Path (Join-Path $pilotPackage '*') -DestinationPath $pilotZip -CompressionLevel Optimal
    $pilotHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pilotZip).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($pilotZip + '.sha256') -Encoding ASCII -Value "$pilotHash *$(Split-Path -Leaf $pilotZip)"

    # Garde-fou croise : verifier que chaque package porte bien le verrou attendu.
    $pilotCmd = Get-Content -LiteralPath (Join-Path $pilotPackage 'INSTALLER-GAMESAVEHUB-PILOTE.cmd') -Raw
    if ($pilotCmd -notmatch 'INSTALL-GAMESAVEHUB-CLIENT\.ps1" -EnableWgsTransfer') {
        throw 'La variante pilote n ouvre pas le verrou : elle serait identique au package standard.'
    }
    if (Test-Path -LiteralPath (Join-Path $clientPackage 'INSTALLER-GAMESAVEHUB-PILOTE.cmd')) {
        throw 'Le package standard contient le lanceur pilote : le verrou pourrait etre ouvert par erreur.'
    }

    Write-Host "Package PILOTE : $pilotZip"
    Write-Host "SHA-256        : $pilotHash"
    Write-Host 'ATTENTION : ce package autorise l ecriture dans les sauvegardes du jeu.' -ForegroundColor Yellow
}

Write-Host "`n=== 6/6 Contexte Docker API 0.3.0 ===" -ForegroundColor Cyan
$apiTar = Join-Path $artifactRoot 'GameSaveHub-API-0.3.0-Portainer-Build-Context.tar'
if (Test-Path -LiteralPath $apiTar) { Remove-Item -LiteralPath $apiTar -Force }

# Cette etape arrive APRES la compilation : src/**/bin et src/**/obj sont donc pleins.
# tar n'applique pas .dockerignore, et Portainer doit recevoir puis analyser tout le
# contexte avant meme de lancer Docker. Sans exclusion explicite le contexte atteint
# ~500 Mo au lieu de ~570 Ko, ce qui a fait echouer le build Portainer du 8 aout 2026.
& tar.exe -cf $apiTar --exclude='bin' --exclude='obj' '.dockerignore' 'global.json' 'Directory.Build.props' 'src'
if ($LASTEXITCODE -ne 0) { throw 'Creation du contexte Docker API echouee.' }

# Garde-fou : refuser un contexte anormal plutot que de le decouvrir dans Portainer.
$apiTarBytes = (Get-Item -LiteralPath $apiTar).Length
$maxContextBytes = 20MB
if ($apiTarBytes -gt $maxContextBytes) {
    throw "Contexte Docker anormal : $([math]::Round($apiTarBytes / 1MB, 1)) Mo (limite $($maxContextBytes / 1MB) Mo). Des artefacts de compilation sont probablement inclus."
}
$apiTarEntries = & tar.exe -tf $apiTar
if ($LASTEXITCODE -ne 0) { throw 'Relecture du contexte Docker API echouee.' }
$polluted = $apiTarEntries | Where-Object { $_ -match '(^|/)(bin|obj)/' }
if ($polluted) {
    throw "Contexte Docker pollue par des artefacts de compilation : $($polluted[0])"
}
if (-not ($apiTarEntries -contains 'src/GameSaveHub.Server.Api/Dockerfile')) {
    throw 'Dockerfile absent du contexte Docker API.'
}

$apiHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $apiTar).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($apiTar + '.sha256') -Encoding ASCII -Value "$apiHash *$(Split-Path -Leaf $apiTar)"
Write-Host "API build context : $apiTar"
Write-Host "Taille            : $apiTarBytes octets"
Write-Host "SHA-256           : $apiHash"

Write-Host "`nVALIDATION PHASE 3 TERMINEE" -ForegroundColor Green
Write-Host 'Attendu : 0 echec, au moins 70 cas de test executes.'
Write-Host 'Attendu : canPrepareForHost=true, canImportPortableArtifact=true, canLaunchGame=false.'
Write-Host 'IMPORTANT : FeatureGates__AllowHostTransfer=false ET EnableWgsTransfer=false.'
Write-Host 'Ce build ne contacte pas le NAS et n ecrit pas dans WGS.'
