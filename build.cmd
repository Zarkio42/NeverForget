@echo off
rem Builds NeverForget with the C# compiler that ships with .NET Framework 4.x (already in Windows).
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set OUT=dist

if not exist "%CSC%" (
  echo C# compiler not found at %CSC%. .NET Framework 4.x is required.
  exit /b 1
)

rem WebView2 SDK DLLs are not stored in the repository: download them on first build.
if not exist lib\Microsoft.Web.WebView2.Core.dll (
  echo Downloading WebView2 SDK...
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-webview2.ps1
  if errorlevel 1 ( echo Failed to download WebView2. & exit /b 1 )
)

rem Re-check the SDK DLLs on every build (they could have been replaced after download).
powershell -NoProfile -Command "$bad = Get-ChildItem lib\*.dll | Where-Object { $s = Get-AuthenticodeSignature $_.FullName; $s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation' }; if ($bad) { Write-Output ('Untrusted DLL in lib: ' + ($bad.Name -join ', ')); exit 1 }"
if errorlevel 1 ( echo Delete the lib folder and build again. & exit /b 1 )

rem Start from a clean output folder so no stale files get installed.
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /utf8output ^
  /out:"%OUT%\NeverForget.exe" ^
  /win32icon:assets\neverforget.ico /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

copy /y lib\*.dll "%OUT%\" >nul
copy /y assets\neverforget.ico "%OUT%\" >nul
if exist "%OUT%\web" rmdir /s /q "%OUT%\web"
xcopy /e /i /q /y web "%OUT%\web" >nul
copy /y LICENSE "%OUT%\" >nul

echo Built %OUT%\NeverForget.exe
endlocal
