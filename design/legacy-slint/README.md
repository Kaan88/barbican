# theme.slint (Rust app)

Verbatim copy of the Rust app's `ui/theme.slint` at tag `rust-final`: the tuned design tokens
(palette for dark/light and normal/more contrast, shape per Bento/Nordic style, type scale, motion
tokens and curves, line icons).

It is the **source** of the generated token files:

```
tools/build.ps1 gen-tokens
  -> src/Barbican.App/Themes/Tokens.xaml          palette brushes (default variant) + Motion.* doubles
  -> src/Barbican.App/Themes/Shape.Bento.xaml     shape and type scale, Bento
  -> src/Barbican.App/Themes/Shape.Nordic.xaml    shape and type scale, Nordic
  -> src/Barbican.Presentation/Theme/Tokens.g.cs  every palette variant, shapes, motion, easing, icons
```

`tests/Barbican.Presentation.Tests/Theme/TokenTests.cs` checks that the generated values equal
this file for all eight variants (dark/light x contrast x style), with a hand-copied oracle for the
key colours, and that the committed generated files are fresh.

Change design values here (or move the source of truth to a C#/JSON token file in a later
milestone, with a decision record), then regenerate. Do not edit the generated files.
