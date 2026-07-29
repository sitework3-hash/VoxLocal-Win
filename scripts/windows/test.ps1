[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$vendorRoot = Join-Path $repoRoot 'vendor'
$dotnet = Join-Path $vendorRoot 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { throw 'Run scripts/windows/bootstrap.ps1 first.' }
$env:DOTNET_ROOT = Join-Path $vendorRoot 'dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:NUGET_PACKAGES = Join-Path $vendorRoot 'nuget'
$env:VOXLOCAL_REPO_ROOT = $repoRoot
& $dotnet run --project (Join-Path $repoRoot 'windows\VoxLocal.Tests\VoxLocal.Tests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
