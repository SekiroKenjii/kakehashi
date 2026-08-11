#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verifies that every pointer from source code into docs/ still resolves.

.DESCRIPTION
  Comments that explain a design live in docs/, and the code keeps a one-line pointer to them.
  That trade buys readable source and takes on one new failure mode: a heading gets renamed, the
  pointer keeps compiling, and the comment now sends the next reader nowhere. Nothing about the
  build would notice.

  This is the thing that notices. It collects every "<path>.md" and "<path>.md#anchor" mentioned
  in a source comment or in another document, resolves the file, and — for anchors — slugifies
  the target's headings the way GitHub does and checks the anchor is among them.

  Exits non-zero on the first unresolved pointer, so CI can gate on it.

.EXAMPLE
  pwsh tools/check-doc-links.ps1
#>
[CmdletBinding()]
param(
  # Repository root. Defaults to the parent of this script's folder.
  [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = [System.IO.Path]::GetFullPath($Root)

# Where pointers may appear. Source comments are the point of the exercise; documents are included
# because a document that links to a moved heading fails the reader exactly as badly.
$Patterns = @('client/src/*.cs', 'client/tests/*.cs', 'server/*.go', 'docs/*.md', 'client/docs/*.md', '*.md')

# GitHub builds an anchor by lower-casing the heading, dropping anything that is not a letter,
# digit, space or hyphen, then turning spaces into hyphens.
function ConvertTo-Anchor {
  param([string]$Heading)
  $s = $Heading.Trim().ToLowerInvariant()
  $s = $s -replace '`', ''
  $s = $s -replace '\[([^\]]*)\]\([^)]*\)', '$1'   # a linked heading anchors on its text
  $s = $s -replace '[^a-z0-9 \-]', ''
  return ($s.Trim() -replace ' +', '-')
}

$anchorCache = @{}

function Get-Anchors {
  param([string]$MarkdownPath)
  if ($anchorCache.ContainsKey($MarkdownPath)) { return $anchorCache[$MarkdownPath] }
  $set = [System.Collections.Generic.HashSet[string]]::new()
  $fenced = $false
  foreach ($line in [System.IO.File]::ReadAllLines($MarkdownPath)) {
    if ($line -match '^\s*```') { $fenced = -not $fenced; continue }
    if ($fenced) { continue }
    if ($line -match '^(#{1,6})\s+(.*)$') { [void]$set.Add((ConvertTo-Anchor $Matches[2])) }
  }
  $anchorCache[$MarkdownPath] = $set
  return $set
}

Push-Location $Root
try {
  # --others picks up files that exist but are not committed yet. Without it a document added in
  # the same change that points at it is invisible to this check, which is exactly when a pointer
  # is most likely to be wrong.
  $files = @(git ls-files --cached --others --exclude-standard -- @Patterns |
      Where-Object { $_ -notmatch '/gen/' } | Sort-Object -Unique)
} finally {
  Pop-Location
}

$checked = 0
$failures = @()

foreach ($relative in $files) {
  $full = Join-Path $Root $relative
  $lineNumber = 0
  foreach ($line in [System.IO.File]::ReadAllLines($full)) {
    $lineNumber++
    foreach ($match in [regex]::Matches($line, '(?<path>(?:[\w.\-]+/)+[\w.\-]+\.md)(?<anchor>#[\w\-]+)?')) {
      $target = $match.Groups['path'].Value
      $checked++

      # Two conventions coexist and both are legitimate. Code pointers are written from the
      # repository root, because a comment is read in a file whose depth the reader is not
      # thinking about; links between documents are written relative to the document, because
      # that is what renders on GitHub. Resolve whichever one exists.
      $targetPath = $null
      foreach ($candidate in @(
          (Join-Path $Root $target),
          (Join-Path (Split-Path $full -Parent) $target))) {
        if (Test-Path $candidate) { $targetPath = $candidate; break }
      }

      if (-not $targetPath) {
        $failures += "${relative}:${lineNumber}  no such document: $target"
        continue
      }
      if (-not $match.Groups['anchor'].Success) { continue }

      $anchor = $match.Groups['anchor'].Value.TrimStart('#')
      $anchors = Get-Anchors -MarkdownPath $targetPath
      if (-not $anchors.Contains($anchor)) {
        $failures += "${relative}:${lineNumber}  $target has no heading '#$anchor'"
      }
    }
  }
}

Write-Host ("Checked {0} document pointers across {1} files." -f $checked, $files.Count)

if ($failures.Count -gt 0) {
  Write-Host ""
  Write-Host ("Unresolved pointers: {0}" -f $failures.Count)
  foreach ($f in $failures) { Write-Host ("  $f") }
  exit 1
}

Write-Host "Every pointer resolves."
