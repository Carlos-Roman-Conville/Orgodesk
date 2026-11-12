@echo off
setlocal
cd /d "%~dp0"
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
dotnet test tests\Orgodesk.Core.Tests -nologo -v q
if errorlevel 1 (
    echo Tests failed; not building.
    pause
    goto :eof
)
echo Building the release exe into release\ ...
dotnet publish src\Orgodesk.App\Orgodesk.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o release -nologo -v q
if errorlevel 1 (
    echo Publish failed.
    pause
    goto :eof
)
copy /y THIRD-PARTY-NOTICES.txt release\ >nul
copy /y CHANGELOG.md release\ >nul
echo Done: %~dp0release\Orgodesk.exe (with THIRD-PARTY-NOTICES.txt and CHANGELOG.md; zip all three)
pause
