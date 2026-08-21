@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "RELEASES_DIR=..\releases"
set "OUTPUT_NAME=KK_DragCoordinateLoadBridge.dll"
set "OUTPUT_TMP=%RELEASES_DIR%\%OUTPUT_NAME%.tmp"
set "OUTPUT_DLL=%RELEASES_DIR%\%OUTPUT_NAME%"

echo ======================================================
echo   KK Drag Coordinate Load Bridge v1.2.3 - NET35 Build
echo ======================================================
echo.
echo This build uses your local Koikatu/CharaStudio/BepInEx assemblies.
echo It does NOT use NuGet and does NOT use dotnet restore.
echo The DLL is written only to the repository releases folder.
echo.

if "%~1"=="" (
    set /p "GAME_DIR=Input or drag your Koikatu game folder here: "
) else (
    set "GAME_DIR=%~1"
)
set "GAME_DIR=%GAME_DIR:"=%"

if not exist "%GAME_DIR%\Koikatu.exe" if not exist "%GAME_DIR%\CharaStudio.exe" (
    echo [ERROR] Neither Koikatu.exe nor CharaStudio.exe was found in the selected runtime folder.
    pause
    exit /b 1
)

set "MANAGED="
if exist "%GAME_DIR%\Koikatu_Data\Managed\mscorlib.dll" set "MANAGED=%GAME_DIR%\Koikatu_Data\Managed"
if not defined MANAGED if exist "%GAME_DIR%\CharaStudio_Data\Managed\mscorlib.dll" set "MANAGED=%GAME_DIR%\CharaStudio_Data\Managed"
if not defined MANAGED (
    echo [ERROR] Could not find a usable Managed folder under Koikatu_Data or CharaStudio_Data.
    pause
    exit /b 1
)

if not exist "%MANAGED%\System.dll" (
    echo [ERROR] Game System.dll was not found.
    pause
    exit /b 1
)
if not exist "%MANAGED%\UnityEngine.dll" (
    echo [ERROR] UnityEngine.dll was not found.
    pause
    exit /b 1
)

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] C# compiler csc.exe was not found.
    echo Install/enable .NET Framework 4.x or Visual Studio Build Tools.
    pause
    exit /b 1
)

set "BEPINEX=%GAME_DIR%\BepInEx\core\BepInEx.dll"
if not exist "%BEPINEX%" set "BEPINEX=%GAME_DIR%\BepInEx\BepInEx.dll"
if not exist "%BEPINEX%" (
    echo [ERROR] BepInEx.dll was not found.
    pause
    exit /b 1
)

set "HARMONY=%GAME_DIR%\BepInEx\core\0Harmony.dll"
if not exist "%HARMONY%" set "HARMONY="
if not defined HARMONY (
    for /r "%GAME_DIR%\BepInEx" %%F in (0Harmony.dll) do if not defined HARMONY set "HARMONY=%%~fF"
)
if not defined HARMONY (
    echo [ERROR] 0Harmony.dll was not found under the selected runtime folder.
    pause
    exit /b 1
)

if not exist "%RELEASES_DIR%\" mkdir "%RELEASES_DIR%" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Could not create the repository releases folder.
    pause
    exit /b 1
)

echo [INFO] Runtime dependencies detected.
echo.

if exist "%OUTPUT_TMP%" del /q "%OUTPUT_TMP%" >nul 2>&1

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /codepage:65001 /langversion:4 ^
 /out:"%OUTPUT_TMP%" ^
 /reference:"%MANAGED%\mscorlib.dll" ^
 /reference:"%MANAGED%\System.dll" ^
 /reference:"%BEPINEX%" ^
 /reference:"%HARMONY%" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 "KK_DragCoordinateLoadBridge.cs"

if errorlevel 1 (
    if exist "%OUTPUT_TMP%" del /q "%OUTPUT_TMP%" >nul 2>&1
    echo.
    echo [ERROR] Build failed.
    echo Copy the COMPLETE compiler output when reporting the problem.
    pause
    exit /b 1
)

move /y "%OUTPUT_TMP%" "%OUTPUT_DLL%" >nul
if errorlevel 1 (
    if exist "%OUTPUT_TMP%" del /q "%OUTPUT_TMP%" >nul 2>&1
    echo.
    echo [ERROR] Could not publish the DLL to releases.
    pause
    exit /b 1
)

echo.
echo [OK] Compilation succeeded:
echo      releases\%OUTPUT_NAME%
echo.
echo Runtime compatibility is structural; dependency DLL SHA256/MVID are not pinned.
echo Copy the release DLL manually if you need to test it in a game installation.
echo.
pause
