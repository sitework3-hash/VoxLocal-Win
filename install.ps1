[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\VoxLocal'),
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch,
    [switch]$SkipModel
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$Message)
{
    Write-Host "`n[voxlocal] $Message" -ForegroundColor Cyan
}

function New-VoxLocalShortcut([string]$Path, [string]$Target)
{
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = Split-Path -Parent $Target
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = 'VoxLocal — локальный голосовой ввод'
    $shortcut.Save()
}

if (-not [Environment]::Is64BitOperatingSystem)
{
    throw 'VoxLocal поддерживает только 64-разрядную Windows 10/11.'
}
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)
{
    throw 'Этот установщик предназначен для Windows.'
}

$repoRoot = $PSScriptRoot
$bootstrapScript = Join-Path $repoRoot 'scripts\windows\bootstrap.ps1'
$buildScript = Join-Path $repoRoot 'scripts\windows\build.ps1'
$modelScript = Join-Path $repoRoot 'scripts\windows\download_sherpa_model.ps1'
foreach ($requiredFile in @($bootstrapScript, $buildScript, $modelScript))
{
    if (-not (Test-Path -LiteralPath $requiredFile))
    {
        throw "Не найден файл $requiredFile. Запустите install.ps1 из корня клонированного репозитория VoxLocal."
    }
}

Write-Step 'Проверка и загрузка инструментов сборки'
& $bootstrapScript
if ($LASTEXITCODE -ne 0) { throw "bootstrap.ps1 завершился с кодом $LASTEXITCODE." }

Write-Step 'Сборка приложения'
& $buildScript
if ($LASTEXITCODE -ne 0) { throw "build.ps1 завершился с кодом $LASTEXITCODE." }

if (-not $SkipModel)
{
    Write-Step 'Установка локальной русской модели распознавания (~140 МБ)'
    & $modelScript
    if ($LASTEXITCODE -ne 0) { throw "download_sherpa_model.ps1 завершился с кодом $LASTEXITCODE." }
}
else
{
    Write-Warning 'Установка модели пропущена. До ручной установки модели распознавание речи работать не будет.'
}

$buildDirectory = Join-Path $repoRoot 'dist\windows\VoxLocal'
$appSource = Join-Path $buildDirectory 'VoxLocal.exe'
if (-not (Test-Path -LiteralPath $appSource))
{
    throw "Собранное приложение не найдено: $appSource"
}

if ([string]::IsNullOrWhiteSpace($InstallDirectory))
{
    throw 'Каталог установки не может быть пустым.'
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$installedApp = Join-Path $InstallDirectory 'VoxLocal.exe'

# Stop only the copy that is being updated. Other development builds are left alone.
Get-Process -Name 'VoxLocal' -ErrorAction SilentlyContinue | ForEach-Object {
    try
    {
        if ($_.Path -and [IO.Path]::GetFullPath($_.Path).Equals(
            $installedApp, [StringComparison]::OrdinalIgnoreCase))
        {
            Write-Step 'Остановка установленной версии перед обновлением'
            Stop-Process -Id $_.Id -Force
            Wait-Process -Id $_.Id -Timeout 10 -ErrorAction SilentlyContinue
        }
    }
    catch
    {
        Write-Warning "Не удалось проверить процесс VoxLocal PID=$($_.Id): $($_.Exception.Message)"
    }
}

Write-Step "Установка в $InstallDirectory"
$stagingDirectory = "$InstallDirectory.installing"
if (Test-Path -LiteralPath $stagingDirectory)
{
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $buildDirectory '*') -Destination $stagingDirectory -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory 'VoxLocal.exe')))
{
    throw 'Не удалось скопировать VoxLocal.exe во временный каталог установки.'
}
if (Test-Path -LiteralPath $InstallDirectory)
{
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
}
Move-Item -LiteralPath $stagingDirectory -Destination $InstallDirectory

$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VoxLocal'
New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
New-VoxLocalShortcut -Path (Join-Path $startMenuDirectory 'VoxLocal.lnk') -Target $installedApp

$uninstallSource = Join-Path $repoRoot 'uninstall.ps1'
if (Test-Path -LiteralPath $uninstallSource)
{
    Copy-Item -LiteralPath $uninstallSource -Destination (Join-Path $InstallDirectory 'uninstall.ps1') -Force
    $uninstallShortcut = Join-Path $startMenuDirectory 'Удалить VoxLocal.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($uninstallShortcut)
    $shortcut.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDirectory 'uninstall.ps1')`""
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.Description = 'Удалить VoxLocal'
    $shortcut.Save()
}

if (-not $NoDesktopShortcut)
{
    $desktop = [Environment]::GetFolderPath('Desktop')
    New-VoxLocalShortcut -Path (Join-Path $desktop 'VoxLocal.lnk') -Target $installedApp
}

Write-Host "`n[voxlocal] Установка завершена: $installedApp" -ForegroundColor Green
Write-Host '[voxlocal] Настройки и модели находятся в %LOCALAPPDATA%\VoxLocal.'
Write-Host '[voxlocal] Для диктовки удерживайте Alt + Space, говорите и отпустите клавиши.'

if (-not $NoLaunch)
{
    Start-Process -FilePath $installedApp -WorkingDirectory $InstallDirectory
    Write-Host '[voxlocal] VoxLocal запущен. Значок программы находится в области уведомлений.'
}
