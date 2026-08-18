@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ======================================================
echo   KK Timeline State Cleaner v1.3.1 - NET35 Build
echo ======================================================
echo.
echo IMPORTANT:
echo   Koikatu/Timeline targets .NET Framework 3.5 / Unity Mono.
echo   This script compiles against CharaStudio's own runtime assemblies.
echo   The old v1.2.0 script incorrectly used the .NET 4 standard library.
echo.

if "%~1"=="" (
    set /p "GAME_DIR=Input or drag your Koikatu game folder here: "
) else (
    set "GAME_DIR=%~1"
)
set "GAME_DIR=%GAME_DIR:"=%"

if not exist "%GAME_DIR%\CharaStudio.exe" (
    echo [ERROR] CharaStudio.exe was not found in:
    echo         %GAME_DIR%
    pause
    exit /b 1
)

set "MANAGED=%GAME_DIR%\CharaStudio_Data\Managed"
if not exist "%MANAGED%\Assembly-CSharp.dll" (
    echo [ERROR] CharaStudio managed folder was not found:
    echo         %MANAGED%
    pause
    exit /b 1
)

if not exist "%MANAGED%\mscorlib.dll" (
    echo [ERROR] Game mscorlib.dll was not found:
    echo         %MANAGED%
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
    echo [ERROR] Timeline.dll was not found under BepInEx\plugins.
    pause
    exit /b 1
)

if not exist "%MANAGED%\UnityEngine.dll" (
    echo [ERROR] UnityEngine.dll was not found in %MANAGED%
    pause
    exit /b 1
)

set "FRAMEWORK_REFS=/reference:""%MANAGED%\mscorlib.dll"""
if exist "%MANAGED%\System.dll" set "FRAMEWORK_REFS=!FRAMEWORK_REFS! /reference:""%MANAGED%\System.dll"""
if exist "%MANAGED%\System.Core.dll" set "FRAMEWORK_REFS=!FRAMEWORK_REFS! /reference:""%MANAGED%\System.Core.dll"""

echo [INFO] Compiler      : %CSC%
echo [INFO] Game runtime  : %MANAGED%\mscorlib.dll
echo [INFO] BepInEx       : %BEPINEX%
echo [INFO] Timeline      : %TIMELINE%
echo.
echo [INFO] Compiling against GAME/NET35 assemblies, not .NET 4 mscorlib.
echo.

if exist "KK_TimelineStateCleaner.dll" del /q "KK_TimelineStateCleaner.dll"

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /codepage:65001 /langversion:4 ^
 /out:"KK_TimelineStateCleaner.dll" ^
 !FRAMEWORK_REFS! ^
 /reference:"%BEPINEX%" ^
 /reference:"%TIMELINE%" ^
 /reference:"%MANAGED%\Assembly-CSharp.dll" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 "TimelineStateCleaner.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo Copy the complete compiler output when reporting the problem.
    pause
    exit /b 1
)

echo.
echo [OK] Build succeeded:
echo      %CD%\KK_TimelineStateCleaner.dll

echo.
set "INSTALL_DIR=%GAME_DIR%\BepInEx\plugins\KK_TimelineStateCleaner"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /y "KK_TimelineStateCleaner.dll" "%INSTALL_DIR%\KK_TimelineStateCleaner.dll" >nul
if errorlevel 1 (
    echo [WARN] Build succeeded, but automatic local install failed.
) else (
    echo [OK] Installed local test copy to:
    echo      %INSTALL_DIR%
)

echo.
echo Before testing, DELETE any older KK_TimelineStateCleaner.dll copies

echo from other BepInEx\plugins subfolders, then restart CharaStudio.
echo.
pause
