@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ======================================================
echo   KK Drag Coordinate Load Bridge v1.2.3 - NET35 Build
echo ======================================================
echo.
echo This build uses your local Koikatu/CharaStudio/BepInEx assemblies.
echo It does NOT use NuGet and does NOT use dotnet restore.
echo.

if "%~1"=="" (
    set /p "GAME_DIR=Input or drag your Koikatu game folder here: "
) else (
    set "GAME_DIR=%~1"
)
set "GAME_DIR=%GAME_DIR:"=%"

if not exist "%GAME_DIR%\Koikatu.exe" if not exist "%GAME_DIR%\CharaStudio.exe" (
    echo [ERROR] Neither Koikatu.exe nor CharaStudio.exe was found in:
    echo         %GAME_DIR%
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
    echo [ERROR] Game System.dll was not found in:
    echo         %MANAGED%
    pause
    exit /b 1
)
if not exist "%MANAGED%\UnityEngine.dll" (
    echo [ERROR] UnityEngine.dll was not found in:
    echo         %MANAGED%
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
    echo [ERROR] 0Harmony.dll was not found under BepInEx.
    pause
    exit /b 1
)

echo [INFO] Compiler : %CSC%
echo [INFO] BepInEx  : %BEPINEX%
echo [INFO] Harmony  : %HARMONY%
echo [INFO] Managed  : %MANAGED%
echo [INFO] Runtime  : %MANAGED%\mscorlib.dll
echo.

set "BUILD_TMP=%TEMP%\KK_DragCoordinateLoadBridge_v123_%RANDOM%_%RANDOM%"
if exist "%BUILD_TMP%" rmdir /s /q "%BUILD_TMP%"
mkdir "%BUILD_TMP%" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Could not create temporary build folder.
    pause
    exit /b 1
)
set "BUILD_DLL=%BUILD_TMP%\KK_DragCoordinateLoadBridge.dll"

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /codepage:65001 /langversion:4 ^
 /out:"%BUILD_DLL%" ^
 /reference:"%MANAGED%\mscorlib.dll" ^
 /reference:"%MANAGED%\System.dll" ^
 /reference:"%BEPINEX%" ^
 /reference:"%HARMONY%" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 "KK_DragCoordinateLoadBridge.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo Copy the COMPLETE compiler output when reporting the problem.
    pause
    exit /b 1
)

echo.
echo [OK] Compilation succeeded:
echo      %BUILD_DLL%

echo.
echo [INFO] Removing older copies of this bridge DLL only...
if exist "%GAME_DIR%\BepInEx\plugins" (
    for /r "%GAME_DIR%\BepInEx\plugins" %%F in (KK_DragCoordinateLoadBridge.dll) do del /q "%%~fF" >nul 2>&1
)

set "INSTALL_DIR=%GAME_DIR%\BepInEx\plugins\KK_DragCoordinateLoadBridge"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /y "%BUILD_DLL%" "%INSTALL_DIR%\KK_DragCoordinateLoadBridge.dll" >nul
if errorlevel 1 (
    echo [WARN] Build succeeded, but automatic install failed.
) else (
    echo [OK] Installed to:
    echo      %INSTALL_DIR%
)

if exist "%BUILD_TMP%" rmdir /s /q "%BUILD_TMP%"
echo.
echo Runtime compatibility is structural; dependency DLL SHA256/MVID are not pinned.
echo Restart Koikatu or CharaStudio after replacing the plugin DLL.
echo.
pause
