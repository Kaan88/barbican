# Current UI guidelines

Approved by the user on 2026-11-19. Design studies by Codex / Terra High; direction recorded by the Sol coordinator.

The selected directions are **Bento Balanced**, **Nordic Focus** and **Studio**. Use these as the UI guidelines from now on, including work by Claude and future Codex agents. Bento Balanced and Nordic Focus refine the existing styles; Studio is also approved as a distinct layout direction. Record its production integration in the milestone plan before extending the current two-style selector. Where the older design boards differ, these directions take precedence. Preserve established functionality, accessibility and safety rules while applying the selected visual language.

## Bento Balanced

![Approved Bento Balanced direction](mockups/approved-2026-11-19/bento-balanced-light.png)

Keep separate rail, folder, message-list, reader and context tiles. Use calm spacing, clear sender/subject/preview hierarchy, restrained hairlines and consistent rounded corners. Retain the compact two-item glance strip and context panel when space allows. Keep the existing B mark, rose primary accent and semantic tints; avoid decorating every row with competing color.

Starting dimensions in effective pixels: 14 window inset, 12 tile gutters, 66 rail, approximately 208-222 folders, 345-360 list, 190-210 context, and a flexible reader. Tile corners are approximately 24-26. These are implementation starting points, not fixed widths at every text scale.

## Nordic Focus

![Approved Nordic Focus direction](mockups/approved-2026-11-19/nordic-focus-light.png)

Keep the rail and folders on the ground, with one continuous rounded list/reader/context surface separated by hairlines. Favor a spacious reading column and minimal chrome. Use pills/circles and the same B identity and palette. Nordic Focus includes the integrated sender/context panel selected by the user on 2026-11-19; keep the name Nordic Focus. The sender panel MUST be toggleable through an accessible show/hide control with a clear pressed state. Expand the reader when it is hidden, preserve the panel state when toggling, and provide keyboard access. Do not permanently reserve empty panel width.

Starting dimensions: 76 rail, approximately 210-228 folders, 330-350 list, flexible reader and about 190-210 for the context panel when open. Keep the existing shell breakpoints; collapse auxiliary context before compressing the reading area below roughly 420, with its show/hide control still accessible. At narrow widths use list/reader navigation rather than squeezing the entire desktop layout.

## Studio

![Approved Studio direction](mockups/approved-2026-11-19/studio-light.png)

Studio is an approved direction, selected by the user on 2026-11-19. Keep its horizontal app navigation bar, warm parchment/umber character, muted teal accent and restrained 7-10 pixel corners. Retain the folder column, vertical message list and spacious editorial reader. Sender/context information lives behind an accessible show/hide control that returns space to the reader when hidden. Preserve commands, keyboard navigation and focus order when moving app navigation from the rail to the top bar.

Starting dimensions: about 62 high for top navigation, 205 for folders and 405 for the message list, with a flexible reader. Use native Grid, Button, Border and virtualized ListView controls with existing reader/context views. At narrow widths or with enlarged text, adapt navigation and use list/reader navigation rather than compressing text. Map Studio's palette through shared semantic tokens, including dark and high-contrast modes. Atlas remains an unapproved exploration; the user's correction selects Studio instead.

## Modern WinUI 3 polish

The user also requested modern WinUI 3 visual polish. Apply it across the approved directions: layered surfaces, restrained elevation, responsive controls and fluid panel/navigation transitions. On Windows, favor a Mica-style main-window backdrop and selective Acrylic-style material for transient surfaces where supported. Keep mail content and editing surfaces opaque and readable; use subtle shadows and borders to distinguish layers. Provide solid-brush fallbacks for unsupported platforms, disabled transparency and high contrast.

Use native visual states and composition-based decoration for gentle hover/press feedback, a moving selection indicator and short panel entry or expansion transitions. Update model state, focus and content immediately; animation must remain interruptible, follow MOTION.md and become instant under reduced motion. Preserve hidden in-window popup rules. Material support, Uno fallbacks, contrast, keyboard focus and frame performance must be verified during implementation. These are implementation requirements; the current static mockups do not demonstrate native materials or performance.

## All approved directions and screens

- Carry the selected language into Settings, Calendar, Contacts, Files, composer, dialogs and admin views. Support both light and dark modes with the shared palette. The checked-in images illustrate geometry and light/dark treatment; carry each direction through shared semantic tokens.
- Use Segoe UI Variable / Segoe UI, legible body and secondary type, consistent spacing, stable selection indicators and controlled wrapping. Respect text scaling, localization, keyboard focus, high contrast and reduced motion.
- Implement native chrome with existing WinUI 3 controls: Grid, Border, TextBlock, Button, PathIcon and virtualized ListView, plus the existing editor and mail-body host. Use shared brushes, StyleMetrics and StyleState. Do not hand-edit generated token output or introduce browser-only effects into native chrome.
- Follow [MOTION.md](MOTION.md): immediate state/focus/action updates; about 100 ms feedback and 150 ms entry decoration; exits instant or at most 90 ms; no animation over 200 ms; interrupt/retarget rather than queue; zero duration under reduced motion. Preserve incremental lists and avoid per-frame code-behind or refresh animations across every row.
- Existing popup, automation and account-safety rules remain mandatory. Use in-window selection/dialog patterns where required by ADR 0007.

These are approved design guidelines, not evidence that production controls already match them or that native accessibility/performance has passed. Record adoption and actual verification in milestone reports and the Codex/Astra implementation review log. Additional alternatives being explored by the same Terra agent remain proposals unless the user selects them.
