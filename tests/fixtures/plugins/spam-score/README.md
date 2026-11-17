# spam-score plugin (reference source)

Source of the Rust app's sample plugin `barbican_spam_score` (docs/legacy/PLUGINS.md), copied from the Rust repository at `rust-final`.
It is the **reference for the port**: the bundled C# plugin lives in `plugins/Barbican.Plugin.SpamScore` (a native .NET plugin over
`src/Barbican.Plugins.Abstractions`) and must give the same rows for the same headers (`spam.rs` is the analysis).

The Rust C ABI is dropped (M2 plan 3.2, D-234): the host does not load a DLL through `NativeLibrary`, and **no DLL is ever committed**
(the Rust build embedded the builder's home path in the binary, and this repository is public). The plugin test (T-206) loads the build of
the C# plugin that Core.Tests copies to `<test output>/plugins/spam-score/`.
