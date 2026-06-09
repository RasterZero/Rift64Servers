@echo off
setlocal enabledelayedexpansion

echo =======================================================================
echo               RiftServe64 Server Build & Launch Script
echo =======================================================================
echo.

:: Build the project in Release mode
echo Building RiftServe64 in Release mode...
dotnet build "%~dp0riftserve64.csproj" -c Release
if %ERRORLEVEL% neq 0 (
    echo.
    echo Error: Failed to compile RiftServe64.
    pause
    exit /b 1
)
echo.

:: Run the compiled server
echo Launching RiftServe64 App Server...
echo.
dotnet run --project "%~dp0riftserve64.csproj" -c Release --no-build

exit /b 0
