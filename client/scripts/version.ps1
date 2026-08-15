#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Reads and updates the product version (the single source of truth in Version.props).

.DESCRIPTION
  Version.props holds VersionMajor/Minor/Patch; Directory.Build.props derives every assembly's
  version from it. This script bumps or sets that version, keeps the MSIX manifest
  (Package.appxmanifest <Identity Version>) in sync, and can create the matching annotated git tag.

  With no arguments it prints the current version and exits (useful in CI / release notes).

.PARAMETER Bump
  Which SemVer part to increment: major (X+1.0.0), minor (x.Y+1.0) or patch (x.y.Z+1).

.PARAMETER Set
  Set an explicit version "X.Y.Z" (mutually exclusive with -Bump).

.PARAMETER Tag
  After updating, create an annotated git tag vX.Y.Z at HEAD. Fails if the tag already exists.

.PARAMETER Commit
  Stage and commit the version files before tagging (recommended so the tag points at the bump).

.EXAMPLE
  pwsh scripts/version.ps1
  pwsh scripts/version.ps1 -Bump patch
  pwsh scripts/version.ps1 -Bump minor -Commit -Tag
  pwsh scripts/version.ps1 -Set 1.0.0 -Commit -Tag
#>
[CmdletBinding(DefaultParameterSetName = 'Show')]
param(
  [Parameter(ParameterSetName = 'Bump', Mandatory)]
  [ValidateSet('major', 'minor', 'patch')]
  [string]$Bump,

  [Parameter(ParameterSetName = 'Set', Mandatory)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$Set,

  [switch]$Tag,
  [switch]$Commit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionPropsPath = Join-Path $repoRoot 'Version.props'
$manifestPath = Join-Path $repoRoot 'src/App/__APP_NAME__.App/Package.appxmanifest'

function Get-CurrentVersion {
  [xml]$xml = Get-Content -LiteralPath $versionPropsPath -Raw
  $pg = $xml.Project.PropertyGroup | Where-Object { $null -ne $_.VersionMajor } | Select-Object -First 1
  return [pscustomobject]@{
    Major = [int]$pg.VersionMajor
    Minor = [int]$pg.VersionMinor
    Patch = [int]$pg.VersionPatch
  }
}

function Format-Version([object]$v) { "$($v.Major).$($v.Minor).$($v.Patch)" }

$current = Get-CurrentVersion

if ($PSCmdlet.ParameterSetName -eq 'Show') {
  Write-Output (Format-Version $current)
  return
}

# Compute the target version.
if ($PSCmdlet.ParameterSetName -eq 'Bump') {
  switch ($Bump) {
    'major' { $next = [pscustomobject]@{ Major = $current.Major + 1; Minor = 0; Patch = 0 } }
    'minor' { $next = [pscustomobject]@{ Major = $current.Major; Minor = $current.Minor + 1; Patch = 0 } }
    'patch' { $next = [pscustomobject]@{ Major = $current.Major; Minor = $current.Minor; Patch = $current.Patch + 1 } }
  }
} else {
  $parts = $Set.Split('.')
  $next = [pscustomobject]@{ Major = [int]$parts[0]; Minor = [int]$parts[1]; Patch = [int]$parts[2] }
}

$nextStr = Format-Version $next
$tagName = "v$nextStr"
Write-Host "Version: $(Format-Version $current) -> $nextStr"

# 1) Update Version.props (preserve formatting via targeted regex replaces). The presence checks
#    guard against a malformed file; setting the current version again is a valid no-op.
$props = Get-Content -LiteralPath $versionPropsPath -Raw
foreach ($part in 'Major', 'Minor', 'Patch') {
  if (-not [regex]::IsMatch($props, "<Version$part>\d+</Version$part>")) {
    throw "Could not find <Version$part> in $versionPropsPath."
  }
}
$props = [regex]::Replace($props, '<VersionMajor>\d+</VersionMajor>', "<VersionMajor>$($next.Major)</VersionMajor>")
$props = [regex]::Replace($props, '<VersionMinor>\d+</VersionMinor>', "<VersionMinor>$($next.Minor)</VersionMinor>")
$props = [regex]::Replace($props, '<VersionPatch>\d+</VersionPatch>', "<VersionPatch>$($next.Patch)</VersionPatch>")
# Normalize to LF (repo is .editorconfig end_of_line = lf) and write without a BOM.
$props = $props -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($versionPropsPath, $props, (New-Object System.Text.UTF8Encoding $false))
Write-Host "  updated Version.props"

# 2) Sync the MSIX manifest <Identity> version (4-part: X.Y.Z.0). Scoped to the Identity element so
#    it never touches TargetDeviceFamily's MinVersion / MaxVersionTested.
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identityPattern = '(?s)(<Identity\b.*?\bVersion=")\d+\.\d+\.\d+\.\d+(")'
if (-not [regex]::IsMatch($manifest, $identityPattern)) {
  throw "Could not find <Identity ... Version> in $manifestPath."
}
$updated = [regex]::Replace($manifest, $identityPattern, "`${1}$nextStr.0`$2")
$updated = $updated -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($manifestPath, $updated, (New-Object System.Text.UTF8Encoding $false))
Write-Host "  updated Package.appxmanifest"

# 3) Optionally commit the version files.
if ($Commit) {
  & git -C $repoRoot add -- 'Version.props' 'src/App/__APP_NAME__.App/Package.appxmanifest'
  if ($LASTEXITCODE -ne 0) { throw "git add failed." }
  & git -C $repoRoot commit -m "Release $tagName" | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "git commit failed." }
  Write-Host "  committed 'Release $tagName'"
}

# 4) Optionally create the annotated tag.
if ($Tag) {
  $existing = & git -C $repoRoot tag --list $tagName
  if ($existing) { throw "Tag $tagName already exists." }
  & git -C $repoRoot tag -a $tagName -m "Release $tagName"
  if ($LASTEXITCODE -ne 0) { throw "git tag failed." }
  Write-Host "  created tag $tagName"
}

Write-Output $nextStr
