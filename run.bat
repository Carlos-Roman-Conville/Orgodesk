@echo off
setlocal
cd /d "%~dp0"
rem Needs the .NET 10 SDK. Uses a copy in ..\dotnet10\sdk if there is one, else the dotnet on PATH.
set "DOTNET=dotnet"
if exist "%~dp0..\dotnet10\sdk\dotnet.exe" set "DOTNET=%~dp0..\dotnet10\sdk\dotnet.exe"
if "%~1"=="--exit" (
    "src\Orgodesk.App\bin\Debug\net10.0-windows\Orgodesk.exe" --exit
    goto :eof
)
if "%~1"=="--restore" (
    "src\Orgodesk.App\bin\Debug\net10.0-windows\Orgodesk.exe" --restore
    goto :eof
)
"%DOTNET%" build Orgodesk.sln -nologo -v q
if errorlevel 1 (
    echo Build failed.
    pause
    goto :eof
)
start "" "src\Orgodesk.App\bin\Debug\net10.0-windows\Orgodesk.exe"
