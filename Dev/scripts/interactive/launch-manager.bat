@echo off
setlocal
set ROOT=%~dp0..\..
start "" "%ROOT%\reEngine.exe" %*
exit /b 0
