[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_]+$')][string]$Map='de_dust2',[string]$JavaHome='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$installPath=Join-Path $project 'runtime/installation.json'
if(!(Test-Path -LiteralPath $installPath)){throw 'Run tools/Build.ps1 and tools/Prepare-CS2.ps1 first.'}
$install=Get-Content -LiteralPath $installPath -Raw | ConvertFrom-Json
$renderer=Join-Path $install.GameDirectory 'addons/counterstrikesharp/plugins/CS2Craft.Server/CS2Craft.Renderer.dll'
if(!(Test-Path -LiteralPath $renderer)){throw 'Run tools/Build.ps1 and tools/Prepare-CS2.ps1 to deploy the renderer.'}
if(!$JavaHome){$JavaHome='C:\Program Files (x86)\Minecraft Launcher\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon'}
if(!(Test-Path -LiteralPath (Join-Path $JavaHome 'bin/java.exe'))){throw 'Pass -JavaHome pointing to Java 25.'}
& (Join-Path $PSScriptRoot 'Start-CS2.ps1') -Map $Map
$hostProcessId=[int](Get-Content (Join-Path $project 'runtime/last-cs2-pid.txt'))
$runId=Get-Date -Format 'yyyyMMdd-HHmmss'
$log=Join-Path $project "runtime/minecraft-$runId.log"
$errors=Join-Path $project "runtime/minecraft-$runId.stderr.log"
$script=Join-Path $PSScriptRoot 'Start-Minecraft-Dev.ps1'
# PowerShell's Start-Process joins ArgumentList into a Windows command line; quote paths explicitly.
if($JavaHome.Contains('"') -or $script.Contains('"')){throw 'Invalid quote in launcher path.'}
$arguments=@('-NoProfile','-File',('"'+$script+'"'),'-JavaHome',('"'+$JavaHome+'"'),
    '-WorldName',"CS2Craft-$Map",'-HostProcessId',"$hostProcessId")
$launcher=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $errors
$launcher.Id | Set-Content (Join-Path $project 'runtime/last-minecraft-launcher-pid.txt')
Write-Host 'In CS2, join a team, open the console (~), and enter: css_cs2craft_play'
Write-Host 'Allow Minecraft to load. Detach command: css_cs2craft_detach'
Write-Host "Minecraft save: CS2Craft-$Map; log: $log"
