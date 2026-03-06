@echo off
setlocal enabledelayedexpansion

echo ============================================
echo  SRB2 Installer Creator
echo ============================================
echo.

REM Find 7-Zip
set "SEVENZIP="
if exist "C:\Program Files\7-Zip\7z.exe" set "SEVENZIP=C:\Program Files\7-Zip\7z.exe"
if exist "C:\Program Files (x86)\7-Zip\7z.exe" set "SEVENZIP=C:\Program Files (x86)\7-Zip\7z.exe"

REM Check if found
if not defined SEVENZIP (
    where 7z >nul 2>nul
    if errorlevel 1 (
        echo ERROR: 7-Zip not found
        echo Install from: https://www.7-zip.org/
        pause
        exit /b 1
    )
    set "SEVENZIP=7z"
)

echo Using 7-Zip: !SEVENZIP!
echo.

REM Set installer name
set "INSTALLER_NAME=SRB2-Custom-Installer"

echo Creating installer from release folder...
echo This may take a few minutes...
echo.

REM Create the self-extracting installer
"!SEVENZIP!" a -sfx7z.sfx -t7z "%INSTALLER_NAME%.exe" .\release\* -mx=5 -mmt=on

if errorlevel 1 (
    echo.
    echo ERROR: Failed to create installer
    pause
    exit /b 1
)

echo.
echo ============================================
echo  SUCCESS!
echo ============================================
echo Installer created: %INSTALLER_NAME%.exe
echo.
echo Users can run this single EXE file to:
echo  1. Extract all game files
echo  2. Run Srb2Win.exe
echo.
echo File size: ~170 MB
echo.

pause
endlocal
