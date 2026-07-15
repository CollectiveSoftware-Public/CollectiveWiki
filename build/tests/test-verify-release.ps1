# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/../lib/Signing.ps1"

$verify = "$PSScriptRoot/../verify-release.ps1"
$script:Failures = 0

function Assert-Verify {
    param(
        [Parameter(Mandatory)][string]$Dir,
        [Parameter(Mandatory)][string[]]$Keys,
        [Parameter(Mandatory)][int]$ExpectedExit,
        [string[]]$MustContain = @(),
        [string[]]$MustNotContain = @(),
        [Parameter(Mandatory)][string]$What
    )
    $out = & pwsh -NoProfile -File $verify -Dir $Dir -PublicKeyBase64 $Keys 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ok = $true
    if ($code -ne $ExpectedExit) { $ok = $false; $why = "expected exit $ExpectedExit, got $code" }
    foreach ($m in $MustContain)    { if ($out -notlike "*$m*") { $ok = $false; $why = "output missing '$m'" } }
    foreach ($m in $MustNotContain) { if ($out -like  "*$m*")   { $ok = $false; $why = "output wrongly contains '$m'" } }
    if ($ok) { Write-Host "  PASS  $What" }
    else { Write-Host "  FAIL  $What`n        $why`n--- output ---`n$out"; $script:Failures++ }
}

# Build a genuine, signed release directory.
function New-Fixture {
    $d = Join-Path ([System.IO.Path]::GetTempPath()) ("vr-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $d | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $d 'CollectiveWiki-1.0.0-win-x64.exe'), 'win-bits')
    [System.IO.File]::WriteAllText((Join-Path $d 'CollectiveWiki-1.0.0-linux-x64'), 'linux-bits')
    & pwsh -NoProfile -File "$PSScriptRoot/../make-manifest.ps1" `
        -Version '1.0.0' -Tag 'v1.0.0' -Repo 'CollectiveSoftware-Public/CollectiveWiki' `
        -StagedDir $d -OutFile (Join-Path $d 'manifest.json') *> $null
    $kp = New-SigningKeyPair
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $d 'manifest.json'))
    $sig = Get-ManifestSignature -ManifestBytes $bytes -PrivateKeyBase64 $kp.PrivateKeyBase64
    [System.IO.File]::WriteAllText((Join-Path $d 'manifest.json.sig'), $sig, [System.Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ Dir = $d; PublicKey = $kp.PublicKeyBase64 }
}

Write-Host 'genuine release still verifies (no regression)'
$f = New-Fixture
Assert-Verify -Dir $f.Dir -Keys @($f.PublicKey) -ExpectedExit 0 `
    -MustContain 'Release verified' -MustNotContain 'FAIL' -What 'clean release passes'

Write-Host 'ROGUE asset present but not declared by the signed manifest'
[System.IO.File]::WriteAllText((Join-Path $f.Dir 'CollectiveWiki-1.0.0-Setup.exe'), 'ROGUE PAYLOAD')
Assert-Verify -Dir $f.Dir -Keys @($f.PublicKey) -ExpectedExit 1 `
    -MustContain 'CollectiveWiki-1.0.0-Setup.exe', 'not declared' `
    -MustNotContain 'safe to run' -What 'ROGUE asset is rejected AND named'

Remove-Item (Join-Path $f.Dir 'CollectiveWiki-1.0.0-Setup.exe') -Force

# Case-variant / ordinal-exclusion coverage (e.g. a rogue MANIFEST.JSON or Manifest.Json.Sig
# riding along disguised as sidecar metadata) is already proven at the function level —
# deterministically, cross-platform, with zero disk I/O — by test-release-guard.ps1
# (Get-ArtifactSetMismatch's ordinal HashSet exclusion, exercised via
# Test-RemoteAssetSetMatchesManifest). Do not re-add an E2E scenario for it here; it is not a gap.

Write-Host 'MISSING artifact stays benign — users download only their platform'
Remove-Item (Join-Path $f.Dir 'CollectiveWiki-1.0.0-linux-x64') -Force
Assert-Verify -Dir $f.Dir -Keys @($f.PublicKey) -ExpectedExit 0 `
    -MustContain 'Release verified', 'SKIP' -MustNotContain 'FAIL' `
    -What 'declared-but-absent artifact still passes (SKIP, not a failure)'

Write-Host 'the rogue gate runs AFTER the signature check, not before'
$other = New-SigningKeyPair
[System.IO.File]::WriteAllText((Join-Path $f.Dir 'CollectiveWiki-1.0.0-Setup.exe'), 'ROGUE PAYLOAD')
Assert-Verify -Dir $f.Dir -Keys @($other.PublicKeyBase64) -ExpectedExit 1 `
    -MustContain 'signature does NOT verify' `
    -What 'an untrusted key fails at the SIGNATURE stage, not the rogue stage'

Remove-Item $f.Dir -Recurse -Force

Write-Host 'a signed manifest with no "published" field must not crash the display path'
$g = New-Fixture
# Re-sign a manifest that deliberately omits "published" — the only shape that defeats the regex.
$noPub = '{"version":"1.0.0","notesUrl":"https://example.invalid","artifacts":[]}'
[System.IO.File]::WriteAllText((Join-Path $g.Dir 'manifest.json'), $noPub, [System.Text.UTF8Encoding]::new($false))
$kp2 = New-SigningKeyPair
$b2 = [System.IO.File]::ReadAllBytes((Join-Path $g.Dir 'manifest.json'))
$s2 = Get-ManifestSignature -ManifestBytes $b2 -PrivateKeyBase64 $kp2.PrivateKeyBase64
[System.IO.File]::WriteAllText((Join-Path $g.Dir 'manifest.json.sig'), $s2, [System.Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $g.Dir -File | Where-Object { $_.Name -notlike 'manifest.json*' } | Remove-Item -Force
# artifacts is empty, so this exits 1 via the $checked -eq 0 guard — but it must reach that
# guard cleanly rather than throwing a StrictMode PropertyNotFoundException in the display line.
Assert-Verify -Dir $g.Dir -Keys @($kp2.PublicKeyBase64) -ExpectedExit 1 `
    -MustContain 'no artifacts were present to verify' `
    -MustNotContain 'cannot be found on this object' `
    -What 'missing "published" does not crash the display path'
Remove-Item $g.Dir -Recurse -Force

if ($script:Failures -gt 0) { Write-Host "`n$($script:Failures) FAILED"; exit 1 }
Write-Host "`nAll verify-release tests passed"; exit 0
