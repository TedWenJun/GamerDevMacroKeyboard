<#
.SYNOPSIS
    Build MacroHub from source and start it (development).

.EXAMPLE
    .\scripts\run.ps1                    # Release build, config %APPDATA%\MacroHub\hub.json, opens the web UI
    .\scripts\run.ps1 -Elevated          # run as administrator (send keys into elevated windows)
    .\scripts\run.ps1 -Config .\my.json  # another config file
    .\scripts\run.ps1 -Console           # run in this console and show the log (Ctrl+C to stop)
#>
param(
    [switch]$Elevated,
    [string]$Config = '',
    [int]$Port = 17900,
    [switch]$NoBuild,
    [switch]$Console
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\MacroHub\MacroHub.csproj'
$exe = Join-Path $root 'src\MacroHub\bin\Release\net10.0-windows\MacroHub.exe'

if (-not $NoBuild) {
    Get-Process MacroHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
    dotnet build $proj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
}

$argList = @('--port', $Port, '--open')
if ($Config) { $argList += @('--config', (Resolve-Path $Config).Path) }

if ($Console) {
    # MacroHub is a GUI-subsystem app; piping keeps its log visible in this console.
    & $exe @argList | Out-Host
} elseif ($Elevated) {
    Start-Process -FilePath $exe -ArgumentList $argList -Verb RunAs -WorkingDirectory (Split-Path $exe)
} else {
    Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory (Split-Path $exe)
}
