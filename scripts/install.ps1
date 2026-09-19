<#
.SYNOPSIS
    Install MacroHub for the current user (no administrator rights needed).

.DESCRIPTION
    Copies MacroHub to %LOCALAPPDATA%\Programs\MacroHub, adds a Start Menu shortcut and an entry in
    "Settings > Apps > Installed apps", optionally starts it at sign-in, and launches it.
    Configuration (%APPDATA%\MacroHub) and logs (%LOCALAPPDATA%\MacroHub\logs) are kept across reinstalls.

    Run it from the extracted release folder:
        powershell -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart

.PARAMETER Source
    Folder containing MacroHub.exe. Defaults to the folder of this script (release package layout).
.PARAMETER AutoStart
    Start MacroHub when you sign in (HKCU Run key).
.PARAMETER AutoStartElevated
    Start MacroHub elevated at sign-in via a scheduled task, so it can send keys into administrator windows.
    Creating the task requires an elevated PowerShell.
.PARAMETER NoShortcut
    Do not create the Start Menu shortcut.
.PARAMETER NoLaunch
    Do not start MacroHub after installing.
.PARAMETER NoAppsEntry
    Do not register MacroHub under "Installed apps" (portable / test installs).
#>
param(
    [string]$Source = $PSScriptRoot,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\MacroHub'),
    [switch]$AutoStart,
    [switch]$AutoStartElevated,
    [switch]$NoShortcut,
    [switch]$NoLaunch,
    [switch]$NoAppsEntry
)
$ErrorActionPreference = 'Stop'
$exeName = 'MacroHub.exe'
$srcExe = Join-Path $Source $exeName
if (-not (Test-Path $srcExe)) { throw "$exeName not found in '$Source'. Run this script from the extracted release folder or pass -Source." }
$version = (Get-Item $srcExe).VersionInfo.ProductVersion
if ($version) { $version = ($version -split '\+')[0] } else { $version = '0.0.0' }

$sourceFull = (Resolve-Path $Source).Path.TrimEnd('\')
$installFull = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$targetExe = Join-Path $installFull $exeName

Write-Host "Installing MacroHub $version to $installFull"

# 1. stop a running copy of this installation
Get-Process MacroHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $targetExe } | ForEach-Object {
    Write-Host "  stopping running MacroHub (pid $($_.Id))"
    $_ | Stop-Process -Force
    $_.WaitForExit(5000) | Out-Null
}

# 2. copy files (replace program files only; user data lives elsewhere)
if ($sourceFull -ine $installFull) {
    if (Test-Path $installFull) { Remove-Item $installFull -Recurse -Force }
    New-Item -ItemType Directory -Force $installFull | Out-Null
    Copy-Item (Join-Path $sourceFull '*') $installFull -Recurse -Force
}
if (-not (Test-Path (Join-Path $installFull 'uninstall.ps1'))) {
    Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $installFull -ErrorAction SilentlyContinue
}

# 3. Start Menu shortcut (opens the web UI; starts the Hub if it is not running)
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MacroHub.lnk'
if (-not $NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($shortcut)
    $lnk.TargetPath = $targetExe
    $lnk.Arguments = '--open'
    $lnk.WorkingDirectory = $installFull
    $lnk.Description = 'MacroHub macro keyboard system layer'
    $lnk.Save()
    Write-Host "  shortcut: $shortcut"
}

# 4. Autostart
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($AutoStart -and -not $AutoStartElevated) {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'MacroHub' -Value "`"$targetExe`""
    Write-Host '  autostart: at sign-in (HKCU Run)'
}
if ($AutoStartElevated) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw '-AutoStartElevated needs an elevated PowerShell (Run as administrator).' }
    Remove-ItemProperty -Path $runKey -Name 'MacroHub' -ErrorAction SilentlyContinue
    $action = New-ScheduledTaskAction -Execute $targetExe -WorkingDirectory $installFull
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName 'MacroHub' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host '  autostart: at sign-in, elevated (scheduled task "MacroHub")'
}

# 5. "Installed apps" entry
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MacroHub'
if (-not $NoAppsEntry) {
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $installFull 'uninstall.ps1')`""
@{
    DisplayName     = 'MacroHub'
    DisplayVersion  = $version
    Publisher       = 'MacroHub contributors'
    InstallLocation = $installFull
    DisplayIcon     = $targetExe
    UninstallString = $uninstallCmd
    NoModify        = 1
    NoRepair        = 1
}.GetEnumerator() | ForEach-Object {
    $type = if ($_.Value -is [int]) { 'DWord' } else { 'String' }
    New-ItemProperty -Path $uninstallKey -Name $_.Key -Value $_.Value -PropertyType $type -Force | Out-Null
}
}

# 6. Launch
if (-not $NoLaunch) {
    Start-Process -FilePath $targetExe -ArgumentList '--open' -WorkingDirectory $installFull
    Write-Host '  started: http://127.0.0.1:17900/'
}
Write-Host 'Done.'
