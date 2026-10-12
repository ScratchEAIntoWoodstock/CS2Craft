[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_]+$')][string]$Map='de_dust2',[switch]$Dedicated,[switch]$SmokeTest,[switch]$ClientProbe,[switch]$InputProbe)
$ErrorActionPreference='Stop'
function Read-Shared([string]$Path) {
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=[IO.StreamReader]::new($stream)
    try {return $reader.ReadToEnd()} finally {$reader.Dispose()}
}
$project=Split-Path $PSScriptRoot -Parent
$installation=Join-Path $project 'runtime/installation.json'
if (!(Test-Path -LiteralPath $installation)) { throw 'Run tools/Prepare-CS2.ps1 first.' }
$install=Get-Content -LiteralPath $installation -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath (Join-Path $install.GameDirectory "maps/$Map.vpk"))) { throw "Map $Map has not been copied into the private runtime." }
if (Get-Process cs2 -ErrorAction SilentlyContinue) { throw 'Close the current CS2 session before starting the isolated mod.' }
& (Join-Path $PSScriptRoot 'Install-PendingUpdate.ps1')
$mapFile=Join-Path $install.GameDirectory "maps/$Map.vpk"
$collisionFile=Join-Path $install.GameDirectory "addons/counterstrikesharp/plugins/CS2Craft.Server/collision/$Map.c2map"
$stampFile=$collisionFile+'.json'
$source=Get-Item -LiteralPath $mapFile
$stamp=if(Test-Path -LiteralPath $stampFile){Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json}else{$null}
if(!(Test-Path -LiteralPath $collisionFile) -or !$stamp -or $stamp.Length -ne $source.Length -or $stamp.Modified -ne $source.LastWriteTimeUtc.Ticks -or $stamp.Format -ne 1){
    Write-Host "Preparing exact map collision for $Map..."
    dotnet run --project (Join-Path $project 'tools/CS2Craft.MapExport') -c Release -- $mapFile $collisionFile
    if($LASTEXITCODE -ne 0){throw 'Map collision export failed; gameplay was not started.'}
    @{Length=$source.Length;Modified=$source.LastWriteTimeUtc.Ticks;Format=1}|ConvertTo-Json|Set-Content -LiteralPath $stampFile
}
$runId=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stdout=Join-Path $project "runtime/cs2-$runId.stdout.log"
$stderr=Join-Path $project "runtime/cs2-$runId.stderr.log"
$logDirectory=Join-Path $install.GameDirectory 'addons/counterstrikesharp/logs'
$oldLengths=@{}
Get-ChildItem $logDirectory -Filter '*.txt' -ErrorAction SilentlyContinue | ForEach-Object {
    $oldLengths[$_.FullName]=(Read-Shared $_.FullName).Length
}
if ($ClientProbe -and ($Dedicated -or $SmokeTest)) { throw 'ClientProbe requires the graphical local client.' }
if($InputProbe){$ClientProbe=$true}
$arguments=@('-game','csgo','-insecure','-dx11','-console','-condebug',
    '+sv_lan','1','+sv_hibernate_when_empty','0','+game_type','0','+game_mode','0','+map',$Map)
if ($ClientProbe) { $arguments+=@('-cs2craft-probe','+sv_cheats','1','+bot_stop','1','+mp_warmup_pausetimer','1') }
if($InputProbe){$arguments+='-cs2craft-input-probe'}
if ($Dedicated -or $SmokeTest) { $arguments+=@('-dedicated','-ip','127.0.0.1','-port','27025') }
else { $arguments+=@('-windowed','-w','1280','-h','720') }
$process=Start-Process -FilePath $install.Executable -ArgumentList $arguments -WorkingDirectory (Split-Path $install.Executable -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$process.Id | Set-Content (Join-Path $project 'runtime/last-cs2-pid.txt')
Write-Host "Started private CS2 process $($process.Id). Console log: $stdout"
if ($SmokeTest) {
    try {
        $deadline=(Get-Date).AddSeconds(50)
        while ((Get-Date) -lt $deadline -and !$process.HasExited) {
            Start-Sleep -Seconds 2
            $process.Refresh()
            $loaded=$false
            Get-ChildItem $logDirectory -Filter '*.txt' -ErrorAction SilentlyContinue | ForEach-Object {
                $all=Read-Shared $_.FullName
                $offset=$oldLengths[$_.FullName]
                if($null -eq $offset -or $all.Length -lt $offset){$offset=0}
                if($all.Substring($offset).Contains('CS2Craft loaded.')){$loaded=$true}
            }
            $console=Read-Shared $stdout
            if ($loaded -and $console.Contains('player server started') -and !$console.Contains('NETWORK_DISCONNECT_CLIENT_NO_MAP')) {
                @{ pluginLoaded=$true; map=$Map; localOnly=$true; processId=$process.Id; stdout=$stdout; time=(Get-Date).ToString('o') } |
                    ConvertTo-Json | Set-Content (Join-Path $project 'runtime/smoke-result.json')
                Write-Host "PASS: CS2Craft loaded inside CS2 and $Map reached an active server."
                return
            }
        }
        throw "Runtime verification failed. Inspect $stdout and $stderr."
    } finally {
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
    }
}
