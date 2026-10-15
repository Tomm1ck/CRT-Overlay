@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0app\crt.exe" (
    if not exist "%~dp0crt.csproj" (
        echo Application files are missing. Extract the complete release ZIP again.
        pause
        exit /b 1
    )
    call "%~dp0Build.bat"
    if errorlevel 1 exit /b 1
)
start "" /d "%~dp0" "%~dp0app\crt.exe" --preset "%~dp0presets\balanced.json" %*

