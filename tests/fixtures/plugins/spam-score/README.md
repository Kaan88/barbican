# spam-score plugin (ABI fixture)

Source of the Rust app's sample plugin `barbican_spam_score` (docs/legacy/PLUGINS.md), copied from
the Rust repository at `rust-final`. The C# plugin host (M2) must load the built DLL through
`NativeLibrary` with the same C ABI.

The built `barbican_spam_score.dll` is **not** committed yet: the Rust build embeds the builder's
home path (from the cargo registry) in the binary, and this repository is public. M2 adds it,
built with `--remap-path-prefix` (or `RUSTFLAGS=--remap-path-prefix=<home>=~`) and scanned for
local paths before committing.
