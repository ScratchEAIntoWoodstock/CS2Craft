[CmdletBinding()]
param([string]$JavaHome='',[string]$CS2Root='')
# Read-only: reports what this machine still needs before tools/Build.ps1, Prepare-CS2.ps1 and Play.ps1.
# Exit code 0 = ready (warnings allowed), 1 = something required is missing. See docs/INSTALL.md.
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib\Prerequisites.ps1')
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

# Visual Studio's build tracker fails once build paths pass Windows' 260-character limit; the deepest
# native build file sits about 120 characters below the project folder.
if($project.Length -gt 130){Report FAIL 'Project folder path' "$($project.Length) characters is too long for the C++ build. Move the folder somewhere short, such as C:\CS2Craft."}
elseif($project.Length -gt 80){Report WARN 'Project folder path' "$($project.Length) characters; a shorter path (such as C:\CS2Craft) avoids Windows path-length limits."}
else{Report PASS 'Project folder path' $project}

# Steam and Counter-Strike 2 (same discovery as Prepare-CS2.ps1)
$steam=Get-SteamPath
if($steam){Report PASS 'Steam' $steam}else{Report FAIL 'Steam' 'Not installed (no HKCU\Software\Valve\Steam). Install-Prerequisites.ps1 can install it.'}
$explicitCS2=$CS2Root
$CS2Root=Find-CS2 $CS2Root
$cs2Ready=$false
if($CS2Root){
    if(Test-Path -LiteralPath (Join-Path $CS2Root 'game/csgo/maps/de_dust2.vpk')){Report PASS 'Counter-Strike 2' $CS2Root;$cs2Ready=$true}
    else{Report FAIL 'Counter-Strike 2' "Found at $CS2Root but de_dust2.vpk is missing; let Steam finish installing or verify the files."}
}elseif($explicitCS2){Report FAIL 'Counter-Strike 2' "No game\bin\win64\cs2.exe under $explicitCS2."}
else{Report FAIL 'Counter-Strike 2' 'Not found in any Steam library. Install it in Steam (free), or pass -CS2Root.'}
if(Get-Process steam -ErrorAction SilentlyContinue){Report PASS 'Steam running' 'yes'}
else{Report WARN 'Steam running' 'Not running now; start Steam before Play.ps1 (CS2 needs it).'}

# .NET 10 SDK
$sdk=Get-DotNet10Sdk
if($sdk){Report PASS '.NET 10 SDK' $sdk}
elseif(Get-Command dotnet -ErrorAction SilentlyContinue){Report FAIL '.NET 10 SDK' 'dotnet is installed but has no 10.x SDK. Install the .NET 10 SDK (not just the runtime).'}
else{Report FAIL '.NET 10 SDK' 'dotnet not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download.'}

# Visual Studio 2022 C++ tools, CMake and the Windows SDK (tools/Build-Native.ps1)
$vs=Find-VisualStudioCpp
if($vs){Report PASS 'VS 2022 C++ tools' $vs}
elseif(Find-VisualStudio2022){Report FAIL 'VS 2022 C++ tools' 'Visual Studio 2022 is installed without the "Desktop development with C++" workload; add it in the Visual Studio Installer.'}
else{Report FAIL 'VS 2022 C++ tools' 'Install Visual Studio 2022 (Community is fine) with the "Desktop development with C++" workload.'}
$cmake=Find-CMake $vs
if($cmake){Report PASS 'CMake' $cmake}
else{Report FAIL 'CMake' 'Not found. Add "C++ CMake tools for Windows" in the Visual Studio Installer.'}
$kits=Find-WindowsSdk
if($kits){Report PASS 'Windows SDK' $kits}
else{Report FAIL 'Windows SDK' 'Not found. Add a Windows 10/11 SDK in the Visual Studio Installer.'}

# Java 25 (Minecraft 26.3): the build and launch scripts find it the same way.
$java=Find-Java25 $JavaHome
if($java){Report PASS 'Java 25' "$($java.Home) [$($java.From)]"}
elseif($JavaHome){
    $major=Get-JavaMajor $JavaHome
    Report FAIL 'Java 25' $(if($major){"$JavaHome is Java $major; Java 25 or newer is needed."}else{"No Java installation at $JavaHome (expected bin\java.exe and a release file)."})
}else{Report FAIL 'Java 25' 'Not found. Install the Minecraft Launcher (bundles Java 25) or Eclipse Temurin 25.'}

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
    Write-Host "$($script:failures) required item(s) missing, $($script:warnings) warning(s). .\tools\Install-Prerequisites.ps1 can install them; see docs\INSTALL.md." -ForegroundColor Red
    exit 1
}
Write-Host "Ready ($($script:warnings) warning(s)). Next: $(if(!$built){'.\tools\Build.ps1'}elseif(!$prepared){'.\tools\Prepare-CS2.ps1'}else{'.\tools\Play.ps1'})" -ForegroundColor Green
exit 0
