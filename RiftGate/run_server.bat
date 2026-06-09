@echo off
setlocal enabledelayedexpansion

echo =======================================================================
echo               RiftGate Gateway Build ^& Launch Script
echo =======================================================================
echo.

:: Build the project in Release mode
echo Building RiftGate in Release mode...
dotnet build "%~dp0RiftGate.csproj" -c Release
if %ERRORLEVEL% neq 0 (
    echo.
    echo Error: Failed to compile RiftGate.
    pause
    exit /b 1
)
echo.

:: Run the compiled gateway
echo Launching RiftGate Local Proxy Server...
echo.
dotnet run --project "%~dp0RiftGate.csproj" -c Release --no-build

exit /b 0
