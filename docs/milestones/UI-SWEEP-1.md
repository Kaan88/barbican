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
| G-01 | Glance "Flagged" tile | Count stays stale after starring (loaded once at start). Reloaded on every folder/tag change; in-folder star still waits for the next change | g06 | M | fixed (partly), live refresh on star open |
| C-07 | Composer Schedule card, docked | Last button still clipped by a few px after the scroll fix; spacing tightened (unverified by shot) | g10 | L | fixed, needs re-shoot |
| C-08 | Docked composer, keyboard focus | A focus ring stays on the Cc button behind the scrim while a card is open | g10 | L | open |
| A-01 | Move with a bad target | `act mail.move Archive` (a name, not an id) shows the raw server error "<emailId>: Invalid mailboxIds ..." in the toast | - | M | open (friendly message, validate target) |
| R-06 | Conversation strip | Rows show only sender and time, same text for each row of one sender; no snippet or subject | g06 | L | open |
| A-02 | Automation | `compose-do schedule N` replies "not sent" although the message was scheduled | g10 | L | open |
| N-01 | Real data previews | "Heading One Normalboldword" style previews come from old HTML-only messages (server preview); a fresh send from this app previews with spaces ("Heading Three line b line c") | - | n/a | verified, not a defect of ours |
| S-01 | Reader scheduled banner (Reschedule, Cancel and edit, Cancel send) | Every button answered "Unknown action": ScheduledBannerActions was never registered as an IBannerActionHandler. Now it is (Owns + RunAsync(snapshot, ...)); Cancel send verified on the test account ("Sending cancelled. Moved to Drafts.") | - | H | fixed, test added |
| S-02 | Reader scheduled banner | After Cancel send the banner stays on screen until the message is reopened | - | L | open |
| S-03 | Folder switch by click | The reader keeps the previously open message when another folder is opened (a g d jump clears it) | m7 | L | open |
| K-01 | Keys on real data (test account) | j/k, Enter, Escape, /, ctrl+k (palette), ?, g i / g t / g d (two keys, 1.5 s apart), s all behave; g s (Starred) is unhandled when the account has no flagged folder, x and r without a selection are unhandled | k-*.png | n/a | verified, no defect |
| N-02 | Sent text part (DocHtml.ToText) | Blocks are joined with newlines (HtmlExporter.ToText); fresh sends preview with spaces on the server. Run-together previews come from old HTML-only mail, whose preview the server builds, so only a client-side body fetch could change them | - | n/a | verified, not fixable without fetching bodies |

Cleanup on the test account (done): all "UISWEEP" mails removed from Inbox, Sent, Drafts and Trash (Trash emptied of them); the message scheduled for Thu 19 Nov 08:00 (the forward of "UISWEEP three") was found by moving it back to Sent, its send cancelled through the banner, then deleted. Light/Nordic composer on real data, hover strip, attachments on real data and the motion/flicker pass were not done; the open rows above stay open. Real-account screenshots (g*, h*, k*, m*, n*) stay untracked (rtifacts/shots, ignored).
