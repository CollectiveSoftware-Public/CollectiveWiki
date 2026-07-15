# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/../lib/Signing.ps1"

$script:Failures = 0
function Assert-Equal($Expected, $Actual, $What) {
    if ($Expected -eq $Actual) { Write-Host "  PASS  $What" }
    else { Write-Host "  FAIL  $What`n        expected: $Expected`n        actual:   $Actual"; $script:Failures++ }
}
function Assert-True($Actual, $What)  { Assert-Equal $true  $Actual $What }
function Assert-False($Actual, $What) { Assert-Equal $false $Actual $What }
function Assert-Throws([scriptblock]$Action, $What) {
    try {
        & $Action
        Write-Host "  FAIL  $What`n        expected: throw`n        actual:   no exception"
        $script:Failures++
    } catch { Write-Host "  PASS  $What" }
}

$sha = [System.Security.Cryptography.HashAlgorithmName]::SHA256
$bytes = [System.Text.Encoding]::UTF8.GetBytes('{"version":"1.0.0"}')

Write-Host 'New-SigningKeyPair'
$kp = New-SigningKeyPair
Assert-True ($kp.PublicKeyBase64.Length -gt 0)  'exports a public key'
Assert-True ($kp.PrivateKeyBase64.Length -gt 0) 'exports a private key'
Assert-True ($kp.PublicKeyBase64 -ne $kp.PrivateKeyBase64) 'public and private differ'

Write-Host 'Get-ManifestSignature / Test-ManifestSignature'
$sig = Get-ManifestSignature -ManifestBytes $bytes -PrivateKeyBase64 $kp.PrivateKeyBase64
Assert-Equal 64 ([Convert]::FromBase64String($sig)).Length 'signature is 64 bytes'
Assert-True  (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'genuine signature verifies'

$tampered = [System.Text.Encoding]::UTF8.GetBytes('{"version":"9.9.9"}')
Assert-False (Test-ManifestSignature -ManifestBytes $tampered -SignatureBase64 $sig -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'TAMPERED manifest is rejected'

$other = New-SigningKeyPair
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @($other.PublicKeyBase64)) 'WRONG key is rejected'

$raw = [Convert]::FromBase64String($sig)
$truncB64 = [Convert]::ToBase64String($raw[0..($raw.Length - 8)])
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $truncB64 -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'TRUNCATED signature is rejected (no throw)'
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 'not-base64!!' -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'GARBAGE signature is rejected (no throw)'
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @('not-a-key')) 'MALFORMED trusted key is rejected (no throw)'
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @()) 'EMPTY trusted key list is rejected'

Write-Host 'malformed input never throws (parameter-binder edge cases)'
# The PowerShell parameter binder validates Mandatory arguments BEFORE the function body (and its
# try/catch) ever runs. [AllowEmptyCollection()] alone exempts only the whole collection from the
# null/empty check — not $null itself, and not null/empty individual elements. Every one of these
# must return $false, never throw, per Test-ManifestSignature's documented contract.
Assert-False (Test-ManifestSignature -ManifestBytes $null -SignatureBase64 $sig -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'NULL ManifestBytes is rejected (no throw)'
Assert-False (Test-ManifestSignature -ManifestBytes @() -SignatureBase64 $sig -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'EMPTY ManifestBytes is rejected (no throw)'
Assert-False (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 $null) 'NULL TrustedPublicKeysBase64 is rejected (no throw)'
Assert-True (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @($null, $kp.PublicKeyBase64)) 'a null entry beside a valid key still verifies — must not defeat key rotation (no throw)'

Write-Host 'key rotation (D10 — trusted keys are a list)'
Assert-True (Test-ManifestSignature -ManifestBytes $bytes -SignatureBase64 $sig -TrustedPublicKeysBase64 @($other.PublicKeyBase64, $kp.PublicKeyBase64)) 'accepts when signer is any trusted key'

Write-Host 'Get-FileSha256'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("sha-" + [Guid]::NewGuid())
[System.IO.File]::WriteAllText($tmp, 'hello')
Assert-Equal '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824' (Get-FileSha256 -Path $tmp) 'sha256("hello") matches the known vector'
Remove-Item $tmp -Force

Write-Host 'New-UpdateManifest'
$a1 = Join-Path ([System.IO.Path]::GetTempPath()) ("a1-" + [Guid]::NewGuid())
[System.IO.File]::WriteAllText($a1, 'hello')
$json = New-UpdateManifest -Version '1.0.0' -NotesUrl 'https://example.invalid/notes' `
    -PublishedUtc ([datetime]::new(2026, 7, 20, 14, 0, 0, [DateTimeKind]::Utc)) `
    -Artifacts @(@{ Rid = 'win-x64'; Path = $a1; Url = 'https://example.invalid/a.exe' })
$m = $json | ConvertFrom-Json
Assert-Equal '1.0.0' $m.version 'version'
# NOTE: asserted against the raw JSON text, not $m.published — PowerShell's ConvertFrom-Json
# (pwsh 7.6.0, Newtonsoft.Json under the hood, both with and without -AsHashtable) auto-converts
# ISO-8601-looking strings to [datetime], which then round-trips through Assert-Equal's -eq
# (string-vs-datetime coercion) using culture-formatted ToString(), not the ISO form. Checking the
# literal bytes is also the more faithful test here: the signature covers exact manifest bytes.
Assert-True ($json -match '"published":\s*"2026-07-20T14:00:00Z"') 'published is ISO-8601 UTC'
Assert-Equal 'https://example.invalid/notes' $m.notesUrl 'notesUrl'
Assert-Equal 1 $m.artifacts.Count 'one artifact'
Assert-Equal 'win-x64' $m.artifacts[0].rid 'artifact rid'
Assert-Equal 'https://example.invalid/a.exe' $m.artifacts[0].url 'artifact url'
Assert-Equal '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824' $m.artifacts[0].sha256 'artifact sha256'
Assert-Equal 5 $m.artifacts[0].size 'artifact size'

# [datetime]::Parse(...) yields DateTimeKind.Unspecified, which .ToUniversalTime() silently
# treats as local time (a silent shift) rather than guessing wrong — reject it outright.
Assert-Throws ({
    New-UpdateManifest -Version '1.0.0' -NotesUrl 'https://example.invalid/notes' `
        -PublishedUtc ([datetime]::Parse('2026-07-20T14:00:00')) `
        -Artifacts @(@{ Rid = 'win-x64'; Path = $a1; Url = 'https://example.invalid/a.exe' })
}) 'non-UTC PublishedUtc.Kind (Unspecified) is rejected, not silently shifted'
Remove-Item $a1 -Force

Write-Host 'round-trip: a real manifest signs and verifies'
$mb = [System.Text.Encoding]::UTF8.GetBytes($json)
$msig = Get-ManifestSignature -ManifestBytes $mb -PrivateKeyBase64 $kp.PrivateKeyBase64
Assert-True (Test-ManifestSignature -ManifestBytes $mb -SignatureBase64 $msig -TrustedPublicKeysBase64 @($kp.PublicKeyBase64)) 'manifest round-trips'

Write-Host 'make-manifest.ps1'
$staged = Join-Path ([System.IO.Path]::GetTempPath()) ("staged-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $staged | Out-Null
[System.IO.File]::WriteAllText((Join-Path $staged 'CollectiveWiki-1.0.0-win-x64.exe'), 'hello')
[System.IO.File]::WriteAllText((Join-Path $staged 'CollectiveWiki-1.0.0-linux-x64'), 'hello')
$out = Join-Path $staged 'manifest.json'

# Capture output (never *> $null here) so a failure assertion can check *why* the script
# failed, not just that it exited non-zero — an unrelated crash (e.g. a future Set-StrictMode
# violation) also exits 1, and this project has shipped three defects of exactly that class
# where a crash's exit 1 silently matched an intended exit 1.
function Assert-ManifestOutput {
    param(
        [Parameter(Mandatory)][int]$ExpectedExit,
        [string[]]$MustContain = @(),
        [string[]]$MustNotContain = @(),
        [Parameter(Mandatory)][string]$What,
        [Parameter(Mandatory)][string[]]$ScriptArgs,
        # Which script to run — defaults to make-manifest.ps1 (this helper's original caller);
        # verify-release.ps1's tests pass their own path so both scripts share one real
        # "assert on text, not just $LASTEXITCODE" helper instead of a second copy of it.
        [string]$ScriptPath = "$PSScriptRoot/../make-manifest.ps1"
    )
    $out = & pwsh -NoProfile -File $ScriptPath @ScriptArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ok = ($code -eq $ExpectedExit)
    foreach ($needle in $MustContain)    { if ($ok -and $out -notlike "*$needle*") { $ok = $false } }
    foreach ($needle in $MustNotContain) { if ($ok -and $out -like    "*$needle*") { $ok = $false } }
    if ($ok) { Write-Host "  PASS  $What" }
    else {
        Write-Host "  FAIL  $What (exit $code, expected $ExpectedExit)"
        Write-Host "        output: $out"
        $script:Failures++
    }
}

# A live [datetime] object round-tripped through `pwsh -File`'s cross-process argument
# marshalling: PowerShell stringifies it using .NET's plain invariant "general" format when
# building the child process's command line, which drops Kind/offset entirely regardless of
# the source value's Kind (verified empirically). That bare string is indistinguishable from
# a human typing an ambiguous local time, so post-fix it is correctly REJECTED — a caller
# round-tripping a real datetime object across this boundary must format it self-describingly
# first; 'o' is .NET's actual "round-trip" specifier and keeps the 'Z'.
$publishedArg = ([datetime]::new(2026, 7, 20, 14, 0, 0, [DateTimeKind]::Utc)).ToString('o')

Assert-ManifestOutput -ExpectedExit 0 -What 'make-manifest exits 0 when both artifacts are present' -ScriptArgs @(
    '-Version', '1.0.0', '-Tag', 'v1.0.0', '-Repo', 'CollectiveSoftware-Public/CollectiveWiki',
    '-StagedDir', $staged, '-OutFile', $out, '-PublishedUtc', $publishedArg
)
Assert-True (Test-Path $out) 'manifest.json is written'

$rawManifest = Get-Content -LiteralPath $out -Raw
$mm = $rawManifest | ConvertFrom-Json
Assert-Equal '1.0.0' $mm.version 'manifest version'
Assert-Equal 2 $mm.artifacts.Count 'both RIDs present'
$win = $mm.artifacts | Where-Object { $_.rid -eq 'win-x64' }
$lin = $mm.artifacts | Where-Object { $_.rid -eq 'linux-x64' }
Assert-Equal 'https://github.com/CollectiveSoftware-Public/CollectiveWiki/releases/download/v1.0.0/CollectiveWiki-1.0.0-win-x64.exe' $win.url 'win-x64 download URL'
Assert-Equal 'https://github.com/CollectiveSoftware-Public/CollectiveWiki/releases/download/v1.0.0/CollectiveWiki-1.0.0-linux-x64' $lin.url 'linux-x64 download URL'
Assert-Equal '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824' $win.sha256 'win-x64 sha256'
# Asserted against the raw JSON text, not $mm.published, for the same ConvertFrom-Json
# auto-datetime-coercion reason noted above: this proves the round-tripped [datetime]
# object produced the correct, unshifted UTC instant (14:00Z), not just that the run exited 0.
Assert-True ($rawManifest -match '"published":\s*"2026-07-20T14:00:00Z"') 'PublishedUtc datetime-object round-trip is unshifted UTC'

Remove-Item (Join-Path $staged 'CollectiveWiki-1.0.0-linux-x64') -Force
Remove-Item -LiteralPath $out -Force
Assert-ManifestOutput -ExpectedExit 1 `
    -MustContain 'linux-x64', 'Refusing to build a partial manifest' `
    -What 'make-manifest FAILS when an artifact is missing (never ships a partial release), naming the missing RID' `
    -ScriptArgs @(
        '-Version', '1.0.0', '-Tag', 'v1.0.0', '-Repo', 'CollectiveSoftware-Public/CollectiveWiki',
        '-StagedDir', $staged, '-OutFile', $out
    )
Assert-False (Test-Path $out) 'no manifest.json is written when an artifact is missing'

# Restore both artifacts so the two cases below fail for one reason only: the date.
[System.IO.File]::WriteAllText((Join-Path $staged 'CollectiveWiki-1.0.0-linux-x64'), 'hello')

Assert-ManifestOutput -ExpectedExit 1 `
    -MustContain 'PublishedUtc must be', 'Unspecified' `
    -What 'ambiguous/no-offset -PublishedUtc (2026-07-20T09:00:00) is REJECTED, not silently assumed UTC' `
    -ScriptArgs @(
        '-Version', '1.0.0', '-Tag', 'v1.0.0', '-Repo', 'CollectiveSoftware-Public/CollectiveWiki',
        '-StagedDir', $staged, '-OutFile', $out, '-PublishedUtc', '2026-07-20T09:00:00'
    )
Assert-False (Test-Path $out) 'no manifest.json is written for an ambiguous PublishedUtc'

Assert-ManifestOutput -ExpectedExit 1 `
    -MustContain 'ERROR', 'not-a-date' `
    -What 'garbage -PublishedUtc ("not-a-date") fails closed with a clear error' `
    -ScriptArgs @(
        '-Version', '1.0.0', '-Tag', 'v1.0.0', '-Repo', 'CollectiveSoftware-Public/CollectiveWiki',
        '-StagedDir', $staged, '-OutFile', $out, '-PublishedUtc', 'not-a-date'
    )
Assert-False (Test-Path $out) 'no manifest.json is written for a garbage PublishedUtc'

Remove-Item $staged -Recurse -Force

Write-Host 'verify-release.ps1'
$vdir = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $vdir | Out-Null
[System.IO.File]::WriteAllText((Join-Path $vdir 'CollectiveWiki-1.0.0-win-x64.exe'), 'hello')
[System.IO.File]::WriteAllText((Join-Path $vdir 'CollectiveWiki-1.0.0-linux-x64'), 'hello')
pwsh -NoProfile -File "$PSScriptRoot/../make-manifest.ps1" -Version '1.0.0' -Tag 'v1.0.0' `
    -Repo 'CollectiveSoftware-Public/CollectiveWiki' -StagedDir $vdir -OutFile (Join-Path $vdir 'manifest.json') *> $null
$vkp = New-SigningKeyPair
$vbytes = [System.IO.File]::ReadAllBytes((Join-Path $vdir 'manifest.json'))
$vsig = Get-ManifestSignature -ManifestBytes $vbytes -PrivateKeyBase64 $vkp.PrivateKeyBase64
[System.IO.File]::WriteAllText((Join-Path $vdir 'manifest.json.sig'), $vsig, [System.Text.UTF8Encoding]::new($false))

$verify       = "$PSScriptRoot/../verify-release.ps1"
$manifestFile = Join-Path $vdir 'manifest.json'
$sigFile      = Join-Path $vdir 'manifest.json.sig'
$winArtifact  = Join-Path $vdir 'CollectiveWiki-1.0.0-win-x64.exe'
$linArtifact  = Join-Path $vdir 'CollectiveWiki-1.0.0-linux-x64'

# $LASTEXITCODE alone cannot tell "correctly rejected" from "crashed before it checked anything" —
# a broken copy that throws on an undefined variable BEFORE the signature check also exits 1,
# which would make every assertion below pass green while verifying nothing. Assert on the
# actual output text (as make-manifest's tests above already do), the same way.
Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 0 `
    -MustContain 'Release verified' -MustNotContain 'FAIL' `
    -What 'verify-release accepts a genuine, intact release' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', $vkp.PublicKeyBase64)

Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 1 `
    -MustContain 'FAIL: manifest signature does NOT verify' `
    -What 'verify-release REJECTS a release signed by an untrusted key' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', (New-SigningKeyPair).PublicKeyBase64)

[System.IO.File]::WriteAllText($winArtifact, 'EVIL')
# Both musts matter: the signature legitimately verifying PROVES the rejection below happened at
# the hash stage (on tampered content), not from some earlier crash coincidentally exiting 1.
Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 1 `
    -MustContain 'OK: manifest signature verifies', 'hash mismatch' `
    -What 'verify-release REJECTS a tampered artifact (sig still valid, hash is not)' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', $vkp.PublicKeyBase64)
[System.IO.File]::WriteAllText($winArtifact, 'hello')

Write-Host 'verify-release.ps1 — negative-path regression coverage (previously zero automated coverage)'

# Missing manifest.json: the missing-file loop checks manifestPath before sigPath, so with the
# sig left in place this can only be reporting the manifest as missing.
Remove-Item -LiteralPath $manifestFile -Force
Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 1 `
    -MustContain 'FAIL: missing', 'manifest.json' -MustNotContain 'manifest.json.sig' `
    -What 'verify-release FAILS when manifest.json is missing' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', $vkp.PublicKeyBase64)
[System.IO.File]::WriteAllBytes($manifestFile, $vbytes)

# Missing manifest.json.sig: manifest is back, so this can only be reporting the sig as missing.
Remove-Item -LiteralPath $sigFile -Force
Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 1 `
    -MustContain 'FAIL: missing', 'manifest.json.sig' `
    -What 'verify-release FAILS when manifest.json.sig is missing' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', $vkp.PublicKeyBase64)
[System.IO.File]::WriteAllText($sigFile, $vsig, [System.Text.UTF8Encoding]::new($false))

# Valid signature, zero artifacts present locally — the tool's worst failure mode: "I verified
# nothing" must never print as success. Requiring the signature-OK line proves this exercises the
# $checked -eq 0 guard, not the signature or missing-file checks above.
Remove-Item -LiteralPath $winArtifact -Force
Remove-Item -LiteralPath $linArtifact -Force
Assert-ManifestOutput -ScriptPath $verify -ExpectedExit 1 `
    -MustContain 'OK: manifest signature verifies', 'no artifacts were present to verify' `
    -What 'verify-release FAILS closed when the signature verifies but no artifacts are present locally' `
    -ScriptArgs @('-Dir', $vdir, '-PublicKeyBase64', $vkp.PublicKeyBase64)

Remove-Item $vdir -Recurse -Force

if ($script:Failures -gt 0) { Write-Host "`n$($script:Failures) FAILED"; exit 1 }
Write-Host "`nAll signing tests passed"; exit 0
