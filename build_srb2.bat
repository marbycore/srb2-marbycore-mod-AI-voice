@echo off
:: ============================================================================
:: SRB2 MarbyCore AI Mod - Unified Build Script (32-bit Robust Version)
:: ============================================================================
set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

if not exist "%MSBUILD_PATH%" (
    echo [ERROR] MSBuild not found at: "%MSBUILD_PATH%"
    echo Please install Visual Studio Build Tools 2022.
    pause
    exit /b 1
)

echo [1/4] Cleaning zombie processes...
taskkill /F /IM Srb2Win.exe /T 2>nul
taskkill /F /IM TelemetryDashboard.exe /T 2>nul
taskkill /F /IM AICommandDashboard.exe /T 2>nul

echo.
echo [2/4] Building SRB2 Game Engine (Win32)...
"%MSBUILD_PATH%" srb2-vc10.sln /p:Configuration=Release /p:Platform=Win32 /t:Srb2Win /m
if %ERRORLEVEL% neq 0 (
    echo [ERROR] SRB2 Build Failed with error %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Copying Game Executable and 32-bit Libraries...
if exist "bin\VC10\Win32\Release\Srb2Win.exe" (
    copy /Y bin\VC10\Win32\Release\Srb2Win.exe Srb2Win.exe
)

:: Copying ALL 32-bit (x86/i686) runtime DLLs to prevent 0xc000007b errors
echo Deploying 32-bit dependencies...
copy /Y libs\SDL2\lib\x86\SDL2.dll SDL2.dll >nul 2>&1

:: SDL2_mixer and its dependencies
copy /Y libs\SDL2_mixer\lib\x86\SDL2_mixer.dll SDL2_mixer.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libFLAC-8.dll libFLAC-8.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libmodplug-1.dll libmodplug-1.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libmpg123-0.dll libmpg123-0.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libogg-0.dll libogg-0.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libopus-0.dll libopus-0.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libopusfile-0.dll libopusfile-0.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libvorbis-0.dll libvorbis-0.dll >nul 2>&1
copy /Y libs\SDL2_mixer\lib\x86\libvorbisfile-3.dll libvorbisfile-3.dll >nul 2>&1

:: libopenmpt and its dependencies
copy /Y libs\libopenmpt\bin\x86\libopenmpt.dll libopenmpt.dll >nul 2>&1
copy /Y libs\libopenmpt\bin\x86\openmpt-mpg123.dll openmpt-mpg123.dll >nul 2>&1
copy /Y libs\libopenmpt\bin\x86\openmpt-ogg.dll openmpt-ogg.dll >nul 2>&1
copy /Y libs\libopenmpt\bin\x86\openmpt-vorbis.dll openmpt-vorbis.dll >nul 2>&1
copy /Y libs\libopenmpt\bin\x86\openmpt-zlib.dll openmpt-zlib.dll >nul 2>&1

:: Core components and networking
copy /Y libs\curl\lib32\libcurl.dll libcurl.dll >nul 2>&1
copy /Y libs\dll-binaries\i686\exchndl.dll exchndl.dll >nul 2>&1
copy /Y libs\dll-binaries\i686\libgme.dll libgme.dll >nul 2>&1
copy /Y libs\dll-binaries\i686\mgwhelp.dll mgwhelp.dll >nul 2>&1

:: Check for missing game assets (.pk3)
echo.
echo Verifying Game Assets (.pk3)...
set "ASSETS_PATH=bin\VC10\Win32\Release"
set MISSING_ASSETS=0

for %%f in (srb2.pk3 zones.pk3 music.pk3 characters.pk3) do (
    if exist "%ASSETS_PATH%\%%f" (
        copy /Y "%ASSETS_PATH%\%%f" "%%f" >nul 2>&1
    ) else if not exist "%%f" (
        echo [WARNING] Missing file: %%f
        set MISSING_ASSETS=1
    )
)

if %MISSING_ASSETS% equ 1 (
    echo -------------------------------------------------------------
    echo [IMPORTANT] Some game assets ^(.pk3^) are missing in the root.
    echo Please copy them from your original SRB2 v2.2.x installation.
    echo -------------------------------------------------------------
)

echo.
echo [4/4] Building AI Dashboards (C#)...
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "NAUDIO=mod\naudio_1.10_lib\lib\net35\NAudio.dll"

if not exist "%CSC%" (
    echo [ERROR] C# Compiler ^(csc.exe^) not found.
    pause
    exit /b 1
)

:: Build Telemetry Dashboard
"%CSC%" /nologo /out:TelemetryDashboard.exe /reference:"%NAUDIO%" /target:winexe mod\TelemetryDashboard.cs mod\TTSProvider.cs mod\LanguageManager.cs mod\IntentClassifier.cs
if %ERRORLEVEL% equ 0 (
    echo TelemetryDashboard.exe -> OK
    copy /Y "%NAUDIO%" NAudio.dll >nul 2>&1
) else (
    echo [ERROR] TelemetryDashboard.exe Build Failed
)

:: Build AI Command Dashboard
"%CSC%" /nologo /out:AICommandDashboard.exe /target:winexe mod\AICommandDashboard.cs
if %ERRORLEVEL% equ 0 (
    echo AICommandDashboard.exe -> OK
) else (
    echo [ERROR] AICommandDashboard.exe Build Failed
)

echo.
echo ============================================================================
echo BUILD COMPLETE!
echo ============================================================================
echo 1. Double-click Srb2Win.exe to start the game.
echo 2. Double-click TelemetryDashboard.exe to start the AI Companion ^(Tails^).
echo ============================================================================
pause
