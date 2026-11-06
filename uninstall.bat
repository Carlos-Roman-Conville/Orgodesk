@echo off
setlocal
set "EXE=%LOCALAPPDATA%\Programs\Orgodesk\Orgodesk.exe"
if not exist "%EXE%" set "EXE=%~dp0dist\Orgodesk.exe"
"%EXE%" --uninstall
