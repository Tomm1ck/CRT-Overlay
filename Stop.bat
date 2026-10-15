@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0app\crt.exe" (
    "%~dp0app\crt.exe" --stop
) else if exist "%~dp0crt.csproj" (
    dotnet run -c Release -- --stop
) else (
    echo Application files are missing. Extract the complete release ZIP again.
    exit /b 1
)
if errorlevel 1 (
    echo Could not stop CRT Overlay. Use Ctrl+Alt+Shift+Q.
    pause
    exit /b 1
)

