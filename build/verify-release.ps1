# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Verifies a CollectiveWiki release directory: manifest signature, then artifact hashes.
.DESCRIPTION
  Anyone can run this against a downloaded release to confirm the binaries are genuine.
  A release is trustworthy only if the signature verifies against a trusted public key AND
  every artifact present hashes to what the signed manifest says. Both checks are required,
  and the order matters: an unsigned/forged manifest's hashes are meaningless, so artifact
  hashes are never compared until the manifest itself has been authenticated against a
  trusted key.
.EXAMPLE
  gh release download v1.0.0 -R CollectiveSoftware-Public/CollectiveWiki --dir rel
  ./build/verify-release.ps1 -Dir rel -PublicKeyBase64 <key>
#>
#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Dir,
    [Parameter(Mandatory)][string[]]$PublicKeyBase64
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/lib/Signing.ps1"
. "$PSScriptRoot/lib/ReleaseGuard.ps1"

$manifestPath = Join-Path $Dir 'manifest.json'
$sigPath      = Join-Path $Dir 'manifest.json.sig'
foreach ($p in @($manifestPath, $sigPath)) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Host "FAIL: missing $p"
        Write-Host '      Cannot verify this release. Do not run these binaries.'
        exit 1
    }
}

# Read the manifest's exact bytes — the signature covers these bytes precisely. Never
# re-serialize or round-trip through ConvertFrom-Json/ConvertTo-Json before verifying, or the
# signature check breaks (whitespace/property-order/number-formatting can all change).
$bytes = [System.IO.File]::ReadAllBytes($manifestPath)
$sig   = (Get-Content -LiteralPath $sigPath -Raw).Trim()

# Check the signature FIRST. An unsigned or forged manifest's hashes are meaningless — never
# compare artifact hashes against a manifest that hasn't been authenticated against a trusted key.
if (-not (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 $PublicKeyBase64)) {
    Write-Host 'FAIL: manifest signature does NOT verify against any trusted key.'
    Write-Host '      This release cannot be authenticated. Do not run these binaries.'
    exit 1
}
Write-Host 'OK: manifest signature verifies'

# Only now — after the signature is authenticated — is it safe to trust anything the manifest
# says. Parsing it here (for display + the artifact list) does not touch the signature check
# above, which already ran against the untouched raw bytes.
$rawJson  = [System.Text.Encoding]::UTF8.GetString($bytes)
$manifest = $rawJson | ConvertFrom-Json
# Display only — never re-derive what gets verified from this. ConvertFrom-Json auto-coerces the
# signed ISO-8601 'Z' string to [datetime], whose default ToString() renders culture-formatted and
# timezone-less (e.g. "7/20/2026 2:00:00 PM") — not what was signed. So read the raw signed text.
# No fallback: the only shape that defeats this regex is a missing/malformed "published", and
# under StrictMode any fallback touching $manifest.published would itself throw. Display is not
# security-relevant; degrade to (unknown) rather than crash a verification the user needs.
$publishedDisplay = if ($rawJson -match '"published"\s*:\s*"([^"]*)"') { $Matches[1] } else { '(unknown)' }
Write-Host "     version: $($manifest.version)   published: $publishedDisplay"

# Every file present must be accounted for by the signed manifest. Checking only that each
# DECLARED artifact is present and correct (the loop below) never notices a file that is
# present but NOT declared — so a rogue asset attached to the release rides along beside a
# valid signature and this tool would call the directory safe. The README's own instructions
# (`gh release download --dir cw`, no --pattern) fetch every asset, creating exactly that
# directory. Rogue => fail; Missing => benign (a user downloads only their own platform's
# binary), and the $checked -eq 0 guard below already covers "nothing was verified".
$mismatch = Get-ArtifactSetMismatch -ActualNames @(Get-ChildItem -LiteralPath $Dir -File | ForEach-Object { $_.Name }) `
                                    -ManifestArtifacts @($manifest.artifacts)
if ($mismatch.Rogue.Count -gt 0) {
    Write-Host "FAIL: file(s) present that are not declared by the signed manifest: $($mismatch.Rogue -join ', ')"
    Write-Host '      The signature vouches only for the files the manifest lists. Anything else'
    Write-Host '      was NOT signed by the project. Do not run these binaries.'
    exit 1
}

$checked = 0
foreach ($a in $manifest.artifacts) {
    $name = ($a.url -split '/')[-1]
    $path = Join-Path $Dir $name
    if (-not (Test-Path -LiteralPath $path)) { Write-Host "SKIP: $name not present locally"; continue }
    $actual = Get-FileSha256 -Path $path
    if ($actual -ne $a.sha256) {
        Write-Host "FAIL: $name hash mismatch"
        Write-Host "      expected $($a.sha256)"
        Write-Host "      actual   $actual"
        Write-Host '      This file does not match the signed release. Do not run it.'
        exit 1
    }
    Write-Host "OK: $name matches its signed hash"
    $checked++
}

if ($checked -eq 0) {
    Write-Host 'FAIL: no artifacts were present to verify.'
    Write-Host '      Nothing was checked, so this is NOT a pass. Do not run any binaries'
    Write-Host '      claiming to be part of this release until they are re-verified.'
    exit 1
}
Write-Host "`nRelease verified ($checked artifact(s)). Signature and hashes match — safe to run."
exit 0
