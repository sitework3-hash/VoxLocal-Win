[CmdletBinding()]
param(
    [ValidateSet('tiny', 'tiny.en', 'base', 'base.en', 'small', 'small.en', 'medium', 'large-v3', 'large-v3-turbo')]
    [string]$Model = 'base',
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$sizes = @{ tiny = 78; 'tiny.en' = 78; base = 148; 'base.en' = 148; small = 488; 'small.en' = 488; medium = 1530; 'large-v3' = 3100; 'large-v3-turbo' = 1620 }
if (-not $Yes) {
    $confirmation = Read-Host "Download $Model (~$($sizes[$Model]) MB)? [y/N]"
    if ($confirmation -notmatch '^(y|yes)$') { Write-Host '[voxlocal] Model download cancelled.'; exit 0 }
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$models = Join-Path $env:LOCALAPPDATA 'VoxLocal\models'
$fileName = "ggml-$Model.bin"
$destination = Join-Path $models $fileName
$temporary = "$destination.partial"
New-Item -ItemType Directory -Force -Path $models | Out-Null
Write-Host "[voxlocal] Downloading $fileName…"
Invoke-WebRequest -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$fileName" -OutFile $temporary
if ((Get-Item -LiteralPath $temporary).Length -lt 1MB) { Remove-Item -LiteralPath $temporary -Force; throw 'Downloaded model is unexpectedly small.' }
Move-Item -LiteralPath $temporary -Destination $destination -Force
Write-Host "[voxlocal] Model ready: $destination"
