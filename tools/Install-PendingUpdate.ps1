[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$pending=Join-Path $project 'runtime/pending-update'
$manifestPath=Join-Path $pending 'ready.json'
if(!(Test-Path -LiteralPath $manifestPath)){return}
if(Get-Process cs2 -ErrorAction SilentlyContinue){throw 'Close CS2 before installing the pending CS2Craft update.'}
$install=Get-Content -LiteralPath (Join-Path $project 'runtime/installation.json') -Raw | ConvertFrom-Json
$destination=Join-Path $install.GameDirectory 'addons/counterstrikesharp/plugins/CS2Craft.Server'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach($entry in $manifest.Files){
    $file=[IO.Path]::GetFullPath((Join-Path $pending $entry.Name))
    $target=[IO.Path]::GetFullPath((Join-Path $destination $entry.Name))
    if([IO.Path]::IsPathRooted($entry.Name) -or
        !$file.StartsWith([IO.Path]::GetFullPath($pending)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        !$target.StartsWith([IO.Path]::GetFullPath($destination)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid update filename.'}
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Hash){throw "Update checksum mismatch: $($entry.Name)"}
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach($entry in $manifest.Files){
    $target=Join-Path $destination $entry.Name
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $pending $entry.Name) -Destination $target -Force
}
foreach($entry in $manifest.Files){
    if((Get-FileHash -LiteralPath (Join-Path $destination $entry.Name) -Algorithm SHA256).Hash -ne $entry.Hash){throw "Installed checksum mismatch: $($entry.Name)"}
}
Remove-Item -LiteralPath $manifestPath
Write-Host 'Installed the pending CS2Craft death/respawn and mouse-input update.'
