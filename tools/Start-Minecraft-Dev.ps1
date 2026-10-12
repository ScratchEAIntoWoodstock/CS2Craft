[CmdletBinding()]
param([string]$JavaHome='',[ValidatePattern('^[a-zA-Z0-9_-]{1,64}$')][string]$WorldName='CS2Craft',[ValidateRange(0,2147483647)][int]$HostProcessId=0)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$JavaHome){$JavaHome='C:\Program Files (x86)\Minecraft Launcher\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon'}
if(!(Test-Path -LiteralPath (Join-Path $JavaHome 'bin/java.exe'))){throw 'Pass -JavaHome for Java 25.'}
$env:JAVA_HOME=$JavaHome
$env:GRADLE_USER_HOME=Join-Path $project '.cache/gradle'
& (Join-Path $project 'minecraft/gradlew.bat') -p (Join-Path $project 'minecraft') runClient --no-daemon "-Pcs2craftWorld=$WorldName" "-Pcs2craftHostPid=$HostProcessId"
exit $LASTEXITCODE
