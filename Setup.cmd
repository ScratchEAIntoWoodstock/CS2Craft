@echo off
rem CS2Craft setup. Unblocks the files Windows marked as downloaded, then installs only what this PC is
rem missing (asking before each install) and offers to build CS2Craft. See docs\INSTALL.md.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Recurse -File | Unblock-File"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Install-Prerequisites.ps1 %*
pause
