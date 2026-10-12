# Shared prerequisite detection, dot-sourced by Check-Prerequisites.ps1, Install-Prerequisites.ps1,
# Build.ps1, Play.ps1 and Start-Minecraft-Dev.ps1. Read-only; defines functions only.

$script:MinecraftLauncherJava='C:\Program Files (x86)\Minecraft Launcher\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon'

function Get-SteamPath {
    try{(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath}catch{$null}
}

function Find-CS2([string]$CS2Root='') {
    if($CS2Root){if(Test-Path -LiteralPath (Join-Path $CS2Root 'game/bin/win64/cs2.exe')){return $CS2Root};return $null}
    $steam=Get-SteamPath
    if(!$steam){return $null}
    $libraries=@($steam)
    $vdf=Join-Path $steam 'steamapps/libraryfolders.vdf'
    if(Test-Path -LiteralPath $vdf){
        foreach($m in [regex]::Matches([IO.File]::ReadAllText($vdf),'"path"\s+"([^"]+)"')){$libraries+=$m.Groups[1].Value.Replace('\\','\')}
    }
    foreach($library in $libraries|Select-Object -Unique){
        $candidate=Join-Path $library 'steamapps/common/Counter-Strike Global Offensive'
        if(Test-Path -LiteralPath (Join-Path $candidate 'game/bin/win64/cs2.exe')){return $candidate}
    }
    $null
}

function Get-DotNet10Sdk {
    $dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
    if(!$dotnet){$default=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe';if(Test-Path -LiteralPath $default){$dotnet=Get-Item $default}}
    if(!$dotnet){return $null}
    $path=if($dotnet.Source){$dotnet.Source}else{$dotnet.FullName}
    $sdks=@(& $path --list-sdks 2>$null|Where-Object{$_ -match '^10\.'})
    if($sdks.Count){($sdks[-1] -split ' ')[0]}else{$null}
}

function Get-VsWhere {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if(Test-Path -LiteralPath $vswhere){$vswhere}else{$null}
}

# Visual Studio 2022 with the x64 C++ compiler (the native build uses its CMake generator).
function Find-VisualStudioCpp {
    $vswhere=Get-VsWhere
    if(!$vswhere){return $null}
    & $vswhere -version '[17.0,18.0)' -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -latest -property installationPath 2>$null|Select-Object -First 1
}

# Any Visual Studio 2022 instance, with or without the C++ workload.
function Find-VisualStudio2022 {
    $vswhere=Get-VsWhere
    if(!$vswhere){return $null}
    & $vswhere -version '[17.0,18.0)' -products * -latest -property installationPath 2>$null|Select-Object -First 1
}

function Find-CMake([string]$VisualStudio='') {
    $cmake=(Get-Command cmake -ErrorAction SilentlyContinue).Source
    if($cmake){return $cmake}
    if(!$VisualStudio){$VisualStudio=Find-VisualStudioCpp}
    if($VisualStudio){
        $bundled=Join-Path $VisualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        if(Test-Path -LiteralPath $bundled){return $bundled}
    }
    $null
}

function Find-WindowsSdk {
    try{$kits=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction Stop).KitsRoot10}catch{return $null}
    if($kits -and (Test-Path -LiteralPath (Join-Path $kits 'Include'))){$kits}else{$null}
}

function Get-JavaMajor([string]$JavaHome) {
    $release=Join-Path $JavaHome 'release'
    if(!(Test-Path -LiteralPath (Join-Path $JavaHome 'bin/java.exe')) -or !(Test-Path -LiteralPath $release)){return 0}
    $m=[regex]::Match([IO.File]::ReadAllText($release),'JAVA_VERSION="(\d+)')
    if($m.Success){[int]$m.Groups[1].Value}else{0}
}

# Java 25+ for Minecraft 26.3. Order: explicit -JavaHome, the Minecraft Launcher's runtime, JAVA_HOME
# (this process, then the user's and the machine's saved value: a fresh install is not in this
# process yet), Eclipse Adoptium installs, then java on PATH. Returns @{Home; From} or $null.
function Find-Java25([string]$JavaHome='') {
    if($JavaHome){if((Get-JavaMajor $JavaHome) -ge 25){return @{Home=$JavaHome;From='-JavaHome'}};return $null}
    $candidates=@(@{Home=$script:MinecraftLauncherJava;From='Minecraft Launcher'})
    foreach($scope in 'Process','User','Machine'){
        $value=[Environment]::GetEnvironmentVariable('JAVA_HOME',$scope)
        if($value){$candidates+=@{Home=$value;From="JAVA_HOME ($($scope.ToLower()))"}}
    }
    foreach($root in (Join-Path $env:ProgramFiles 'Eclipse Adoptium'),(Join-Path $env:LOCALAPPDATA 'Programs\Eclipse Adoptium')){
        if(Test-Path -LiteralPath $root){
            foreach($jdk in Get-ChildItem -LiteralPath $root -Directory -Filter 'jdk-*'|Sort-Object Name -Descending){$candidates+=@{Home=$jdk.FullName;From='Eclipse Adoptium'}}
        }
    }
    $onPath=(Get-Command java -ErrorAction SilentlyContinue).Source
    if($onPath){$candidates+=@{Home=(Split-Path (Split-Path $onPath -Parent) -Parent);From='PATH'}}
    foreach($c in $candidates){if((Get-JavaMajor $c.Home) -ge 25){return $c}}
    $null
}

# Picks up PATH changes made by an installer that ran after this PowerShell started.
function Update-SessionPath {
    $machine=[Environment]::GetEnvironmentVariable('Path','Machine')
    $user=[Environment]::GetEnvironmentVariable('Path','User')
    $env:Path=(@($machine,$user)|Where-Object{$_}) -join ';'
}
