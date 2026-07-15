# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Generates the CollectiveWiki release signing keypair. Run ONCE, by a human.
.DESCRIPTION
  Writes the private key OUTSIDE any repo and prints the public key for embedding.
  Refuses to overwrite an existing key: losing the key means no existing install can
  ever be updated again (auto-update spec R2).
#>
#requires -Version 7
[CmdletBinding()]
param(
    [string]$KeyPath = (Join-Path $env:USERPROFILE '.collective/keys/collectivewiki-release.key.b64')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/lib/Signing.ps1"

if (Test-Path -LiteralPath $KeyPath) {
    Write-Host "REFUSING: a key already exists at $KeyPath"
    Write-Host "Overwriting it would permanently orphan every installed copy. Delete it deliberately if you truly mean to."
    exit 1
}

$dir = Split-Path -Parent $KeyPath
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$kp = New-SigningKeyPair
[System.IO.File]::WriteAllText($KeyPath, $kp.PrivateKeyBase64, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText("$KeyPath.pub", $kp.PublicKeyBase64, [System.Text.UTF8Encoding]::new($false))

if ($IsWindows) {
    # Owner-only ACL: strip inheritance, grant the current user alone.
    $acl = Get-Acl -LiteralPath $KeyPath
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { [void]$acl.RemoveAccessRule($_) }
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $me, 'FullControl', 'Allow'))
    Set-Acl -LiteralPath $KeyPath -AclObject $acl
} else {
    chmod 600 $KeyPath
}

Write-Host "Private key : $KeyPath   (NEVER commit; back this up NOW)"
Write-Host "Public key  : $KeyPath.pub"
Write-Host ''
Write-Host 'PUBLIC KEY (embed this in the app in Plan 2):'
Write-Host $kp.PublicKeyBase64
Write-Host ''
Write-Host 'BACK UP THE PRIVATE KEY BEFORE RELEASING:'
Write-Host '  1. Store it in CollectiveVault.'
Write-Host '  2. Keep a second offline copy.'
Write-Host 'If it is lost, no existing install can ever be auto-updated again.'
