# spec/: the behaviour catalogue

The only way the Rust app's knowledge enters the C# code (PORT-PLAN.md §2.3). Built in M0 by the
catalogue stream; this README is the placeholder the bootstrap left.

## Layout (to be filled)
```
README.md               ID scheme, statuses, how to update, the pinned Bulwark/Stalwart versions
rust-tests.csv          T- rows: module, Rust test fn, one-line assertion, C# test name, status
behaviours/<area>.md    B-<AREA>-### rows: one rule each, with source (file:line or test, commit), Bulwark ref
decisions.md            D-### rows: decision, reason, commit hash, area, linked B-/I- ids
interactions.csv        I-###    jmap.csv  J-###    gaps.csv  G-###
settings.csv            S-<key>: page, kind, Bulwark default, read-by (Rust), read-by (C#)
menus.md, shortcuts.md, live-checks.md (L-###), automation-protocol.md, layout/<screen>.md
feel/                   rust-baseline/ (present), scenarios, C# results per milestone (feel/M<n>/)
```

## Present now
- `feel/rust-baseline/flicker/*.json`, `feel/rust-baseline/motion/*.json`: the Rust app's flicker
  and motion probe results, **the bar to beat** (PORT-PLAN.md §4.5). Copied verbatim from the Rust
  repository's `tools/flicker-results` and `tools/motion-results` at `rust-final`.

## Pinned references
- Bulwark 1.21.2: `<bulwark>` (see .local/accounts.md). Bulwark refs keep their `file:line` form.
- Stalwart 0.16.27: `stalwart-0.16.27.zip` in `<downloads>`.
- Rust app: `Kaan88/barbican-rust` at tag `rust-final` (`bba9613`), locally `<rust-repo>`.

## Privacy
This repository is public. Catalogue rows cite accounts by placeholder (`<readonly-account>`,
`<test-account>`), never by address, and never quote real mail content.
