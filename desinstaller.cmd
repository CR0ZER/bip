@echo off
rem Retire les hooks ClaudeNotch de ~/.claude/settings.json (les autres hooks ne sont
rem pas touches), le demarrage avec Windows, et ferme l'encoche. Le dossier peut
rem ensuite etre supprime.
setlocal
"%~dp0ClaudeNotch.exe" --uninstall
