# M6 Windows portable preview

Prepared by Codex / Sol on 2026-11-19 at source checkpoint `aa0e96b1007eeee85459392b82ce1aa4d4760908`. This is an interim test build, not M6 completion.

- Artifact: `artifacts/portable/Barbican-M6-preview-aa0e96b-win-x64.zip` (149,366,518 bytes).
- SHA-256: `42EAB70AE365B5EDEC740901257840F842E59D33D1C22BDAAB107BA4407C2DBA`.
- Extract the entire archive and run `Barbican.exe` inside its `win-x64` folder. No installer or OS changes were performed.
- Full combined Windows Automation suite: 2,833 passed, zero failed, four live/Linux skips. Spec M6, hook generation and token checks passed.
- Release publish and package checks rejected automation components. The archive was extracted and its executable passed the hidden Release smoke check: exit 0, first frame 404.4 ms and shell ready 703.5 ms in that run. Linux was not tested.

Includes settings/server screens, assistant and archive fixes, preference flushing on Windows close, accessibility quick controls and fonts, toolbar labels and three Compose placements. The bundled M6-PREVIEW.txt explains the interim scope. Remaining timezone/vacation work continues toward final M6; further Sieve acknowledgement/modal checks remain in progress. Studio/Focus visual redesign and WinUI polish remain M10 work.

The initial smoke command used a relative report path, so the wrapper could not find its report despite exit 0. The successful verification above repeated the extracted executable with an absolute report path. Reports and package verification files are ignored artifacts; no real-account screenshots were packaged. Nothing was merged to main, tagged or published as a GitHub release.
