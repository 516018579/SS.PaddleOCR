#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds (packs) every SS.PaddleOCR NuGet package into nupkgs/.

.DESCRIPTION
    PowerShell equivalent of build/01-build-packages.linq — no LINQPad needed.
    Packs the 7 packages in dependency order with per-package versions passed as
    -p:<Name>Version=<ver>, so a package never picks up a stale ProjectReference
    version (never pass a global -p:Version=; NuGet would copy it onto every
    ProjectReference and the dependency ranges would go wrong).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/pack-nuget.ps1

.EXAMPLE
    # bump everything to 1.0.1 at once
    powershell -ExecutionPolicy Bypass -File build/pack-nuget.ps1 -Version 1.0.1

.EXAMPLE
    # only one package (dependencies must already be in nupkgs/)
    powershell -ExecutionPolicy Bypass -File build/pack-nuget.ps1 -Only SS.PaddleOCR.Models.KoreanV5Mobile

.NOTES
    Keep the version table below in sync with Directory.Build.props defaults.
#>
param(
    [string]$Configuration = 'Release',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\nupkgs'),
    [string]$Version,
    [string[]]$Only,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Project name -> default version. Dependencies first so the console order is readable;
# nuget.org itself does not care about push order.
$packages = @(
    @{ Name = 'SS.PaddleOCR.ModelProvider';                  Version = '1.0.1' }
    @{ Name = 'SS.PaddleOCR.Models.TextLineOrientation';    Version = '1.0.1' }
    @{ Name = 'SS.PaddleOCR.Models.ChineseV6Tiny';          Version = '1.0.1' }
    @{ Name = 'SS.PaddleOCR.Models.ChineseV6Small';         Version = '1.0.1' }
    @{ Name = 'SS.PaddleOCR.Models.ChineseV6Medium';        Version = '1.0.1' }
    @{ Name = 'SS.PaddleOCR.Models.KoreanV5Mobile';         Version = '1.0.2' }
    @{ Name = 'SS.PaddleOCR';                               Version = '1.0.2' }
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    foreach ($p in $packages) { $p.Version = $Version }
}

if ($Only) {
    $selected = $packages | Where-Object { $Only -contains $_.Name }
    if (-not $selected) { throw "None of -Only ($($Only -join ', ')) is a known package name." }
    $packages = $selected
}

if ($Clean -and (Test-Path -LiteralPath $OutDir)) {
    Write-Host "cleaning $OutDir" -ForegroundColor Yellow
    Get-ChildItem -LiteralPath $OutDir -Filter *.nupkg | Remove-Item -Force
    Get-ChildItem -LiteralPath $OutDir -Filter *.snupkg | Remove-Item -Force
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Find-Project([string]$name) {
    # NB: do not name this $matches — $Matches is the automatic -match result variable.
    $found = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter "$name.csproj" -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|packages|external)[\\/]' })
    if ($found.Count -eq 0) { return $null }
    if ($found.Count -gt 1) {
        throw "Multiple $name.csproj found:`n" + (($found | ForEach-Object FullName) -join "`n")
    }
    return $found[0].FullName
}

# One -p:<Name>Version=<ver> per package: Directory.Build.props maps these onto the
# packable projects, and FixPackProjectReferenceVersions rewrites the ProjectReference ranges.
# @() keeps a single-entry table (e.g. -Only) an array: Windows PowerShell splats a bare
# string into one argument per character the moment the list has one element.
$versionArgs = @($packages | ForEach-Object { "-p:$($_.Name.Replace('.', '') + 'Version')=$($_.Version)" })

$results = @()
foreach ($pkg in $packages) {
    $proj = Find-Project $pkg.Name
    if (-not $proj) {
        Write-Warning "skip (project not found): $($pkg.Name).csproj"
        continue
    }

    Write-Host ''
    Write-Host "packing $($pkg.Name) $($pkg.Version)" -ForegroundColor Cyan
    Write-Host "  $proj"

    # -p:Version is deliberately NOT passed on the command line (see the note above).
    dotnet pack $proj -c $Configuration -o $OutDir --nologo @versionArgs
    if ($LASTEXITCODE -ne 0) { throw "pack failed: $($pkg.Name) (exit $LASTEXITCODE)" }

    $nupkg = Join-Path $OutDir "$($pkg.Name).$($pkg.Version).nupkg"
    if (-not (Test-Path -LiteralPath $nupkg)) {
        throw "pack reported success but $nupkg is missing."
    }
    $results += [pscustomobject]@{
        Package = $pkg.Name
        Version = $pkg.Version
        SizeMB  = [math]::Round((Get-Item -LiteralPath $nupkg).Length / 1MB, 2)
        File     = Split-Path $nupkg -Leaf
    }
}

Write-Host ''
$results | Format-Table -AutoSize
Write-Host ("{0} package(s) in {1}" -f $results.Count, (Resolve-Path -LiteralPath $OutDir).ProviderPath) -ForegroundColor Green
Write-Host ''
Write-Host 'next: powershell -ExecutionPolicy Bypass -File build/publish-nuget.ps1' -ForegroundColor Green