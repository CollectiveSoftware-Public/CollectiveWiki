# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  One-command release cut: version-bump PR on the release repo, merged, tagged — CI does the rest.
.DESCRIPTION
  Automates the manual half of a release EXCEPT signing (deliberate — signing authority stays on
  the maintainer's machine; see sign-release.ps1):

    1. branch release-vX.Y.Z off <Remote>/main in a TEMPORARY worktree — your checkout, branch,
       and any uncommitted work are untouched
    2. bump Directory.Build.props <Version> and commit
    3. push the branch, open a PR on $Repo, and merge it with --admin (bypassing the PR-review
       requirement is intentional here: the human gate for a release is SIGNING, not a
       one-line version bump this script just generated)
    4. tag the merge commit vX.Y.Z and push the tag → .github/workflows/release.yml asserts the
       tag against the props version, builds win-x64 + linux-x64, and creates the DRAFT release
       with artifacts + unsigned manifest

  Then prints the one remaining manual step: ./build/sign-release.ps1 -Tag vX.Y.Z — publishing
  IS signing, so nothing this script does can put an update in front of users by itself.

  Fails closed before touching anything remote: bad version shape, version not newer than
  <Remote>/main's, tag or release branch already existing.
.EXAMPLE
  ./build/cut-release.ps1 -Version 1.2.0 -Note "direct internet sync + nav buttons"
#>
#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    # Short parenthetical for the bump commit, e.g. "in-app updater" -> "Release: bump Version to 1.2.0 (in-app updater)".
    [string]$Note,
    [string]$Repo = 'CollectiveSoftware-Public/CollectiveWiki',
    [string]$Remote = 'public'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/lib/ReleaseCut.ps1"

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $out = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed:`n$($out | Out-String)" }
    $out
}

if (-not (Test-ReleaseVersionFormat -Version $Version)) {
    throw "Not a valid release version: '$Version' (expected X.Y.Z or X.Y.Z-suffix, no leading v)."
}
$tag = "v$Version"
$branch = "release-$tag"
$root = (Invoke-Git @('-C', $PSScriptRoot, 'rev-parse', '--show-toplevel') | Select-Object -First 1).Trim()

Write-Host "==> Fetching $Remote"
Invoke-Git @('-C', $root, 'fetch', $Remote) | Out-Null
$base = (Invoke-Git @('-C', $root, 'rev-parse', "refs/remotes/$Remote/main") | Select-Object -First 1).Trim()

Write-Host "==> Preflight against $Remote/main ($($base.Substring(0,7)))"
$propsAtBase = (Invoke-Git @('-C', $root, 'show', "${base}:Directory.Build.props")) -join "`n"
$current = Get-PropsVersionFromText -Text $propsAtBase
Assert-ReleaseVersionIsNewer -New $Version -Current $current
Write-Host "    $current -> $Version"

if (@(Invoke-Git @('-C', $root, 'ls-remote', '--tags', $Remote, "refs/tags/$tag")).Count -gt 0) {
    throw "Tag $tag already exists on $Remote. A published version is immutable — pick the next version."
}
if (@(Invoke-Git @('-C', $root, 'tag', '--list', $tag)).Count -gt 0) {
    throw "Tag $tag already exists locally. Delete it (git tag -d $tag) only if you are certain it never shipped."
}
if (@(Invoke-Git @('-C', $root, 'ls-remote', '--heads', $Remote, "refs/heads/$branch")).Count -gt 0) {
    throw "Branch $branch already exists on $Remote — is another cut of $tag in flight?"
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "cw-cut-$tag-$([Guid]::NewGuid())"
Write-Host "==> Preparing the bump in a temporary worktree ($work)"
Invoke-Git @('-C', $root, 'worktree', 'add', '--detach', $work, $base) | Out-Null
try {
    Invoke-Git @('-C', $work, 'switch', '-c', $branch) | Out-Null
    $old = Update-PropsVersion -PropsPath (Join-Path $work 'Directory.Build.props') -NewVersion $Version
    $msg = "Release: bump Version to $Version" + $(if ($Note) { " ($Note)" } else { '' })
    Invoke-Git @('-C', $work, 'add', 'Directory.Build.props') | Out-Null
    Invoke-Git @('-C', $work, 'commit', '-m', $msg) | Out-Null
    Write-Host "    committed: $msg (was $old)"

    Write-Host "==> Pushing $branch and opening the PR on $Repo"
    Invoke-Git @('-C', $work, 'push', $Remote, "HEAD:refs/heads/$branch") | Out-Null
    $prUrl = (& gh pr create -R $Repo --base main --head $branch --title "Release $tag" --body $msg)
    if ($LASTEXITCODE -ne 0) { throw 'gh pr create failed' }
    Write-Host "    $prUrl"

    Write-Host '==> Merging the release PR (--admin: the human release gate is signing, not this bump)'
    & gh pr merge -R $Repo $branch --merge --admin --delete-branch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "gh pr merge failed — merge $prUrl yourself, then tag its merge commit $tag and push the tag." }

    Write-Host "==> Tagging the merge commit and pushing $tag"
    Invoke-Git @('-C', $root, 'fetch', $Remote) | Out-Null
    $mergeSha = (& gh pr view $prUrl -R $Repo --json mergeCommit --jq '.mergeCommit.oid')
    if ($LASTEXITCODE -ne 0 -or -not $mergeSha) { throw "Could not resolve the merge commit of $prUrl — tag it manually: git tag $tag <sha> && git push $Remote $tag" }
    # Belt + braces: the tag must never point at a commit whose props disagree with it.
    $propsAtMerge = (Invoke-Git @('-C', $root, 'show', "${mergeSha}:Directory.Build.props")) -join "`n"
    if ((Get-PropsVersionFromText -Text $propsAtMerge) -cne $Version) {
        throw "Merge commit $mergeSha does not carry <Version>$Version</Version> — did something else land on main mid-cut? Not tagging."
    }
    Invoke-Git @('-C', $root, 'tag', $tag, $mergeSha) | Out-Null
    Invoke-Git @('-C', $root, 'push', $Remote, $tag) | Out-Null
} finally {
    Invoke-Git @('-C', $root, 'worktree', 'remove', '--force', $work) | Out-Null
}

Write-Host ''
Write-Host "Cut $tag — CI is building the draft release now."
Write-Host "  watch:   gh run list -R $Repo --workflow=release"
Write-Host "  then:    ./build/sign-release.ps1 -Tag $tag     # sign + publish (the only remaining manual step)"
Write-Host "  note:    the Internal repo's main is now behind $Remote/main — sync it via the usual internal flow."
