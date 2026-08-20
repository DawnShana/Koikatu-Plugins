@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ======================================================
echo   KKPE Height/Body Lock v1.2.4 - NET35 Build
echo ======================================================
echo.
echo IMPORTANT:
echo   Koikatu/KKPE targets .NET Framework 3.5 / Unity Mono.
echo   This script only reads dependencies from the Koikatu folder.
echo   It does NOT install, replace, delete, or modify game/plugin files.
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
if not exist "%HARMONY%" (
    echo [ERROR] 0Harmony.dll was not found:
    echo         %HARMONY%
    pause
    exit /b 1
)

set "KKPE="
for /r "%GAME_DIR%\BepInEx\plugins" %%F in (KKPE.dll) do (
    if exist "%%~fF" (
        set "KKPE=%%~fF"
        goto :kkpe_found
    )
)

:kkpe_found
if not defined KKPE (
    echo [ERROR] KKPE.dll was not found under BepInEx\plugins.
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
echo [INFO] Harmony       : %HARMONY%
echo [INFO] KKPE          : %KKPE%
echo.

if exist "KKPEHeightLockStandalone.dll" del /q "KKPEHeightLockStandalone.dll"

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /codepage:65001 /langversion:4 ^
 /out:"KKPEHeightLockStandalone.dll" ^
 !FRAMEWORK_REFS! ^
 /reference:"%BEPINEX%" ^
 /reference:"%HARMONY%" ^
 /reference:"%KKPE%" ^
 /reference:"%MANAGED%\Assembly-CSharp.dll" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 "KKPEHeightLockStandalone.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo [OK] Build succeeded:
echo      %CD%\KKPEHeightLockStandalone.dll
echo.
echo No files were written to the Koikatu directory.
echo Manually copy KKPEHeightLockStandalone.dll to BepInEx\plugins\
echo.
pause
