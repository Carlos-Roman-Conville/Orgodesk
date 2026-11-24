# Orgodesk: handoff

Where the work stands, for the next agent. Issues are in [ISSUES.md](ISSUES.md), decisions in [DESIGN.md](DESIGN.md)
section 7, release state in [TESTING.md](TESTING.md).

## Unfinished (end of the 2026-11-24 session)

- **Release blockers outside this repo:** the suite bundle's Gumroad product id (`OrgoSuite.BundleProductId` in crc-appshell); until it is set a Release build and the Release installer stop at CS0020 (intended). A code-signing certificate (Rex): the first run of the unsigned exe costs 2.5 to 5.6 s before `Main` (FC-66; 4 of 5 cold starts within 5 s, the 5-of-5 target was not met).
- **Not built:** pages through commands; pruning old "Before AI change" snapshots; moving Orgodesk's own licence copy (`Orgodesk.Core.Licensing`) to crc-appshell (FC-56); the Qwen pass rate for the 14 command actions.
- **Not run:** a Defender and SmartScreen scan of the installer, Windows 10, the installer wizard's tasks page by eye, a VM soak of the new command mode.
- **Open rows:** FC-58 (library has no undo for non-file changes; orgo-commands OC-14), FC-63 (empty program folder after uninstall, Low), FC-66 (fold under release prep's FC-64 when branch `v1-rc` merges; that branch numbers release prep's rows FC-64/65 and this repo's installer rows FC-62/63).
- **Never edit** the untracked `tests/Orgodesk.LeakProbe/packages.lock.json` unless it is yours.

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
"Before AI change" snapshots; the Qwen pass rate (no runner run for Orgodesk yet); (the VM round of the three new actions was run, FC-57).
See FC-55, FC-57. Pro is free through the kit's `SalesConfig.ProFreeForNow` (`Sales.Premium`, kit 53c8e10); the Release build also stops unless Pro has an id or that flag.

**Waiting on the controller (orgo-commands, not changed here):** undo for changes that are not files; the main lock
for `ApplyOther`; the shown sentences passed to `ApplyOther`. FC-58 and the report to the controller.

**Installer (FC-62, 2026-11-23):** `build-installer.bat [debug]`, `installer\Orgodesk.iss`, `installer\make-legal-txt.ps1`; the app gained `--autostart on|off`. The Release installer cannot be built until `OrgoSuite.BundleProductId` is set (same stop as the exe). Inno Setup 6 is at `%LOCALAPPDATA%\Programs\Inno Setup 6`. The exe's own `--install` and `--uninstall` remain for zip users (release prep FC-61 decides zip or installer). VM round script and screenshots: `tests\installer\vm-round-2026-11-23`. The AppId GUID in the .iss must never change after a release.

**Download decision (FC-61, release prep, row on branch v1-rc): the installer is the one v1 download; no zip is listed.** The controller's call (2026-11-23), open to Rex's change: one way in means one set of support answers, and the installer keeps layouts and handles upgrades. `release.bat` (the zip build) stays in the repo, unused for v1. Record it in FC-61 itself when v1-rc is merged.

**FC-64 (release prep, row on branch v1-rc) and FC-66 (the finding, here):** a cold first start of the published exe spends 2.5 to 5.6 s before `Main` (Defender reading the file); command mode itself is 0.15 s. The exe is now published compressed (47 MB). Set `ORGODESK_CMD_TRACE=<file>` to get stage lines from command mode. Merge note: v1-rc numbers release prep's rows FC-64 and FC-65 and this repo's installer rows FC-62 and FC-63; fold FC-66 under FC-64 when v1-rc merges.

**Working safely:** never run the exe in command mode against the real `%APPDATA%\orgodesk` on Rex's PC; use
`--data <throwaway folder>` (a Debug build writes no trial or registry). Command mode only signals an Orgodesk running
with the same data folder. VM rounds lease a VM (`G:\VMs\orgo-tests\lease.ps1`), see orgo-suite `VM-TESTING.md`.
