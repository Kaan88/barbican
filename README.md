# Barbican

A native desktop mail client for [Stalwart](https://stalw.art) over JMAP, with calendar, contacts,
files and server administration. **WinUI 3** on Windows, **Uno Platform** on Linux, all in C#.

Barbican aims at parity with the [Bulwark](https://github.com/bulwarkmail/webmail) web client and
its server data (Sieve filters, templates, tags, settings), with a native UI: the only web content
is the body of an e-mail. It is a port of an earlier Rust/Slint version; the plan, the decisions
and what was learned there are in [docs/PORT-PLAN.md](docs/PORT-PLAN.md).

Windows development is at M6b. M6 delivered a portable preview. The [roadmap](docs/ROADMAP.md) tracks milestones and the [M6b work list](docs/milestones/M6b-plan.md#current-work) identifies the next visible fixes and add-on checks.

## Build
Requirements: the .NET 10 SDK. On Windows the app head also needs the Windows App SDK (restored
from NuGet).

```powershell
tools/build.ps1          # build
tools/build.ps1 test     # build and run the tests
```

`tools/build.ps1` (and `tools/build.sh` on Linux) limits how many builds run at once on a machine
and runs them at low priority; see the script header. Contributors and agents: read
[CLAUDE.md](CLAUDE.md) first.

## Repository map
- `src/`: Core (JMAP, sync, services), Presentation (ViewModels, tokens), Platform, the App head
- `tests/`: xUnit v3 suites; the Rust app's tests are ported first, as the spec
- `spec/`: the behaviour catalogue the port is checked against
- `design/`: the approved Bento × Nordic mockups and the motion rules
- `docs/`: plan, roadmap, architecture decisions (`docs/adr/`), Rust-era documents (`docs/legacy/`)
- `prototype/winui/`: the WinUI 3 prototype the port started from

## License
GNU Affero General Public License v3.0 only ([LICENSE](LICENSE)); see [NOTICE.md](NOTICE.md) for
what is derived from Bulwark and the decision in [docs/adr/0002](docs/adr/0002-public-repo-privacy-license.md).
