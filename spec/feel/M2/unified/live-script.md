# M2 unified: live script (written, NOT run; the integrate stage runs it)

L-038 (mail export of a folder and import of an .mbox). Sequential, on `<test-account>` only, with the Automation build, port 8885, a profile with
`<test-account>` signed in by the integrator (a password-kind entry of the ADR 0008 import; no OAuth token refreshed). `<readonly-account>`
is never written: `MailImportService` checks `myRights.mayAddItems` and the read-only client refuses Email/import anyway.

1. Make a folder for the run: `act folder.new MXUNI-Test` (menus/actions), wait for its sidebar row.
2. Put two messages there: `rx-import MXUNI1` and `rx-import MXUNI2` (`AUTOMATION_WRITES` build), then move both into `MXUNI-Test`
   (`dnd-sim`).
3. Export (G-046): `MailTransfer.ExportAsync` through the folder menu's Export item once menus wires it (request in the Fable section); until then
   the integrator runs `MailExportServiceTests`' steps by hand in a scratch test against the live client (read-only on the server). Expect 2 messages
   in the .mbox, `Status`/`X-Status` lines, Message-IDs kept.
4. Import back: `import-sim` refuses a non-loopback server by design, so the import runs through the folder menu's "Import messages…" with a
   queued pick (`TransferPicks.Queue`): import the exported .mbox into `MXUNI-Test`. Expect `Imported 0 messages, 2 already in the folder`
   (dedupe by Message-ID against the folder).
5. Import a fresh .mbox of one message with token MXUNI3 into `MXUNI-Test`: `Imported 1 message`; `rx-find MXUNI3` finds it with the mbox
   `Status: RO` state as `$seen` and `receivedAt` from its Date header.
6. Cleanup: `rx-destroy MXUNI1 ...`, `rx-destroy MXUNI2 ...`, `rx-destroy MXUNI3 ...`, delete `MXUNI-Test` (`act folder.delete`, confirm), and prove
   it: `rx-find MXUNI` returns nothing and the folder is gone. Delete the exported files from the run's temp folder.
7. `popup-guard` 0 open, `focus-guard` unchanged, `close`.

Unified on live (optional, read-only): with `<test-account>` and `<readonly-account>` both signed in, `unified on`; the list merges both inboxes;
a star on a `<readonly-account>` row must be refused before any request (read-only client) and the per-account request log
(`BARBICAN_JMAP_TRACE`) must show no Email/set for it.
