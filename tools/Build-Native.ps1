[CmdletBinding()]
param([string]$CMake='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib\Prerequisites.ps1')
# Any Visual Studio 2022 edition with the C++ tools: Community, Professional, Enterprise or Build Tools.
$vs=Find-VisualStudioCpp
if(!$CMake){$CMake=Find-CMake $vs}
if(!$CMake -or !(Test-Path -LiteralPath $CMake)){throw 'Install the Visual Studio 2022 C++ tools and CMake (tools\Install-Prerequisites.ps1 can), or pass -CMake.'}
$build=Join-Path $project 'native/build'
$configure=@('-S',(Join-Path $project 'native'),'-B',$build,'-G','Visual Studio 17 2022','-A','x64')
# Pin the first configure to the instance that has the C++ tools; a later one must not change it.
if($vs -and !(Test-Path -LiteralPath (Join-Path $build 'CMakeCache.txt'))){$configure+="-DCMAKE_GENERATOR_INSTANCE=$($vs.TrimEnd('\').Replace('\','/'))"}
& $CMake @configure
if($LASTEXITCODE -ne 0){throw 'Native configure failed'}
& $CMake --build $build --config Release
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
& (Join-Path $project 'native/build/Release/CS2Craft.RendererTest.exe')
if($LASTEXITCODE -ne 0){throw 'Native renderer tests failed'}
