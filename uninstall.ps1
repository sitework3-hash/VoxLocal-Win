[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$installDirectory = $PSScriptRoot
$installedApp = Join-Path $installDirectory 'VoxLocal.exe'

Get-Process -Name 'VoxLocal' -ErrorAction SilentlyContinue | ForEach-Object {
    try
    {
        if ($_.Path -and [IO.Path]::GetFullPath($_.Path).Equals(
            [IO.Path]::GetFullPath($installedApp), [StringComparison]::OrdinalIgnoreCase))
        {
            Stop-Process -Id $_.Id -Force
            Wait-Process -Id $_.Id -Timeout 10 -ErrorAction SilentlyContinue
        }
    }
    catch
    {
        Write-Warning "Не удалось остановить VoxLocal PID=$($_.Id): $($_.Exception.Message)"
    }
}

$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VoxLocal'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'VoxLocal.lnk'
Remove-Item -LiteralPath $startMenuDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue

if ($RemoveUserData)
{
    Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'VoxLocal') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '[voxlocal] Настройки, журнал, история и модели удалены.'
}
else
{
    Write-Host '[voxlocal] Пользовательские данные сохранены в %LOCALAPPDATA%\VoxLocal.'
}

# A running script cannot remove its own directory. Launch a short, hidden
# cleanup process after this PowerShell instance exits.
$escapedDirectory = $installDirectory.Replace("'", "''")
$cleanup = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$escapedDirectory' -Recurse -Force -ErrorAction SilentlyContinue"
Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -ArgumentList '-NoProfile', '-WindowStyle', 'Hidden', '-Command', $cleanup `
    -WindowStyle Hidden

Write-Host '[voxlocal] Программа удалена.' -ForegroundColor Green
