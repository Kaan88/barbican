# Applies the host-origin adaptation only to the exact pinned Bulwark runtime source.
param([Parameter(Mandatory = $true)][string]$RuntimeFile)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$baseline = '1B65A79B5801E990A606959C8B3F6EA428F60A0ABC27A0AB9F971404F31CD11E'
if ((Get-FileHash -LiteralPath $RuntimeFile -Algorithm SHA256).Hash -cne $baseline) { throw 'Unexpected upstream runtime source.' }
$text = [IO.File]::ReadAllText($RuntimeFile)
$old = '  const origin = window.location.origin;'
$new = @(
    '  // Barbican uses a distinct secure child origin to isolate its native bridge.',
    '  // This fixed marker is served only by the host-created child document.',
    '  let origin = window.location.origin;',
    "  if (origin === 'https://managed-plugin.invalid') {",
    '    const markers = document.querySelectorAll(''meta[name="barbican-parent-origin"]'');',
    "    if (markers.length !== 1 || markers[0].getAttribute('content') !== 'https://plugin.invalid') return null;",
    "    origin = 'https://plugin.invalid';",
    '  }'
) -join "`r`n"
if ($text.Split(@($old), [StringSplitOptions]::None).Length -ne 2) { throw 'Runtime patch anchor is missing or ambiguous.' }
$text = $text.Replace($old, $new)
[IO.File]::WriteAllText($RuntimeFile, $text, [Text.UTF8Encoding]::new($false))
