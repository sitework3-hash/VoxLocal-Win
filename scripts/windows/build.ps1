[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$vendorRoot = Join-Path $repoRoot 'vendor'
$dotnet = Join-Path $vendorRoot 'dotnet\dotnet.exe'
$whisperRoot = Join-Path $vendorRoot 'whisper'
$distRoot = Join-Path $repoRoot 'dist\windows\VoxLocal'
if (-not (Test-Path $dotnet)) { throw 'Run scripts/windows/bootstrap.ps1 first.' }
$whisperCli = Get-ChildItem -Path $whisperRoot -Filter 'whisper-cli.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $whisperCli) { throw 'whisper-cli.exe not found. Run scripts/windows/bootstrap.ps1 first.' }

$env:DOTNET_ROOT = Join-Path $vendorRoot 'dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:NUGET_PACKAGES = Join-Path $vendorRoot 'nuget'
$env:VOXLOCAL_REPO_ROOT = $repoRoot
$project = Join-Path $repoRoot 'windows\VoxLocal.Windows\VoxLocal.Windows.csproj'
& $dotnet restore $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $distRoot -p:PublishSingleFile=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath $whisperCli.FullName -Destination (Join-Path $distRoot 'whisper-cli.exe') -Force
Get-ChildItem -LiteralPath $whisperCli.DirectoryName -Filter '*.dll' -File | Copy-Item -Destination $distRoot -Force
Write-Host "[voxlocal] Build ready: $(Join-Path $distRoot 'VoxLocal.exe')"
