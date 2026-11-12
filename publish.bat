@echo off
setlocal
cd /d "%~dp0"
rem Needs the .NET 10 SDK. Uses a copy in ..\dotnet10\sdk if there is one, else the dotnet on PATH.
set "DOTNET=dotnet"
if exist "%~dp0..\dotnet10\sdk\dotnet.exe" set "DOTNET=%~dp0..\dotnet10\sdk\dotnet.exe"
echo Building a single self-contained exe into dist\ ...
"%DOTNET%" publish src\Orgodesk.App\Orgodesk.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist -nologo -v q
if errorlevel 1 (
    echo Publish failed.
    pause
    goto :eof
)
echo Done: %~dp0dist\Orgodesk.exe
