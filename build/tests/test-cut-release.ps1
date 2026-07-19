# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/../lib/ReleaseCut.ps1"

$script:Failures = 0
function Assert-True($Cond, $What) {
    if ($Cond) { Write-Host "  PASS  $What" }
    else { Write-Host "  FAIL  $What"; $script:Failures++ }
}
function Assert-Throws([scriptblock]$Block, $MustContain, $What) {
    try { & $Block; Write-Host "  FAIL  $What (did not throw)"; $script:Failures++ }
    catch {
        if ("$_" -like "*$MustContain*") { Write-Host "  PASS  $What" }
        else { Write-Host "  FAIL  $What (wrong error: $_)"; $script:Failures++ }
    }
}

Write-Host 'Test-ReleaseVersionFormat'
Assert-True (Test-ReleaseVersionFormat -Version '1.2.0')        'accepts 1.2.0'
Assert-True (Test-ReleaseVersionFormat -Version '0.0.1')        'accepts 0.0.1'
Assert-True (Test-ReleaseVersionFormat -Version '1.2.0-rc.1')   'accepts a pre-release suffix'
Assert-True (-not (Test-ReleaseVersionFormat -Version ''))          'rejects empty'
Assert-True (-not (Test-ReleaseVersionFormat -Version 'v1.2.0'))    'rejects a leading v (the tag adds it)'
Assert-True (-not (Test-ReleaseVersionFormat -Version '1.2'))       'rejects two-part versions'
Assert-True (-not (Test-ReleaseVersionFormat -Version '1.2.0+m'))   'rejects build metadata'
Assert-True (-not (Test-ReleaseVersionFormat -Version '1.2.0-'))    'rejects an empty suffix'
Assert-True (-not (Test-ReleaseVersionFormat -Version '1.2.0 '))    'rejects trailing whitespace'

Write-Host 'Assert-ReleaseVersionIsNewer'
Assert-ReleaseVersionIsNewer -New '1.2.0' -Current '1.1.1'
Write-Host '  PASS  1.2.0 is newer than 1.1.1'
Assert-ReleaseVersionIsNewer -New '1.1.10' -Current '1.1.9'
Write-Host '  PASS  1.1.10 is newer than 1.1.9 (numeric, not lexicographic)'
Assert-ReleaseVersionIsNewer -New '1.2.0' -Current '1.2.0-rc.1'
Write-Host '  PASS  pre-release -> final at the same X.Y.Z is allowed'
Assert-ReleaseVersionIsNewer -New '2.0.0-rc.1' -Current '1.9.9'
Write-Host '  PASS  a pre-release of a HIGHER X.Y.Z is allowed'
Assert-Throws { Assert-ReleaseVersionIsNewer -New '1.1.1' -Current '1.1.1' } 'not newer'   'same version throws'
Assert-Throws { Assert-ReleaseVersionIsNewer -New '1.1.0' -Current '1.1.1' } 'not newer'   'downgrade throws'
Assert-Throws { Assert-ReleaseVersionIsNewer -New '1.2.0-rc.2' -Current '1.2.0-rc.1' } 'not newer' 'pre-release reshuffle at the same X.Y.Z throws (deliberately unmodeled)'
Assert-Throws { Assert-ReleaseVersionIsNewer -New '1.2.0-rc.1' -Current '1.2.0' } 'not newer' 'final -> pre-release throws'
Assert-Throws { Assert-ReleaseVersionIsNewer -New 'nope' -Current '1.1.1' } 'valid release version' 'garbage new version throws'

# Shaped like the real Directory.Build.props: SPDX comment, several properties, and — the trap —
# a Version="…" ATTRIBUTE inside an ItemGroup that a sloppy regex would also rewrite.
$fixture = @'
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- The release tag asserts this value (build/assert-version.ps1); bump here, then tag vX.Y.Z. -->
    <Version>1.1.1</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Foo" Version="9.9.9" />
  </ItemGroup>
</Project>
'@

Write-Host 'Get-PropsVersionFromText'
Assert-True ((Get-PropsVersionFromText -Text $fixture) -ceq '1.1.1') 'reads the element, not the attribute'
Assert-Throws { Get-PropsVersionFromText -Text '<Project></Project>' } 'No <Version> element' 'zero elements throws'
Assert-Throws { Get-PropsVersionFromText -Text '<a><Version>1.0.0</Version><Version>2.0.0</Version></a>' } 'ambiguous' 'multiple elements throw'

Write-Host 'Update-PropsVersion'
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ("cr-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $props = Join-Path $dir 'Directory.Build.props'
    [System.IO.File]::WriteAllText($props, $fixture)

    $old = Update-PropsVersion -PropsPath $props -NewVersion '1.2.0'
    $after = [System.IO.File]::ReadAllText($props)
    Assert-True ($old -ceq '1.1.1')                              'returns the previous version'
    Assert-True ($after.Contains('<Version>1.2.0</Version>'))    'rewrites the element'
    Assert-True ($after.Contains('Version="9.9.9"'))             'leaves the PackageReference attribute alone'
    Assert-True ($after -ceq $fixture.Replace('<Version>1.1.1</Version>', '<Version>1.2.0</Version>')) `
        'changes NOTHING except the version (byte-for-byte, comments and formatting intact)'

    Assert-Throws { Update-PropsVersion -PropsPath $props -NewVersion 'v1.3.0' } 'valid release version' 'invalid new version throws before touching the file'
    Assert-True (([System.IO.File]::ReadAllText($props)) -ceq $after) 'a rejected update leaves the file untouched'
} finally {
    Remove-Item $dir -Recurse -Force
}

if ($script:Failures -gt 0) { Write-Host "`n$($script:Failures) FAILED"; exit 1 }
Write-Host "`nAll cut-release tests passed"; exit 0
