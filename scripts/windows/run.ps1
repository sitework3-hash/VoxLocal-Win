[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$app = Join-Path $repoRoot 'dist\windows\VoxLocal\VoxLocal.exe'
if (-not (Test-Path $app)) { throw 'Build artifact is missing. Run scripts/windows/build.ps1 first.' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app)
Write-Host "[voxlocal] Started: $app"
