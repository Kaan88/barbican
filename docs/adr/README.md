# Decision records

| ADR | Decision | Fallback (and when) | Owner |
|---|---|---|---|
| [0001](0001-stack-winui3-uno-csharp.md) | C# with WinUI 3 on Windows and Uno on Linux; the Rust app is reference only | n/a | M0 |
| [0002](0002-public-repo-privacy-license.md) | Public repo, privacy rules, AGPL-3.0-only, no GitHub CI (local Windows + Linux VM) | n/a | M0 |
| [0003](0003-packaging.md) | Windows portable zip until M8; MSI, setup exe, AppImage/.deb/.rpm and policy templates at the end | n/a | M8-M10 |
| [0004](0004-uno-single-project.md) | S1: one Uno single project (Uno.Sdk 6.7.49 now), WinAppSDK 2.6.5, unpackaged self-contained. **Move to Uno.Sdk 7.0.x first in M1** | Stay on 6.7 and vendor RichTextKit on SkiaSharp 3 for M3, if Uno 7 fails the S1/S2 re-run. Option B (separate heads) only for an unfixable WinUI-head break | M1 |
| [0005](0005-body-shell.md) | S2: body shell 1:1 on Windows; Linux go with stand-in images under dialogs and scrims, and shell-routed links; menu host is a constrained Popup | Plain-text body on Linux, if WebKitGTK regresses; dim until presented, for the Linux skew | M1, M2 |
| [0006](0006-editor-engine.md) | S3: RichTextKit 0.6.173 on SkiaSharp 4.156, `Barbican.Editor.Layout`, SKXamlCanvas / SKCanvasElement, hidden-TextBox IME | Vendor RichTextKit; Win2D backend; SKSwapChainPanel; visible TextBox during composition | M3 |
| [0007](0007-hidden-automation.md) | S4: off-screen non-activating window, line protocol, UIA-pattern clicks, WGC capture, `wgc` probe; popups fail closed | In-process peers over the WebView2; drag, hover and IME by hand until a throwaway VM exists | M1 |
| [0008](0008-credential-migration.md) | S5: own credential names, local-machine persistence, explicit read-only import from the Rust entries | Roaming account list on request; no Linux import unless verified | M1 |

ADRs 0004-0008 each end with an "M0 review" section, written at the merge of the M0 branches. It
records cross-spike findings and makes the go/fallback choice explicit.
