@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo Construction et validation de GameSave Hub - Integrated Client Phase 3 / 0.3.0 r2
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build-integrated-phase3.ps1"
set EXITCODE=%ERRORLEVEL%
echo.
if not "%EXITCODE%"=="0" (
  echo La validation Phase 3 a echoue.
) else (
  echo La validation Phase 3 est terminee avec succes.
)
pause
exit /b %EXITCODE%
