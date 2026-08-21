@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "RELEASES_DIR=..\releases"
set "OUTPUT_NAME=KK_TimelineStateCleaner.dll"
set "OUTPUT_TMP=%RELEASES_DIR%\%OUTPUT_NAME%.tmp"
set "OUTPUT_DLL=%RELEASES_DIR%\%OUTPUT_NAME%"

echo ======================================================
echo   KK Timeline State Cleaner v1.3.1 - NET35 Build
echo ======================================================
echo.
echo IMPORTANT:
echo   Koikatu/Timeline targets .NET Framework 3.5 / Unity Mono.
echo   This script compiles against CharaStudio's own runtime assemblies.
echo   The old v1.2.0 script incorrectly used the .NET 4 standard library.
echo   The DLL is written only to the repository releases folder.
echo.

if "%~1"=="" (
    set /p "GAME_DIR=Input or drag your Koikatu game folder here: "
) else (
    set "GAME_DIR=%~1"
)
set "GAME_DIR=%GAME_DIR:"=%"

if not exist "%GAME_DIR%\CharaStudio.exe" (
    echo [ERROR] CharaStudio.exe was not found in the selected runtime folder.
    pause
    exit /b 1
)

set "MANAGED=%GAME_DIR%\CharaStudio_Data\Managed"
if not exist "%MANAGED%\Assembly-CSharp.dll" (
    echo [ERROR] CharaStudio managed folder was not found.
    pause
    exit /b 1
)

if not exist "%MANAGED%\mscorlib.dll" (
    echo [ERROR] Game mscorlib.dll was not found.
    pause
    exit /b 1
)

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] C# compiler csc.exe was not found.
    echo Install/enable .NET Framework 4.x or use Visual Studio/MSBuild.
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

set "TIMELINE="
for /r "%GAME_DIR%\BepInEx\plugins" %%F in (Timeline.dll) do (
    if exist "%%~fF" (
        set "TIMELINE=%%~fF"
        goto :timeline_found
    )
)

:timeline_found
if not defined TIMELINE (
    echo [ERROR] Timeline.dll was not found under the selected runtime folder.
    pause
    exit /b 1
)

if not exist "%MANAGED%\UnityEngine.dll" (
    echo [ERROR] UnityEngine.dll was not found.
    pause
    exit /b 1
)

if not exist "%RELEASES_DIR%\" mkdir "%RELEASES_DIR%" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Could not create the repository releases folder.
    pause
    exit /b 1
)

set "FRAMEWORK_REFS=/reference:""%MANAGED%\mscorlib.dll"""
if exist "%MANAGED%\System.dll" set "FRAMEWORK_REFS=!FRAMEWORK_REFS! /reference:""%MANAGED%\System.dll"""
if exist "%MANAGED%\System.Core.dll" set "FRAMEWORK_REFS=!FRAMEWORK_REFS! /reference:""%MANAGED%\System.Core.dll"""

echo [INFO] Runtime dependencies detected.
echo.
echo [INFO] Compiling against GAME/NET35 assemblies, not .NET 4 mscorlib.
echo.

if exist "%OUTPUT_TMP%" del /q "%OUTPUT_TMP%" >nul 2>&1

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /codepage:65001 /langversion:4 ^
 /out:"%OUTPUT_TMP%" ^
 !FRAMEWORK_REFS! ^
 /reference:"%BEPINEX%" ^
 /reference:"%TIMELINE%" ^
 /reference:"%MANAGED%\Assembly-CSharp.dll" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 "TimelineStateCleaner.cs"

if errorlevel 1 (
    if exist "%OUTPUT_TMP%" del /q "%OUTPUT_TMP%" >nul 2>&1
    echo.
    echo [ERROR] Build failed.
    echo Copy the complete compiler output when reporting the problem.
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
echo [OK] Build succeeded:
echo      releases\%OUTPUT_NAME%
echo.
echo No files were written outside the repository releases folder.
echo.
pause
