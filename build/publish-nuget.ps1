#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pushes the SS.PaddleOCR packages in nupkgs/ to nuget.org.

.DESCRIPTION
    Version-agnostic: whatever Release builds sit in nupkgs/ get pushed, in dependency
    order, side by side with their .snupkg. Re-runs are harmless (--skip-duplicate).

    Requires an API key with push rights on the SS.PaddleOCR.* package ids
    (nuget.org -> Account -> API keys, scope "Push new packages and package versions").
    The key is only read from your environment / parameter; it is never stored here:

        $env:NUGET_API_KEY = 'oy2...'
        powershell -ExecutionPolicy Bypass -File build/publish-nuget.ps1

    Pack first:  powershell -ExecutionPolicy Bypass -File build/pack-nuget.ps1
    Works in Windows PowerShell 5.1 and PowerShell 7+.

.EXAMPLE
    # dry run: show what would be pushed, push nothing
    powershell -ExecutionPolicy Bypass -File build/publish-nuget.ps1 -WhatIf

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish-nuget.ps1 -Source https://api.nuget.org/v3/index.json
#>
param(
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Source = 'https://api.nuget.org/v3/index.json',
    [string]$PackageDir = (Join-Path $PSScriptRoot '..\nupkgs'),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKey) -and -not $WhatIf) {
    throw 'No API key. Set $env:NUGET_API_KEY (or pass -ApiKey) before publishing SS.PaddleOCR.* packages.'
}

if (-not (Test-Path -LiteralPath $PackageDir)) { throw "Package directory not found: $PackageDir" }
$dir = (Resolve-Path -LiteralPath $PackageDir).ProviderPath

# Dependency order (leaf packages first). An id that is not listed still gets pushed,
# last and sorted by name, so a newly added package is never silently ignored.
$order = @(
    'SS.PaddleOCR.ModelProvider'
    'SS.PaddleOCR.Models.TextLineOrientation'
    'SS.PaddleOCR.Models.ChineseV6Tiny'
    'SS.PaddleOCR.Models.ChineseV6Small'
    'SS.PaddleOCR.Models.ChineseV6Medium'
    'SS.PaddleOCR.Models.KoreanV5Mobile'
    'SS.PaddleOCR'
)

$nupkgs = @(Get-ChildItem -LiteralPath $dir -Filter *.nupkg -File)
if ($nupkgs.Count -eq 0) { throw "No .nupkg in $dir - run build/pack-nuget.ps1 first." }

# "SS.PaddleOCR.Models.ChineseV6Small.1.0.0.nupkg" -> id = longest known prefix, version = the rest.
function Resolve-PackageName([System.IO.FileInfo]$file) {
    $id = $null
    foreach ($candidate in $order) {
        $prefix = $candidate + '.'
        if ($file.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $id = $candidate
            break
        }
    }

    if ($null -ne $id) {
        $version = $file.Name.Substring($id.Length + 1) -replace '\.nupkg$', ''
        $rank = [array]::IndexOf($order, $id)
    }
    else {
        # Unknown id: strip the trailing "<version>.nupkg" by taking everything before the second-to-last dot group.
        $version = $file.Name -replace '\.nupkg$', ''
        $rank = $order.Count
    }

    return [pscustomobject]@{
        Rank    = $rank
        Id      = $id
        Version = $version
        File    = $file
    }
}

$plan = @($nupkgs | ForEach-Object { Resolve-PackageName $_ } | Sort-Object Rank, @{ Expression = { $_.File.Name } })

Write-Host ''
Write-Host "source : $Source"
Write-Host "folder : $dir"
Write-Host ("pushing {0} package(s):" -f $plan.Count) -ForegroundColor Cyan
foreach ($item in $plan) {
    $kind = if ($item.Id) { 'known' } else { 'unlisted id' }
    Write-Host ("  {0,-52} {1,9:N1} KB  {2}" -f $item.File.Name, ($item.File.Length / 1KB), $kind)
}

if ($WhatIf) {
    Write-Host ''
    Write-Host 'WhatIf: nothing was pushed.' -ForegroundColor Yellow
    return
}

Write-Host ''
foreach ($item in $plan) {
    Write-Host "pushing $($item.File.Name)" -ForegroundColor Cyan
    dotnet nuget push $item.File.FullName --api-key $ApiKey --source $Source --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "push failed: $($item.File.Name) (exit $LASTEXITCODE)" }
}

Write-Host ''
Write-Host 'done. verify with:' -ForegroundColor Green
foreach ($item in $plan) {
    if ($item.Id) { Write-Host ("  https://www.nuget.org/packages/" + $item.Id) }
}
Write-Host '  (indexing can take a few minutes; the search API lags behind the package page)'