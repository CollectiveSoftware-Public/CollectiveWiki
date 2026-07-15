# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script = "$PSScriptRoot/../assert-version.ps1"
$script:Failures = 0
function Assert-Exit($Expected, $Tag, $Props, $What) {
    pwsh -NoProfile -File $script -Tag $Tag -PropsPath $Props *> $null
    $code = $LASTEXITCODE
    if ($code -eq $Expected) { Write-Host "  PASS  $What" }
    else { Write-Host "  FAIL  $What (expected exit $Expected, got $code)"; $script:Failures++ }
}
# Exit code alone can't tell a graceful, intended failure apart from a crash
# that happens to also return 1 (Set-StrictMode + $ErrorActionPreference=Stop
# turns an uncaught PropertyNotFoundException into exit 1 too). Capture output
# and check for the real failure signature, not just the process exit code.
function Assert-Output($Expected, $Tag, $Props, $MustContain, $MustNotContain, $What) {
    $out = & pwsh -NoProfile -File $script -Tag $Tag -PropsPath $Props 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ok = ($code -eq $Expected) -and ($out -like "*$MustContain*") -and ($out -notlike "*$MustNotContain*")
    if ($ok) { Write-Host "  PASS  $What" }
    else {
        Write-Host "  FAIL  $What (exit $code, expected $Expected)"
        Write-Host "        output: $out"
        $script:Failures++
    }
}

$dir = Join-Path ([System.IO.Path]::GetTempPath()) ("av-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $dir | Out-Null
$good = Join-Path $dir 'good.props'
@'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $good -Encoding utf8

$noversion = Join-Path $dir 'noversion.props'
@'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $noversion -Encoding utf8

# Shaped like the real Directory.Build.props after Task 3: TWO <PropertyGroup>
# elements, and <Version> lives only in one of them. Under Set-StrictMode,
# naive `$_.Version` dot-access throws when a PropertyGroup lacks the element
# — this fixture is what exposed that bug during Task 2 development.
$multi = Join-Path $dir 'multi.props'
@'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <PropertyGroup>
    <Version>2.3.4</Version>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath $multi -Encoding utf8

# A props file with zero <PropertyGroup> elements at all — the shape of an
# import-only / <ItemGroup>-only props file. Naive `$xml.Project.PropertyGroup`
# dot-access throws under Set-StrictMode when there's no PropertyGroup child at
# all (distinct from $multi/$noversion, which each have >=1 PropertyGroup but one
# lacking <Version>) — this fixture is what exposed that bug during Task 2 review.
$nopg = Join-Path $dir 'nopg.props'
@'
<Project>
  <ItemGroup>
    <PackageReference Include="Foo" Version="9.9.9" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $nopg -Encoding utf8

Assert-Exit 0 'v1.0.0'  $good      'matching tag passes'
Assert-Exit 1 'v1.1.0'  $good      'MISMATCHED tag fails'
Assert-Exit 1 '1.0.0'   $good      'tag without v prefix fails'
Assert-Exit 1 'v1.0.0'  $noversion 'missing <Version> fails'
Assert-Exit 1 'v1.0.0'  (Join-Path $dir 'nope.props') 'missing props file fails'
Assert-Exit 1 'V1.0.0'  $good      'case-mismatched tag (V vs v) fails'
Assert-Exit 0 'v2.3.4'  $multi     'matching tag passes w/ multiple PropertyGroups, Version in 2nd'

Assert-Output 1 'v1.0.0' $noversion `
    -MustContain 'no <Version> element' -MustNotContain 'PropertyNotFoundException' `
    -What 'missing <Version> fails gracefully (no strict-mode crash)'
Assert-Output 0 'v2.3.4' $multi `
    -MustContain 'matches' -MustNotContain 'PropertyNotFoundException' `
    -What 'multi-PropertyGroup props parse without a strict-mode crash'
# Exit code alone can't distinguish this script's own "empty tag doesn't match"
# message from the PowerShell binder's "Cannot bind argument... because it is an
# empty string" stack trace — both happen to exit 1 (Mandatory alone rejects an
# empty string before the script body runs). Assert on the actual output so this
# test can't pass vacuously without exercising the version-comparison logic.
Assert-Output 1 '' $good `
    -MustContain "does not match" -MustNotContain 'Cannot bind argument' `
    -What 'empty tag fails via the comparison logic, not the parameter binder'
Assert-Output 1 'v1.0.0' $nopg `
    -MustContain 'no <Version> element' -MustNotContain 'cannot be found on this object' `
    -What 'props file with zero <PropertyGroup> elements fails gracefully (no strict-mode crash)'

Remove-Item $dir -Recurse -Force
if ($script:Failures -gt 0) { Write-Host "`n$($script:Failures) FAILED"; exit 1 }
Write-Host "`nAll version-guard tests passed"; exit 0
