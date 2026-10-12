[CmdletBinding()]
param([string]$CS2Root = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$CS2Root) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps/libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
            $libraries += $m.Groups[1].Value.Replace('\\','\')
        }
    }
    foreach ($library in $libraries | Select-Object -Unique) {
        $candidate = Join-Path $library 'steamapps/common/Counter-Strike Global Offensive'
        if (Test-Path -LiteralPath (Join-Path $candidate 'game/bin/win64/cs2.exe')) { $CS2Root = $candidate; break }
    }
}
if (!$CS2Root) { throw 'CS2 not found. Pass -CS2Root with the installation directory.' }
$CS2Root = [IO.Path]::GetFullPath($CS2Root)
$exe = Join-Path $CS2Root 'game/bin/win64/cs2.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Missing CS2 executable: $exe" }
$game = Join-Path $project 'runtime/game/csgo'
$cache = Join-Path $project '.cache/downloads'
New-Item -ItemType Directory -Force $game,$cache | Out-Null
$downloads = @(
    @{ File='metamod-1474.zip'; Url='https://github.com/alliedmodders/metamod-source/releases/download/2.0.0.1474/mmsource-2.0.0-git1474-windows.zip'; Hash='846158A0F67AD1E286B1AEC5EDB216D29DD35BB0FE754718A30F0FA2FE77A8CA' },
    @{ File='counterstrikesharp-1.0.376.zip'; Url='https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.376/counterstrikesharp-with-runtime-windows-1.0.376.zip'; Hash='4F2777545A0940351450440288E7487509D539F770E66CF1C30CC2419084C5F8' }
)
foreach ($d in $downloads) {
    $file = Join-Path $cache $d.File
    if (!(Test-Path -LiteralPath $file)) { Invoke-WebRequest $d.Url -OutFile $file }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $d.Hash) { throw "Checksum mismatch: $file" }
    Expand-Archive -LiteralPath $file -DestinationPath $game -Force
}
$sourceInfo = Join-Path $CS2Root 'game/csgo/gameinfo.gi'
$info = [IO.File]::ReadAllText($sourceInfo)
# Private copies only: no links allow writes to flow back to the Steam installation.
# Source 2 hardcodes several csgo-relative assets and needs the complete base pack.
$stockGame = Join-Path $CS2Root 'game/csgo'
foreach ($entry in Get-ChildItem -LiteralPath $stockGame -File) {
    if ($entry.Name -in @('gameinfo.gi','console.log')) { continue }
    $target = Join-Path $game $entry.Name
    if (!(Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $target).Length -ne $entry.Length -or (Get-Item -LiteralPath $target).LastWriteTimeUtc -ne $entry.LastWriteTimeUtc) {
        Copy-Item -LiteralPath $entry.FullName -Destination $target -Force
    }
}
foreach ($folder in @('bin','cfg','panorama','resource')) {
    $source = Join-Path $stockGame $folder
    if (Test-Path -LiteralPath $source) {
        $target = Join-Path $game $folder
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    }
}
$maps = Join-Path $game 'maps'
New-Item -ItemType Directory -Force $maps | Out-Null
Copy-Item -LiteralPath (Join-Path $stockGame 'maps/de_dust2.vpk') -Destination $maps -Force
foreach ($mapFolder in @('prefabs','ui')) {
    Copy-Item -LiteralPath (Join-Path $stockGame "maps/$mapFolder") -Destination $maps -Recurse -Force
}
$core = Join-Path $project 'runtime/game/core'
New-Item -ItemType Directory -Force $core | Out-Null
Copy-Item -Path (Join-Path $CS2Root 'game/core/*') -Destination $core -Recurse -Force
$searchPaths = @"
SearchPaths
        {
            Game csgo/addons/metamod
            Game csgo
            Game core
            Mod csgo
        }
"@
$info = [regex]::Replace($info, 'SearchPaths\s*\{[^}]*\}', $searchPaths, 1)
[IO.File]::WriteAllText((Join-Path $game 'gameinfo.gi'),$info)
$plugin = Join-Path $game 'addons/counterstrikesharp/plugins/CS2Craft.Server'
New-Item -ItemType Directory -Force $plugin | Out-Null
dotnet publish (Join-Path $project 'src/CS2Craft.Server') -c Release -o $plugin
if ($LASTEXITCODE -ne 0) { throw 'CS2 plugin build failed.' }
$renderer=Join-Path $project 'native/build/Release/CS2Craft.Renderer.dll'
if(Test-Path -LiteralPath $renderer){Copy-Item -LiteralPath $renderer -Destination $plugin -Force}
# This CS2 build ignores an absolute -game path when launched from the Steam binary tree.
# A private executable tree makes its default csgo root the isolated runtime directory.
$privateBin = Join-Path $project 'runtime/game/bin'
New-Item -ItemType Directory -Force $privateBin | Out-Null
Copy-Item -Path (Join-Path $CS2Root 'game/bin/*') -Destination $privateBin -Recurse -Force
$exe = Join-Path $privateBin 'win64/cs2.exe'
@{ CS2Root=$CS2Root; GameDirectory=$game; Executable=$exe } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $project 'runtime/installation.json') -Encoding utf8
Write-Host "Prepared isolated mod at $game"
Write-Host 'The Steam installation was read but not modified.'
