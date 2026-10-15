@echo off
setlocal

echo ===================================================
echo   ThanhVNC Windows Server Installation & Setup
echo ===================================================

:: Check Administrator privileges
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Please right-click this script and select "Run as administrator".
    pause
    exit /b 1
)

set "INSTALL_DIR=%~dp0..\dist\windows-server"
set "EXE_PATH=%INSTALL_DIR%\VncServer.App.exe"

if not exist "%EXE_PATH%" (
    echo [ERROR] Server executable not found at "%EXE_PATH%".
    echo Please run scripts\build-release.bat first.
    pause
    exit /b 1
)

echo [1/2] Configuring Windows Defender Firewall rules...
netsh advfirewall firewall delete rule name="ThanhVNC Remote Desktop" >nul 2>&1
netsh advfirewall firewall delete rule name="ThanhVNC Discovery" >nul 2>&1

netsh advfirewall firewall add rule name="ThanhVNC Remote Desktop" dir=in action=allow protocol=TCP localport=5900 profile=any description="Allows inbound remote desktop connections to ThanhVNC Server" >nul
if %ERRORLEVEL% neq 0 (
    echo [WARNING] Failed to add firewall rule for TCP port 5900.
) else (
    echo [OK] Firewall rule for TCP port 5900 added.
)

netsh advfirewall firewall add rule name="ThanhVNC Discovery" dir=in action=allow protocol=UDP localport=5902 profile=any description="Allows inbound UDP discovery broadcasts for ThanhVNC Server" >nul
if %ERRORLEVEL% neq 0 (
    echo [WARNING] Failed to add firewall rule for UDP port 5902.
) else (
    echo [OK] Firewall rule for UDP port 5902 added.
)

echo.
echo [2/2] Configuring Windows Auto-Start...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ThanhVNC Server" /t REG_SZ /d "\"%EXE_PATH%\" --autostart" /f >nul
if %ERRORLEVEL% neq 0 (
    echo [WARNING] Failed to register startup registry key.
) else (
    echo [OK] Auto-start configured in Windows Startup registry.
)

echo.
echo ===================================================
echo   INSTALLATION COMPLETED SUCCESSFULLY!
echo   ThanhVNC Server is ready to start.
echo   Command to launch: "%EXE_PATH%"
echo ===================================================
pause
