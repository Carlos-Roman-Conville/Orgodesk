@echo off
setlocal
cd /d "%~dp0"
rem Needs the .NET 10 SDK. Uses a copy in ..\dotnet10\sdk if there is one, else the dotnet on PATH.
set "DOTNET=dotnet"
if exist "%~dp0..\dotnet10\sdk\dotnet.exe" set "DOTNET=%~dp0..\dotnet10\sdk\dotnet.exe"
rem Builds the version you sell. Refuses while Sales.cs has no Gumroad product id,
rem because that build would have no trial and no license check.
findstr /r /c:"ProductId = \"\";" src\Orgodesk.App\Sales.cs >nul
if not errorlevel 1 (
    echo Sales.cs still has an empty ProductId. Paste the Gumroad product id into
    echo src\Orgodesk.App\Sales.cs first ^(see SALES.md, step 2^).
    pause
    goto :eof
)
echo Running tests...
"%DOTNET%" test tests\Orgodesk.Core.Tests -nologo -v q
if errorlevel 1 (
    echo Tests failed; not building.
    pause
    goto :eof
)
echo Building the release exe into release\ ...
"%DOTNET%" publish src\Orgodesk.App\Orgodesk.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o release -nologo -v q
if errorlevel 1 (
    echo Publish failed.
    pause
    goto :eof
)
copy /y THIRD-PARTY-NOTICES.txt release\ >nul
copy /y CHANGELOG.md release\ >nul
copy /y README-buyers.txt release\ >nul
copy /y legal\EULA.md release\EULA.txt >nul
echo Done: %~dp0release\Orgodesk.exe (with README-buyers.txt, EULA.txt, THIRD-PARTY-NOTICES.txt, CHANGELOG.md; zip all five)
pause
