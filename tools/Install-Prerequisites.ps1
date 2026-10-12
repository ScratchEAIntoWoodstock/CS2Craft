[CmdletBinding()]
param([switch]$Yes,[switch]$SkipSetup,[string]$CS2Root='')
# Installs what CS2Craft needs and this machine lacks, from each vendor's official download only:
# Steam (Valve), Counter-Strike 2 (through Steam), the .NET 10 SDK and Visual Studio 2022 Community's
# C++ tools (Microsoft), and Java 25 (Eclipse Temurin). Every download's digital signature is checked
# against its publisher before it runs. It asks before each install (-Yes skips the questions); the
# vendors' own installers still show their licence and progress, and Windows asks for administrator
# rights (UAC) where they need it. Afterwards it can run Build.ps1 and Prepare-CS2.ps1. See docs/INSTALL.md.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue' # Windows PowerShell downloads far faster without the progress bar
[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib\Prerequisites.ps1')
$downloads=Join-Path $project '.cache\prerequisites'
$script:rebootNeeded=$false
$script:skipped=@()

function Step([string]$Text){Write-Host "`n== $Text" -ForegroundColor Cyan}
function Ok([string]$Text){Write-Host "   $Text" -ForegroundColor Green}
function Note([string]$Text){Write-Host "   $Text"}
function Ask([string]$Question){
    if($Yes){return $true}
    $answer=Read-Host "   $Question [Y/n]"
    return ($answer -eq '' -or $answer -match '^\s*[yY]')
}

# Downloads an official installer and refuses it unless Windows reports a valid signature from $Publisher.
function Get-Installer([string]$Name,[string]$Url,[string]$Publisher){
    New-Item -ItemType Directory -Force -Path $downloads|Out-Null
    $file=Join-Path $downloads $Name
    Note "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $file -UseBasicParsing
    Test-Publisher $file $Publisher
    Note ("Downloaded {0:N0} MB; signed by {1}" -f ((Get-Item -LiteralPath $file).Length/1MB),$Publisher)
    $file
}
function Test-Publisher([string]$File,[string]$Publisher){
    $signature=Get-AuthenticodeSignature -LiteralPath $File
    $subject=if($signature.SignerCertificate){$signature.SignerCertificate.Subject}else{'none'}
    if($signature.Status -ne 'Valid' -or $subject -notmatch ('(^|, )O='+[regex]::Escape($Publisher)+'(,|$)')){
        Remove-Item -LiteralPath $File -ErrorAction SilentlyContinue
        throw "Refusing $File`: signature $($signature.Status), signer '$subject', expected $Publisher."
    }
}
# Runs an installer and waits. 3010 and 1641 mean success with a restart pending.
function Invoke-Installer([string]$File,[string]$Arguments){
    $process=if($Arguments){Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru}else{Start-Process -FilePath $File -Wait -PassThru}
    if($process.ExitCode -in 3010,1641){$script:rebootNeeded=$true;return}
    if($process.ExitCode -ne 0){throw "$([IO.Path]::GetFileName($File)) exited with code $($process.ExitCode)."}
}
function Skip([string]$Name){$script:skipped+=$Name;Note "Skipped $Name."}

Write-Host 'CS2Craft prerequisite installer' -ForegroundColor Cyan
Write-Host 'Everything comes from the vendor''s official download and is free: Steam and Counter-Strike 2 (Valve),'
Write-Host '.NET 10 SDK and Visual Studio 2022 Community (Microsoft), Java 25 (Eclipse Temurin).'
if(!([Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Platform -eq 'Win32NT')){throw 'CS2Craft needs 64-bit Windows 10 or 11.'}

# 1. Steam
Step 'Steam'
if(Get-SteamPath){Ok "Already installed: $(Get-SteamPath)"}
elseif(Ask 'Install Steam? Licence: https://store.steampowered.com/subscriber_agreement/'){
    $setup=Get-Installer 'SteamSetup.exe' 'https://cdn.fastly.steamstatic.com/client/installer/SteamSetup.exe' 'Valve Corp.'
    Note 'Follow the Steam setup window, then sign in to Steam (or create a free account).'
    Invoke-Installer $setup ''
    if(Get-SteamPath){Ok 'Steam installed.'}else{throw 'Steam setup finished but Steam is not registered; run this script again after installing Steam.'}
}else{Skip 'Steam'}

# 2. Counter-Strike 2: started now so its large download runs while the other tools install.
Step 'Counter-Strike 2'
$cs2=Find-CS2 $CS2Root
$cs2Started=$false
if($cs2 -and (Test-Path -LiteralPath (Join-Path $cs2 'game/csgo/maps/de_dust2.vpk'))){Ok "Already installed: $cs2"}
elseif(!(Get-SteamPath)){Note 'Needs Steam first.';Skip 'Counter-Strike 2'}
elseif(Ask 'Open Steam''s install page for Counter-Strike 2 (free to play, about 60 GB)?'){
    Start-Process 'steam://install/730'
    Note 'In Steam: sign in if asked, choose a library folder and click Install. The download continues in the'
    Note 'background while this script installs the rest; the script waits for it at the end.'
    $cs2Started=$true
}else{Skip 'Counter-Strike 2'}

# 3. .NET 10 SDK
Step '.NET 10 SDK'
if(Get-DotNet10Sdk){Ok "Already installed: $(Get-DotNet10Sdk)"}
elseif(Ask 'Install the .NET 10 SDK? Licence: https://github.com/dotnet/core/blob/main/license-information.md'){
    $setup=Get-Installer 'dotnet-sdk-10-win-x64.exe' 'https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe' 'Microsoft Corporation'
    Invoke-Installer $setup '/install /passive /norestart'
    Update-SessionPath
    if(Get-DotNet10Sdk){Ok "Installed: $(Get-DotNet10Sdk)"}else{throw '.NET 10 SDK setup finished but no 10.x SDK was found.'}
}else{Skip '.NET 10 SDK'}

# 4. Visual Studio 2022 C++ tools (with CMake and the Windows SDK, both recommended parts of the workload)
Step 'Visual Studio 2022 C++ tools'
$vs=Find-VisualStudioCpp
$workload='--add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended --passive --norestart'
if($vs -and (Find-CMake $vs) -and (Find-WindowsSdk)){Ok "Already installed: $vs"}
else{
    $existing=Find-VisualStudio2022
    $licence='Licence: https://visualstudio.microsoft.com/license-terms/vs2022-ga-community/ (Community is free for individuals)'
    if($existing){
        if(Ask "Add the ""Desktop development with C++"" workload to $existing? $licence"){
            $installer=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'
            Test-Publisher $installer 'Microsoft Corporation'
            Note 'The Visual Studio Installer downloads a few GB; this takes a while.'
            Invoke-Installer $installer "modify --installPath ""$existing"" $workload"
        }else{Skip 'Visual Studio C++ tools'}
    }elseif(Ask "Install Visual Studio 2022 Community with only the C++ workload (a few GB)? $licence"){
        $setup=Get-Installer 'vs_community.exe' 'https://aka.ms/vs/17/release/vs_community.exe' 'Microsoft Corporation'
        Note 'The Visual Studio Installer downloads a few GB; this takes a while.'
        Invoke-Installer $setup "$workload --wait"
    }else{Skip 'Visual Studio C++ tools'}
    if(!($script:skipped -contains 'Visual Studio C++ tools')){
        $vs=Find-VisualStudioCpp
        if($vs -and (Find-CMake $vs) -and (Find-WindowsSdk)){Ok "Installed: $vs"}
        else{throw 'Visual Studio setup finished but the C++ compiler, CMake or Windows SDK is still missing; open the Visual Studio Installer and add "Desktop development with C++".'}
    }
}

# 5. Java 25
Step 'Java 25'
$java=Find-Java25
if($java){Ok "Already installed: $($java.Home) [$($java.From)]"}
elseif(Ask 'Install Eclipse Temurin 25 (Java)? Licence: GPLv2 with Classpath Exception, https://openjdk.org/legal/gplv2+ce.html'){
    $setup=Get-Installer 'temurin-25-jdk-x64.msi' 'https://api.adoptium.net/v3/installer/latest/25/ga/windows/x64/jdk/hotspot/normal/eclipse' 'Eclipse Foundation'
    # FeatureJavaHome sets JAVA_HOME, which Build.ps1 and Play.ps1 use when no -JavaHome is passed.
    Invoke-Installer (Join-Path $env:SystemRoot 'System32\msiexec.exe') "/i ""$setup"" /passive /norestart ADDLOCAL=FeatureMain,FeatureEnvironment,FeatureJavaHome"
    Update-SessionPath
    $java=Find-Java25
    if($java){Ok "Installed: $($java.Home)"}else{throw 'Temurin setup finished but Java 25 was not found.'}
}else{Skip 'Java 25'}

# 6. Wait for Counter-Strike 2
if($cs2Started){
    Step 'Waiting for Counter-Strike 2'
    while($true){
        $cs2=Find-CS2 $CS2Root
        if($cs2 -and (Test-Path -LiteralPath (Join-Path $cs2 'game/csgo/maps/de_dust2.vpk'))){Ok "Installed: $cs2";break}
        if($Yes){Note 'Not finished yet; checking again in a minute (Ctrl+C to stop and rerun this script later).';Start-Sleep -Seconds 60;continue}
        $answer=Read-Host '   Press Enter once Steam shows Counter-Strike 2 as installed (or type S to skip)'
        if($answer -match '^\s*[sS]'){Skip 'Counter-Strike 2';break}
    }
}

# Remove the downloaded installers; the tools are installed.
if(Test-Path -LiteralPath $downloads){Remove-Item -LiteralPath $downloads -Recurse -Force}

Step 'Check'
Update-SessionPath
& (Join-Path $PSScriptRoot 'Check-Prerequisites.ps1') -CS2Root $CS2Root
$ready=$LASTEXITCODE -eq 0
Write-Host ''
Write-Host 'Minecraft: CS2Craft runs a Minecraft development client and downloads the game files from Mojang during the build.' -ForegroundColor Yellow
Write-Host 'Please own Minecraft: Java Edition (https://www.minecraft.net/) to play it.' -ForegroundColor Yellow
if($script:rebootNeeded){Write-Host 'An installer asked for a restart. Restart Windows before building.' -ForegroundColor Yellow}
if($script:skipped.Count){Note "Skipped: $($script:skipped -join ', ')."}
if(!$ready){Write-Host 'Not ready yet: fix the FAIL lines above (or rerun this script).' -ForegroundColor Red;exit 1}
if($SkipSetup -or $script:rebootNeeded){Note 'Next: .\tools\Build.ps1, then .\tools\Prepare-CS2.ps1, then .\tools\Play.ps1.';exit 0}

# 7. Build and prepare (optional)
Step 'Build and prepare'
if(Ask 'Build CS2Craft now (a few minutes; downloads Minecraft and Fabric the first time)?'){
    & (Join-Path $PSScriptRoot 'Build.ps1')
    $question=if(Test-Path -LiteralPath (Join-Path $project 'runtime/installation.json')){'Update the private CS2 copy with this build now?'}
        else{'Prepare the private CS2 copy now (copies about 55 GB; Steam''s copy is not changed)?'}
    if(Ask $question){
        if($CS2Root){& (Join-Path $PSScriptRoot 'Prepare-CS2.ps1') -CS2Root $CS2Root}else{& (Join-Path $PSScriptRoot 'Prepare-CS2.ps1')}
    }else{Note 'Next: .\tools\Prepare-CS2.ps1, then .\tools\Play.ps1.';exit 0}
    Ok 'Done. Start a game with .\tools\Play.ps1 (Steam running), then type css_cs2craft_play in the CS2 console.'
}else{Note 'Next: .\tools\Build.ps1, then .\tools\Prepare-CS2.ps1, then .\tools\Play.ps1.'}
