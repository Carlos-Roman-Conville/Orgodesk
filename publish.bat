@echo off
setlocal
cd /d "%~dp0"
echo Building a single self-contained exe into dist\ ...
dotnet publish src\Orgodesk.App\Orgodesk.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist -nologo -v q
if errorlevel 1 (
    echo Publish failed.
    pause
    goto :eof
)
echo Done: %~dp0dist\Orgodesk.exe
