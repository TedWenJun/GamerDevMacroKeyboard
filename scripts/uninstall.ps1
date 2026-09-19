<#
.SYNOPSIS
    Remove MacroHub installed by install.ps1.

.DESCRIPTION
    Stops MacroHub, removes autostart (Run key and scheduled task), the Start Menu shortcut, the
    "Installed apps" entry and the program folder. Your configuration and logs are kept unless -RemoveUserData.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\MacroHub\uninstall.ps1"
    ... -RemoveUserData     # also delete %APPDATA%\MacroHub and %LOCALAPPDATA%\MacroHub
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\MacroHub'),
    [switch]$RemoveUserData
)
$ErrorActionPreference = 'Stop'
$installFull = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$targetExe = Join-Path $installFull 'MacroHub.exe'
Write-Host "Uninstalling MacroHub from $installFull"

Get-Process MacroHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $targetExe } | ForEach-Object {
    Write-Host "  stopping MacroHub (pid $($_.Id))"
    $_ | Stop-Process -Force
    $_.WaitForExit(5000) | Out-Null
}

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MacroHub' -ErrorAction SilentlyContinue
if (Get-ScheduledTask -TaskName 'MacroHub' -ErrorAction SilentlyContinue) {
    try { Unregister-ScheduledTask -TaskName 'MacroHub' -Confirm:$false; Write-Host '  removed scheduled task' }
    catch { Write-Warning 'Scheduled task "MacroHub" could not be removed (needs an elevated PowerShell).' }
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MacroHub.lnk'
Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MacroHub' -Recurse -Force -ErrorAction SilentlyContinue

if (Test-Path $installFull) {
    # this script may live inside the folder being removed; delete everything else first
    Get-ChildItem $installFull -Force | Where-Object { $_.FullName -ne $PSCommandPath } | Remove-Item -Recurse -Force
    if ($PSCommandPath -and $PSCommandPath.StartsWith($installFull, [StringComparison]::OrdinalIgnoreCase)) {
        Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$installFull`""
    } else {
        Remove-Item $installFull -Recurse -Force
    }
}

if ($RemoveUserData) {
    Remove-Item (Join-Path $env:APPDATA 'MacroHub') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:LOCALAPPDATA 'MacroHub') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '  removed configuration and logs'
} else {
    Write-Host "  kept configuration ($env:APPDATA\MacroHub) and logs ($env:LOCALAPPDATA\MacroHub\logs)"
}
Write-Host 'Done.'
