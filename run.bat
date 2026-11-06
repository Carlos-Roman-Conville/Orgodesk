@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="--exit" (
    "src\Orgodesk.App\bin\Debug\net8.0-windows\Orgodesk.exe" --exit
    goto :eof
)
if "%~1"=="--restore" (
    "src\Orgodesk.App\bin\Debug\net8.0-windows\Orgodesk.exe" --restore
    goto :eof
)
dotnet build Orgodesk.sln -nologo -v q
if errorlevel 1 (
    echo Build failed.
    pause
    goto :eof
)
start "" "src\Orgodesk.App\bin\Debug\net8.0-windows\Orgodesk.exe"
