# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Crypto + manifest construction for CollectiveWiki release signing.
.DESCRIPTION
  Pure functions only — no network, no gh, no ambient clock. ECDSA P-256 / SHA-256
  (see the auto-update spec D4: .NET ships no Ed25519, and P-256 already backs
  Wiki.Sync/DeviceIdentity.cs). Public keys are base64 SPKI; private keys base64 PKCS#8.
#>
#requires -Version 7
Set-StrictMode -Version Latest

$script:Sha = [System.Security.Cryptography.HashAlgorithmName]::SHA256

function New-SigningKeyPair {
    [CmdletBinding()]
    param()
    $k = [System.Security.Cryptography.ECDsa]::Create(
        [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    try {
        [pscustomobject]@{
            PublicKeyBase64  = [Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo())
            PrivateKeyBase64 = [Convert]::ToBase64String($k.ExportPkcs8PrivateKey())
        }
    } finally { $k.Dispose() }
}

function Get-FileSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-UpdateManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$NotesUrl,
        [Parameter(Mandatory)][datetime]$PublishedUtc,
        [Parameter(Mandatory)][object[]]$Artifacts   # @{ Rid=; Path=; Url= }
    )
    if ($PublishedUtc.Kind -ne [System.DateTimeKind]::Utc) {
        throw "PublishedUtc must be DateTimeKind.Utc (got '$($PublishedUtc.Kind)'). " +
              "A caller using [datetime]::Parse(...) gets Kind=Unspecified, which " +
              "ToUniversalTime() silently treats as local time and shifts — construct it " +
              "explicitly with DateTimeKind.Utc instead."
    }
    $list = @(foreach ($a in $Artifacts) {
        if (-not (Test-Path -LiteralPath $a.Path)) { throw "Artifact not found: $($a.Path)" }
        [ordered]@{
            rid    = $a.Rid
            url    = $a.Url
            sha256 = Get-FileSha256 -Path $a.Path
            size   = (Get-Item -LiteralPath $a.Path).Length
        }
    })
    ([ordered]@{
        version   = $Version
        published = $PublishedUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        notesUrl  = $NotesUrl
        artifacts = $list
    }) | ConvertTo-Json -Depth 5
}

function Get-ManifestSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][byte[]]$ManifestBytes,
        [Parameter(Mandatory)][string]$PrivateKeyBase64
    )
    $k = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $k.ImportPkcs8PrivateKey([Convert]::FromBase64String($PrivateKeyBase64), [ref]0)
        [Convert]::ToBase64String($k.SignData($ManifestBytes, $script:Sha))
    } finally { $k.Dispose() }
}

function Test-ManifestSignature {
    <# Returns $false for any malformed input. NEVER throws — a verification failure and a
       parse failure are both "do not trust this", and callers must not distinguish them. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][byte[]]$ManifestBytes,
        [Parameter(Mandatory)][AllowEmptyString()][string]$SignatureBase64,
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][AllowEmptyString()][string[]]$TrustedPublicKeysBase64
    )
    # Null/empty here are parse failures, not verification failures — same "do not trust
    # this" outcome as everything else in this function, so return $false rather than throw.
    if ($null -eq $ManifestBytes -or $ManifestBytes.Count -eq 0) { return $false }
    if ($null -eq $TrustedPublicKeysBase64 -or $TrustedPublicKeysBase64.Count -eq 0) { return $false }
    $sig = $null
    try { $sig = [Convert]::FromBase64String($SignatureBase64) } catch { return $false }
    foreach ($pub in $TrustedPublicKeysBase64) {
        # A null/empty entry beside valid keys must not defeat key rotation — skip it, don't throw.
        if ([string]::IsNullOrEmpty($pub)) { continue }
        $v = [System.Security.Cryptography.ECDsa]::Create()
        try {
            $v.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($pub), [ref]0)
            if ($v.VerifyData($ManifestBytes, $sig, $script:Sha)) { return $true }
        } catch { continue } finally { $v.Dispose() }
    }
    return $false
}
