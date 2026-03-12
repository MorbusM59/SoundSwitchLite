@echo off
:: ============================================================
:: build.bat – build SoundSwitch Lite and produce the installer
:: ============================================================
:: Requirements:
::   • .NET 8 SDK    https://dotnet.microsoft.com/download
::   • Inno Setup 6  https://jrsoftware.org/isinfo.php  (for the installer step)
::
:: Usage:
::   build.bat           – publish the app only
::   build.bat installer – publish the app AND compile the installer
:: ============================================================

setlocal enabledelayedexpansion

set SCRIPT_DIR=%~dp0
set PROJECT=%SCRIPT_DIR%SoundSwitchLite\SoundSwitchLite.csproj
set PUBLISH_DIR=%SCRIPT_DIR%installer\publish
set ISS_FILE=%SCRIPT_DIR%installer\SoundSwitchLite.iss

echo.
echo =========================================
echo  SoundSwitch Lite – Build
echo =========================================
echo.

:: --- Step 1: publish self-contained single-file exe ---
echo [1/2] Publishing self-contained single-file exe...
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 (
    echo.
    echo ERROR: dotnet publish failed.
    exit /b 1
)

echo.
echo Published to: %PUBLISH_DIR%
echo.

:: --- Step 2 (optional): compile Inno Setup installer ---
if /i "%1"=="installer" (
    echo [2/2] Compiling Inno Setup installer...

    :: Try the default Inno Setup 6 install location
    set ISCC=
    if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
        set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    ) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
        set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    ) else (
        where iscc >nul 2>&1
        if not errorlevel 1 set "ISCC=iscc"
    )

    if "!ISCC!"=="" (
        echo.
        echo WARNING: Inno Setup not found.  Please install Inno Setup 6 from
        echo          https://jrsoftware.org/isinfo.php and then re-run:
        echo            build.bat installer
        exit /b 1
    )

    "!ISCC!" "%ISS_FILE%"
    if errorlevel 1 (
        echo.
        echo ERROR: Inno Setup compiler failed.
        exit /b 1
    )

    echo.
    echo Installer written to: %SCRIPT_DIR%installer\Output\SoundSwitchLiteSetup.exe
) else (
    echo [2/2] Skipped installer step.  Run "build.bat installer" to also build the installer.
)

echo.
echo Done.
endlocal
