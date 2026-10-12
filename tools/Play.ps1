[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_]+$')][string]$Map='de_dust2',[string]$JavaHome='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$installPath=Join-Path $project 'runtime/installation.json'
if(!(Test-Path -LiteralPath $installPath)){throw 'Run tools/Build.ps1 and tools/Prepare-CS2.ps1 first.'}
$install=Get-Content -LiteralPath $installPath -Raw | ConvertFrom-Json
$renderer=Join-Path $install.GameDirectory 'addons/counterstrikesharp/plugins/CS2Craft.Server/CS2Craft.Renderer.dll'
if(!(Test-Path -LiteralPath $renderer)){throw 'Run tools/Build.ps1 and tools/Prepare-CS2.ps1 to deploy the renderer.'}
. (Join-Path $PSScriptRoot 'lib\Prerequisites.ps1')
$java=Find-Java25 $JavaHome
if(!$java){throw 'Java 25 not found. Run tools\Install-Prerequisites.ps1, or pass -JavaHome pointing to Java 25.'}
$JavaHome=$java.Home
& (Join-Path $PSScriptRoot 'Start-CS2.ps1') -Map $Map
$hostProcessId=[int](Get-Content (Join-Path $project 'runtime/last-cs2-pid.txt'))
$runId=Get-Date -Format 'yyyyMMdd-HHmmss'
$log=Join-Path $project "runtime/minecraft-$runId.log"
$errors=Join-Path $project "runtime/minecraft-$runId.stderr.log"
$script=Join-Path $PSScriptRoot 'Start-Minecraft-Dev.ps1'
# PowerShell's Start-Process joins ArgumentList into a Windows command line, so quote paths explicitly.
# Backslashes before a closing quote must be doubled, or "C:\jdk\" reads as an escaped quote and swallows
# the rest of the command line (a JAVA_HOME ending in \ did exactly that).
function Quote([string]$Value){
    if($Value.Contains('"')){throw "Invalid quote in launcher path: $Value"}
    '"'+($Value -replace '(\\+)$','$1$1')+'"'
}
$arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Quote $script),'-JavaHome',(Quote $JavaHome),
    '-WorldName',"CS2Craft-$Map",'-HostProcessId',"$hostProcessId")
$launcher=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $errors
$launcher.Id | Set-Content (Join-Path $project 'runtime/last-minecraft-launcher-pid.txt')
# A launcher that dies at once (bad Java, blocked script) would otherwise fail silently in its hidden window.
for($i=0;$i -lt 20 -and !$launcher.HasExited;$i++){Start-Sleep -Seconds 1}
if($launcher.HasExited){
    Write-Host "Minecraft did not start (exit code $($launcher.ExitCode)). Its output:" -ForegroundColor Red
    Get-Content -LiteralPath $errors,$log -ErrorAction SilentlyContinue | Select-Object -Last 30 | ForEach-Object {Write-Host "   $_"}
    throw "Minecraft failed to start; logs: $log and $errors"
}
Write-Host 'In CS2, join a team, open the console (~), and enter: css_cs2craft_play'
Write-Host 'Minecraft loads in the background: about a minute, several minutes on the very first launch while it downloads its assets.'
Write-Host 'Detach command: css_cs2craft_detach'
Write-Host "Minecraft save: CS2Craft-$Map; log: $log"
