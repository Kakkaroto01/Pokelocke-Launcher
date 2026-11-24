@echo off
rem ─────────────────────────────────────────────────────────────────────────────
rem  PokeLocke Launcher — arranque normal (sin restricciones de sandbox).
rem  Doble clic para abrir el launcher con los permisos de tu usuario.
rem ─────────────────────────────────────────────────────────────────────────────
setlocal
set "EXE=%~dp0src\PokeLobby.LauncherUi\bin\Debug\net10.0-windows\PokeLockeLauncher.exe"
if not exist "%EXE%" (
  echo No encuentro el launcher compilado:
  echo   %EXE%
  echo.
  echo Compílalo con:
  echo   dotnet build src\PokeLobby.slnx
  pause
  exit /b 1
)
start "" "%EXE%"
