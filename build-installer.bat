@echo off
setlocal
cd /d "%~dp0"
rem Publishes Orgodesk as one self-contained exe, makes the plain-text legal files, then builds
rem dist\Orgodesk-Setup-<version>.exe with Inno Setup 6 (crc-appshell template, adapted: installer\Orgodesk.iss).
rem   build-installer.bat          the Release build. It stops while the suite bundle's product id is empty (Sales.cs, D-9/D-11).
rem   build-installer.bat debug    a Debug build for VM tests only: dist\Orgodesk-Setup-<version>-DEBUG.exe. Never give it to users.

set "CONFIG=Release"
set "SUFFIX="
if /i "%~1"=="debug" ( set "CONFIG=Debug" & set "SUFFIX=-DEBUG" )

rem The .NET 10 SDK: the house copy first, then one beside this repo's folder, then "dotnet" on PATH.
set "DOTNET=E:\AI Programs\Toolchain\dotnet10\sdk\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=%~dp0..\..\..\dotnet10\sdk\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
echo Using .NET SDK: %DOTNET%

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" ( echo Inno Setup 6 was not found. & exit /b 1 )

rem The version is the app project's <Version>, so the installer and the exe never disagree.
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'src\Orgodesk.App\Orgodesk.App.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set "VERSION=%%v"
if "%VERSION%"=="" ( echo Could not read the version from Orgodesk.App.csproj. & exit /b 1 )

powershell -NoProfile -ExecutionPolicy Bypass -File installer\make-legal-txt.ps1
if errorlevel 1 ( echo Legal files failed. & exit /b 1 )

if exist release\publish rmdir /s /q release\publish
"%DOTNET%" publish src\Orgodesk.App\Orgodesk.App.csproj -c %CONFIG% -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o release\publish -nologo -v q
if errorlevel 1 ( echo Publish failed. & exit /b 1 )

"%ISCC%" /Q /DAppVersion=%VERSION% /DSuffix=%SUFFIX% installer\Orgodesk.iss
if errorlevel 1 ( echo Installer build failed. & exit /b 1 )
echo Built: %~dp0dist\Orgodesk-Setup-%VERSION%%SUFFIX%.exe
