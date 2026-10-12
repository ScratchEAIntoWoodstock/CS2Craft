[CmdletBinding()]
param([string]$CMake='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$CMake){
    $command=Get-Command cmake -ErrorAction SilentlyContinue
    if($command){$CMake=$command.Source}
    else{$CMake='C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'}
}
if(!(Test-Path -LiteralPath $CMake)){throw 'Install the Visual Studio C++ tools and CMake, or pass -CMake.'}
& $CMake -S (Join-Path $project 'native') -B (Join-Path $project 'native/build') -G 'Visual Studio 17 2022' -A x64
if($LASTEXITCODE -ne 0){throw 'Native configure failed'}
& $CMake --build (Join-Path $project 'native/build') --config Release
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
& (Join-Path $project 'native/build/Release/CS2Craft.RendererTest.exe')
if($LASTEXITCODE -ne 0){throw 'Native renderer tests failed'}
