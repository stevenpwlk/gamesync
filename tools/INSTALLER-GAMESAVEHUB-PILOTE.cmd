@echo off
setlocal
title Installation de GameSave Hub - version pilote

rem ---------------------------------------------------------------------------
rem Variante PILOTE : elle ouvre le verrou d ecriture local, donc ce PC pourra
rem reellement ecrire dans les sauvegardes du jeu.
rem
rem Ce fichier n existe que dans le package etiquete PILOTE, produit
rem deliberement par le build avec le commutateur -PilotTransfer. Le package
rem standard ne le contient pas et laisse le verrou ferme.
rem ---------------------------------------------------------------------------

echo.
echo   ==========================================
echo      GameSave Hub - installation PILOTE
echo   ==========================================
echo.
echo   Cette version peut ecrire dans vos sauvegardes.
echo   Elle ne remplace jamais un monde existant : elle
echo   ecrit uniquement dans un monde neuf que vous
echo   creerez vous-meme, en suivant les instructions.
echo.

net session >nul 2>&1
if %errorlevel% equ 0 goto :install

echo   Windows va demander l autorisation d installer.
echo   Repondez Oui a la fenetre bleue qui apparait.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process -FilePath '%~f0' -Verb RunAs"
if %errorlevel% neq 0 (
  echo.
  echo   L autorisation a ete refusee. Installation annulee.
  echo.
  pause
)
exit /b

:install
cd /d "%~dp0"

echo   Preparation des fichiers...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue"

echo   Installation en cours, merci de patienter...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALL-GAMESAVEHUB-CLIENT.ps1" -EnableWgsTransfer
set INSTALL_RESULT=%errorlevel%

echo.
if %INSTALL_RESULT% neq 0 (
  echo   ==========================================
  echo      L INSTALLATION A ECHOUE
  echo   ==========================================
  echo.
  echo   Faites une capture d ecran de cette fenetre
  echo   et envoyez-la a Steven.
  echo.
  pause
  exit /b %INSTALL_RESULT%
)

echo   ==========================================
echo      INSTALLATION TERMINEE
echo   ==========================================
echo.
echo   GameSave Hub va s ouvrir.
echo   Suivez les etapes affichees a l ecran.
echo.

start "" "C:\Program Files\GameSaveHub\Client\App\GameSaveHub.Client.App.exe"

timeout /t 6 >nul
exit /b 0
