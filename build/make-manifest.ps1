# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Builds manifest.json for a release from staged artifacts. Run by CI; holds no secrets.
.EXAMPLE
  ./build/make-manifest.ps1 -Version 1.0.0 -Tag v1.0.0 `
      -Repo CollectiveSoftware-Public/CollectiveWiki -StagedDir staged -OutFile staged/manifest.json
#>
#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$StagedDir,
    [Parameter(Mandatory)][string]$OutFile,
    # Deliberately [string], not [datetime]. This CLI is invoked cross-process (pwsh -File),
    # where every argument round-trips through the OS command line as text. PowerShell's
    # built-in string->[datetime] parameter conversion does NOT preserve UTC intent: an
    # unmarked string binds as Kind=Unspecified, and a 'Z'-suffixed ISO string binds as
    # Kind=Local with the clock adjusted to the local zone (verified empirically — a
    # DateTimeKind.Utc value built in a parent script and passed as -PublishedUtc $dt to a
    # child `pwsh -File` process arrives with Kind stripped before this script's body ever
    # runs, same failure class as the Task 1/2 parameter-binder defects). New-UpdateManifest
    # rejects non-Utc Kind, so a [datetime]-typed parameter here would make every explicit
    # -PublishedUtc call fail. Parsing the raw string ourselves below is the fix, not a
    # "helpful" reinterpretation: AdjustToUniversal correctly converts an explicit offset/Z
    # to UTC (no shift, since it's already UTC), so the resulting Kind=Utc value has the exact
    # wall-clock reading the caller intended. Deliberately NOT AssumeUniversal: that style
    # would silently treat an unmarked reading as already being UTC, indistinguishable from
    # someone's local time typed with no marker (a real, measured error baked into a signed
    # manifest is exactly what Task 1's DateTimeKind guard exists to prevent). Without
    # AssumeUniversal, an unmarked/ambiguous string parses to Kind=Unspecified and is rejected
    # by New-UpdateManifest's guard below, same as any other non-Utc Kind — reusing that
    # defense rather than re-deciding what "no offset" means here. A caller that wants to pass
    # a live [datetime] object across this process boundary must format it self-describingly
    # first (e.g. `.ToString('o')`): the implicit stringification done during cross-process
    # argument marshalling uses .NET's plain invariant "general" format, which drops Kind/offset
    # entirely regardless of the source value's Kind — only an explicit round-trip/offset
    # format survives the trip.
    [string]$PublishedUtc
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/lib/Signing.ps1"

# RID -> release asset filename. AssemblyName is CollectiveWiki, so linux has no extension.
$assets = [ordered]@{
    'win-x64'   = "CollectiveWiki-$Version-win-x64.exe"
    'linux-x64' = "CollectiveWiki-$Version-linux-x64"
}

try {
    $publishedUtcValue = if ([string]::IsNullOrWhiteSpace($PublishedUtc)) {
        [datetime]::UtcNow
    } else {
        [datetime]::Parse($PublishedUtc, [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AdjustToUniversal)
    }

    $artifacts = @(foreach ($rid in $assets.Keys) {
        $name = $assets[$rid]
        $path = Join-Path $StagedDir $name
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Missing artifact for '$rid': $path. Refusing to build a partial manifest."
        }
        @{ Rid = $rid; Path = $path; Url = "https://github.com/$Repo/releases/download/$Tag/$name" }
    })

    $json = New-UpdateManifest -Version $Version `
        -NotesUrl "https://github.com/$Repo/releases/tag/$Tag" `
        -PublishedUtc $publishedUtcValue -Artifacts $artifacts

    # UTF-8, no BOM, no trailing newline — the signature covers these exact bytes.
    [System.IO.File]::WriteAllText($OutFile, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $OutFile"
    Get-Content -LiteralPath $OutFile -Raw | Write-Host
    exit 0
} catch {
    Write-Host "ERROR: $_"
    exit 1
}
