@echo off
rem Removes NeverForget: closes it, deletes its shortcuts and the program folder.
rem Notes in %LOCALAPPDATA%\NeverForget are kept unless you run: uninstall.cmd /purge
setlocal
set DEST=%LOCALAPPDATA%\Programs\NeverForget
set DATA=%LOCALAPPDATA%\NeverForget

if exist "%DEST%\NeverForget.exe" "%DEST%\NeverForget.exe" --quit
if exist "%DEST%\NeverForget.exe" "%DEST%\NeverForget.exe" --uninstall
if exist "%DEST%" rmdir /s /q "%DEST%"

if /i "%~1"=="/purge" (
  if exist "%DATA%" rmdir /s /q "%DATA%"
  echo Program and data removed.
) else (
  echo Program removed. Your notes are still in %DATA%
)
endlocal
