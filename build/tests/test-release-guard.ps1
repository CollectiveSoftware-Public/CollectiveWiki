# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/../lib/ReleaseGuard.ps1"
. "$PSScriptRoot/../lib/Signing.ps1"

$script:Failures = 0
function Assert-Equal($Expected, $Actual, $What) {
    if ($Expected -eq $Actual) { Write-Host "  PASS  $What" }
    else { Write-Host "  FAIL  $What`n        expected: $Expected`n        actual:   $Actual"; $script:Failures++ }
}
function Assert-NoThrow([scriptblock]$Action, $What) {
    try { & $Action; Write-Host "  PASS  $What" }
    catch { Write-Host "  FAIL  $What`n        unexpected throw: $($_.Exception.Message)"; $script:Failures++ }
}
# Exit code / throw-or-not alone can't tell "rejected for the right reason, naming the right
# file" apart from any other unrelated throw (e.g. a Set-StrictMode violation) — this project
# has shipped five defects of exactly that class. Assert on the exception's actual message text.
function Assert-ThrowsContaining([scriptblock]$Action, [string[]]$MustContain, $What) {
    try {
        & $Action
        Write-Host "  FAIL  $What`n        expected: throw containing $($MustContain -join ', ')`n        actual:   no exception"
        $script:Failures++
    } catch {
        $msg = $_.Exception.Message
        $missingNeedles = @($MustContain | Where-Object { $msg -notlike "*$_*" })
        if ($missingNeedles.Count -eq 0) { Write-Host "  PASS  $What" }
        else {
            Write-Host "  FAIL  $What`n        message: $msg`n        did not contain: $($missingNeedles -join ', ')"
            $script:Failures++
        }
    }
}

# Shared fixture manifest — two declared artifacts under a fake release URL.
$manifestArtifacts = @(
    @{ url = 'https://example.invalid/releases/download/v1.0.0/CollectiveWiki-1.0.0-win-x64.exe' },
    @{ url = 'https://example.invalid/releases/download/v1.0.0/CollectiveWiki-1.0.0-linux-x64' }
)

Write-Host 'Test-ArtifactSetMatchesManifest (the LOCAL download directory)'
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ("rg-local-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    [System.IO.File]::WriteAllText((Join-Path $dir 'CollectiveWiki-1.0.0-win-x64.exe'), 'a')
    [System.IO.File]::WriteAllText((Join-Path $dir 'CollectiveWiki-1.0.0-linux-x64'), 'b')
    [System.IO.File]::WriteAllText((Join-Path $dir 'manifest.json'), '{}')
    [System.IO.File]::WriteAllText((Join-Path $dir 'manifest.json.sig'), 'sig')

    Assert-NoThrow ({ Test-ArtifactSetMatchesManifest -Dir $dir -ManifestArtifacts $manifestArtifacts -Pattern 'CollectiveWiki-*' }) `
        'ACCEPTS an exact match (manifest.json / manifest.json.sig correctly excluded, not counted as extras)'

    [System.IO.File]::WriteAllText((Join-Path $dir 'CollectiveWiki-rogue.exe'), 'evil')
    Assert-ThrowsContaining ({ Test-ArtifactSetMatchesManifest -Dir $dir -ManifestArtifacts $manifestArtifacts -Pattern 'CollectiveWiki-*' }) `
        @('CollectiveWiki-rogue.exe') 'REJECTS an extra unlisted local file, naming it'
    Remove-Item (Join-Path $dir 'CollectiveWiki-rogue.exe') -Force

    Remove-Item (Join-Path $dir 'CollectiveWiki-1.0.0-linux-x64') -Force
    Assert-ThrowsContaining ({ Test-ArtifactSetMatchesManifest -Dir $dir -ManifestArtifacts $manifestArtifacts -Pattern 'CollectiveWiki-*' }) `
        @('CollectiveWiki-1.0.0-linux-x64') 'REJECTS a declared-but-missing local artifact, naming it'
} finally {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Test-RemoteAssetSetMatchesManifest (the REMOTE gh asset list — Finding 1, the gap the previous fix missed)'

$cleanRemote = @('CollectiveWiki-1.0.0-win-x64.exe', 'CollectiveWiki-1.0.0-linux-x64', 'manifest.json', 'manifest.json.sig')
Assert-NoThrow ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $cleanRemote -ManifestArtifacts $manifestArtifacts }) `
    'ACCEPTS an exact match against the remote asset list'

# This is the exact attack the previous fix (Test-ArtifactSetMatchesManifest alone) could never
# catch: an asset whose name matches NO naming convention at all — not even 'CollectiveWiki-*' —
# so `gh release download --pattern` never fetches it and it never lands in $work. Handing the
# REMOTE list straight to this function is what makes it visible.
$roguePlusAnythingBin = $cleanRemote + @('anything.bin')
Assert-ThrowsContaining ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $roguePlusAnythingBin -ManifestArtifacts $manifestArtifacts }) `
    @('anything.bin') 'REJECTS a rogue asset matching NO download pattern at all, naming it'

$missingRemote = @('CollectiveWiki-1.0.0-win-x64.exe', 'manifest.json', 'manifest.json.sig')
Assert-ThrowsContaining ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $missingRemote -ManifestArtifacts $manifestArtifacts }) `
    @('CollectiveWiki-1.0.0-linux-x64') 'REJECTS a declared-but-missing remote asset, naming it'

Write-Host ''
Write-Host 'manifest.json / manifest.json.sig exclusion is exact-literal, not a hole'
# Prove the sidecar-metadata exclusion is an EXACT name match, not a prefix/wildcard — otherwise
# an attacker could hide a rogue asset under a "looks like metadata" name.
$almostMetadata = $cleanRemote + @('manifest.json.bak')
Assert-ThrowsContaining ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $almostMetadata -ManifestArtifacts $manifestArtifacts }) `
    @('manifest.json.bak') 'a near-miss name (manifest.json.bak) is NOT swallowed by the metadata exclusion — still flagged'
# And the two real sidecar names, present with correct exact casing, verify clean end-to-end
# alongside the declared artifacts (this is exactly $cleanRemote — re-asserted here so the
# "no hole" claim is anchored to an explicit, readable list rather than implied by the earlier
# pass).
$sidecarsPlusDeclared = @('manifest.json', 'manifest.json.sig', 'CollectiveWiki-1.0.0-win-x64.exe', 'CollectiveWiki-1.0.0-linux-x64')
Assert-NoThrow ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $sidecarsPlusDeclared -ManifestArtifacts $manifestArtifacts }) `
    'manifest.json and manifest.json.sig together are excluded without leaving a hole for anything else'

Write-Host ''
Write-Host 'manifest.json / manifest.json.sig exclusion is case-sensitive (ordinal), not a hole'
# PowerShell's -notcontains is case-INSENSITIVE by default, so `$ignoreNames -notcontains
# "MANIFEST.JSON"` evaluates to $false (i.e. treated as excluded/ignored) unless the exclusion
# is done with an ordinal/case-sensitive comparer. Prove a case-variant name is genuinely
# rejected as a rogue asset, not silently classified as expected metadata.
$upperCaseManifest = $cleanRemote + @('MANIFEST.JSON')
Assert-ThrowsContaining ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $upperCaseManifest -ManifestArtifacts $manifestArtifacts }) `
    @('MANIFEST.JSON') 'an upper-case metadata name (MANIFEST.JSON) is NOT swallowed by the exclusion — still flagged as rogue'

$mixedCaseSig = $cleanRemote + @('Manifest.Json.Sig')
Assert-ThrowsContaining ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $mixedCaseSig -ManifestArtifacts $manifestArtifacts }) `
    @('Manifest.Json.Sig') 'a mixed-case metadata name (Manifest.Json.Sig) is NOT swallowed by the exclusion — still flagged as rogue'

# The legitimate path must not be collateral damage: the correctly-cased lower-case sidecars
# alone (no case-variant rogue present) still verify clean end-to-end.
Assert-NoThrow ({ Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $cleanRemote -ManifestArtifacts $manifestArtifacts }) `
    'lowercase manifest.json / manifest.json.sig are still correctly excluded (the legitimate path is not broken)'

Write-Host ''
Write-Host 'Get-EffectivePublicKeyBase64'
$kp = New-SigningKeyPair
$pubPath = Join-Path ([System.IO.Path]::GetTempPath()) ("rg-pub-" + [Guid]::NewGuid() + '.pub')
[System.IO.File]::WriteAllText($pubPath, $kp.PublicKeyBase64, [System.Text.UTF8Encoding]::new($false))
try {
    Assert-Equal $kp.PublicKeyBase64 (Get-EffectivePublicKeyBase64 -PrivateKeyBase64 $kp.PrivateKeyBase64 -PublicKeyPath $pubPath) `
        'reads the real .pub file verbatim when present'

    $missingPubPath = Join-Path ([System.IO.Path]::GetTempPath()) ("rg-pub-missing-" + [Guid]::NewGuid() + '.pub')
    $derived = Get-EffectivePublicKeyBase64 -PrivateKeyBase64 $kp.PrivateKeyBase64 -PublicKeyPath $missingPubPath
    Assert-Equal $kp.PublicKeyBase64 $derived 'derives a public key BYTE-IDENTICAL to the real .pub when the .pub is absent'

    # And it must genuinely be usable to verify a real signature — not merely string-equal by
    # coincidence of this test's fixture.
    $probe = [System.Text.Encoding]::UTF8.GetBytes('{"probe":true}')
    $sig = Get-ManifestSignature -ManifestBytes $probe -PrivateKeyBase64 $kp.PrivateKeyBase64
    Assert-Equal $true (Test-ManifestSignature -ManifestBytes $probe -SignatureBase64 $sig -TrustedPublicKeysBase64 @($derived)) `
        'the derived-in-memory public key actually verifies a real signature from the same private key'
} finally {
    Remove-Item $pubPath -Force -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) { Write-Host "`n$($script:Failures) FAILED"; exit 1 }
Write-Host "`nAll release-guard tests passed"; exit 0
