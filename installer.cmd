@echo off
rem Branche ClaudeNotch : hooks dans ~/.claude/settings.json (sauvegarde datee a cote),
rem demarrage avec Windows, puis lance l'encoche. Relancable sans risque.
setlocal
if not exist "%~dp0ClaudeNotch.exe" call "%~dp0build.cmd" || exit /b 1
"%~dp0ClaudeNotch.exe" --install || exit /b 1
start "" "%~dp0ClaudeNotch.exe"
echo.
echo ClaudeNotch est lance. Les nouvelles demandes de permission passent par l'encoche.
