@echo off
setlocal

echo ===================================================
echo   ThanhVNC Windows Server Clean Uninstallation
echo ===================================================

:: Check Administrator privileges
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Please right-click this script and select "Run as administrator".
    pause
    exit /b 1
)

echo [1/3] Terminating any running ThanhVNC Server instances...
taskkill /F /IM VncServer.App.exe >nul 2>&1

echo [2/3] Removing Windows Defender Firewall rules...
netsh advfirewall firewall delete rule name="ThanhVNC Remote Desktop" >nul 2>&1
netsh advfirewall firewall delete rule name="ThanhVNC Discovery" >nul 2>&1
echo [OK] Firewall rules removed.

echo.
echo [3/3] Removing Windows Auto-Start Registry key...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ThanhVNC Server" /f >nul 2>&1
echo [OK] Auto-start registry key removed.

echo.
echo ===================================================
echo   UNINSTALLATION COMPLETED CLEANLY!
echo   ThanhVNC Server has been removed from startup and firewall.
echo ===================================================
pause
