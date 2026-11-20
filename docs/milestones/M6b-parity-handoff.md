# M6b Bulwark parity handoff

**Historical UI checkpoint, November 20.** Later verification is in the [Windows results](M6b-solo-parity.md#native-interaction-check). Use the [current work list](M6b-plan.md#current-work) when resuming. The following is the original pause record.
The user reported that the preview theme does not match Bulwark.
Treat visual and interaction parity as unfinished. **Astra has not approved
parity.** Astra reviewed `6de166e` and requested changes; later fixes have not
passed a re-review. Do not start M7.

## Where to resume

- C# Barbican, branch `codex/m6b-bulwark-ui`, worktree
  `<downloads>/wt-m6b-bulwark-ui`. Last code checkpoint: `b459504`.
  The in-flight integration finished after the pause: it was pushed and the
  Windows Automation app built with **0 warnings and 0 errors**. No combined
  hidden UI or visual verification was run at that revision.
- The coordinator branch `codex/m6b` is separate. Its main checkout had
  pre-existing test-file deletions; do not reset it or use it for a clean
  verification build. This handoff covers UI parity, not plugin runtime readiness.
- Pin the comparison to [Bulwark 1.21.2](../../spec/README.md#pinned-references).
  See the [parity ledger](M6b-ui-parity.md) for source paths, observed behaviors,
  and the existing `spec/` catalogs. The live walkthrough prioritized Mail,
  thread reading, quick reply and composer windows; Calendar, Contacts, Files
  and Settings still have unverified nested dialogs and write variants.
- Bento Balanced, Nordic Focus and Studio design work remains on hold.
  Preserve existing saved legacy styles while changing the Bulwark default.

## What landed, and what it proves

| Slice | Landed | Evidence and limit |
|---|---|---|
| Inbox and shell | `7c63a25`, `b459504` | Flush 56/256/384 px panes; select-all, search and folder actions; dedicated rail/sidebar/list surfaces, no default Bento mark, revised typography and toolbar density. Worker light/dark captures exist; the combined branch has only a build at `b459504`. Folder menu is incomplete, and the Windows title strip and duplicate conversation rows remain. |
| Reader | `9dd3d5e`, `8f41a68`, `aca6ebf` | Hidden automation verified fullscreen moves the reader x696 → x56 → x696 with the rail retained. The 32 px fullscreen button and disclosure placement were measured; 23 reader tests passed, including the eight-message model case. The native sample still has five messages. Opening a header still swaps the single body rather than expanding another card. |
| Quick reply | `6de166e`, `55868c8` | Hidden automation verified Cancel, no text carryover on message switch, and full-editor handoff with text above the quote. The footer now fills the reader with a 24 px input inset and 58 → 78 → 58 px input heights. The hint still uses the displayed sender, not a resolved reply envelope. Inline Send, Ctrl+Enter, undo and contributed actions are not done. |
| Composer | `45df962`, `9ce77a7`, `6e6e86e` | Send/Schedule eligibility passed 41 composer tests and hidden control checks. Seven dock layout tests passed; hidden automation measured 508 px in an 846 px pane and verified draft subject survives compact “Show the message” / “Back to reply.” Later surface, field and footer styling has worker light/dark captures and an integrated build. No drag divider, composer fullscreen or pop-out round trip yet. |

The available Windows x64 ZIP is
`artifacts/portable/Barbican-M6b-Bulwark-preview-9ce77a78cdaf-win-x64.zip`
in the main checkout. It was built from `9ce77a7`, **before** the latest inbox,
reader and composer changes. The extracted Release app reached shell-ready
in 885 ms and exited 0. [Checksum and revision](../../spec/feel/M6b/windows-bulwark-preview.json).
That proves startup, not parity; it is not the final M6b portable.

## First work after resume

1. **Verify the combined visuals before changing more styling.** Start hidden
   Windows automation on the integrated revision with synthetic mail. At the
   same client size as the reference, capture selected inbox, expanded
   conversation, quick reply, and new and docked composer in light and dark.
   Compare hierarchy, density, typography, icons, surfaces, borders and control
   shapes. Also check narrow layouts, keyboard focus and enlarged targets.
   Local native baselines are in `artifacts/auto/8942/` (`baseline-8f41a68-*`);
   separate worker captures are under `artifacts/auto/8935/` and `8936/` in the
   composer and rail/list worktrees. They are not committed or portable.
2. **Fix the inbox and reader structurally, not with cosmetic deduplication.**
   Use server-side thread collapse where appropriate, with expandable members.
   Verify a thread spanning pages, threading disabled, stable selection and
   explicit message ownership for row actions. Build independently expandable
   reader cards: in an eight-message synthetic thread reveal five hidden
   headers, open an older card without closing the newest, and verify Reply
   and Print target that card. Preserve message/account ownership and body
   isolation.
3. **Complete the write and window flows.** Use the same resolved quick-reply
   recipients for the hint and send, including Reply-To and self-sent cases.
   Verify no duplicate shortcut submission, text retained on failure, undo,
   and message/account switches. For dock/fullscreen/pop-out, keep exactly
   one editable draft and verify recipients, body, quote, attachments and
   close/save/discard across transitions.
4. **Close the remaining walkthrough and review.** Record unchanged and
   missing behaviors against the source and live reference, including other
   app surfaces. Submit the exact candidate revision to Astra with rendered
   comparisons and interaction evidence. Fix and re-review until Astra
   explicitly approves, then build and smoke-test the final milestone
   portable from that approved revision. Pause before M7.

Use only repo build wrappers and hidden in-app automation; never drive the
real desktop. Before capturing, confirm the applied theme and visible target
controls. For the reader use whole-window capture with the WebView rendered;
XAML-only capture proves chrome, not the body. Keep real-account material
out of Git. Follow [CLAUDE.md](../../CLAUDE.md) for privacy, accounts and
delegation rules.
