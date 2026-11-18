# UI sweep 1 (branch m4-ui-sweep)

Method: hidden automation (`tools/auto-run.ps1`), sample data and the throwaway test account, `wshot` screenshots in `UI-SWEEP-1/`.
Severity: H = broken function or layout, M = clipped/missing/misleading, L = polish.

| ID | Screen | Defect | Shot | Sev | Status |
|---|---|---|---|---|---|
| C-05 | Composer, real account | Send always failed with "Not sent" and no reason: the send coordinator toasted from a pool thread and WinUI threw 0x8001010E ("marshalled for a different thread"). ToastService now adds and removes toasts on the UI thread; the composer status shows the reason when the coordinator stays quiet. Test added | g04-sent.png | H | fixed |
| C-06 | Composer autocomplete, real account | `ChipSources.RecentFromSent` threw ArgumentNullException when a sent summary had a null Cc/To (crash log, suggestions never loaded). Null-safe now, test added | (crash log) | H | fixed |
| R-01 | Reader toolbar | Archive and Delete were hard-disabled ("M2/M3") although the commands exist; now run `mail.archive` / `mail.delete` on the open message | a02-reader.png | H | fixed |
| R-04 | Reader toolbar, Nordic / narrow pane | Seven buttons in a StackPanel: Delete and More cut off at the pane edge. New `WrapRow` panel wraps them | c03-nordic-dark.png, d01 | M | fixed |
| R-05 | Reader header, Nordic / narrow pane | Sender name squeezed to "Dav" or nothing by the verdict pill and Details; the pill now drops under the line below 440 px, name has a minimum width | d02, d03 | M | fixed |
| L-02 | List, Nordic multi-select | Ticked rows showed no sign of selection (no avatar to turn into a check); ticked Nordic rows take the hover tint and a check | d01, d02 | M | fixed |
| C-03 | Composer, docked reply (Schedule, Templates cards) | Card taller than the docked pane: buttons cut off. Card now scrolls, margin smaller | e07, f01 | M | fixed (scrolls; still tall, see open) |
| D-01 | Dialog host | Cancel/OK buttons square, every other button a pill | f04 | L | fixed |
| R-02 | Reader header, Bento | Date line truncated ("Mon 0...") when the pane is narrow | a02 | L | open: needs a design decision on where the date goes |
| C-04 | Docked reply | The inline "Reply to ..." bar stays above the open reply composer | f02 | L | open |
| P-01 | Palette | Forward, Delete, Mark as spam rows have no icon; the list is cut at the "Actions" header without a scroll hint | b02 | L | open |
| U-01 | Unified inbox | Rows carry no account marker | f03 | L | open (check Bulwark's unified list) |
| X-01 | `toast` automation command | Not implemented (listed in the protocol) | - | L | open |
| W-01 | Window | Below 960 px logical the shell overflows; the real window has a 960 minimum (`ShellLayout.MinWidth`), `resize` bypasses it | c04 | n/a | not a defect |
