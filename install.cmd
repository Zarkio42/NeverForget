@echo off
rem Installs NeverForget for the current user (no admin rights) and creates Desktop/Start Menu shortcuts.
setlocal
cd /d "%~dp0"
set DEST=%LOCALAPPDATA%\Programs\NeverForget

if not exist dist\NeverForget.exe call build.cmd || exit /b 1

rem Close the running version (if any) so the files can be updated.
rem (--quit waits for the app to exit before returning)
if exist "%DEST%\NeverForget.exe" "%DEST%\NeverForget.exe" --quit

if not exist "%DEST%" mkdir "%DEST%"
if exist "%DEST%\web" rmdir /s /q "%DEST%\web"
xcopy /e /i /q /y dist "%DEST%" >nul

"%DEST%\NeverForget.exe" --install
start "" "%DEST%\NeverForget.exe"
echo Installed to %DEST%
endlocal
