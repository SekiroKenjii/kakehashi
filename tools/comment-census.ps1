#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Measures comment volume across both halves of the repository, and extracts every sentence the
  comments state so a cleanup can prove what it lost.

.DESCRIPTION
  Two jobs, and the second is the important one.

  The census counts lines: how much of each area is comment, how much of that is prose, and how
  much is XML ceremony carrying no words at all.

  The corpus is the safety net. It reduces every comment to the set of sentences it asserts,
  normalised so that re-wrapping a paragraph or changing its punctuation does not read as a
  change. Take a corpus before a cleanup, take another after, and -Diff names every sentence that
  stopped being asserted anywhere in the source. Each one then has to be accounted for: deleted on
  purpose, or moved somewhere that still says it. A cleanup without this is a cleanup that cannot
  tell deletion from loss.

.EXAMPLE
  pwsh tools/comment-census.ps1 -Out baseline.txt
  pwsh tools/comment-census.ps1 -Diff baseline.txt
#>
[CmdletBinding()]
param(
  # Repository root. Defaults to the parent of this script's folder.
  [string]$Root = (Split-Path $PSScriptRoot -Parent),

  # Write the prose corpus here.
  [string]$Out,

  # Compare the current corpus against one written earlier, and report what is no longer asserted.
  [string]$Diff
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = [System.IO.Path]::GetFullPath($Root)

# The areas measured separately, and how a comment line looks in each.
$Areas = @(
  @{ Name = 'client/src   (C#)'; Prefix = 'client/src/'; Ext = '.cs'; Marker = '^\s*//' }
  @{ Name = 'client/tests (C#)'; Prefix = 'client/tests/'; Ext = '.cs'; Marker = '^\s*//' }
  @{ Name = 'server       (Go)'; Prefix = 'server/'; Ext = '.go'; Marker = '^\s*//' }
)

# Asking git for the file list is what keeps obj/, bin/ and every other build artefact out without
# maintaining a second exclusion list that can drift from .gitignore. --others includes files not
# committed yet, so a census taken mid-change still sees everything.
function Get-SourceFiles {
  param([string]$Prefix, [string]$Ext)
  Push-Location $Root
  try {
    git ls-files --cached --others --exclude-standard -- "$Prefix*$Ext" |
      Where-Object { $_ -notmatch '/gen/' } |
      ForEach-Object { Join-Path $Root $_ }
  } finally {
    Pop-Location
  }
}

# Splits a file into runs of consecutive comment lines. Sentences wrap across lines, so anything
# that compares line by line reports a re-wrap as a rewrite.
function Get-CommentBlocks {
  param([string[]]$Lines, [string]$Marker)
  $blocks = [System.Collections.Generic.List[string]]::new()
  $current = [System.Collections.Generic.List[string]]::new()
  foreach ($line in $Lines) {
    if ($line -match $Marker) {
      $current.Add(($line -replace '^\s*///?/?', '').Trim())
    } elseif ($current.Count -gt 0) {
      $blocks.Add(($current -join ' '))
      $current.Clear()
    }
  }
  if ($current.Count -gt 0) { $blocks.Add(($current -join ' ')) }
  return $blocks
}

# One comment block becomes the set of claims it makes. Normalisation is deliberately blunt:
# case, punctuation and whitespace all collapse, because a cleanup that re-wraps a paragraph or
# swaps an em dash for a comma has not lost anything and should not be reported as if it had.
function Get-Sentences {
  param([string]$Block)
  $text = $Block -replace '<[^>]+>', ' '          # XML doc tags carry no claim
  $text = $text -replace '\s+', ' '
  $out = [System.Collections.Generic.List[string]]::new()
  foreach ($raw in ($text -split '(?<=[.!?])\s+')) {
    $s = ($raw.ToLowerInvariant() -replace '[^a-z0-9]+', ' ').Trim()
    # Fragments are noise in a diff: a bare tag remnant or a two-word label matches too much.
    if (($s -split ' ').Count -ge 4) { $out.Add($s) }
  }
  return $out
}

$corpus = [System.Collections.Generic.HashSet[string]]::new()
$rows = @()

foreach ($area in $Areas) {
  $files = @(Get-SourceFiles -Prefix $area.Prefix -Ext $area.Ext)
  $total = 0
  $comment = 0
  $ceremony = 0

  foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file)
    $total += $lines.Count
    foreach ($line in $lines) {
      if ($line -notmatch $area.Marker) { continue }
      $comment++
      # A line whose whole content is an XML tag pair states nothing. These are the lines a
      # cleanup can take without reading them.
      if ($line -match '^\s*///\s*</?(summary|remarks|para|list|item|description|code|returns|value|example)>\s*$') {
        $ceremony++
      }
    }
    foreach ($block in (Get-CommentBlocks -Lines $lines -Marker $area.Marker)) {
      foreach ($sentence in (Get-Sentences -Block $block)) { [void]$corpus.Add($sentence) }
    }
  }

  $rows += [pscustomobject]@{
    Area     = $area.Name
    Files    = $files.Count
    Lines    = $total
    Comment  = $comment
    Percent  = if ($total -gt 0) { [Math]::Round($comment * 100.0 / $total, 1) } else { 0 }
    Ceremony = $ceremony
  }
}

$rows | Format-Table -AutoSize @(
  @{ L = 'Area'; E = 'Area' }
  @{ L = 'Files'; E = 'Files'; A = 'right' }
  @{ L = 'Lines'; E = 'Lines'; A = 'right' }
  @{ L = 'Comment'; E = 'Comment'; A = 'right' }
  @{ L = '%'; E = 'Percent'; A = 'right' }
  @{ L = 'XML ceremony'; E = 'Ceremony'; A = 'right' }
) | Out-String | Write-Host

Write-Host ("Distinct claims asserted by comments: {0}" -f $corpus.Count)

$sorted = $corpus | Sort-Object

if ($Out) {
  $path = [System.IO.Path]::GetFullPath($Out)
  [System.IO.File]::WriteAllLines($path, $sorted)
  Write-Host ("Corpus written: {0}" -f $path)
}

if ($Diff) {
  $path = [System.IO.Path]::GetFullPath($Diff)
  if (-not (Test-Path $path)) { throw "No corpus to compare against at $path" }
  $before = [System.Collections.Generic.HashSet[string]]::new(
    [string[]][System.IO.File]::ReadAllLines($path))

  $gone = @($before | Where-Object { -not $corpus.Contains($_) })
  $added = @($sorted | Where-Object { -not $before.Contains($_) })

  Write-Host ""
  Write-Host ("No longer asserted anywhere in source: {0}" -f $gone.Count)
  foreach ($s in $gone) { Write-Host ("  - {0}" -f $s) }
  Write-Host ""
  Write-Host ("Newly asserted: {0}" -f $added.Count)
  foreach ($s in $added) { Write-Host ("  + {0}" -f $s) }
  Write-Host ""
  Write-Host "Every line above must be accounted for: deleted on purpose, or moved into docs/."
}
