# Linux baseline: the first Linux run of M1 (M2 stage F, step 0)

Run on 2026-11-17 on the Linux VM (Debian 13, kernel 6.12, 10 cores) with `tools/linux.ps1 test`.

## What happened
| Attempt | Branch and commit | Result |
|---|---|---|
| 1 | `m2` at `ceba042` (m1 plus the sign-in hotfix, the M2 plan) | **Build failed at restore**: `NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true` on `src/Barbican.Platform.Windows` (the solution lists it). M1 had never built on Linux. No test ran. |
| 2 | `m2-f` at `954dcd5` (the same tree plus one `tools/build.sh` change: `-p:EnableWindowsTargeting=true -p:BarbicanHead=desktop`) | **Build succeeded, tests passed.** Restore took **16 min 28 s** (it re-tried the large download of `Microsoft.Windows.SDK.Win32Metadata` for the Windows project over the VM's slow link; the package is cached now), build 16 min 50 s in all. |

Attempt 2 is the baseline of M1 on Linux: **967 tests, 961 passed, 0 failed, 6 skipped**.

| Project | Result |
|---|---|
| Core.Tests | passed |
| Presentation.Tests | passed |
| Editor.Layout.Tests | passed |
| Automation.Tests | passed |
| Build.Tests | passed |
| Platform.Tests | passed; 3 skipped (2 "Windows only", 1 "no Secret Service on the session bus: Connection closed by peer") |
| Jmap.Live | passed; 3 skipped (live suite, `BARBICAN_LIVE=1` not set) |

## Items for the integrator and the user ("M1 on Linux")
1. **The solution does not build on Linux without a change.** The change made here (the two MSBuild properties in `tools/build.sh`) works, but it makes Linux restore the Windows-only project and its large CsWin32 metadata package. Better: do not restore it on Linux (a Linux solution file without `Barbican.Platform.Windows`, or a project that is empty off Windows). `.slnf` pointing at a `.slnx` is not accepted by this SDK (MSB5025). Not fixed in F beyond the property change.
2. **Secret Service**: the one real Linux-specific test of M1 (`The_secret_service_store_round_trips_when_a_service_runs`) is skipped on the VM (no service on the session bus). The secret store has never been exercised against a real Secret Service. Hand test or a VM with gnome-keyring.
3. No test failed, so there is no "M1 on Linux" defect list from the unit suite. The head itself (`net10.0-desktop` under Xvfb, `tools/linux.ps1 xrun`) was not run in stage F.
