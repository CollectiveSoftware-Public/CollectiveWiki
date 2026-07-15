# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Signs a draft release's manifest with the offline key and publishes it.
.DESCRIPTION
  Re-verifies CI's hashes against the actual downloaded bytes before signing — CI is not
  trusted blindly. Publishing IS signing: a release only goes public through this script,
  so an unsigned release cannot reach users. Three invariants CI cannot be trusted to enforce
  on itself: (1) every downloaded file must be accounted for in the manifest — a rogue asset
  attached by a compromised/buggy CI step must never ride along beside a valid signature —
  (2) that check must also cover assets `gh release download --pattern` never fetched in the
  first place, by checking GitHub's actual attached-asset list, not just the local download
  directory — and (3) self-verification of the signature just produced is mandatory, never
  silently skippable, even if the public-key file isn't beside the private key.
  NOTE for operators: nothing re-checks the remote asset list between that check and
  `--draft=false` — an asset attached in that exact window would still slip through. That
  TOCTOU window is inherent (GitHub has no atomic verify-then-publish primitive), so the
  remote check below is placed as close to the publish call as is practical, to shrink it.
.EXAMPLE
  ./build/sign-release.ps1 -Tag v1.0.0
#>
#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Repo = 'CollectiveSoftware-Public/CollectiveWiki',
    [string]$KeyPath = (Join-Path $env:USERPROFILE '.collective/keys/collectivewiki-release.key.b64')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/lib/Signing.ps1"
. "$PSScriptRoot/lib/ReleaseGuard.ps1"

if (-not (Test-Path -LiteralPath $KeyPath)) { throw "Signing key not found: $KeyPath" }

$artifactPattern = 'CollectiveWiki-*'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "cw-sign-$Tag-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Force -Path $work | Out-Null
Write-Host "Working in $work"

try {
    Write-Host "==> Downloading draft $Tag from $Repo"
    gh release download $Tag -R $Repo --dir $work --pattern $artifactPattern --pattern 'manifest.json'
    if ($LASTEXITCODE -ne 0) { throw "gh release download failed for $Tag" }

    $manifestPath = Join-Path $work 'manifest.json'
    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $manifest = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json

    if ($manifest.version -ne $Tag.TrimStart('v')) {
        throw "Manifest version '$($manifest.version)' does not match tag '$Tag'."
    }

    Write-Host '==> Checking the local download contains no artifacts the manifest does not declare'
    Test-ArtifactSetMatchesManifest -Dir $work -ManifestArtifacts $manifest.artifacts -Pattern $artifactPattern

    Write-Host '==> Re-verifying CI hashes against the real bytes'
    foreach ($a in $manifest.artifacts) {
        $name = ($a.url -split '/')[-1]
        $path = Join-Path $work $name
        if (-not (Test-Path -LiteralPath $path)) { throw "Artifact missing from the draft: $name" }
        $actual = Get-FileSha256 -Path $path
        if ($actual -ne $a.sha256) {
            throw "HASH MISMATCH for $name`n  manifest: $($a.sha256)`n  actual:   $actual`nRefusing to sign."
        }
        Write-Host "    OK  $name"
    }

    Write-Host '==> Signing the manifest'
    $priv = (Get-Content -LiteralPath $KeyPath -Raw).Trim()
    $sig = Get-ManifestSignature -ManifestBytes $bytes -PrivateKeyBase64 $priv
    $sigPath = Join-Path $work 'manifest.json.sig'
    [System.IO.File]::WriteAllText($sigPath, $sig, [System.Text.UTF8Encoding]::new($false))

    Write-Host '==> Verifying the signature we just produced'
    $pubPath = "$KeyPath.pub"
    $pubB64 = Get-EffectivePublicKeyBase64 -PrivateKeyBase64 $priv -PublicKeyPath $pubPath
    & "$PSScriptRoot/verify-release.ps1" -Dir $work -PublicKeyBase64 $pubB64
    if ($LASTEXITCODE -ne 0) { throw 'Self-verification failed. Refusing to publish.' }

    Write-Host '==> Uploading manifest.json.sig'
    gh release upload $Tag $sigPath -R $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw 'gh release upload failed' }

    # Broader gate (closes the rest of the CRITICAL rogue-asset finding): the local check above
    # only sees files that matched --pattern in the `gh release download` call, so it is blind
    # to any asset whose name doesn't follow the CollectiveWiki-* convention — a convention our
    # own CI happens to follow, not one GitHub or `gh` enforces. Re-check the ACTUAL remote
    # asset list right before publishing. Deliberately placed as the LAST check before
    # --draft=false to shrink (not eliminate) the TOCTOU window described above.
    Write-Host '==> Checking the remote release has no undeclared assets attached (any name, not just --pattern matches)'
    $remoteAssetNames = @(gh release view $Tag -R $Repo --json assets --jq '.assets[].name')
    if ($LASTEXITCODE -ne 0) { throw "gh release view failed for $Tag" }
    Test-RemoteAssetSetMatchesManifest -RemoteAssetNames $remoteAssetNames -ManifestArtifacts $manifest.artifacts

    Write-Host '==> Publishing the release'
    gh release edit $Tag -R $Repo --draft=false
    if ($LASTEXITCODE -ne 0) { throw 'gh release edit failed' }

    Write-Host ''
    Write-Host "Published $Tag — https://github.com/$Repo/releases/tag/$Tag"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
