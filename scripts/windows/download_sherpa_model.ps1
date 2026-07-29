[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modelName = 'sherpa-onnx-streaming-t-one-russian-2025-09-08'
$modelsRoot = Join-Path $env:LOCALAPPDATA 'VoxLocal\models'
$target = Join-Path $modelsRoot $modelName
$modelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$modelName.tar.bz2"

function ConvertTo-GitPath([string] $path)
{
    $fullPath = [IO.Path]::GetFullPath($path).Replace('\', '/')
    if ($fullPath -match '^([A-Za-z]):/(.*)$')
    {
        return "/$($Matches[1].ToLower())/$($Matches[2])"
    }
    throw "Cannot convert path for Git Bash: $path"
}

New-Item -ItemType Directory -Force -Path $modelsRoot | Out-Null
if (Test-Path -LiteralPath $target)
{
    $model = Join-Path $target 'model.onnx'
    $tokens = Join-Path $target 'tokens.txt'
    if ((Test-Path -LiteralPath $model) -and (Get-Item -LiteralPath $model).Length -gt 100MB -and (Test-Path -LiteralPath $tokens))
    {
        Write-Host "[voxlocal] Sherpa Russian model is already installed: $target"
        exit 0
    }
    throw "Existing Sherpa model directory is incomplete: $target. It was not changed."
}

$temporaryRoot = Join-Path $modelsRoot '.sherpa-download'
$archive = Join-Path $temporaryRoot "$modelName.tar.bz2"
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
$gitBzipDirectory = Join-Path (Split-Path (Split-Path (Get-Command git.exe -ErrorAction Stop).Source -Parent) -Parent) 'usr\bin'
$bzip = Join-Path $gitBzipDirectory 'bzip2.exe'
$bash = Join-Path $gitBzipDirectory 'bash.exe'
if (-not (Test-Path -LiteralPath $bzip))
{
    throw 'Git for Windows bzip2.exe was not found. Git is required to unpack the official Sherpa model.'
}
if (-not (Test-Path -LiteralPath $bash))
{
    throw 'Git for Windows bash.exe was not found. Git is required to unpack the official Sherpa model.'
}
$completed = $false
try
{
    $archiveIsComplete = $false
    if (Test-Path -LiteralPath $archive)
    {
        & $bzip -t $archive 2>$null
        $archiveIsComplete = $LASTEXITCODE -eq 0
    }
    if (-not $archiveIsComplete)
    {
        Write-Host '[voxlocal] Downloading the official Sherpa-ONNX Russian streaming model (~140 MB)...'
        # curl resumes a partial official release download after an interrupted
        # connection or a stopped terminal task instead of restarting from zero.
        & curl.exe -L --fail --retry 3 --continue-at - --output $archive $modelUrl
        if ($LASTEXITCODE -ne 0)
        {
            throw "Failed to download the Sherpa model (curl exit code $LASTEXITCODE). Re-run this script to resume."
        }
    }
    $extracted = Join-Path $temporaryRoot $modelName
    # Remove only a prior interrupted extraction in this script-owned temporary
    # directory; the resumable archive itself is kept intact.
    if (Test-Path -LiteralPath $extracted)
    {
        Remove-Item -LiteralPath $extracted -Recurse -Force
    }
    # Keep the binary bzip2-to-tar stream inside Git Bash. PowerShell pipelines
    # are text pipelines and would corrupt an archive on Windows.
    $archiveGitPath = ConvertTo-GitPath $archive
    $temporaryGitPath = ConvertTo-GitPath $temporaryRoot
    & $bash -lc "bzip2 -dc -- '$archiveGitPath' | tar -xf - -C '$temporaryGitPath'"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to unpack the Sherpa model (tar exit code $LASTEXITCODE)."
    }
    $model = Join-Path $extracted 'model.onnx'
    $tokens = Join-Path $extracted 'tokens.txt'
    if (-not (Test-Path -LiteralPath $model) -or (Get-Item -LiteralPath $model).Length -le 100MB -or -not (Test-Path -LiteralPath $tokens))
    {
        throw 'The downloaded Sherpa model did not pass the local file check.'
    }
    Move-Item -LiteralPath $extracted -Destination $target
    $completed = $true
    Write-Host "[voxlocal] Sherpa Russian model installed: $target"
}
finally
{
    if ($completed -and (Test-Path -LiteralPath $temporaryRoot))
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
