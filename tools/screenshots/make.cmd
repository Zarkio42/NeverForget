@echo off
rem Regenerates docs\screenshots\*.png from the demo data (needs the WebView2 DLLs in lib\, run build.cmd once first).
setlocal
cd /d "%~dp0\..\.."
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OUT=%TEMP%\neverforget-shot-tool
if not exist "%OUT%" mkdir "%OUT%"

"%FW%\csc.exe" /nologo /target:exe /platform:x64 /out:"%OUT%\Shot.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  tools\screenshots\Shot.cs || exit /b 1
copy /y lib\*.dll "%OUT%\" >nul

"%OUT%\Shot.exe" "%CD%"
set RC=%ERRORLEVEL%
rmdir /s /q "%OUT%" 2>nul
exit /b %RC%
endlocal
