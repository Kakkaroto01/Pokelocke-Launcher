@echo off
chcp 65001 >nul
echo ========================================
echo  PokeLocke Launcher - Desinstalador
echo ========================================
echo.
echo Este desinstalador borrara TODO lo relacionado
echo con el PokeLocke Launcher:
echo   1) Carpeta de datos: %APPDATA%\PokeLockeLauncher
echo   2) Carpeta de instalacion del launcher
echo.
echo NO borra: ROMs, partidas .sav ni emuladores.
echo.
pause
start "" "PokeLockeLauncher.Uninstall.exe"
