@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0app\crt.exe" (
    "%~dp0app\crt.exe" --stop
    if errorlevel 1 exit /b 1
)
dotnet publish "%~dp0crt.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%~dp0app"
if errorlevel 1 (
    echo Build failed. Review the errors above and the building guide in docs/BUILDING.md.
    pause
    exit /b 1
)

