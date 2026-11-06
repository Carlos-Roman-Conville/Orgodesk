@echo off
setlocal
cd /d "%~dp0"
if not exist "dist\Orgodesk.exe" call publish.bat
"dist\Orgodesk.exe" --install
