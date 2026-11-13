@echo off
setlocal
set ROOT=%~dp0..
"%ROOT%\reEngine.exe" --test-mode --headless --register-assoc --data-dir "%ROOT%\data" --log-dir "%ROOT%\logs" %*
exit /b %ERRORLEVEL%
