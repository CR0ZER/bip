@echo off
rem Compile ClaudeNotch.exe (encoche, WPF) et NotchHook.exe (hook) avec le csc.exe
rem du .NET Framework livre avec Windows. Aucun SDK a installer.
rem Quitter ClaudeNotch avant de compiler : un exe qui tourne est verrouille.

setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo Erreur : csc.exe introuvable.
  exit /b 1
)
set "CSC=%FW%\csc.exe"
set "WPF=%FW%\WPF"

"%CSC%" /nologo /target:winexe /optimize+ /warn:1 ^
  /out:"%~dp0ClaudeNotch.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Xaml.dll ^
  /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" ^
  "%~dp0src\App.cs" "%~dp0src\Json.cs" "%~dp0src\Model.cs" "%~dp0src\NotchWindow.cs" "%~dp0src\Bip.cs" "%~dp0src\Version.cs" ^
  "%~dp0src\Audit.cs" "%~dp0src\Rules.cs" "%~dp0src\Report.cs"
if errorlevel 1 (
  echo.
  echo Compilation de ClaudeNotch echouee.
  exit /b 1
)

rem NotchHook.exe est lance a chaque appel d'outil : s'il est verrouille par une
rem session active, on compile a cote puis on retente la mise en place.
"%CSC%" /nologo /target:winexe /optimize+ /warn:1 ^
  /out:"%~dp0NotchHook.new" /r:System.dll /r:System.Core.dll ^
  "%~dp0src\NotchHook.cs" "%~dp0src\Version.cs" "%~dp0src\Json.cs" "%~dp0src\Audit.cs"
if errorlevel 1 (
  echo.
  echo Compilation de NotchHook echouee.
  exit /b 1
)

set "PLACED="
for /l %%A in (1,1,25) do (
  if not defined PLACED (
    move /y "%~dp0NotchHook.new" "%~dp0NotchHook.exe" >nul 2>&1
    if not errorlevel 1 set "PLACED=1"
    if not defined PLACED ping -n 1 -w 150 127.0.0.1 >nul 2>&1
  )
)
if not defined PLACED (
  del "%~dp0NotchHook.new" >nul 2>&1
  echo Note : NotchHook.exe reste verrouille, l'ancien binaire est conserve.
)

rem bip.exe : la commande "bip run", en console pour partager le terminal.
"%CSC%" /nologo /target:exe /optimize+ /warn:1 ^
  /out:"%~dp0bip.exe" /r:System.dll ^
  "%~dp0src\BipCli.cs" "%~dp0src\Version.cs"
if errorlevel 1 (
  echo.
  echo Compilation de bip echouee.
  exit /b 1
)

for %%F in ("%~dp0ClaudeNotch.exe") do echo Compile : ClaudeNotch.exe  %%~zF octets
for %%F in ("%~dp0NotchHook.exe")   do echo Compile : NotchHook.exe    %%~zF octets
for %%F in ("%~dp0bip.exe")         do echo Compile : bip.exe          %%~zF octets
