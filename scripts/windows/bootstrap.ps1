[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$vendorRoot = Join-Path $repoRoot 'vendor'
$toolsRoot = Join-Path $vendorRoot 'tools'
$dotnetRoot = Join-Path $vendorRoot 'dotnet'
$whisperRoot = Join-Path $vendorRoot 'whisper'
$dotnetZip = Join-Path $toolsRoot 'dotnet-sdk-8.0.418-win-x64.zip'
$whisperZip = Join-Path $toolsRoot 'whisper-bin-x64-v1.9.1.zip'
$dotnetUrl = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.418/dotnet-sdk-8.0.418-win-x64.zip'
$whisperUrl = 'https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.1/whisper-bin-x64.zip'

New-Item -ItemType Directory -Force -Path $toolsRoot, $dotnetRoot, $whisperRoot | Out-Null

if (-not (Test-Path (Join-Path $dotnetRoot 'dotnet.exe'))) {
    Write-Host '[voxlocal] Downloading project-local .NET SDK 8.0.418…'
    Invoke-WebRequest -Uri $dotnetUrl -OutFile $dotnetZip
    Expand-Archive -LiteralPath $dotnetZip -DestinationPath $dotnetRoot -Force
    Remove-Item -LiteralPath $dotnetZip -Force
}

if (-not (Get-ChildItem -Path $whisperRoot -Filter 'whisper-cli.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)) {
    Write-Host '[voxlocal] Downloading official whisper.cpp v1.9.1 Windows binary…'
    Invoke-WebRequest -Uri $whisperUrl -OutFile $whisperZip
    Expand-Archive -LiteralPath $whisperZip -DestinationPath $whisperRoot -Force
    Remove-Item -LiteralPath $whisperZip -Force
}

$whisperCli = Get-ChildItem -Path $whisperRoot -Filter 'whisper-cli.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $whisperCli) {
    throw 'whisper-cli.exe was not found after extraction.'
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"
$env:NUGET_PACKAGES = Join-Path $vendorRoot 'nuget'
& (Join-Path $dotnetRoot 'dotnet.exe') --info
Write-Host "[voxlocal] whisper-cli ready: $($whisperCli.FullName)"
