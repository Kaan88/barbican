# What the dropped C# port leaves for the Rust app

Written 2026-11-23, after the C# port (WinUI 3 / Uno, archived as `Kaan88/barbican-csharp`) was dropped. See PORT-PLAN-DROPPED.md for why it was started. Most of the C# work is on the branch `m6b/bulwark-ui`, not `main`. Plugin slots are on `m6b/plugin-slots-2`. Paths starting with `cs:` are in that repository. The specs, docs, plugin scripts and tools named here are also copied into `docs/from-csharp/` (same layout); C# source code is not.

The goal is still feature parity with Bulwark webmail 1.21.2. The C# port is useful for three things:
- specs that cite Bulwark file:line;
- security fixes that came out of its reviews;
- a working proof that unmodified Bulwark plugins can run in a desktop WebView.

Its theming approach is not worth copying: most of the trouble came from WinUI's own Fluent styles, which Slint does not have.

---

## 1. Plugins: Bulwark compatibility next to our own

**Bulwark's model** (`bulwark-1.21.2/lib/plugin-*.ts`, `lib/plugin-sandbox/`):
- A plugin is a ZIP (at most 5 MB) holding `manifest.json` and a CommonJS `index.js` that exports `activate(api)` and `hooks`.
- There are three plugin types: ui-extension, sidebar-app and hook.
- Permissions: about 68, with consent covering the permissions plus the plugin's declared web origins and POST paths.
- Hooks: 218, each of one of four kinds (observer, intercept, transform, takeover), with time budgets. Send hooks fail closed.
- Slots: 58, of three kinds (frame, action, data). Each prop needs its own read permission.
- Host API: about 186 methods, called by postMessage RPC. Plugins run in null-origin iframes.
- Privileged tier: an Ed25519-signed bundle installed by an admin.
- Storage: per login, with a 1 MiB quota.

**Rust today** (`src/plugins.rs`, PLUGINS.md):
- Native DLLs with a C ABI, loaded in-process with full trust.
- Three hooks: `email.details`, `compose.check` and `settings`.
- No permissions, consent, packaging, signing, slots or JavaScript.
- `compose_check` fails open: a plugin that errors lets the send through.

**C# port**:
- **Bulwark plugins.** It ran Bulwark's **own** sandbox runtime unmodified inside WebView2: built from the pinned commit, with one origin patch, behind a trusted parent page. The API probe, the proofreading plugin and a signed managed plugin reached Running.
- **Slot hosts.** Partial. Action slots, search results and the side panels were unfinished.
- **Native plugins.** It dropped the C ABI for a .NET interface (decision D-234). Both native and JS plugins used Bulwark's hook names.

| Topic | Rust | C# | Keep for Rust |
|---|---|---|---|
| Native ABI | C ABI DLL | .NET interface | **C ABI** (works from any language) |
| Hook names | our own (`email.details`, `compose.check`) | Bulwark's 218 | **Bulwark's names**; keep the old ones as aliases |
| JS plugins | none | Bulwark runtime in WebView2 | **Yes, on wry** (WebView2 / WebKitGTK are already in `src/platform`) |
| Loading | everything in the folder, on | user plugins off by default, consent per package, hash pinned | C# rules |
| Hook failures | silent `None` | budget per hook; send hooks fail closed | C# rules |

**To adopt, in order:**
1. **Hook and slot catalogue (S, about 1 day).**
   - Copy `cs:spec/plugins/{hooks,slots,host-api}.csv`, its README and `cs:tools/extract-plugin-catalogue.py`.
   - Generate a Rust `hook_names.rs` from them.
   - Rename `email.details` → the `email-details-section` slot and `compose.check` → `onBeforeEmailSend`, keeping the old names as aliases.
2. **Native hook safety (S).**
   - User plugins are off until a setting turns them on (review finding R3-04).
   - Each call runs on a worker thread with a time budget.
   - Send hooks fail closed (R2-10).
3. **Security contracts (S, copy and swap paths).** `cs:docs/milestones/M6b-package-authority.md`, `M6b-runtime-transport.md`, `M6b-api-inventory.md` and `M6b-plugin-slots.md` (on `plugin-slots-2`).
4. **Bulwark-compatible JS host on wry (L, 3–6 weeks for core slots, hooks, storage and fetch).** Reuse:
   - `cs:tools/plugin-runtime-build/`;
   - the four bridge scripts in `cs:src/Barbican.App/Assets/plugins/`, with their `tools/tests/*.mjs` tests;
   - the inspection fixtures in `cs:tests/Barbican.Core.Tests/Plugins/Inspection/Fixtures/`;
   - Bulwark's own `integration/fixtures/plugin-api-probe`.

   Port the slot policy and the dispatcher's permission and size checks to Rust.
5. **Package and consent flow (M).**
   - ZIP inspection.
   - A consent dialog listing the permissions and web origins.
   - A hash-pinned record that starts disabled.
   - Signed managed bundles later.

**Pitfalls the C# reviews found** (`cs:docs/FABLE-REVIEW.md` from line 1853; `fable-review-2.md`; `fable-review-3.md`):
- A hostile iframe can post to the native bridge directly. Check both the source and the origin, and cap message size.
- Tear a plugin down when navigation starts, not at load. Reject reply IDs that have already been used.
- Re-check grants at each operation and on every account switch.
- Re-run the send checks when a transform changes the recipients.
- Never load native code just to read its manifest.
- Removing a visible WebView2 control from the tree crashes the process. Never re-parent a live web view.

## 2. Feature parity: done in C#, missing in Rust

`cs:docs/milestones/M6b-feature-parity.md` holds the live status. Its last column gives the C# file paths, and its long feature cells cite Bulwark file:line. `cs:spec/gaps.csv` is our GAPS.md with a C# status column.

| Area | Rows (C# done or partial, Rust missing) |
|---|---|
| Calendar | G-001/002 (apply a COUNTER; remove a CANCEL; Rust detects both but cannot apply them), G-003 all-day cap, G-004..006 .ics alerts and the webcal LAN block, G-008 print |
| Reader safety | G-053 per-sender trust, G-051 external-sender chip, G-050/049 phishing report, G-052 BIMI badge, G-056 refetch a truncated body, G-047 winmail.dat, RD-05 retention banner, RD-06 rename an attachment |
| Mail list | G-048 copy between accounts (`Email/copy`), G-042/046 folder export, G-073 tag picker and category tabs, ML-11 swipe |
| Composer | G-076 attach from Files, G-063 right-to-left blocks, G-078 next/previous proofreading issue, G-075 @mentions, G-086 disclaimer, G-082 outbox (exactly once by Message-ID) |
| Contacts | G-010/011 print and activity tiles, G-012 vCard limits (D-M5-03, A03, A04) |
| Files | G-013..019: preview, details, thumbnails, tree, upload conflict dialog, zip, new text file, filename rules |
| Account and server | G-027..030 password, TOTP, app passwords, API keys, encryption at rest; G-031 app lock; G-026 masked addresses; G-021..025 vacation and identities; G-032..035 templates; G-036..041 folder admin; G-043 sharing; G-057 quota |
| Shell | G-045/059 notification centre, G-055 pop-outs, G-044 live updates outside mail, SH-11 phone layout, SB-02 folded sidebar, G-085 tour |

**Suggested order:**
1. Calendar P0 rows (exact specs in D-M4-07..11).
2. Reader safety.
3. Files: port `FileTransfer.Zip`/`Upload` and the `FileNames` rules, which are pure logic with tests.
4. Server settings.
5. Account security.
6. Cross-account copy and folder export.
7. Large items last: notification centre, pop-outs, outbox, structured mail, print.

**Reusable specs:**
- `cs:spec/settings.csv` (`rust_read_by` column): the settings Rust still ignores.
- `cs:spec/jmap.csv`: Bulwark line references for each JMAP method.
- `cs:spec/behaviours/*.md`: describe what Rust already does, so they work as a regression list.
- `cs:spec/live-checks.md` (L-001..058): things never checked live in Rust.

## 3. Decisions where C# differed

**Calendar**
- D-M4-09: an .ics import asks about the file's alerts (only when the file has any) instead of keeping them silently.
- D-M4-14 and D-M5-12: printing is a model built inside the app, with a preview in the window.

**Contacts**
- D-M5-03: hard limits on vCard files.
- D-M5-A06: fixes Rust's contact save, which replaced the whole `units` list and lost data.
- D-M5-A07: fixes the 29 February birthday bug.
- D-M5-B03: deleting a category asks first.

**Files**
- D-M5-C03/C04: trash never destroys anything.
- D-M5-14: the upload conflict dialog defaults to "Keep both".

**Settings, mail and security**
- D-M6-A03: settings that nothing uses are labelled "inert" in the UI.
- D-M6-A11 and D-M6-C07: identity order and pinned folders live in Barbican's own metadata keys. **This is not Bulwark-compatible; think twice before copying it.**
- D-071: mail frames never get `allow-scripts`. Rust adds it when the dark or accessibility script runs (`src/mailview.rs:140`).
- D-M6b-08: the AI assistant is a bundled add-on, not part of the app core.
- G-049: Bulwark 1.21.2 has no link-mismatch warning, so that GAPS item should read "links turned off in a reported phishing message".

## 4. Architecture, security and tooling

**Mail frame hardening** (ADR 0005, `cs:docs/fable-review-2.md` R2-04):
- add `base-uri 'none'`;
- strip `<meta http-equiv=refresh>`;
- filter navigation in wry so only `about:srcdoc` loads;
- remove `name`/`id` attributes that hide `document` members (`<img name="querySelectorAll">`), and call through `Document.prototype` in the shell.

**Review findings that apply to Rust:**
- Authentication-Results can be forged through submission on the same server (R3-06).
- `src/jmap.rs:72` follows redirects with reqwest's defaults. Redirects should be followed by hand, with credentials sent only to same-origin hops and no https→http switch.
- Decoders need size caps (R2-08).
- Sign-out must remove per-account caches and WebView data (R3-11).
- A 5xx response to a send means the outcome is uncertain; never re-send blind.
- Two instances on one profile can lose a rotated refresh token (R2-06).

**Credentials** (ADR 0008):
- Keyring entries named per profile (`BARBICAN_PROFILE`), so dev and automation runs never see the user's sign-in.
- Local persistence, not roaming, for tokens.

**Theme:**
- Diff `cs:design/legacy-slint/theme.slint` (already Slint syntax; 585 lines against our 330) into `ui/theme.slint`.
- Worth taking: the `Bulwark` palette from `globals.css`, the feedback hover/pressed/selected ladder, `unread-wash`, the `sb-row-*` tokens and the contrast fixes (hover ≥1.15, text ≥4.5:1).
- Not worth taking: the three-style matrix, or theme import before the default look is solid.

**Tooling, cheap to port:**
- `cs:tools/privacy-check.sh` as a pre-push hook. **This repository is public now.**
- The `BARBICAN_LIVE_WRITE` gate for live write tests.
- A FakeJmap server and `BARBICAN_SAMPLE=1` data for screenshots.
- A profile-scoped debug log with no secrets.
- Clippy `disallowed-methods`, in place of BannedSymbols.
- ADR 0009's started-marker rollback, on top of `src/updater.rs`.
- Retire `tools/drive.ps1`: it moves the real cursor. The rule is to never touch the real screen, mouse or focus.

**Do not adopt:**
- the XAML token pipeline;
- the RichTextKit editor;
- the Uno/Linux head work;
- the T-row and review-gate process (a review log of 3,800 lines);
- the UI Automation click layer;
- C#'s credential names.
