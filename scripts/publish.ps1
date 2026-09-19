<#
.SYNOPSIS
    Build a release package: artifacts\MacroHub-<version>-<rid>.zip (+ .sha256).

.DESCRIPTION
    Publishes MacroHub as a self-contained single-file app (no .NET runtime needed on the target PC), adds the
    install/uninstall scripts, README and LICENSE, zips it and writes a SHA-256 checksum. Used by the release
    workflow (.github/workflows/release.yml) and locally.

.EXAMPLE
    .\scripts\publish.ps1                          # version from Directory.Build.props, win-x64
    .\scripts\publish.ps1 -Version 0.2.0 -Runtime win-arm64
    .\scripts\publish.ps1 -SkipTests
#>
param(
    [string]$Version = '',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = '',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.VersionPrefix | Select-Object -First 1
}
$Version = $Version.TrimStart('v')
$name = "MacroHub-$Version-$Runtime"
$stage = Join-Path $OutputDir "stage\$name"
$zip = Join-Path $OutputDir "$name.zip"

Write-Host "==> MacroHub $Version ($Runtime)"
if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tests\MacroHub.Core.Tests\MacroHub.Core.Tests.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'unit tests failed' }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish (Join-Path $root 'src\MacroHub\MacroHub.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $stage --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Copy-Item (Join-Path $root 'scripts\install.ps1'), (Join-Path $root 'scripts\uninstall.ps1') $stage
Copy-Item (Join-Path $root 'README.md') $stage
foreach ($f in 'README.zh-CN.md', 'LICENSE', 'CHANGELOG.md', 'CHANGELOG.zh-CN.md') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $stage } }

New-Item -ItemType Directory -Force $OutputDir | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii -NoNewline

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "==> $zip ($sizeMb MB)"
Write-Host "    sha256 $hash"
