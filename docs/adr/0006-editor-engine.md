# ADR 0006: Composer editor engine (spike S3)

- Status: accepted for M3 (spike result, 2026-11-17); the Uno items under "Pending" are open until
  spike S1's App head exists
- Deciders: recorded by the M0 S3 spike agent, within the options PORT-PLAN.md §3.8 already chose
- Context documents: [PORT-PLAN.md](../PORT-PLAN.md) §3.8, §5/M0 (S3), §8 risk 2;
  [ADR 0001](0001-stack-winui3-uno-csharp.md); Rust `src/rte/` at tag `rust-final`
- Evidence: [0006/](0006/) (sample-text captures and the harness log)

## Context
The composer is a custom native editor (PORT-PLAN.md §3.8): the Rust document model, commands and
HTML in Core, and a view that lays text out and paints it itself, with one text engine on both
heads. The plan proposed Topten.RichTextKit on SkiaSharp and named three risks for S3 to settle:

1. Does RichTextKit work with the SkiaSharp the heads use (the plan worried about SkiaSharp 3)?
2. Does its text look like a XAML `TextBlock` (the gate: Segoe UI 15 px, side by side)?
3. Do the caret geometry and the hidden-`TextBox` IME path hold up?

## What the spike built
| Piece | Where | Proves |
|---|---|---|
| Core document model (port of `rte/model.rs`, its 7 tests, 4 new offset tests) | `src/Barbican.Core/Editor/`, `tests/Barbican.Core.Tests/Editor/` | The Rust model ports one to one onto C# records and lists |
| Layout library: `ITextLayoutEngine`, `RichTextKitEngine`, `DocLayout`, `StyleMapper`, `BlockMetrics` | `src/Barbican.Editor.Layout/` (net10.0, no UI types) | Layout, caret, hit test, selection, paint; runs headless |
| 14 layout tests | `tests/Barbican.Editor.Layout.Tests/` | Same results on Windows (Segoe UI) and the Linux VM (DejaVu Sans) |
| WinUI harness | `spikes/s3-editor/S3Harness/` (not in the solution) | TextBlock against `SKXamlCanvas`, caret overlay, hidden `TextBox`, perf |

The harness window is moved outside every monitor, hidden from Alt+Tab and the taskbar and shown
without activation (`AppWindow.Show(false)`), captured with `RenderTargetBitmap`, and exits by
itself: it never touches the user's screen, mouse or keyboard. Run it with
`S3Harness.exe <outDir>` after `tools/build.ps1 build -Project spikes/s3-editor/S3Harness/S3Harness.csproj`.

## Findings

### 1. RichTextKit and SkiaSharp versions: no vendoring needed
- Topten.RichTextKit **0.6.173** (2026-11-12) depends on **SkiaSharp 4.153.1** (SkiaSharp is at 4.x
  now; the "SkiaSharp 3" worry is obsolete). Uno 7.0.65 (`Uno.WinUI.Graphics2DSK`) uses
  SkiaSharp **4.156.0**; `SkiaSharp.Views.WinUI` 4.156.0 exists. The repo pins SkiaSharp,
  SkiaSharp.HarfBuzz and the Linux native assets to 4.156.0 and HarfBuzzSharp to 14.4.0.100
  (`Directory.Packages.props`): native assets must match the managed version exactly, and
  RichTextKit alone would pull 4.153.1 natives.
- RichTextKit is **Apache-2.0** (the plan said MIT). Apache-2.0 code can be combined into an
  AGPL-3.0 work; vendoring it would mean keeping its license and NOTICE file.
- Both OSes load the natives and the fonts: 14/14 layout tests pass on Windows and on the VM.

### 2. Text quality against XAML `TextBlock` (the gate): passes, with a contrast curve
Same paragraph (sample text with ligatures, quotes, accents, ½ and €), Segoe UI 15 px, line height
22.5 px, 420 DIP wide, scale 1.0, captured with `RenderTargetBitmap`:

| Measure | Result |
|---|---|
| Line breaks | identical (4 lines, same characters on each) |
| Caret x vs `TextPointer.GetCharacterRect` | mean 0.08 DIP, max 0.26 DIP over all 252 characters |
| Line ink extents (left/right) | identical to the pixel |
| Baseline | TextBlock 0.8 DIP lower (see 4) |
| Ink (darkness), Skia's own coverage | 85 % of TextBlock's: visibly lighter |
| Ink with `TextContrast = 1.6` | 99 %; mean pixel difference 3.2/255, no pixel off by more than 64 |

DirectWrite's grayscale text applies enhanced contrast; Skia's coverage does not. Hinting, subpixel
positioning and LCD edging were all tried (harness log): the default (grayscale, normal hinting,
subpixel positioning) is the closest, and only a contrast curve closes the gap.
`LayoutStyle.TextContrast` paints the text through a layer whose alpha table maps coverage `a` to
`1 - (1 - a)^k`; the selection is painted as rectangles outside that layer. See
[0006/zoom-textblock-k16-k1.png](0006/zoom-textblock-k16-k1.png): TextBlock, Skia k = 1.6,
Skia k = 1, enlarged 4×.

The Win2D (DirectWrite) second backend the plan kept in reserve is **not needed**.

### 3. Offsets, caret geometry and hit testing
- **Offsets.** Rust used UTF-8 byte offsets; Core/Editor now uses **UTF-16 code units** (C# string
  indices), never splitting a surrogate pair, and caret steps are **grapheme clusters**
  (`TextOffsets`, UAX #29) where Rust stepped by code point. RichTextKit counts UTF-32 code points;
  the layout maps with its own `CharacterToCodePointIndex`/`CodePointToCharacterIndex`, so nothing
  outside `Barbican.Editor.Layout` sees code points. RichTextKit's caret stops are grapheme
  boundaries too (test: no stop inside an emoji with a skin tone or between `e` and a combining
  accent).
- **Line height.** RichTextKit's `Style.LineHeight` multiplies the font's own line spacing
  (ascent + descent + leading, 1.33 for Segoe UI), while CSS and Rust's parley multiply the font
  size. `StyleMapper.CssLineHeight` converts, so a 15 px line is 22.5 px as in Bulwark's composer;
  tested on both fonts.
- **Caret.** `GetCaretInfo` gives a rectangle per position, with `AltPosition` for the end of the
  previous line at a soft wrap (Rust's upstream affinity): tested.
- **Hit test.** Hitting the middle of every caret returns that caret, on wrapped LTR text and on mixed
  text. One known exception, pinned by a test: at a **direction boundary** (LTR text next to a
  Hebrew run) two logical offsets share one visual x, and RichTextKit draws the logical end of an
  RTL run at its visual left, so the hit returns the other offset. M3 needs explicit BiDi caret
  affinity (which run the caret belongs to) for arrow keys and clicks at run boundaries.

### 4. Vertical placement differs by design
With `LineStackingStrategy.BlockLineHeight`, XAML spreads the extra line height in proportion to
ascent and descent; CSS (Bulwark) and RichTextKit split it in half. The baseline is 0.8 DIP lower in
the TextBlock at 22.5 px. The editor follows CSS. Where an editor line must align with an adjacent
XAML text line (for example the subject field), position the canvas by the baseline, not the top.

### 5. Hosting the canvas
- **Windows: `SKXamlCanvas`** (CPU raster into a `WriteableBitmap`). It composes in XAML like any
  element and is captured by `RenderTargetBitmap`, which the hidden-automation screenshots need.
  Cost measured on a 60-paragraph draft at 150 % (1290×1600 px): full layout 5.8 ms, full paint
  13.8 ms, re-layout of one edited block 0.25 ms. The full paint is close to a 60 Hz frame, so M3
  paints only the visible blocks and caches each block's layout (Rust's `View` cache did the same).
- **`SKSwapChainPanel`** (GPU) is the fallback if paint cost grows; `RenderTargetBitmap` does not
  capture swap chains, so its screenshots would need Windows Graphics Capture (spike S4).
- **Uno: `SKCanvasElement`** (`Uno.WinUI.Graphics2DSK`) draws straight into Uno's Skia compositor.
  Not run yet: there is no App head before spike S1. Since Uno's own `TextBlock` is drawn by Skia,
  `TextContrast` should stay 1 there; the gate is to be repeated on Uno.

### 6. IME: the hidden-TextBox path
- **WinUI:** a transparent `TextBox` (opacity 0) at the caret, moved with the caret overlay. The
  harness puts text into it the way committed IME text arrives, and `TextChanged` forwards it into
  the `Doc` at the caret with the caret's marks, then empties the box: works (block 252 → 254
  characters, caret and box moved from x 34.6 to 57.4). `TextCompositionStarted/Changed/Ended` are
  wired; with no real IME input allowed on this machine they did not fire, as expected.
- **Uno 7.0.65 on X11:** the source has `IBusInputMethod` and `FcitxInputMethod`
  (`src/Uno.UI.Runtime.X11/IME/`) feeding `TextBoxCore.IME`, which raises the same composition
  events. Read in the source, not run.
- **Preedit display.** RichTextKit has IME underline styles (`UnderlineStyle.ImeInput`,
  `ImeConverted`, `ImeTargetConverted`, `ImeTargetNonConverted`), so M3 can draw the composition
  inline in the editor from `TextCompositionChanged` and the box's text, and keep the box invisible.
- Real IME input (Japanese, Chinese, dead keys, emoji panel) is a **[HAND TEST]** in M3 on both OSes.

### 7. A changed fact: Uno 7.0 has a Skia `RichEditBox`
ADR 0001 and PORT-PLAN.md §3.8 say `RichEditBox` is not on Uno Skia. That was true for Uno 6.7
(one file in `src/Uno.UI/UI/Xaml/Controls/RichEditBox/`), but **Uno 7.0.65 ships a 36-file
implementation** with IME, formatting and accessibility. The choice of a custom editor still stands:
the other reason in §3.8 is unchanged (an RTF/`ITextDocument` model cannot round-trip Bulwark's HTML:
tables, nested lists, the collapsed quoted original, `cid:` images, Bulwark's spacing), and WinUI's
`RichEditBox` (the RichEdit control) and Uno's reimplementation would still behave as two editors.
Recorded so the user can reconsider with the new fact; nothing in this spike depends on it.

## Decision
- **Engine:** Topten.RichTextKit 0.6.173 on SkiaSharp 4.156.0 on both heads, behind
  `ITextLayoutEngine`/`IBlockLayout` (UTF-16 offsets outside, code points inside).
- **Model:** `Barbican.Core.Editor` as ported here (UTF-16 offsets, grapheme caret steps); M3 ports
  `editor.rs` and `html.rs` onto it.
- **Layout and paint:** `Barbican.Editor.Layout` stays a separate net10.0 project (Skia and
  RichTextKit, no UI framework), so its tests run headless on both OSes. The App head only hosts it.
- **Windows host:** `SKXamlCanvas`, `TextContrast = 1.6`, grayscale antialiasing, normal hinting,
  subpixel positioning. **Uno host:** `SKCanvasElement`, `TextContrast = 1` until measured.
- **Overlays:** the caret, the IME box, the floating toolbar, the link popover, the table menu and
  image handles are XAML elements over the canvas; the selection is painted in Skia behind the text.
- **IME:** the hidden `TextBox` at the caret, preedit drawn inline with RichTextKit's IME
  underlines.

## Fallbacks
| If | Then |
|---|---|
| RichTextKit stalls or a bug blocks M3 | Vendor it into `src/ThirdParty/RichTextKit` (Apache-2.0: keep LICENSE and NOTICE) |
| Windows text quality fails a later check (other scales, ClearType expectations, dark theme) | Win2D `CanvasTextLayout` backend behind `ITextLayoutEngine` for the Windows head; model and commands unchanged |
| CPU paint is too slow after viewport culling | `SKSwapChainPanel` on Windows (screenshots through WGC, spike S4) |
| Uno's X11 IME does not deliver composition events | Show the `TextBox` at the caret during composition (the Rust approach), commit into the `Doc` on end |

## Pending (owner: M3 editor-view stream, after S1)
- Repeat the gate on Uno (`SKCanvasElement` next to Uno's `TextBlock`) and at 125 % and 150 %
  scale on Windows (the harness ran at 100 %), in dark theme as well.
- Real IME [HAND TEST] on Windows and Linux (IBus and Fcitx).
- BiDi caret affinity at direction boundaries; visual (not logical) arrow-key movement in RTL runs.
- Viewport culling and per-block layout cache; measure typing latency with the frame probe (S4).
- List markers, tables, images and the quoted original in `DocLayout` (the spike draws atomic
  blocks as placeholder boxes and no list bullets).
- `TextContrast` for coloured text and highlights: the alpha curve also darkens anti-aliased edges
  of coloured runs; check links and highlights against TextBlock.

## M0 review (stream m0-review, at the merge of all M0 branches)
- At the merge, the SkiaSharp 4.156.0 pins are withheld from the Uno.Sdk 6.7 App head (see ADR 0004,
  "M0 review"). The editor projects and their tests use 4.156.0 on both OSes (62/62 green). The App
  head may reference `Barbican.Editor.Layout` only after the Uno 7 move (the first M1 task) or after
  the vendoring fallback. The "Pending" Uno gate runs after that move.
- The number 0006 was kept; the S4 and S5 records, which had also used 0004 and 0005, became 0007 and 0008.
- `RustTestAttribute` now lives in `tests/Shared/` for every test project. The `rte::model` tags drop
  the `tests::` segment to match `spec/rust-tests.csv`, where the 7 rows are now `ported`
  (`tools/spec-check.py`).
- Uno 7's Skia `RichEditBox` does not change the decision (RTF cannot round-trip Bulwark's HTML), but it
  contradicts PORT-PLAN §3.8, so the user should see it (listed in `docs/FABLE-REVIEW.md`).
