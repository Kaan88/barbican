<#
.SYNOPSIS
  Reads the CHANGELOG.md entry of one version (the release workflow's source for the GitHub release
  body, Hub's one-line note and the release-notes-<version>.md asset). Runs locally too.

.EXAMPLE
  pwsh tools/changelog-notes.ps1 -Version 1.2.0                       # prints the entry
  pwsh tools/changelog-notes.ps1 -Version 1.2.0 -Out dist/release-notes-1.2.0.md

  An entry is the text under a heading "## <version>" or "## <version> - <date>" (a leading v and
  [brackets] are accepted) up to the next "## " heading. "## Unreleased" is never an entry: the
  release is blocked until it has been renamed to the version. Exits 1, writing nothing, when the
  version has no entry, the entry is empty or it is over 64 KiB (Hub's limit for notes.file).

  Outputs an object: Body (the entry without its heading) and OneLine (its first line that is not
  a heading, without the list marker: the note Hub shows; write-hub-txt.ps1 cuts it to 1024 bytes).
  With -Out the body is also written there as UTF-8 without a BOM.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'

function Fail([string]$m) { [Console]::Error.WriteLine("changelog-notes: $m"); exit 1 }

$v = $Version.Trim().TrimStart('v')
if ($v -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$') { Fail "version '$Version' is not X.Y.Z[-pre]" }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "$Path does not exist" }

$heading = '^##\s+\[?v?' + [regex]::Escape($v) + '\]?(\s+[' + [char]0x2014 + [char]0x2013 + '-]\s+.*)?\s*$'
$body = New-Object System.Collections.Generic.List[string]
$found = $false
foreach ($line in [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)) {
    if ($found) {
        if ($line -match '^##\s') { break }
        $body.Add($line)
    } elseif ($line -match $heading) {
        $found = $true
    }
}
if (-not $found) { Fail "CHANGELOG.md has no entry for $v (add '## $v - <date>', or rename '## Unreleased')" }

# trim blank lines at both ends
while ($body.Count -gt 0 -and $body[0].Trim() -eq '') { $body.RemoveAt(0) }
while ($body.Count -gt 0 -and $body[$body.Count - 1].Trim() -eq '') { $body.RemoveAt($body.Count - 1) }
if ($body.Count -eq 0) { Fail "the entry for $v is empty" }
$text = ($body -join "`n") + "`n"
if ([Text.Encoding]::UTF8.GetByteCount($text) -gt 65536) { Fail "the entry for $v is over 64 KiB" }

$first = $body | Where-Object { $_.Trim() -ne '' -and $_ -notmatch '^\s*#' } | Select-Object -First 1
if (-not $first) { Fail "the entry for $v has no text line" }
$one = ($first.Trim() -replace '^([-*+]\s+)', '' -replace '[\p{Cc}]+', ' ').Trim()

if ($Out -ne '') {
    $full = [IO.Path]::GetFullPath($Out)
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [IO.File]::WriteAllText($full, $text, (New-Object System.Text.UTF8Encoding($false)))
}
[pscustomobject]@{ Body = $text; OneLine = $one }
