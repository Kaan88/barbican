#Requires -Version 5.1
<#
.SYNOPSIS
  Zips the win-x64 publish into artifacts/portable/Barbican-<sha>-win-x64.zip (docs/milestones/M1-plan.md
  §5 F1 step 5): the portable build a tester unpacks and runs; no installer, no MSIX.

.DESCRIPTION
  Run tools/build.ps1 publish -Head win first (Release, self-contained, unpackaged; the publish
  check there already refused automation content). This script checks again before zipping, so a
  stale or hand-copied folder cannot ship FlaUI, Win2D or Barbican.Automation.

.EXAMPLE
  tools/build.ps1 publish -Head win
  tools/package-portable.ps1
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Rid = 'win-x64',
    [string]$BootstrapDirectory,
    [string]$RuntimeNoticesDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot
$hasBootstrap = $PSBoundParameters.ContainsKey('BootstrapDirectory')
$hasNotices = $PSBoundParameters.ContainsKey('RuntimeNoticesDirectory')
$optional = $hasBootstrap -or $hasNotices
if ($optional -and (-not $hasBootstrap -or -not $hasNotices -or
    [string]::IsNullOrWhiteSpace($BootstrapDirectory) -or [string]::IsNullOrWhiteSpace($RuntimeNoticesDirectory))) {
    throw '[package-portable] both nonempty runtime input directories are required'
}
if ($optional -and $PSBoundParameters.ContainsKey('Source') -and [string]::IsNullOrWhiteSpace($Source)) { throw '[package-portable] source cannot be empty' }
if ($optional -and $Rid -notmatch '^win-(x64|arm64)$') { throw '[package-portable] unsupported Windows RID' }
if (-not $Source) { $Source = Join-Path $Root "artifacts/publish/win/$Rid" }
$outDir = [IO.Path]::Combine($Root, 'artifacts', 'portable')
if ($optional) {
    if (-not ('BarbicanPortableDriveType' -as [type])) {
        Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class BarbicanPortableDriveType {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, [Out] char[] target, uint capacity);
    public static bool IsOrdinaryLocalVolume(string drive) {
        char[] target = new char[32768];
        uint count = QueryDosDeviceW(drive, target, (uint)target.Length);
        if (count == 0 || count > target.Length) return false;
        string mappings = new string(target, 0, (int)count);
        string[] entries = mappings.Split(new char[] { '\0' }, System.StringSplitOptions.RemoveEmptyEntries);
        return entries.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(entries[0],
            @"^\\Device\\HarddiskVolume[0-9]+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFileAttributesW(string path);
    public static int AttributesOrMissing(string path) {
        uint attributes = GetFileAttributesW(path);
        if (attributes != 0xffffffff) return (int)attributes;
        int error = Marshal.GetLastWin32Error();
        if (error == 2 || error == 3) return -1;
        throw new System.InvalidOperationException("Cannot inspect path attributes.");
    }
}
"@
    }
    function Assert-LocalFixedPath([string]$Path) {
        if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw '[package-portable] path must be an absolute local drive path' }
        $full = [IO.Path]::GetFullPath($Path)
        $drive = $full.Substring(0, 3)
        if ([BarbicanPortableDriveType]::GetDriveTypeW($drive) -ne 3 -or -not [BarbicanPortableDriveType]::IsOrdinaryLocalVolume($drive.Substring(0, 2))) { throw '[package-portable] path must use an ordinary local fixed volume (no redirected or SUBST drive)' }
        return $full
    }
    # Only direct, single HarddiskVolume mappings are accepted; redirected, SUBST, network, and nonstandard volume targets fail closed.
    # Check every drive first, before querying any supplied directory.
    $Source = Assert-LocalFixedPath $Source
    $BootstrapDirectory = Assert-LocalFixedPath $BootstrapDirectory
    $RuntimeNoticesDirectory = Assert-LocalFixedPath $RuntimeNoticesDirectory
    $Root = Assert-LocalFixedPath $Root
    $outDir = Assert-LocalFixedPath $outDir
    function Assert-PathChain([string]$Path, [bool]$MayBeMissing) {
        $rootPath = [IO.Path]::GetPathRoot($Path)
        $node = $rootPath
        $parts = $Path.Substring($rootPath.Length).Split([char[]]@('\','/'), [StringSplitOptions]::RemoveEmptyEntries)
        $all = @($rootPath) + @($parts)
        $first = $true
        $missing = $false
        foreach ($part in $all) {
            if ($first) { $first = $false } else { $node = [IO.Path]::Combine($node, $part) }
            if ($missing) { continue }
            $attributes = [BarbicanPortableDriveType]::AttributesOrMissing($node)
            if ($attributes -eq -1) {
                if (-not $MayBeMissing) { throw '[package-portable] input path does not exist' }
                $missing = $true
                continue
            }
            if (($attributes -band [IO.FileAttributes]::Directory) -eq 0 -or ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '[package-portable] path contains a reparse point or non-directory' }
        }
    }
    foreach ($inputPath in @($Source, $BootstrapDirectory, $RuntimeNoticesDirectory, $Root)) { Assert-PathChain $inputPath $false }
    Assert-PathChain $outDir $true
    if (-not ('BarbicanPortablePathAudit' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
public static class BarbicanPortablePathAudit {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    public static string Resolve(string path) {
        using (SafeFileHandle handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new InvalidOperationException("Cannot resolve local path.");
            StringBuilder result = new StringBuilder(32768);
            uint count = GetFinalPathNameByHandleW(handle, result, (uint)result.Capacity, 1);
            if (count == 0 || count >= result.Capacity) throw new InvalidOperationException("Cannot resolve local volume path.");
            return result.ToString().TrimEnd('\\');
        }
    }
}
"@
    }
    # Resolve the nearest existing parent and append the missing lexical tail before creating anything.
    $nearest = $outDir
    while ([BarbicanPortableDriveType]::AttributesOrMissing($nearest) -eq -1) {
        $next = [IO.Path]::GetDirectoryName($nearest)
        if ([string]::IsNullOrEmpty($next) -or $next -eq $nearest) { throw '[package-portable] cannot resolve output ancestor' }
        $nearest = $next
    }
    $physicalSourceBefore = [BarbicanPortablePathAudit]::Resolve($Source)
    $physicalNearest = [BarbicanPortablePathAudit]::Resolve($nearest)
    $tail = $outDir.Substring($nearest.Length).TrimStart('\', '/')
    $physicalOutputBefore = if ($tail) { $physicalNearest + '\' + $tail } else { $physicalNearest }
    if ([string]::Equals($physicalSourceBefore, $physicalOutputBefore, [StringComparison]::OrdinalIgnoreCase) -or
        $physicalOutputBefore.StartsWith($physicalSourceBefore + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw '[package-portable] output cannot be inside publish source'
    }
    $appPinsDirectory = [IO.Path]::Combine($Root, 'src', 'Barbican.App', 'Plugins', 'Sandbox')
    Assert-PathChain $appPinsDirectory $false
    $appPinsAttributes = [BarbicanPortableDriveType]::AttributesOrMissing([IO.Path]::Combine($appPinsDirectory, 'PluginSandboxBootstrapAssets.cs'))
    if ($appPinsAttributes -eq -1 -or ($appPinsAttributes -band ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) -ne 0) { throw '[package-portable] invalid app inventory file' }
    function Assert-Tree([string]$Directory) {
        foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '[package-portable] reparse points are not allowed' }
            if ($item.PSIsContainer) { Assert-Tree $item.FullName }
        }
    }
    function Get-SafeFiles([string]$Directory) {
        foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '[package-portable] reparse points are not allowed' }
            if ($item.PSIsContainer) { Get-SafeFiles $item.FullName } else { $item }
        }
    }
    Assert-Tree $Source
}
if (-not (Test-Path (Join-Path $Source 'Barbican.exe'))) { throw "[package-portable] no Barbican.exe in $Source (run tools/build.ps1 publish -Head win)" }

if ($optional) {
    $bad = @(Get-SafeFiles $Source | Where-Object { $_.Name -like 'FlaUI*.dll' -or $_.Name -like 'Microsoft.Graphics.Canvas*.dll' -or $_.Name -eq 'Barbican.Automation.dll' -or $_.Name -eq 'Interop.UIAutomationClient.dll' })
} else {
    $bad = @(Get-ChildItem -Path $Source -Recurse -File -Include 'FlaUI*.dll', 'Microsoft.Graphics.Canvas*.dll', 'Barbican.Automation.dll', 'Interop.UIAutomationClient.dll')
}
if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "  $($_.FullName)" }; throw '[package-portable] automation content in the publish folder' }
$text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes((Join-Path $Source 'Barbican.dll')))
if ($text.Contains('BarbicanAutomationBuild')) { throw '[package-portable] the App assembly is an AUTOMATION build' }

$sha = (& git -C $Root rev-parse --short=12 HEAD).Trim()
$dirty = (& git -C $Root status --porcelain --untracked-files=no)
if ($dirty) { $sha += '-dirty' }
New-Item -ItemType Directory -Force $outDir | Out-Null
$zip = Join-Path $outDir "Barbican-$sha-$Rid.zip"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Optional runtime archive mode: its inputs are separately pinned and never modify the publish tree.
function Assert-Input([string]$Directory, [object[]]$Pins, [string[]]$Ignored = @()) {
    $items = @(Get-ChildItem -LiteralPath $Directory -Force)
    foreach ($item in $items) {
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '[package-portable] invalid input entry' }
        $allowed = @($Pins | Where-Object { $_.Name -ceq $item.Name }).Count -eq 1 -or @($Ignored | Where-Object { $_ -ceq $item.Name }).Count -eq 1
        if (-not $allowed) { throw '[package-portable] unexpected input entry' }
    }
    foreach ($pin in $Pins) {
        $found = @($items | Where-Object { $_.Name -ceq $pin.Name })
        if ($found.Count -ne 1 -or $found[0].Length -ne $pin.Size -or (Get-FileHash -LiteralPath $found[0].FullName -Algorithm SHA256).Hash -cne $pin.Sha) { throw '[package-portable] pinned input mismatch' }
    }
}
function Assert-AppPins([object[]]$Pins) {
    $file = Join-Path $Root 'src/Barbican.App/Plugins/Sandbox/PluginSandboxBootstrapAssets.cs'
    $content = Get-Content -LiteralPath $file -Raw
    if (-not $content.Contains('EntryScript = "runtime-7YZTQ2LX.js"') -or -not $content.Contains('Style = "kit-556dca3ab5af.css"')) { throw '[package-portable] app entrypoint pins differ' }
    $pattern = 'new\((?<token>EntryScript|Style|"[^"]+"),\s*(?<size>\d+),\s*"(?<sha>[A-F0-9]{64})"'
    $entries = [regex]::Matches($content, $pattern)
    if ($entries.Count -ne $Pins.Count) { throw '[package-portable] app inventory count differs' }
    foreach ($pin in $Pins) {
        $matched = @($entries | Where-Object { $_.Groups['token'].Value -ceq $pin.Token -and $_.Groups['size'].Value -ceq [string]$pin.Size -and $_.Groups['sha'].Value -ceq $pin.Sha })
        if ($matched.Count -ne 1) { throw '[package-portable] app inventory differs' }
    }
}
function Test-ArchiveEntry([System.IO.Compression.ZipArchive]$Archive, [string]$Name, [long]$Size, [string]$Sha) {
    $found = @($Archive.Entries | Where-Object { $_.FullName -ceq $Name })
    if ($found.Count -ne 1 -or $found[0].Length -ne $Size) { throw '[package-portable] ZIP entry size or name mismatch' }
    $stream = $found[0].Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $actual = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $hasher.Dispose(); $stream.Dispose() }
    if ($actual -cne $Sha) { throw '[package-portable] ZIP entry hash mismatch' }
}
function Assert-CanonicalZipName([string]$Name) {
    if ([string]::IsNullOrEmpty($Name) -or $Name.Contains('\') -or $Name.StartsWith('/') -or $Name.Contains('//')) { throw '[package-portable] noncanonical ZIP name' }
    $parts = $Name.TrimEnd('/').Split('/')
    foreach ($part in $parts) {
        if ($part -eq '.' -or $part -eq '..' -or $part -match '[\x00-\x1f<>:"|?*]' -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw '[package-portable] unsafe ZIP name' }
    }
}
function Add-CanonicalTree([System.IO.Compression.ZipArchive]$Archive, [string]$Directory, [string]$Prefix) {
    Assert-CanonicalZipName $Prefix
    [void]$Archive.CreateEntry($Prefix)
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '[package-portable] reparse points are not allowed' }
        $name = $Prefix + $item.Name
        if ($item.PSIsContainer) { Add-CanonicalTree $Archive $item.FullName ($name + '/') }
        else {
            Assert-CanonicalZipName $name
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $item.FullName, $name) | Out-Null
        }
    }
}
function New-VerifiedPortable([string]$Zip, [string]$OutDir) {
    if ($Rid -notmatch '^win-(x64|arm64)$') { throw '[package-portable] unsupported Windows RID' }
    if (Test-Path -LiteralPath $Zip) { throw '[package-portable] destination already exists' }
    $assets = @(
        @{ Name='manifest.json'; Size=172; Sha='D6FEE1ED99C3E67548D99B894AF0A5AAE7DB764E082750BB4D6A3454629CB7B9'; Token='"manifest.json"' },
        @{ Name='runtime-7YZTQ2LX.js'; Size=34612; Sha='19661279909ED661AB3F5844EC096D225105E1FB531A6695A8C738FC1733F3A5'; Token='EntryScript' },
        @{ Name='kit-556dca3ab5af.css'; Size=34515; Sha='556DCA3AB5AF52E3882ED907D7C49FA3961E495AAD48DE67CFC3E33D2006184D'; Token='Style' },
        @{ Name='chunk-BWK7OKB3.js'; Size=8534; Sha='E3C6E952AF788637C143A8BDE4C23C178FB5A985D96FB69909FF3A9F0F1D15E4'; Token='"chunk-BWK7OKB3.js"' },
        @{ Name='chunk-DQG63ATX.js'; Size=1593; Sha='69409106994D2F4204CFEA8AD119B324B04FE62B1EC4E34ADB5077F16F34A92D'; Token='"chunk-DQG63ATX.js"' },
        @{ Name='chunk-PIOFL43R.js'; Size=297281; Sha='CE546EEEF09A50217DEA942EDB397E3E8B87D7DF1B71B39DC136F0D876545F14'; Token='"chunk-PIOFL43R.js"' },
        @{ Name='chunk-CT56IUXM.js'; Size=93637; Sha='87B89A99C2DCC560F9D857787E60263C603C16386CDDA95F51389D0FA3AB7E63'; Token='"chunk-CT56IUXM.js"' },
        @{ Name='chunk-IQHV4AYX.js'; Size=2486061; Sha='BE5125664A99C94F2B64F5D5C312B7699F47FDA3E5AAE76320FC40FB6B2C572B'; Token='"chunk-IQHV4AYX.js"' }
    )
    $notices = @(
        @{ Name='bulwark--LICENSE'; Size=39760; Sha='72E29520FED9BE9110F64C579C836589D214E6B2CC2325FDA4BD34308ABED793' },
        @{ Name='clsx--license'; Size=1104; Sha='9A9EDAD7BAAE52622BDDF3C15B2EF8A33D2C89F2D25408AD13E8A7481C6B0C97' },
        @{ Name='react--LICENSE'; Size=1088; Sha='DA6D3703ED11CBE42BD212C725957C98DA23CBFF1998C05FA4B3D976D1A58E93' },
        @{ Name='react-dom--LICENSE'; Size=1088; Sha='DA6D3703ED11CBE42BD212C725957C98DA23CBFF1998C05FA4B3D976D1A58E93' },
        @{ Name='scheduler--LICENSE'; Size=1088; Sha='DA6D3703ED11CBE42BD212C725957C98DA23CBFF1998C05FA4B3D976D1A58E93' },
        @{ Name='tabler__icons--LICENSE'; Size=1073; Sha='B740A1D46122672DA62833E97F7E7C8A13FA85CBC7445B584B297CC00DDE93DB' },
        @{ Name='tabler__icons-react--LICENSE'; Size=1073; Sha='B740A1D46122672DA62833E97F7E7C8A13FA85CBC7445B584B297CC00DDE93DB' },
        @{ Name='tailwind-merge--LICENSE.md'; Size=1070; Sha='D4C70C7CE38CEA8778F0AED3FC0BEF0A9DBD27F13BD8B6773CBD6D37941971E5' },
        @{ Name='tailwindcss--LICENSE'; Size=1071; Sha='60E0B68C0F35C078EEF3A5D29419D0B03FF84EC1DF9C3F9D6E39A519A5AE7985' }
    )
    Assert-AppPins $assets
    $sourceFull = [BarbicanPortablePathAudit]::Resolve($Source)
    $outFull = [BarbicanPortablePathAudit]::Resolve($OutDir)
    if ([string]::Equals($sourceFull, $outFull, [StringComparison]::OrdinalIgnoreCase) -or $outFull.StartsWith($sourceFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '[package-portable] output cannot be inside publish source' }
    Assert-Tree $Source
    Assert-Input $BootstrapDirectory $assets
    Assert-Input $RuntimeNoticesDirectory $notices @('inventory.json', 'dependency-output.json')
    $baseName = (Get-Item -LiteralPath $Source).Name
    if (-not $baseName -or $baseName.Contains('/') -or $baseName.Contains('\')) { throw '[package-portable] invalid archive root' }
    $assetPrefix = "$baseName/Assets/plugin-sandbox-bootstrap/"
    $noticePrefix = "$baseName/THIRD-PARTY-NOTICES/plugin-runtime/"
    $provenance = "Bulwark Webmail 1.21.2, commit 933bc18f517135e38245821c5cc1bc7117ce2d64.`nSource: https://github.com/bulwarkmail/webmail/tree/933bc18f517135e38245821c5cc1bc7117ce2d64`nLicense: AGPL-3.0-only; see bulwark--LICENSE and the accompanying dependency notices.`n"
    $provenanceBytes = [Text.Encoding]::UTF8.GetBytes($provenance)
    $provenanceHash = [Security.Cryptography.SHA256]::Create()
    try { $provenanceSha = [BitConverter]::ToString($provenanceHash.ComputeHash($provenanceBytes)).Replace('-', '') }
    finally { $provenanceHash.Dispose() }

    # The scratch directory is exclusively named, retained on failure, and holds no source-tree edits.
    $scratch = Join-Path $OutDir ('.package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
    $tempZip = Join-Path $scratch 'portable.zip'
    $archive = [System.IO.Compression.ZipFile]::Open($tempZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try { Add-CanonicalTree $archive $Source ($baseName + '/') } finally { $archive.Dispose() }
    $archive = [System.IO.Compression.ZipFile]::Open($tempZip, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $allNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            Assert-CanonicalZipName $entry.FullName
            if ([string]::Equals($entry.FullName, ($baseName + '/Assets'), [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($entry.FullName, ($baseName + '/THIRD-PARTY-NOTICES'), [StringComparison]::OrdinalIgnoreCase) -or
                -not $allNames.Add($entry.FullName.TrimEnd('/')) -or $entry.FullName.StartsWith($assetPrefix, [StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.StartsWith($noticePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($entry.FullName.TrimEnd('/'), $assetPrefix.TrimEnd('/'), [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($entry.FullName.TrimEnd('/'), $noticePrefix.TrimEnd('/'), [StringComparison]::OrdinalIgnoreCase)) { throw '[package-portable] ZIP path collision' }
        }
        foreach ($pin in $assets) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $BootstrapDirectory $pin.Name), ($assetPrefix + $pin.Name)) | Out-Null }
        foreach ($pin in $notices) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $RuntimeNoticesDirectory $pin.Name), ($noticePrefix + $pin.Name)) | Out-Null }
        $entry = $archive.CreateEntry($noticePrefix + 'SOURCE.txt')
        $stream = $entry.Open()
        try { $stream.Write($provenanceBytes, 0, $provenanceBytes.Length) } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
    try {
        foreach ($entry in $archive.Entries) { Assert-CanonicalZipName $entry.FullName }
        foreach ($pin in $assets) { Test-ArchiveEntry $archive ($assetPrefix + $pin.Name) $pin.Size $pin.Sha }
        foreach ($pin in $notices) { Test-ArchiveEntry $archive ($noticePrefix + $pin.Name) $pin.Size $pin.Sha }
        Test-ArchiveEntry $archive ($noticePrefix + 'SOURCE.txt') $provenanceBytes.Length $provenanceSha
    } finally { $archive.Dispose() }
    # File.Move without an overwrite argument is atomic and fails if another producer won the destination.
    [IO.File]::Move($tempZip, $Zip)
    [IO.Directory]::Delete($scratch, $false)
    Write-Host "[package-portable] verified 8 runtime assets and 9 notices in $Zip"
}

if ($optional) { New-VerifiedPortable $zip $outDir; return }
if (Test-Path $zip) { Remove-Item $zip -Force }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
[System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$sw.Stop()
$mb = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
$srcMb = [Math]::Round(((Get-ChildItem $Source -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "[package-portable] $zip ($mb MB from $srcMb MB, $([int]$sw.Elapsed.TotalSeconds) s)"
