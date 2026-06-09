@echo off
setlocal enabledelayedexpansion

echo =======================================================================
echo               RiftWriter Server Build & Launch Script
echo =======================================================================
echo.

:: Build the project in Release mode
echo Building RiftWriter in Release mode...
dotnet build "%~dp0RiftWriter.csproj" -c Release
if %ERRORLEVEL% neq 0 (
    echo.
    echo Error: Failed to compile RiftWriter.
    pause
    exit /b 1
)
echo.

:: Run the compiled server
echo Launching RiftWriter Server...
echo.
dotnet run --project "%~dp0RiftWriter.csproj" -c Release --no-build

exit /b 0
