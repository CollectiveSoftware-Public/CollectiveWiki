# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Local-signing safety checks for CollectiveWiki release publishing.
.DESCRIPTION
  Pure, file-system-only functions — no network, no `gh`, no ambient clock — so they can be
  exercised against a local fixture (and a plain in-memory string list) without a live GitHub
  draft release. Kept separate from Signing.ps1 (which owns crypto primitives) and
  verify-release.ps1 (the reviewed/approved verifier) so neither of those files needs to change.

  Two gates against a rogue asset riding along beside a valid signature:
    - Test-ArtifactSetMatchesManifest checks the LOCAL download directory (only ever sees what
      `gh release download --pattern <p>` chose to fetch).
    - Test-RemoteAssetSetMatchesManifest checks the ACTUAL remote asset list attached to the
      draft release (sees everything, regardless of naming convention — the broader gate;
      see its own doc comment for why the first one alone is not enough).
  Both share the pure set-equality core, Get-ArtifactSetMismatch.
#>
#requires -Version 7
Set-StrictMode -Version Latest

function Get-ArtifactSetMismatch {
    <#
    .SYNOPSIS
      Internal. Pure exact-set-equality core shared by both artifact-set gates below.
    .DESCRIPTION
      Excludes manifest.json / manifest.json.sig (expected sidecar metadata, never artifacts)
      from $ActualNames by EXACT literal name — case-sensitive ordinal, not a prefix/wildcard
      match, and NOT PowerShell's default case-insensitive comparison — so an attacker cannot
      hide a rogue asset under a "looks like metadata" name such as manifest.json.bak, nor under
      a case variant such as MANIFEST.JSON.
      Returns the two disjoint failure sets: Rogue (present but not declared) and Missing
      (declared but not present). Never throws; callers decide the error text and whether a
      given set makes a check fail.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ActualNames,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ManifestArtifacts
    )
    $ignoreNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@('manifest.json', 'manifest.json.sig'), [System.StringComparer]::Ordinal)

    $actual = @($ActualNames | Where-Object { -not $ignoreNames.Contains($_) })
    $expected = @($ManifestArtifacts | ForEach-Object { ($_.url -split '/')[-1] })

    $expectedSet = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$expected, [System.StringComparer]::Ordinal)
    $actualSet = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$actual, [System.StringComparer]::Ordinal)

    [pscustomobject]@{
        Rogue   = @($actual | Where-Object { -not $expectedSet.Contains($_) })
        Missing = @($expected | Where-Object { -not $actualSet.Contains($_) })
    }
}

function Test-ArtifactSetMatchesManifest {
    <#
    .SYNOPSIS
      Asserts the artifact files actually present in a downloaded release directory are an
      EXACT match for the filenames the manifest declares — not merely a superset check.
    .DESCRIPTION
      The hash-verification loop in sign-release.ps1 only walks $manifest.artifacts, so it
      proves every manifest-listed file is present and correct. It never notices a file that
      is present but NOT listed. A compromised or buggy CI step can attach a rogue asset to
      the draft (one that still matches the download --pattern, e.g.
      'CollectiveWiki-debug-payload.exe') and this script would download it, never look at it,
      sign the (accurate) manifest, and publish — leaving the rogue file live next to a valid
      signature that appears to vouch for it.
      This function closes that gap over the LOCAL download directory: it enumerates $Dir for
      files matching $Pattern (excluding manifest.json/manifest.json.sig) and throws — naming
      the offending file(s) — on any file present that the manifest does not declare, or any
      manifest-declared file that is not present.
      NOTE: this only ever sees what $Pattern actually matched — i.e. what `gh release
      download --pattern $Pattern` chose to fetch in the first place. An asset whose name
      does not match $Pattern is never downloaded, so it is invisible here too. That is what
      Test-RemoteAssetSetMatchesManifest (below) exists to catch; it is an ADDITIONAL gate,
      not a replacement for this one.
      $Pattern is Mandatory (no default) so the artifact-naming convention lives in exactly one
      place — sign-release.ps1's own $artifactPattern variable — and this file can never drift
      out of sync with it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Dir,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ManifestArtifacts,
        [Parameter(Mandatory)][string]$Pattern
    )
    $actualNames = @(Get-ChildItem -LiteralPath $Dir -File |
        Where-Object { $_.Name -like $Pattern } |
        ForEach-Object { $_.Name })

    $mismatch = Get-ArtifactSetMismatch -ActualNames $actualNames -ManifestArtifacts $ManifestArtifacts

    if ($mismatch.Rogue.Count -gt 0) {
        throw "Unexpected artifact(s) present in the draft that manifest.json does NOT " +
              "declare: $($mismatch.Rogue -join ', '). This may be a compromised or buggy CI step. " +
              "Refusing to sign or publish."
    }
    if ($mismatch.Missing.Count -gt 0) {
        throw "Manifest declares artifact(s) not present in the download: " +
              "$($mismatch.Missing -join ', '). Refusing to sign."
    }
}

function Test-RemoteAssetSetMatchesManifest {
    <#
    .SYNOPSIS
      Asserts the assets actually ATTACHED TO THE DRAFT RELEASE on GitHub are an exact match
      for the filenames the manifest declares — regardless of what any download --pattern
      would have fetched. This is the broader gate that closes the CRITICAL finding
      Test-ArtifactSetMatchesManifest only partially addressed.
    .DESCRIPTION
      Test-ArtifactSetMatchesManifest only ever inspects the LOCAL download directory, which is
      populated solely by `gh release download --pattern <p>`. An asset whose name doesn't
      match that pattern is never downloaded — never lands in the local directory — so it is
      invisible to that check. And `gh release edit --draft=false` publishes the release AS A
      WHOLE: GitHub has no notion of "publish only the assets I verified." Every attached asset
      goes live. The 'CollectiveWiki-*' naming is a convention our own CI happens to follow; it
      is not enforced by GitHub or `gh`. A compromised or buggy CI step can run
      `gh release upload $tag anything.bin` with any name at all, and the local-only check
      would not download it, hash it, name it in an error, or refuse to publish — it would go
      public beside a signature that appears to vouch for the release.
      This function closes that gap: it is handed the REMOTE asset name list directly (fetched
      by the caller via `gh release view $Tag -R $Repo --json assets --jq '.assets[].name'`, so
      this function itself stays network-free and unit-testable without `gh`) and performs the
      same genuine set-equality check — naming the offending file(s) — over what GitHub
      actually has attached, not over what a --pattern filter chose to fetch.
      This is an ADDITIONAL, broader gate: sign-release.ps1 still runs the local
      Test-ArtifactSetMatchesManifest check too; this does not replace it. Callers should run
      this as close to the `gh release edit --draft=false` call as practical — nothing re-checks
      the asset list between this call and the publish call itself, so an asset attached in
      that exact window would still slip through. That TOCTOU window is inherent to the design
      (GitHub gives no atomic "verify-then-publish" primitive); it can be shrunk, not eliminated.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$RemoteAssetNames,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ManifestArtifacts
    )
    $mismatch = Get-ArtifactSetMismatch -ActualNames $RemoteAssetNames -ManifestArtifacts $ManifestArtifacts

    if ($mismatch.Rogue.Count -gt 0) {
        throw "Unexpected asset(s) are attached to the draft release that manifest.json does " +
              "NOT declare: $($mismatch.Rogue -join ', '). This asset does not need to match " +
              "any download naming convention to be dangerous — every attached asset goes " +
              "public the instant this release is published. This may be a compromised or " +
              "buggy CI step. Refusing to sign or publish."
    }
    if ($mismatch.Missing.Count -gt 0) {
        throw "Manifest declares artifact(s) not attached to the draft release: " +
              "$($mismatch.Missing -join ', '). Refusing to sign."
    }
}

function Get-EffectivePublicKeyBase64 {
    <#
    .SYNOPSIS
      Returns the base64 SPKI public key to self-verify a just-produced signature against.
    .DESCRIPTION
      Reads $PublicKeyPath if it exists. If it does not — e.g. an operator restored only the
      private key from CollectiveVault or an offline backup after a disk loss — this derives
      the public key in-memory from the private key instead of allowing the caller to skip
      self-verification. Self-verification must never be silently skippable: it is the only
      local proof that the signature and hashes about to be published are correct.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PrivateKeyBase64,
        [Parameter(Mandatory)][string]$PublicKeyPath
    )
    if (Test-Path -LiteralPath $PublicKeyPath) {
        return (Get-Content -LiteralPath $PublicKeyPath -Raw).Trim()
    }
    Write-Host "    (no .pub file at $PublicKeyPath — deriving the public key in-memory from the private key)"
    $k = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $k.ImportPkcs8PrivateKey([Convert]::FromBase64String($PrivateKeyBase64), [ref]0)
        [Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo())
    } finally {
        $k.Dispose()
    }
}
