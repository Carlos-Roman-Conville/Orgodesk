# Orgodesk: handoff

Where the work stands, for the next agent. Issues are in [ISSUES.md](ISSUES.md), decisions in [DESIGN.md](DESIGN.md)
section 7, release state in [TESTING.md](TESTING.md).

## 2026-11-23: command mode for OrgoAI (FC-55) and the AI-connection gate (FC-56)

**Built, on main, not in a release.** OrgoAI's first version ships with Orgodesk (controller decision 8aca5a5).

| What | Where |
|---|---|
| The adapter (`IOrgoTool`): 4 lists, status, 13 previews, apply with undo through snapshots | `src/Orgodesk.Commands/DeskTool.cs` |
| The shared lock around layout.json, app and command mode | `src/Orgodesk.Commands/SharedLayout.cs`; used by `UI/DesktopSurface.cs` (SaveSoon holds, SaveNow saves), `UI/TrayContext.cs` (load), `UI/SnapshotsForm.cs` (save, delete) |
| Telling the running app (reload, quick hide, quick show) | `src/Orgodesk.Commands/DeskSignals.cs`; the app listens in `TrayContext.ListenForCommands` |
| `--orgo-command` (and `--data <folder>` for tests and VM copies), registration for the `orgo` CLI | `src/Orgodesk.App/Program.cs` |
| The gate: `CommandSales("Orgodesk", DevBuild)`, bundle-only, Release stops while the bundle id is empty; "Pro free for now" stated | `src/Orgodesk.App/Sales.cs`; tray item "AI connection..." |
| Known folders for `portal folder="Downloads"` | `src/Orgodesk.Windows/Shell/KnownFolders.cs` |
| Tests (66) and the test requests (136 lines, ContractCheck) | `tests/Orgodesk.Commands.Tests`, `tests/commands/requests.jsonl` |

**Run the tests:** `"E:\AI Programs\Toolchain\dotnet10\sdk\dotnet.exe" test Orgodesk.sln` (Debug). A Release build
stops at CS0020 in `Sales.cs` until Rex fills in the suite bundle's Gumroad id in crc-appshell
(`OrgoSuite.BundleProductId`); so do `publish.bat` and `release.bat`. That is on purpose (crc-appshell D-9, D-11). VM
copies are `-c Debug` until then.

**Added later the same day (FC-57):** `rename_group`, `remove_group`, `move_icon`, and the data-safety tests (v1.0.1 data, damaged,
half-written, read-only, kill mid-apply with `tests/commands/kill-mid-apply.py`). **Not built:** pages through commands; pruning old
"Before AI change" snapshots; the Qwen pass rate (no runner run for Orgodesk yet); a VM round of the three new actions.
See FC-55, FC-57. `Sales.ProIsFreeForNow` is marked `shortcut: until the kit flag lands`: switch to crc-appshell's
`ProFreeForNow` when the controller says it is on master.

**Waiting on the controller (orgo-commands, not changed here):** undo for changes that are not files; the main lock
for `ApplyOther`; the shown sentences passed to `ApplyOther`. FC-58 and the report to the controller.

**Working safely:** never run the exe in command mode against the real `%APPDATA%\orgodesk` on Rex's PC; use
`--data <throwaway folder>` (a Debug build writes no trial or registry). Command mode only signals an Orgodesk running
with the same data folder. VM rounds lease a VM (`G:\VMs\orgo-tests\lease.ps1`), see orgo-suite `VM-TESTING.md`.
