@echo off
rem Starts CS2 and the hidden Minecraft client. In CS2: join a team, open the console (~), type css_cs2craft_play.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Play.ps1 %*
pause
