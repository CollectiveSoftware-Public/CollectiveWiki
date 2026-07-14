# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Publishes the CollectiveWiki desktop app as a self-contained, single-file executable.
.DESCRIPTION
  Produces dist/<rid>/CollectiveWiki.exe (no .NET runtime needed on the target).
  Default RID is win-x64; pass another (e.g. linux-x64, osx-arm64) to cross-publish.
.EXAMPLE
  ./build/publish-desktop.ps1
  ./build/publish-desktop.ps1 -Rid linux-x64
#>
param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Wiki.Desktop/Wiki.Desktop.csproj"
$outDir = Join-Path $root "dist/$Rid"

dotnet publish $project `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outDir

Get-ChildItem $outDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Published to $outDir"
Get-ChildItem $outDir -File | Sort-Object Length -Descending |
    Format-Table Name, @{ Name = 'Size(MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
