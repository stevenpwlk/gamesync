@echo off
setlocal
title Installation de GameSave Hub

rem ---------------------------------------------------------------------------
rem Installateur en un clic, destine a un joueur qui ne touchera jamais a
rem PowerShell. Il demande lui-meme les droits administrateur, leve le blocage
rem « fichier telecharge depuis Internet », lance l installation puis ouvre
rem l application. Aucune saisie n est demandee.
rem
rem Le compte joueur enregistre est celui de la session Windows ouverte, pas
rem celui qui valide l invite de securite : valider avec un autre compte
rem administrateur ne fausse donc pas l installation.
rem ---------------------------------------------------------------------------

echo.
echo   ==========================================
echo      GameSave Hub - installation
echo   ==========================================
echo.

rem --- Deja administrateur ? ---
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
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALL-GAMESAVEHUB-CLIENT.ps1"
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
echo   Vous le retrouverez ensuite dans le menu Demarrer.
echo.

start "" "C:\Program Files\GameSaveHub\Client\App\GameSaveHub.Client.App.exe"

rem La fenetre ne se referme pas seule : les dernieres lignes indiquent si ce PC
rem peut ecrire dans les sauvegardes, et cet etat doit avoir ete lu avant qu on
rem entame un transfert.
echo.
pause
exit /b 0
