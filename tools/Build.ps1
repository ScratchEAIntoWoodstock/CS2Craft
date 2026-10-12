[CmdletBinding()]
param([string]$JavaHome='',[switch]$SkipMinecraft)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
Push-Location $project
try {
    & (Join-Path $PSScriptRoot "Build-Native.ps1")
    dotnet build src/CS2Craft.Server -c Release
    if($LASTEXITCODE -ne 0){throw 'CS2 build failed'}
    dotnet run --project tests/CS2Craft.Tests -c Release
    if($LASTEXITCODE -ne 0){throw 'Bridge tests failed'}
    dotnet build tools/CS2Craft.MapExport -c Release
    if($LASTEXITCODE -ne 0){throw 'Map exporter build failed'}
    dotnet build tools/CS2Craft.Probe -c Release
    if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
    if(!$SkipMinecraft) {
        if(!$JavaHome) {
            $candidate='C:\Program Files (x86)\Minecraft Launcher\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon'
            if(Test-Path -LiteralPath (Join-Path $candidate 'bin/java.exe')){$JavaHome=$candidate}
        }
        if(!$JavaHome){throw 'Pass -JavaHome pointing to a Java 25 installation.'}
        $env:JAVA_HOME=$JavaHome
        $env:GRADLE_USER_HOME=Join-Path $project '.cache/gradle'
        & .\minecraft\gradlew.bat -p minecraft build --no-daemon
        if($LASTEXITCODE -ne 0){throw 'Minecraft build failed'}
    }
    $dist=Join-Path $project 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    Copy-Item minecraft/build/libs/cs2craft-fabric-0.1.0.jar $dist -ErrorAction Stop
    Write-Host "Build and tests passed. Minecraft artifact: $dist/cs2craft-fabric-0.1.0.jar"
} finally {Pop-Location}
