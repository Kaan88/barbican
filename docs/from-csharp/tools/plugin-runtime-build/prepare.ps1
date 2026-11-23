#Requires -Version 7.0
# Build only the public Bulwark 1.21.2 sandbox in a new, isolated folder.
# No source checkout edits, account data, or existing output are used.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceCheckout,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = '933bc18f517135e38245821c5cc1bc7117ce2d64'
$lockSha = '006584D714160BB00842A688A5DC0254C82ABA15657B06D26102B85475E8BC75'
$nodeSha = 'A619E2E09EB0D50EF4C733D678B4D22689D2AFEF138CD67FD3FA107B535F8819'
$npmCliSha = '3CE7CBA6F5128DD5F54C98B6A5036B0F850496878CC2E21044B675FE3C594E3E'
$pins = @(
    @{ Name='manifest.json'; Size=172; Sha='259B3861DB585BF9C9AB7EE08DF761BE930212168D3BC969AD5E92108992C73D'; Token='"manifest.json"' },
    @{ Name='runtime-SCQLYMRU.js'; Size=34836; Sha='67BD5F7DB3B4C42FD233016387AECDF5CFC29F8F1544AC66A1FAEF78C6EACDA0'; Token='EntryScript' },
    @{ Name='kit-556dca3ab5af.css'; Size=34515; Sha='556DCA3AB5AF52E3882ED907D7C49FA3961E495AAD48DE67CFC3E33D2006184D'; Token='Style' },
    @{ Name='chunk-BWK7OKB3.js'; Size=8534; Sha='E3C6E952AF788637C143A8BDE4C23C178FB5A985D96FB69909FF3A9F0F1D15E4'; Token='"chunk-BWK7OKB3.js"' },
    @{ Name='chunk-DQG63ATX.js'; Size=1593; Sha='69409106994D2F4204CFEA8AD119B324B04FE62B1EC4E34ADB5077F16F34A92D'; Token='"chunk-DQG63ATX.js"' },
    @{ Name='chunk-PIOFL43R.js'; Size=297281; Sha='CE546EEEF09A50217DEA942EDB397E3E8B87D7DF1B71B39DC136F0D876545F14'; Token='"chunk-PIOFL43R.js"' },
    @{ Name='chunk-CT56IUXM.js'; Size=93637; Sha='87B89A99C2DCC560F9D857787E60263C603C16386CDDA95F51389D0FA3AB7E63'; Token='"chunk-CT56IUXM.js"' },
    @{ Name='chunk-IQHV4AYX.js'; Size=2486061; Sha='BE5125664A99C94F2B64F5D5C312B7699F47FDA3E5AAE76320FC40FB6B2C572B'; Token='"chunk-IQHV4AYX.js"' }
)

function Require-Hash([string]$Path, [string]$Expected, [string]$Label) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $Expected) { throw "Fingerprint mismatch: $Label." }
}
function Invoke-PreparationTool([string]$Label, [string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [switch]$Capture) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment.Clear()
    foreach ($name in $toolEnvironment.Keys) { $start.Environment[$name] = $toolEnvironment[$name] }
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start preparation step: $Label." }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $result = $standardOutput.GetAwaiter().GetResult()
        $null = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Preparation step failed: $Label." }
        if ($Capture) { return $result.Trim() }
    } finally { $process.Dispose() }
}

if (-not ('BarbicanRuntimeBuild.LocalPath' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BarbicanRuntimeBuild
{
    public static class LocalPath
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(string path, IntPtr security);

        public static void CreateNewDirectory(string path)
        {
            if (!CreateDirectoryW(path, IntPtr.Zero))
                throw new InvalidOperationException("Could not reserve a new output directory.");
        }

        public static string ResolveDirectory(string path)
        {
            const uint shareReadWriteDelete = 7;
            const uint openExisting = 3;
            const uint backupSemantics = 0x02000000;
            const uint normalizedVolumeGuidPath = 1;
            using var handle = CreateFileW(path, 0, shareReadWriteDelete, IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
            var resolved = new StringBuilder(32768);
            if (handle.IsInvalid) throw new InvalidOperationException("The local directory could not be opened.");
            var length = GetFinalPathNameByHandleW(handle, resolved, (uint)resolved.Capacity, normalizedVolumeGuidPath);
            if (length == 0 || length >= (uint)resolved.Capacity)
                throw new InvalidOperationException("The local directory could not be resolved.");
            return resolved.ToString().TrimEnd('\\');
        }
    }
}
'@
}

if (-not [IO.Path]::IsPathFullyQualified($SourceCheckout) -or -not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'Both paths must be absolute.' }
$source = [IO.Path]::GetFullPath($SourceCheckout).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $source -PathType Container) -or (Test-Path -LiteralPath $output)) { throw 'Source must exist and output must not exist.' }
$parent = [IO.Path]::GetDirectoryName($output)
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'The output parent must exist.' }
$barbicanRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Refuse path indirections so the isolated output cannot alias the checkout.
foreach ($start in @($parent, $source)) {
    for ($dir = Get-Item -LiteralPath $start; $null -ne $dir; $dir = $dir.Parent) {
        if (($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A path contains a reparse point.' }
    }
}
$physicalParent = [BarbicanRuntimeBuild.LocalPath]::ResolveDirectory($parent)
foreach ($checkout in @($source, $barbicanRoot)) {
    $physicalCheckout = [BarbicanRuntimeBuild.LocalPath]::ResolveDirectory($checkout)
    if ([string]::Equals($physicalParent, $physicalCheckout, [StringComparison]::OrdinalIgnoreCase) -or
        $physicalParent.StartsWith($physicalCheckout + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Output must be outside both checkouts.'
    }
}
$git = (Get-Command git.exe -ErrorAction Stop).Source
$node = (Get-Command node.exe -ErrorAction Stop).Source
$tar = (Get-Command tar.exe -ErrorAction Stop).Source
$npm = Join-Path (Split-Path -Parent $node) 'node_modules/npm/bin/npm-cli.js'
Require-Hash $node $nodeSha 'Node executable'
Require-Hash $npm $npmCliSha 'npm entrypoint'

# Refuse to build against a stale or widened app inventory.
$appPinsPath = Join-Path $PSScriptRoot '../../src/Barbican.App/Plugins/Sandbox/PluginSandboxBootstrapAssets.cs'
$appPins = Get-Content -LiteralPath $appPinsPath -Raw
if (-not $appPins.Contains('EntryScript = "runtime-SCQLYMRU.js"') -or -not $appPins.Contains('Style = "kit-556dca3ab5af.css"')) { throw 'The app entrypoint pins differ.' }
$pattern = 'new\((?<token>EntryScript|Style|"[^"]+"),\s*(?<size>\d+),\s*"(?<sha>[A-F0-9]{64})"'
$matches = [regex]::Matches($appPins, $pattern)
if ($matches.Count -ne $pins.Count) { throw 'The app inventory count differs.' }
foreach ($pin in $pins) {
    $found = @($matches | Where-Object { $_.Groups['token'].Value -ceq $pin.Token -and $_.Groups['size'].Value -ceq [string]$pin.Size -and $_.Groups['sha'].Value -ceq $pin.Sha })
    if ($found.Count -ne 1) { throw 'The app inventory differs.' }
}

# Creation is exclusive. On failure, retain only this new folder for inspection;
# reruns must select a different absent folder.
[BarbicanRuntimeBuild.LocalPath]::CreateNewDirectory($output)
$archive = Join-Path $output 'source.tar'
$tree = Join-Path $output 'source'
$cache = Join-Path $output 'npm-cache'
$scratch = Join-Path $output 'temp'
New-Item -ItemType Directory -Path $tree, $scratch -ErrorAction Stop | Out-Null
$userConfig = New-Item -ItemType File -Path (Join-Path $output 'empty-user-config') -ErrorAction Stop
$globalConfig = New-Item -ItemType File -Path (Join-Path $output 'empty-global-config') -ErrorAction Stop
$windows = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$toolEnvironment = @{
    SystemRoot = $windows
    WINDIR = $windows
    PATH = "$(Split-Path -Parent $node);$(Join-Path $windows 'System32')"
    TEMP = $scratch
    TMP = $scratch
    GIT_TERMINAL_PROMPT = '0'
    GIT_NO_REPLACE_OBJECTS = '1'
    GIT_CONFIG_NOSYSTEM = '1'
    GIT_CONFIG_GLOBAL = $globalConfig.FullName
    NPM_CONFIG_USERCONFIG = $userConfig.FullName
    NPM_CONFIG_GLOBALCONFIG = $globalConfig.FullName
    NPM_CONFIG_REGISTRY = 'https://registry.npmjs.org/'
}
if ((Invoke-PreparationTool 'Node version' $node @('--version') $tree -Capture) -cne 'v26.11.1') { throw 'Unexpected Node version.' }
if ((Invoke-PreparationTool 'npm version' $node @($npm, '--version') $tree -Capture) -cne '11.20.0') { throw 'Unexpected npm version.' }
$actualCommit = Invoke-PreparationTool 'release tag' $git @('rev-parse', '--verify', 'refs/tags/1.21.2^{commit}') $source -Capture
if ($actualCommit -cne $commit) { throw 'The local release tag does not match the pinned commit.' }

Invoke-PreparationTool 'source archive' $git @('-c', 'core.autocrlf=true', '-c', 'core.eol=crlf', 'archive', '--format=tar', "--output=$archive", $commit) $source
Invoke-PreparationTool 'source extraction' $tar @('-xf', $archive, '-C', $tree) $tree
Require-Hash (Join-Path $tree 'package-lock.json') $lockSha 'upstream lockfile'
Require-Hash (Join-Path $tree 'package.json') '64F235C3F8AA53936B676C9C82F7FF265DCE7E25D4CF04C67BB226E43CF6B754' 'upstream package manifest'
Require-Hash (Join-Path $tree 'scripts/build-plugin-sandbox.mjs') 'A6A4862B99613F962AD3EAA3AA63A86912FA9FF58E9EEC503026FB56C0638BE1' 'upstream build script'

$runtimeSource = Join-Path $tree 'lib/plugin-sandbox/runtime.tsx'
& (Join-Path $PSScriptRoot 'patch-runtime.ps1') -RuntimeFile $runtimeSource
Require-Hash $runtimeSource 'B655D9DDABE5A123F63F8016B1F4640984CD24D566038FE42A922DEB76102013' 'patched runtime source'
Invoke-PreparationTool 'npm clean install' $node @($npm, 'ci', '--ignore-scripts', '--no-audit', '--no-fund', '--cache', $cache) $tree
Invoke-PreparationTool 'runtime build' $node @('scripts/build-plugin-sandbox.mjs') $tree
$assets = Join-Path $tree 'public/plugin-sandbox-assets'
$assetFolder = Get-Item -LiteralPath $assets -Force
if (-not $assetFolder.PSIsContainer -or ($assetFolder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Output folder is not an ordinary directory.' }
$entries = @(Get-ChildItem -LiteralPath $assets -Force)
if ($entries.Count -ne $pins.Count) { throw 'Output inventory count differs.' }
foreach ($pin in $pins) {
    $matched = @($entries | Where-Object { $_.Name -ceq $pin.Name })
    if ($matched.Count -ne 1) { throw 'Output entry name differs.' }
    $item = $matched[0]
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -ne $pin.Size) { throw 'Output entry differs.' }
    Require-Hash $item.FullName $pin.Sha ('asset ' + $pin.Name)
}
Write-Output "Verified $($pins.Count) pinned files at: $assets"
