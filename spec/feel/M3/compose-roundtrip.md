# M3 close: compose, send with undo, undo, reopen the draft, send for real

Scenario test: `tests/Barbican.Presentation.Tests/Compose/EndToEnd/ComposeRoundTripTests.cs` (runs in `tools/build.ps1 test`, no window, no network, no input).
It is the whole flow on the REAL services over FakeJmap (the Core `PipelineRig` is linked in): `ComposerViewModel` with the real `EditorSession`, `DocHtmlCodec`, `ComposeSeeder`,
`DraftService`, `MailSender`, `DirectComposerSender`, and the server's HOLDFOR submissions.

| Step | What is checked |
|---|---|
| type | `InsertText`, `ToggleMark(Bold)`, `Enter` on the editor session (the calls `editor-type`, `editor-cmd` and `editor-key` make); a second paragraph holding `2 < 3 & "quotes"` |
| recipient chip | `To.Edited` + Enter, then `Commit`: one chip `anna@example.org` |
| send with undo | window 10 s: one pending submission, the message filed in Sent (onSuccessUpdateEmail), an "Undo" toast |
| undo | the toast action: nothing pending, Sent back to its old count, the composer reopens (seed) with subject, recipient, text and a draft id; the server copy is in Drafts |
| reopen the draft | `ComposeSeeder` EditDraft from the server: subject, text, chip and the bold run come back |
| send for real | no window: one new message in Sent, Drafts empty, no `$draft` keyword; HTML part `Hello <strong>world</strong>` and `2 &lt; 3 &amp; &quot;quotes&quot;` as escaped; text part `Hello world` / `2 < 3 & "quotes"` |

## Bug found and fixed
Text typed after "Bold off, Enter" came out bold (the whole second paragraph was `<strong>`). `EditorSession.EnterCore` carried the marks of the text before the caret and ignored a mark that had just been toggled
(stored marks). It now prefers the stored marks, as ProseMirror's `splitBlock` does (Bulwark's TipTap); a mark left on still carries over. Test: `EditorBehaviourTests.Bold_toggled_off_then_Enter_starts_a_plain_paragraph_and_bold_left_on_carries_over`.

## Not covered here
The hidden App's `compose-*` commands run on the sample stand-ins (`SampleComposeServices`), which keep nothing and cannot undo; the editor probes address `EditorTestHost`, not the composer's own editor. So the window-level flow is
covered by the screenshots (task 4) and the live round trip (task 3), and the storage-level flow by this test.
