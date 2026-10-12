@echo off
rem Branche ClaudeNotch : hooks dans ~/.claude/settings.json (sauvegarde datee a cote),
rem demarrage avec Windows, puis lance l'encoche. Relancable sans risque.
setlocal
set "BUILD="
if not exist "%~dp0ClaudeNotch.exe" set "BUILD=1"
if not exist "%~dp0NotchHook.exe" set "BUILD=1"
if not exist "%~dp0bip.exe" set "BUILD=1"
if defined BUILD call "%~dp0build.cmd" || exit /b 1
"%~dp0ClaudeNotch.exe" --install || exit /b 1
start "" "%~dp0ClaudeNotch.exe"
echo.
echo ClaudeNotch est lance. Les nouvelles demandes de permission passent par l'encoche.
