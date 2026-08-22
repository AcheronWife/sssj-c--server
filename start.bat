@echo off
echo ========================================
echo   GCG2 Offline Server Launcher
echo ========================================
echo.

echo [1/3] Setting up ADB port forwarding...
adb reverse tcp:30400 tcp:30400 2>nul
if errorlevel 1 (
    echo WARNING: ADB reverse 30400 failed, check phone connection
)
adb reverse tcp:18080 tcp:18080 2>nul
if errorlevel 1 (
    echo WARNING: ADB reverse 18080 failed, check phone connection
)
echo ADB port forwarding done
echo.

echo [2/3] Checking .NET environment...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet not found, please install .NET 8 SDK
    pause
    exit /b 1
)
echo .NET environment OK
echo.

echo [3/3] Building and starting server...
dotnet build -c Release --nologo -v q
if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo.
echo HTTP: http://0.0.0.0:18080
echo TCP:  0.0.0.0:30400
echo GM:   http://127.0.0.1:18080/gm?cmd=help
echo.
echo Press Ctrl+C to stop server
echo ========================================
echo.

dotnet run -c Release --no-build --project Gcg2OfflineServer.csproj

pause
