# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Fails unless $Tag exactly equals "v" + Directory.Build.props <Version>.
.DESCRIPTION
  The tag asserts the version; it does not set it. A mistagged release must not build.
  Prints the bare version as the final stdout line so CI can capture it.
.EXAMPLE
  ./build/assert-version.ps1 -Tag v1.0.0
#>
#requires -Version 7
[CmdletBinding()]
param(
    # AllowEmptyString: [Parameter(Mandatory)] alone rejects an empty string at the
    # binder, before this script's body ever runs — so -Tag '' would fail with a
    # binder stack trace, not this script's own "does not match" message. Let an
    # empty tag reach the comparison logic below so it's rejected on its merits.
    [Parameter(Mandatory)][AllowEmptyString()][string]$Tag,
    [string]$PropsPath = "$PSScriptRoot/../Directory.Build.props"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PropsPath)) {
    Write-Host "ERROR: props file not found: $PropsPath"; exit 1
}

try { $xml = [xml](Get-Content -LiteralPath $PropsPath -Raw) }
catch { Write-Host "ERROR: could not parse $PropsPath : $_"; exit 1 }

# Set-StrictMode also makes plain dot-access ($xml.Project.PropertyGroup) THROW when
# <Project> has zero <PropertyGroup> children (import-only / <ItemGroup>-only props
# files are real). Same PSObject.Properties workaround as $_.Version below: a missing
# element becomes $null instead of a crash, and falls through to the same
# "no <Version> element" message.
$propertyGroupProp = $xml.Project.PSObject.Properties['PropertyGroup']
# @() (not $null) when absent: wrapping $null in @($propertyGroups) below would
# produce a one-element array containing $null, and under Set-StrictMode even
# $_.PSObject.Properties on a $null $_ throws — same crash, one layer up.
$propertyGroups = if ($propertyGroupProp) { $propertyGroupProp.Value } else { @() }

$version = @($propertyGroups) |
    ForEach-Object {
        # Set-StrictMode makes plain dot-access ($_.Version) THROW when a
        # <PropertyGroup> has no <Version> child — which every real
        # Directory.Build.props with multiple PropertyGroups can hit. Go
        # through PSObject.Properties so a missing element is $null, not a
        # crash, and falls through to the "no <Version> element" message below.
        $prop = $_.PSObject.Properties['Version']
        if ($prop) { $prop.Value }
    } |
    Where-Object { $_ } |
    Select-Object -First 1

if (-not $version) { Write-Host "ERROR: no <Version> element in $PropsPath"; exit 1 }

$version = "$version".Trim()
$expected = "v$version"
if ($Tag -cne $expected) {
    Write-Host "ERROR: tag '$Tag' does not match <Version> '$version' in $PropsPath (expected tag '$expected')."
    Write-Host "       Fix the tag, or bump <Version> — do not work around this guard."
    exit 1
}

Write-Host "OK: tag '$Tag' matches <Version> '$version'"
Write-Output $version
exit 0
