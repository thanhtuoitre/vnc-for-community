@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   ThanhVNC Release Build & Packaging Script
echo ===================================================

cd /d "%~dp0\.."

echo.
echo [1/2] Publishing Windows VNC Server (Single-File Binary)...
dotnet publish server\src\VncServer.App\VncServer.App.csproj -c Release -r win-x64 --self-contained false -o dist\windows-server /p:PublishSingleFile=true
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to publish Windows Server.
    exit /b %ERRORLEVEL%
)

echo.
echo [2/2] Assembling Android VNC Client APK...
cd client
call gradlew.bat assembleDebug
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to assemble Android APK.
    exit /b %ERRORLEVEL%
)
cd ..

copy /Y client\app\build\outputs\apk\debug\app-debug.apk dist\ThanhVNC.apk >nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to copy Android APK to dist.
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo   RELEASE ARTIFACTS BUILT SUCCESSFULLY!
echo   - Windows Server: dist\windows-server\VncServer.App.exe
echo   - Android Client: dist\ThanhVNC.apk
echo ===================================================
