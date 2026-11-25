# UI jank audit (2026-11-25, main at 687ccb7)

A read-only audit taken from the code, for ROADMAP "Order from here", step 2.1. Nothing here was measured in the running app. An item marked "(inferred)" comes from reading the code, not from watching it run. Line numbers will drift as fixes land.

Two root causes account for much of the jank:
- **Popups share the main window.** Slint's winit backend draws popups inside the main window's surface, not in windows of their own ("We don't render popups as separate windows yet", i-slint-backend-winit 1.18.1 winitwindowadapter.rs:1215). Every menu or dialog therefore sits under the WebView2 child window that shows the mail body.
- **`Settings::load` reads the disk every time.** Each call reads and parses prefs.json (settings.rs:158-166), and there are about 105 call sites, some of them on every keypress or every list rebuild.

## Ranked issues (the one felt most first)

| # | Sev | Issue | Where | Fix | Size |
|---|---|---|---|---|---|
| 1 | high | Keyboard focus sticks in the mail body after a click, and composer file drag-drop is dead. A window has only one winit event filter, and motion re-installs its own after the loop starts, replacing the mailview focus hook and the rte drop handler. | main.rs:1139, 1142, 2503; motion.rs:35-60; rte/mod.rs:814-832; mailview.rs:324-337; automation.rs:217 | Install one combined filter in motion.rs. The other modules expose handler functions for it to call. | S |
| 2 | high | Menus and dialogs are hidden behind the mail body. `body-visible` misses: the reader move, tag and more popups; the account popup; notices; the prompt; Share; Filter; contact import; the Files dialogs; the PIN dialog; the toast; search suggestions; the drag ghost. | app.slint:2256-2258 | One `overlay-covers-body` flag, the OR of every popup and dialog. Drive `body-visible` and `any-overlay` from it. | M |
| 3 | high | The UI freezes after scrolling, resizing or opening a message. Every "dirty" IPC message runs PrintWindow over the whole client area, then swaps BGRA to RGBA one pixel at a time on the UI thread. (Cost inferred.) | mailview.rs:852-869; capture.rs:26-82 | Capture only the WebView's own area, and only when an overlay opens. Skip the capture when nothing changed. Swap channels in bulk. | M |
| 4 | high | Moving the selection with the keyboard (j/k, arrows) does not scroll the list, and the same happens in contacts and files. | main.rs:2205-2222 | Set `viewport-y` to the selected row. This gets easy once rows have a fixed height (#12). | S-M |
| 5 | high | Opening a message does heavy work on the UI thread: the HTML is cloned twice, scanned in full several times, and sanitised (up to 64 passes). The document travels as a JSON string of several MB that includes base64 cid images. Settings are loaded more than 6 times per open. | main.rs:169-200, 333-381, 1028-1052; msgload.rs:221-320; mailview.rs:224-229, 293-296, 364, 419-517 | Build and sanitise the document on the fetch thread and keep it in bodycache. Take one Settings snapshot per open. | M |
| 6 | med-high | Every key press and every list rebuild reads and parses the settings file from disk. `A11y::load` also reads the registry 3 times per key. | settings.rs:158-166; shortcuts.rs:475; main.rs:2226 | Keep the parsed settings in memory and invalidate them on save and on sync. Cache `System::detect`. | S-M |
| 7 | med-high | The reader content jumps while a message loads. The prefill clears the attachments, the thread strip and the banners, then everything comes back. | msgload.rs:175-199 | Change only the header text during prefill. Swap all rows at once when the message arrives. | S |
| 8 | med-high | The whole list is rebuilt on every checkbox click, select-all, load-more, sync or mark. Each row runs several parses, and each rebuild loads the settings 4-5 times. | mail.rs:567-674, 855, 900, 928, 933-951, 1160; main.rs:1483, 1661, 1685, 1744, 2594 | Update single rows with `set_row_data`. Build only the appended rows on load-more. Read the prefs once per rebuild. Cache the gravatar hash and the cleaned preview. | M |
| 9 | med | The WebView lags one frame behind the layout. A 50 ms timer sets its bounds whether anything changed or not. | main.rs:2531-2534; mailview.rs:891-962; app.slint:2261-2271 | Set bounds only when the rectangle changes, and drive that from the changed handlers. Stop the timer. | S |
| 10 | med | Optional reader sections pop in and push the body around: summary, scheduled bar, thread strip, spam verdict, banners, attachments, docked reply, quick reply. The invite banner grows after a delay. The banner model is recreated on every update. | app.slint:2888-3336; reader.rs:388, 537, 606-609 | Compute the async parts before the message is shown. Reserve fixed slots. Sync models in place. | M |
| 11 | med | Pane widths jump: opening a message adds the 236 px sender panel, the list width switches 368/320 while composing, and the sidebar and chat panel appear and disappear. | app.slint:1750-1768 | Use fixed widths with panels drawn over the content, or add hysteresis. Opening a message never changes the list width. | S-M |
| 12 | med | The list jumps: the selection bar pushes it down, "Loading…" sits above it, and the filter and chips rows push it down too. Row heights vary (date header +30 px, chip +24 px), so the scrollbar jitters. The scroll position is not reset on a folder switch (inferred). | app.slint:1290-1330, 2801-2879 | Draw the selection bar over the header. Give rows a fixed height per density. Reset `viewport-y` on a folder change. | S |
| 13 | med | Focus is lost after some dialogs close: prompt, Share, contact import, Filter, notices, popups. Clicking a folder does not refocus the key handler. | app.slint:2264, 2859 | Use the combined overlay flag from #2. Call `kbd.focus()` on a folder click. | S |
| 14 | med | Escape and Enter are inconsistent. Escape in the prompt closes the reader behind it. Escape in search never clears or blurs it. The plugin key hook can run twice. The context menu has no arrow keys. Some dialogs have no Enter to confirm. | app.slint:2335, 2342, 6269-6290, 6321-6330; shortcuts.rs:457-467; main.rs:2233 | Escape closes the topmost overlay and does nothing else. Escape in search clears it, then blurs it. Enter runs the default action. Call the key hook once. | S |
| 15 | med-high | Switching to Calendar fetches every calendar and every event again. Recurrences are expanded over all events on the UI thread, and every event is checked once per cell. | main.rs:2063-2073; calendar.rs:275-341, 424-480, 496-655 | Skip the reload while the cache is current. Expand only the visible range, on a worker. Bucket events by day. | M |
| 16 | med-high | Files lists the whole account on every folder click. Rendering is O(n²). | files.rs:151-167, 243-399 | Render from the cache straight away and refresh in the background. Index nodes by parent id. | M |
| 17 | med | Switching to Contacts reloads them every time. | main.rs:2068; contacts.rs:268 | Load once, keep the cache current through livesync, and render from it. | S |
| 18 | med | Typing lags in long replies: every keystroke replaces the editor models, and each squiggle is drawn as one box per 2 px. | rte/mod.rs:244-328 | Sync the models in place. Draw one path per squiggle. | M |
| 19 | med | Avatar updates are O(n²), recomputed every 150 ms while avatars stream in. | mail.rs:550-565, 985-1018; avatars.rs:236-240 | Look rows up through an id→index map and a HashSet. | S |
| 20 | med | A selected row looks the same as a hovered row (both use `tile2`). | app.slint:1319 | Add selection and hover tokens. Bulwark uses a blue selection. | S |
| 21 | med (L) | The look differs from Bulwark: the accent colour, radii of 12-28 instead of 6-8, base text 13 px instead of 14, faint-text contrast of about 3.9:1, no pressed/destructive/success/warning tokens, row heights, about 25 hard-coded font sizes, and mixed icon sets. | ui/theme.slint; docs/from-csharp/design/legacy-slint/theme.slint | ROADMAP 2.3 / G-070 part 1 (an owner design call: Bento or Bulwark). | L |
| 22 | low-med | About half of the font sizes ignore the text-size setting. Whole files ignore it, among them calendar_ext, compose-extras, dashboard, productivity and share. | ui/*.slint | Route every font size through the type tokens. | S-M |
| 23 | med (L) | Controls cannot be reached with Tab, and there is no focus ring (`focus-ring` is 0 px). | app.slint:803-883; theme.slint:346 | Add focusable controls and a visible ring, after the theme and layout work. | L |
| 24 | low | Polling timers that could go: ctxmenu at 100 ms (and leaked), palette at 150 ms, desktop at 100 ms on quit plus a 1 s check, applock at 1 s, a 1 ms alt-clear timer. Lite render on the UI thread during resize. Fade-in from 0 opacity on every view. SVG favicon decoding on the UI thread. Folders reloaded every time an unread message is opened. | ctxmenu.rs:1053-1061; palette.rs:535-547; desktop.rs:119-182; applock.rs:793-802; app.slint:3395; main.rs:261-269, 385-388; theme.slint:139-176; avatars.rs:226; msgload.rs:151, 315 | Small, independent fixes. | S each |

## Batches (one worker each, in the order they should land)

- **B: settings cache.** settings.rs, a11y.rs, avatars Prefs, tags defs. Covers #6 and the settings part of #5, #8 and #15-17. Do it first.
- **A: WebView, overlays and focus.** mailview.rs, capture.rs, motion.rs, automation.rs:217, rte `wire_drop`, the main.rs filter and timer lines, app.slint 2256-2347 plus the prompt dialog, the Escape handling in shortcuts.rs. Covers #1-3, #9, #13 and #14. The biggest payoff.
- **C: mail list.** mail.rs, listsync.rs, the `rebuild_list` callers, `move_sel`, MailRow and the list column. Covers #4, #8, #12, #19 and #20.
- **D: reader open path.** msgload.rs, reader.rs, attachments.rs, bodycache, main.rs `page()` and the cid inlining, the reader column conditionals. Covers #5, #7 and #10.
- **E: other apps.** calendar.rs, contacts.rs, files.rs, files_view.rs, the app-switch block. Covers #15-17.
- **F: composer paint, after A.** rte refresh, spell marks, layout. Covers #18.
- **G: theme, needs the owner's design decision.** theme.slint, font-size routing, fade starts. Covers #20 (tokens), #21, #22 and the fade part of #24.
- **H: mail layout and toolbar position, after C and D.** app.slint 1750-1768 and 2663-3337, the settings rows, dnd.rs. Covers #11, S-mailLayout and S-toolbarPosition.
- **Later:** Tab focus and the focus ring (#23), after G and H. The timers in #24 can go into any batch.

## S-mailLayout (reading pane right, bottom or off)

- **Bulwark:** the values are `split`, `focus` and `horizontal`, default `split` (stores/settings-store.ts:446-466). use-effective-mail-layout.ts falls back from split to focus when the panes do not fit. The layout itself is in mail-app.tsx:5794-5844 and 6510-6546.
- **Barbican:**
  - **Switching layouts:** the outer HorizontalLayout (app.slint:2674) needs two `if` branches, horizontal and vertical, sharing the list and reader components, because Slint cannot change a layout's orientation at runtime.
  - **Bottom mode:** a vertical resize handle, with the list height stored the way `Pd.list-w` is in dnd.rs, and minimum heights for both panes.
  - **Off ("focus") mode:** the list is hidden while a message is open, and Escape or Back returns to it (ties into #14). j/k opens the next message.
  - **Widths:** redo the width maths in app.slint:1750-1768 for each mode, plus the fallback from split to focus.
  - Land #9 first, so the WebView does not trail behind during the switch.

## S-toolbarPosition (`top` or `below-subject`)

- **Bulwark:** email-viewer.tsx:1623, 4499, 4603 and 5167-5445.
- **Barbican:**
  - The `acts` row (app.slint:2951-3028) sits above the subject.
  - For `below-subject`, show the same block after the subject and from lines (about line 3089).
  - The move, tag and more popups must anchor to whichever copy is showing.
  - Size S once batch H owns that part of app.slint.
