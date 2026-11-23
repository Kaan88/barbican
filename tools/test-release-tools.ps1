<#
  Tests for tools/changelog-notes.ps1 and the way the release workflow calls
  tools/write-hub-txt.ps1. Plain asserts, no Pester: runs in PowerShell 7 and Windows PowerShell 5.1.
    pwsh tools/test-release-tools.ps1          powershell -File tools/test-release-tools.ps1
  Exits 1 on the first failure. Writes only to a scratch folder it deletes.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("barbican-tools-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
$script:fails = 0
function Check([bool]$ok, [string]$what) { if ($ok) { Write-Host "ok    $what" } else { Write-Host "FAIL  $what"; $script:fails++ } }
# runs a script in a child PowerShell of the same edition and returns its exit code
function Run-Child([string[]]$a) {
    $ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1 turns a child's stderr into an error otherwise
    $exe = (Get-Process -Id $PID).Path
    & $exe -NoProfile -File @a 2>$null | Out-Null
    $LASTEXITCODE
}
try {
    $notes = Join-Path $PSScriptRoot 'changelog-notes.ps1'
    $writer = Join-Path $PSScriptRoot 'write-hub-txt.ps1'

    # ---- changelog-notes
    $cl = Join-Path $scratch 'CHANGELOG.md'
    $text = @"
# Changelog

Intro text.

## Unreleased

- Not released yet.

## 1.2.0 $([char]0x2014) 2026-12-01

- Caf$([char]0xE9) and more: first line.

More text.
- second bullet

## 1.2.0-beta.1 - 2026-11-20

- The beta line.

## 1.1.0

Plain paragraph first.
"@
    [IO.File]::WriteAllText($cl, $text, (New-Object Text.UTF8Encoding($false)))
    $r = & $notes -Version v1.2.0 -Path $cl -Out (Join-Path $scratch 'out\release-notes-1.2.0.md')
    Check ($r.OneLine -eq "Caf$([char]0xE9) and more: first line.") 'one-line note is the first line without the bullet'
    Check ($r.Body -notmatch 'beta line' -and $r.Body -match 'second bullet' -and $r.Body -notmatch '^##') 'entry stops at the next heading and has no heading'
    Check (([IO.File]::ReadAllText((Join-Path $scratch 'out\release-notes-1.2.0.md'))) -eq $r.Body) 'the notes file is the body'
    Check ((& $notes -Version 1.2.0-beta.1 -Path $cl).OneLine -eq 'The beta line.') 'a pre-release finds its own entry, not the release'
    Check ((& $notes -Version 1.1.0 -Path $cl).OneLine -eq 'Plain paragraph first.') 'heading without a date'
    Check ((Run-Child @($notes, '-Version', '9.9.9', '-Path', $cl)) -eq 1) 'a version without an entry fails'
    Check ((Run-Child @($notes, '-Version', '1.2.1', '-Path', $cl)) -eq 1) 'Unreleased is not an entry for another version'
    Check ((Run-Child @($notes, '-Version', 'banana', '-Path', $cl)) -eq 1) 'a bad version fails'

    # ---- write-hub-txt, called as the workflow calls it (in-process, hashtable list)
    $setup = Join-Path $scratch 'Barbican-Setup-1.2.0.exe'
    $zip = Join-Path $scratch 'barbican-1.2.0-windows-x86_64.zip'
    [IO.File]::WriteAllBytes($setup, (1..5000 | ForEach-Object { [byte]($_ % 251) }))
    [IO.File]::WriteAllBytes($zip, [byte[]](1..10))
    $long = 'x' * 700 + ([string][char]0xE9) * 200   # 1100 bytes of UTF-8
    $files = @(@{ Name = 'Barbican-Setup-1.2.0.exe'; Path = $setup; Role = 'setup'; Silent = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'; Scope = 'user' })
    $out = Join-Path $scratch 'hub.txt'
    & $writer -App barbican -Repo barbican -Version 1.2.0 -Date 2026-12-01 -Notes $long -NotesFile 'release-notes-1.2.0.md' `
        -Kind setup -Exe barbican.exe -Uninstall '{ACE04D99-4564-4E70-9A43-FE47A6A0E3B5}_is1' -Wait barbican.exe -Files $files -Out $out | Out-Null
    $lines = [IO.File]::ReadAllText($out) -split "`n" | Where-Object { $_ -ne '' }
    $get = { param($k) ($lines | Where-Object { $_ -like "${k}: *" } | Select-Object -First 1) -replace '^[^:]+: ', '' }
    $sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Check ($lines[0] -eq 'hub: 1' -and (& $get 'app') -eq 'barbican') 'hub.txt starts with hub: 1 and the app id'
    Check ((& $get 'windows-x86_64.setup.sha256') -eq $sha -and (& $get 'windows-x86_64.setup.size') -eq '5000') 'sha256 and size are those of the file'
    Check ((& $get 'windows-x86_64.setup.url') -eq 'https://github.com/Kaan88/barbican/releases/download/v1.2.0/Barbican-Setup-1.2.0.exe') 'url points at the release asset'
    Check ([Text.Encoding]::UTF8.GetByteCount((& $get 'notes')) -le 1024 -and (& $get 'notes') -match '\.\.\.$') 'notes are cut to 1024 bytes'
    Check (-not ((& $get 'notes') -match [char]0xFFFD)) 'notes are cut on a character boundary'
    Check ((& $get 'windows-x86_64.uninstall') -eq '{ACE04D99-4564-4E70-9A43-FE47A6A0E3B5}_is1' -and (& $get 'windows-x86_64.wait') -eq 'barbican.exe') 'uninstall key and wait list'
    Check ((& $get 'notes.file') -eq 'release-notes-1.2.0.md') 'notes.file names the notes asset'

    # ---- verify-hub-txt
    $verify = Join-Path $PSScriptRoot 'verify-hub-txt.ps1'
    Check ((Run-Child @($verify, '-HubTxt', $out, '-Dir', $scratch)) -eq 1) 'verify refuses while the notes asset is missing'
    [IO.File]::WriteAllText((Join-Path $scratch 'release-notes-1.2.0.md'), "notes`n")
    Check ((Run-Child @($verify, '-HubTxt', $out, '-Dir', $scratch)) -eq 0) 'verify accepts hub.txt next to its files'
    [IO.File]::AppendAllText($setup, 'x')
    Check ((Run-Child @($verify, '-HubTxt', $out, '-Dir', $scratch)) -eq 1) 'verify refuses a changed installer'

    # ---- the installer compiles (only where Inno Setup is installed; CI always has it)
    $iscc = Get-ChildItem "${env:ProgramFiles(x86)}\Inno Setup *\ISCC.exe", "$env:ProgramFiles\Inno Setup *\ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($iscc) {
        $stand = Join-Path $scratch 'barbican.exe'
        [IO.File]::WriteAllText($stand, 'stand-in')
        & (Join-Path $PSScriptRoot 'build-inno.ps1') -Version 1.2.0-beta.1 -Exe $stand -Out (Join-Path $scratch 'inno') | Out-Null
        Check (Test-Path (Join-Path $scratch 'inno\Barbican-Setup-1.2.0-beta.1.exe')) 'barbican.iss compiles (stand-in exe)'
    } else { Write-Host 'skip  Inno Setup not installed' }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
if ($script:fails -gt 0) { Write-Host "$($script:fails) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
# Explicit: the checks above run child processes that exit 1 on purpose, and a script that ends
# without exit leaves that code in $LASTEXITCODE for the workflow step to trip on.
exit 0
