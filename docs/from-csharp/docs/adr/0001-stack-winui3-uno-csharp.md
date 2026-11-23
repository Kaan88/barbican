# ADR 0001: C# with WinUI 3 on Windows and Uno Platform on Linux

- Status: accepted (2026-11-17)
- Deciders: the user (owner), recorded by the M0 bootstrap agent
- Context documents: [PORT-PLAN.md](../PORT-PLAN.md) §1, §3.1, §3.12

## Context
Barbican was a Rust app with a Slint UI. Its mail body ran in WebView2 as a child window (wry), so
every menu, dialog and toast over the body needed workarounds (a captured image of the body under
each overlay, focus theft by the hidden WebView, keys replayed into the app). After using a WinUI 3
prototype (now in `prototype/winui/`), the user decided: "rust/slint is no more".

## Decision
- All code is C# on .NET 10. No Rust in the new app: no FFI, no sidecar.
- **Windows:** WinUI 3 (Windows App SDK), unpackaged and self-contained. WebView2 is composed into
  the XAML tree, so overlays draw over the body natively.
- **Linux:** Uno Platform, Skia desktop head on X11, the same XAML views. The mail body uses
  WebKitGTK through Uno's WebView2.
- **One Uno single project with two target frameworks** (`net10.0-windows10.0.26100.0` builds real
  WinUI 3; `net10.0-desktop` builds Uno Skia) is the recommended head, to be confirmed by spike S1
  at the end of M0. Fallback: separate WinUI and Uno heads sharing views. Core, Presentation and
  Platform do not change either way.
- **Layers:** `Barbican.Core` (net10.0, no UI types), `Barbican.Presentation` (ViewModels with
  CommunityToolkit.Mvvm, tokens, menus and shortcuts as data), `Barbican.Platform` (interfaces) with
  Windows and Linux implementations, and the App head.
- **Strict C#:** nullable reference types, warnings as errors, `AnalysisLevel latest-recommended`,
  code style enforced in the build, .NET + Threading + Meziantou + BannedApi analyzers, central
  package management, AOT-compatible Core and Presentation. See `Directory.Build.props`.
- **Libraries** replace hand-written code where they fit (MimeKit, AngleSharp, Ical.Net, NodaTime,
  FolkerKinzel.VCards, …, PORT-PLAN.md §3.12), but every library must pass the ported Rust tests.
- **Tests:** xUnit v3 on Microsoft.Testing.Platform (`global.json` opts `dotnet test` into it).

## Consequences
- The Rust repository is frozen (tag `rust-final`) and becomes the read-only reference
  (`Kaan88/barbican-rust`, private).
- Linux keeps the airspace problem (WebKitGTK is a separate X11 window): the Rust capture trick is
  ported for Linux only (PORT-PLAN.md §3.6). Uno has no Linux screen-reader support yet.
- `RichEditBox` is not on Uno Skia: the composer is a custom native editor (PORT-PLAN.md §3.8).
- Build-time cost of two TFMs is contained by `tools/build.ps1 -Head win|desktop` (one TFM in the
  inner loop; the App head reads `BarbicanHead`).
