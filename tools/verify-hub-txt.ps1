<#
.SYNOPSIS
  Checks a hub.txt against the release files it names: every file (and the notes file) must exist
  in -Dir under the asset name its URL gives, with the SHA-256 and size the manifest states.
  The release workflow runs it after tools/write-hub-txt.ps1, on the folder it uploads.
    pwsh tools/verify-hub-txt.ps1 -HubTxt dist/hub.txt -Dir dist
  Exits 1 and says which entry is wrong.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HubTxt, [Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'
function Fail([string]$m) { [Console]::Error.WriteLine("verify-hub-txt: $m"); exit 1 }
$kv = [ordered]@{}
foreach ($l in [IO.File]::ReadAllLines($HubTxt)) {
    if ($l -eq '' -or $l.StartsWith('#')) { continue }
    $i = $l.IndexOf(': ')
    if ($i -lt 1) { Fail "line '$l' is not key: value" }
    $kv[$l.Substring(0, $i)] = $l.Substring($i + 2)
}
if ($kv['hub'] -ne '1') { Fail 'first key is not hub: 1' }
$n = 0
foreach ($k in @($kv.Keys | Where-Object { $_ -like '*.sha256' })) {
    $base = $k.Substring(0, $k.Length - '.sha256'.Length)
    $url = $kv["$base.url"]
    if (-not $url) { Fail "$base has no url" }
    $asset = $url.Substring($url.LastIndexOf('/') + 1)
    $p = Join-Path $Dir $asset
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { Fail "$base names $asset, which is not in $Dir" }
    $sha = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sha -ne $kv[$k]) { Fail "${asset}: sha256 is $sha, hub.txt says $($kv[$k])" }
    if ([string](Get-Item -LiteralPath $p).Length -ne $kv["$base.size"]) { Fail "${asset}: size differs from hub.txt" }
    $n++
}
if ($n -eq 0) { Fail 'hub.txt names no file' }
if ($kv['notes.file'] -and -not (Test-Path -LiteralPath (Join-Path $Dir $kv['notes.file']) -PathType Leaf)) { Fail "notes.file $($kv['notes.file']) is not in $Dir" }
Write-Host "verify-hub-txt: $n file(s) match"
