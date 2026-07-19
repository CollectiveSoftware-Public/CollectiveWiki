# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Pure helpers for cutting a release (version rules + props rewrite) — no network, no gh, no git.
.DESCRIPTION
  Kept separate from Signing.ps1 (crypto primitives) and ReleaseGuard.ps1 (local-signing safety
  gates) so cut-release.ps1's orchestration stays thin and these rules can be unit-tested against
  string/file fixtures (build/tests/test-cut-release.ps1) without a repo or a live GitHub.
#>
#requires -Version 7
Set-StrictMode -Version Latest

function Test-ReleaseVersionFormat {
    <#
    .SYNOPSIS
      True for X.Y.Z with an optional SemVer-style pre-release suffix (1.2.0, 1.2.0-rc.1).
    .DESCRIPTION
      No build metadata (+…): the tag, the props <Version>, and the manifest all carry this exact
      string, and assert-version.ps1 compares it case-sensitively — keep the shape simple.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
    $Version -cmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'
}

function Assert-ReleaseVersionIsNewer {
    <#
    .SYNOPSIS
      Throws unless $New is a strictly newer release than $Current.
    .DESCRIPTION
      Numeric X.Y.Z parts compare first (as numbers — 1.1.10 > 1.1.9). When they are equal, the
      only allowed move is pre-release → final (1.2.0-rc.1 → 1.2.0). Pre-release-to-pre-release
      ordering at the same X.Y.Z is deliberately NOT modeled: UpdatePolicy on the client compares
      versions its own way, so don't cut releases that rely on suffix ordering — bump the numeric
      version instead.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$New,
        [Parameter(Mandatory)][string]$Current
    )
    foreach ($v in @($New, $Current)) {
        if (-not (Test-ReleaseVersionFormat -Version $v)) {
            throw "Not a valid release version: '$v' (expected X.Y.Z or X.Y.Z-suffix)."
        }
    }
    $numNew = [version](($New -split '-', 2)[0])
    $numCur = [version](($Current -split '-', 2)[0])
    if ($numNew -gt $numCur) { return }
    if ($numNew -eq $numCur -and $Current.Contains('-') -and -not $New.Contains('-')) { return }
    throw "Version '$New' is not newer than the current '$Current'. " +
          "Same-version pre-release reshuffles are not supported — bump X.Y.Z."
}

function Get-PropsVersionFromText {
    <#
    .SYNOPSIS
      Extracts the single project-property <Version> value from props-file text.
    .DESCRIPTION
      Matches only the ELEMENT form <Version>…</Version>, so Version="…" attributes (e.g. on a
      <PackageReference>) are never confused for it. Throws on zero or multiple matches — an
      ambiguous file must stop the cut, not silently pick one.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $found = [regex]::Matches($Text, '(?<=<Version>)[^<]*(?=</Version>)')
    if ($found.Count -eq 0) { throw 'No <Version> element found in the props text.' }
    if ($found.Count -gt 1) { throw "Found $($found.Count) <Version> elements — ambiguous; refusing to choose." }
    $found[0].Value.Trim()
}

function Update-PropsVersion {
    <#
    .SYNOPSIS
      Rewrites the single <Version> element in a props file in place; returns the old version.
    .DESCRIPTION
      Textual splice, never [xml] round-tripping — loading and re-saving the XML would reformat
      the whole file (comments, indentation, attribute quoting) and turn a one-line release bump
      into an unreviewable diff. Only the element form is touched, so package Version="…"
      attributes are safe. Same zero/multiple guards as Get-PropsVersionFromText.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PropsPath,
        [Parameter(Mandatory)][string]$NewVersion
    )
    if (-not (Test-ReleaseVersionFormat -Version $NewVersion)) {
        throw "Not a valid release version: '$NewVersion' (expected X.Y.Z or X.Y.Z-suffix)."
    }
    $text = [System.IO.File]::ReadAllText($PropsPath)
    $old = Get-PropsVersionFromText -Text $text   # also enforces exactly-one <Version>
    $m = [regex]::Match($text, '(?<=<Version>)[^<]*(?=</Version>)')
    $newText = $text.Substring(0, $m.Index) + $NewVersion + $text.Substring($m.Index + $m.Length)
    [System.IO.File]::WriteAllText($PropsPath, $newText)   # UTF-8, no BOM — matches the repo
    $old
}
