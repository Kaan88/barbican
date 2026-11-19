# Accessibility fonts

Unmodified native fonts and the accompanying verbatim SIL Open Font License 1.1 texts are distributed with the application. No system font installation is needed. The reference Bulwark app uses these families and the same font versions in `app/accessibility.css`.

| File | Version | Source | SHA-256 |
|---|---|---|---|
| AtkinsonHyperlegibleNext.ttf | 2.001, variable weight | https://github.com/google/fonts/tree/main/ofl/atkinsonhyperlegiblenext | `5a455d1cfa099b601ab70751bb9673e8fe1854dc4500c80e1a220d0d75e31745` |
| Lexend.ttf | 1.007, variable weight | https://github.com/google/fonts/tree/main/ofl/lexend | `3add53e641fbc81da64da4bb254285e2831b52b029527bc0714e2b9610832ee6` |
| OpenDyslexic-Regular.otf | 0.920 | https://github.com/antijingoist/opendyslexic/tree/ff3acf698a30/compiled | `c1a0cbf814a75a61abdf0f18143d347222b85e51209f47f8b685a1e0ca594b88` |
| OpenDyslexic-Bold.otf | 0.920 | https://github.com/antijingoist/opendyslexic/tree/ff3acf698a30/compiled | `0901b984a2c3fdc67e3fb8e2d7e1d373bd6d54c3fab9c6b5017f736a25d8ff13` |

The native fonts cover more glyphs than the reference web subsets. Missing glyphs rely on the text platform's fallback; exact Segoe selection for every unsupported glyph needs runtime proof. Variable fonts and weight selection on Uno Skia need separate validation; Uno documents limited variable-font support there. OpenDyslexic regular and bold are selected explicitly for primary regular and bold controls, while dynamic-weight rows still require a full weight audit.
