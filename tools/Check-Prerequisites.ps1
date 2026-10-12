[CmdletBinding()]
param([string]$JavaHome='',[string]$CS2Root='')
# Read-only: reports what this machine still needs before tools/Build.ps1, Prepare-CS2.ps1 and Play.ps1.
# Exit code 0 = ready (warnings allowed), 1 = something required is missing. See docs/INSTALL.md.
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$script:failures=0;$script:warnings=0
function Report([string]$Status,[string]$Name,[string]$Detail){
    $color=@{PASS='Green';WARN='Yellow';FAIL='Red';INFO='Gray'}[$Status]
    Write-Host ('[{0}] ' -f $Status) -ForegroundColor $color -NoNewline
    Write-Host ('{0,-22} {1}' -f $Name,$Detail)
    if($Status -eq 'FAIL'){$script:failures++}elseif($Status -eq 'WARN'){$script:warnings++}
}
function Size([long]$Bytes){'{0:N1} GB' -f ($Bytes/1GB)}

Write-Host "CS2Craft prerequisite check ($project)`n"

# Operating system
if([Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Platform -eq 'Win32NT'){
    Report PASS 'Windows x64' ([Environment]::OSVersion.VersionString)
}else{Report FAIL 'Windows x64' 'CS2Craft runs on 64-bit Windows 10/11 only.'}

# Steam and Counter-Strike 2 (same discovery as Prepare-CS2.ps1)
$steam=$null
try{$steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath}catch{}
if($steam){Report PASS 'Steam' $steam}else{Report FAIL 'Steam' 'Not installed (no HKCU\Software\Valve\Steam).'}
if(!$CS2Root -and $steam){
    $libraries=@($steam)
    $vdf=Join-Path $steam 'steamapps/libraryfolders.vdf'
    if(Test-Path -LiteralPath $vdf){
        foreach($m in [regex]::Matches([IO.File]::ReadAllText($vdf),'"path"\s+"([^"]+)"')){$libraries+=$m.Groups[1].Value.Replace('\\','\')}
    }
    foreach($library in $libraries|Select-Object -Unique){
        $candidate=Join-Path $library 'steamapps/common/Counter-Strike Global Offensive'
        if(Test-Path -LiteralPath (Join-Path $candidate 'game/bin/win64/cs2.exe')){$CS2Root=$candidate;break}
    }
}
$cs2Ready=$false
if($CS2Root -and (Test-Path -LiteralPath (Join-Path $CS2Root 'game/bin/win64/cs2.exe'))){
    if(Test-Path -LiteralPath (Join-Path $CS2Root 'game/csgo/maps/de_dust2.vpk')){Report PASS 'Counter-Strike 2' $CS2Root;$cs2Ready=$true}
    else{Report FAIL 'Counter-Strike 2' "Found at $CS2Root but de_dust2.vpk is missing; let Steam finish installing or verify the files."}
}else{Report FAIL 'Counter-Strike 2' 'Not found in any Steam library. Install it, or pass -CS2Root.'}
if(Get-Process steam -ErrorAction SilentlyContinue){Report PASS 'Steam running' 'yes'}
else{Report WARN 'Steam running' 'Not running now; start Steam before Play.ps1 (CS2 needs it).'}

# .NET 10 SDK
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
$sdks=@()
if($dotnet){$sdks=@(& $dotnet.Source --list-sdks 2>$null | Where-Object {$_ -match '^10\.'})}
if($sdks.Count){Report PASS '.NET 10 SDK' (($sdks[-1] -split ' ')[0])}
elseif($dotnet){Report FAIL '.NET 10 SDK' 'dotnet is installed but has no 10.x SDK. Install the .NET 10 SDK (not just the runtime).'}
else{Report FAIL '.NET 10 SDK' 'dotnet not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download.'}

# Visual Studio 2022 C++ tools, CMake and the Windows SDK (tools/Build-Native.ps1)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=$null
if(Test-Path -LiteralPath $vswhere){
    $vs=& $vswhere -version '[17.0,18.0)' -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -latest -property installationPath 2>$null | Select-Object -First 1
}
if($vs){Report PASS 'VS 2022 C++ tools' $vs}
else{Report FAIL 'VS 2022 C++ tools' 'Install Visual Studio 2022 (Community is fine) with the "Desktop development with C++" workload.'}
$cmake=(Get-Command cmake -ErrorAction SilentlyContinue).Source
if(!$cmake -and $vs){
    $bundled=Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if(Test-Path -LiteralPath $bundled){$cmake=$bundled}
}
if($cmake){Report PASS 'CMake' $cmake}
else{Report FAIL 'CMake' 'Not found. Add "C++ CMake tools for Windows" in the Visual Studio Installer.'}
$kits=$null
try{$kits=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction Stop).KitsRoot10}catch{}
if($kits -and (Test-Path -LiteralPath (Join-Path $kits 'Include'))){Report PASS 'Windows SDK' $kits}
else{Report FAIL 'Windows SDK' 'Not found. Add a Windows 10/11 SDK in the Visual Studio Installer.'}

# Java 25 (Minecraft 26.3). Same default as Build.ps1 and Play.ps1: the Minecraft Launcher's runtime.
$launcherJava='C:\Program Files (x86)\Minecraft Launcher\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon'
$candidates=@()
if($JavaHome){$candidates+=@{Home=$JavaHome;From='-JavaHome'}}
$candidates+=@{Home=$launcherJava;From='Minecraft Launcher (the scripts'' default)'}
if($env:JAVA_HOME){$candidates+=@{Home=$env:JAVA_HOME;From='JAVA_HOME'}}
$onPath=(Get-Command java -ErrorAction SilentlyContinue).Source
if($onPath){$candidates+=@{Home=(Split-Path (Split-Path $onPath -Parent) -Parent);From='PATH'}}
$java=$null;$javaReported=$false
foreach($c in $candidates){
    $release=Join-Path $c.Home 'release'
    if(!(Test-Path -LiteralPath (Join-Path $c.Home 'bin/java.exe')) -or !(Test-Path -LiteralPath $release)){
        if($c.From -eq '-JavaHome'){Report FAIL 'Java 25' "No Java installation at $($c.Home) (expected bin\java.exe and a release file)."; $javaReported=$true; break}
        continue
    }
    $m=[regex]::Match([IO.File]::ReadAllText($release),'JAVA_VERSION="(\d+)[^"]*"')
    if($m.Success -and [int]$m.Groups[1].Value -ge 25){$java=$c;$java.Version=$m.Groups[0].Value;break}
    if($c.From -eq '-JavaHome'){Report FAIL 'Java 25' "$($c.Home) is not Java 25 or newer ($($m.Groups[0].Value))."; $javaReported=$true; break}
}
if($java){
    $note=if($java.Home -eq $launcherJava){'used automatically'}else{"pass -JavaHome `"$($java.Home)`" to Build.ps1 and Play.ps1"}
    Report PASS 'Java 25' "$($java.Home) [$($java.From)]; $note"
}elseif(!$javaReported){
    Report FAIL 'Java 25' 'Not found. Install the Minecraft Launcher (bundles Java 25) or Eclipse Temurin 25, then pass -JavaHome.'
}

# Disk space for the private CS2 copy (Prepare-CS2.ps1) plus build caches
$runtimeGame=Join-Path $project 'runtime/game/csgo'
$drive=[IO.DriveInfo]([IO.Path]::GetPathRoot($project))
if(Test-Path -LiteralPath (Join-Path $runtimeGame 'gameinfo.gi')){
    Report PASS 'Disk space' "Private CS2 copy already prepared; $(Size $drive.AvailableFreeSpace) free on $($drive.Name)"
}elseif($cs2Ready){
    $stock=Join-Path $CS2Root 'game'
    $needed=(Get-ChildItem -LiteralPath (Join-Path $stock 'csgo') -File | Measure-Object Length -Sum).Sum
    foreach($folder in 'bin','core'){$needed+=(Get-ChildItem -LiteralPath (Join-Path $stock $folder) -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum}
    $needed+=(Get-Item -LiteralPath (Join-Path $stock 'csgo/maps/de_dust2.vpk')).Length+3GB
    if($drive.AvailableFreeSpace -ge $needed){Report PASS 'Disk space' "$(Size $drive.AvailableFreeSpace) free on $($drive.Name), about $(Size $needed) needed"}
    else{Report FAIL 'Disk space' "$(Size $drive.AvailableFreeSpace) free on $($drive.Name); the private CS2 copy and builds need about $(Size $needed). Free space or move the project folder."}
}else{Report WARN 'Disk space' "$(Size $drive.AvailableFreeSpace) free on $($drive.Name); plan for about 60 GB (CS2 not found, so not measured)."}

# Internet for first-time downloads (Metamod, CounterStrikeSharp, NuGet, Gradle, Fabric, Minecraft)
$firstRun=!(Test-Path -LiteralPath (Join-Path $project '.cache/downloads')) -or !(Test-Path -LiteralPath (Join-Path $project '.cache/gradle'))
$hosts='github.com','api.nuget.org','services.gradle.org','maven.fabricmc.net','libraries.minecraft.net'
$unresolved=@($hosts|Where-Object{try{[void][Net.Dns]::GetHostAddresses($_);$false}catch{$true}})
if(!$unresolved.Count){Report PASS 'Internet' 'Download hosts resolve'}
elseif($firstRun){Report FAIL 'Internet' "Cannot resolve $($unresolved -join ', '); the first build and setup must download dependencies."}
else{Report WARN 'Internet' "Cannot resolve $($unresolved -join ', '); fine if everything is already cached."}

# Where this machine is in the setup
$plugin=Join-Path $runtimeGame 'addons/counterstrikesharp/plugins/CS2Craft.Server'
$built=(Test-Path -LiteralPath (Join-Path $project 'native/build/Release/CS2Craft.Renderer.dll')) -and (Test-Path -LiteralPath (Join-Path $project 'minecraft/build/libs'))
$prepared=(Test-Path -LiteralPath (Join-Path $project 'runtime/installation.json')) -and (Test-Path -LiteralPath (Join-Path $plugin 'CS2Craft.Renderer.dll'))
Write-Host ''
Report INFO 'Built' $(if($built){'yes'}else{'no: run tools\Build.ps1'})
Report INFO 'Prepared' $(if($prepared){'yes'}else{'no: run tools\Prepare-CS2.ps1 after building'})

Write-Host ''
if($script:failures){
    Write-Host "$($script:failures) required item(s) missing, $($script:warnings) warning(s). See docs\INSTALL.md." -ForegroundColor Red
    exit 1
}
Write-Host "Ready ($($script:warnings) warning(s)). Next: $(if(!$built){'.\tools\Build.ps1'}elseif(!$prepared){'.\tools\Prepare-CS2.ps1'}else{'.\tools\Play.ps1'})" -ForegroundColor Green
exit 0
