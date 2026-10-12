@echo off
rem Compile et lance les tests (csc du .NET Framework, aucun SDK).
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "SRC=%~dp0..\src"
"%FW%\csc.exe" /nologo /target:exe /out:"%TEMP%\bip-tests.exe" /r:System.dll /r:System.Core.dll ^
  "%~dp0AuditTests.cs" "%SRC%\Audit.cs" "%SRC%\Json.cs" || exit /b 1
"%TEMP%\bip-tests.exe"
exit /b %errorlevel%
