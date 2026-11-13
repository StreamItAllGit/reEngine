@echo off
setlocal
set ROOT=%~dp0..
"%ROOT%\reEngine.exe" --register-assoc %*
exit /b %ERRORLEVEL%
