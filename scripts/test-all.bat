@echo off
echo ===================================================
echo   ThanhVNC Full Automated Verification Suite
echo ===================================================
echo.

echo [1/3] Building ^& Testing Windows VNC Server...
dotnet test server\VncServer.sln -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Windows Server tests failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Testing Android VNC Client (Unit Tests)...
cd client
call gradlew.bat test
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Android unit tests failed!
    cd ..
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Assembling Android VNC Client APK (Debug)...
call gradlew.bat assembleDebug
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Android build failed!
    cd ..
    exit /b %ERRORLEVEL%
)
cd ..

echo.
echo ===================================================
echo   ALL TESTS ^& BUILDS PASSED SUCCESSFULLY!
echo ===================================================
