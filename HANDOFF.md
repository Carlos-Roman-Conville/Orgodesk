# Orgodesk: handoff

Where the work stands, for the next agent. Issues are in [ISSUES.md](ISSUES.md), decisions in [DESIGN.md](DESIGN.md)
section 7, release state in [TESTING.md](TESTING.md).

## Unfinished (end of the 2026-11-24 session)

- **Release blockers outside this repo:** the suite bundle's Gumroad product id (`OrgoSuite.BundleProductId` in crc-appshell); until it is set a Release build and the Release installer stop at CS0020 (intended). A code-signing certificate (Rex): the first run of the unsigned exe costs 2.5 to 5.6 s before `Main` (FC-66; 4 of 5 cold starts within 5 s, the 5-of-5 target was not met).
- **Not built:** pages through commands; pruning old "Before AI change" snapshots; moving Orgodesk's own licence copy (`Orgodesk.Core.Licensing`) to crc-appshell (FC-56); the Qwen pass rate for the 14 command actions.
- **Not run:** a Defender and SmartScreen scan of the installer, Windows 10, the installer wizard's tasks page by eye, a VM soak of the new command mode.
- **Open rows:** FC-58 (library has no undo for non-file changes; orgo-commands OC-14), FC-63 (empty program folder after uninstall, Low), FC-66 (fold under release prep's FC-64 when branch `v1-rc` merges; that branch numbers release prep's rows FC-64/65 and this repo's installer rows FC-62/63).
- **Never edit** the untracked `tests/Orgodesk.LeakProbe/packages.lock.json` unless it is yours.
- **Not seen on screen (Rex's checks):** FC-67 (Orgos and free icons go back to a monitor that returns): Rex pulls his HDMI cable out and back in with 1.1.0 running. FC-68 (icons without a 256 px image drawn tiny at 150%): Rex's screenshot after the install showed Aseprite, darktable, Tiefsee and Tiled full size, but no one has checked it against a test. FC-69 (switch_preset keeps groups on a second monitor): tested only.
- **Open question to Rex (asked, not answered), text readability:** (1) names are drawn white on every Orgo, so they are hard to read on a light Orgo (Rex's global Orgo colour is `#8CC0C0C0`; his new "Orgo-Suite" Orgo uses it); pick dark or light text by the Orgo's colour. (2) Names get one line and are cut at about 9 characters, where Explorer gives two. (3) The Label size setting (Settings, 6 to 24 pt) already exists, but cells do not grow with it, so bigger text is cut sooner; cells should fit two lines at the chosen size. Wait for his yes before building; it changes how every Orgo looks.

## 2026-11-25: both HDMI cables out, Orgos stuck again (FC-71)

- **Cause:** with both monitors gone Windows reported a 1920x1080 screen at 100%, so `Rescale` ran too; it clamped every Orgo into the main work area and kept no `HomeBounds`, so FC-67's return had nothing to go back to. Fixed in 6211be3 (Rescale only converts; `EnsureGridScale` then runs `ClampToArea` on the virtual screen). 312 + 77 tests pass.
- **Installed for Rex:** 1.1.0 RC from 6211be3, `_caches	mp1-rc\orgodeskƑ1be3a5\` (placeholder bundle id, NOT FOR SALE; `releaseuild-rc.ps1` taken from branch `v1-rc` for the run, then removed). Started 04:42 with no errors.
- **Rex's layout (he asked):** with Orgodesk closed (`--exit`), the bounds of Places, Music and Sound, AI & image gen, Internet, Dev tools and Orgo-Suite (by id) came from the 24 Nov 06:04 `layout.json.bak`; nothing else changed. Data folder from just before: `E:\AI Programs\_backups\orgodesk-data-2026-11-25-before-fc71`.
- **Not seen on screen:** FC-71 with both cables pulled and put back.

## 2026-11-24 (evening): Rex's "the orgos are all zoomed in" (FC-67, FC-68, FC-69)

- **Cause:** Rex's PC is a 2560x1440 main monitor at 150% and a 1920x1080 monitor at 100% on its left. He pulled the HDMI cable out and back in; the display-change clamp (`ClampToArea`) moved the left monitor's Orgos onto the main screen at x 0 at their 100% pixel size, so they drew 1.5x bigger in the same box, and they stayed when the monitor returned (FC-34). Same code in 1.0.1, the 2b1861be8 RC and main.
- **Fixed on main:** 035c07a (`Fence.HomeBounds`: a pulled Orgo goes back; moving or resizing it by hand forgets it), 31a426e (`Page.FreeHomes` for free icons; `DeskTool.Screen` so command-mode `switch_preset` clamps to the virtual screen, FC-69), 4357371 (`IconProvider.SmallPictureArea`, FC-68). Tests: `MonitorReturnTests`, `Preset_keeps_a_group_on_the_second_monitor`, `IconCornerTests`; each failed before its fix. 310 + 77 tests pass at 4357371.
- **Installed for Rex:** 1.1.0 from f5a055a (adds the app icon, FC-70), installed by the controller, in `%LOCALAPPDATA%\Programs\Orgodesk`, start with Windows on. Earlier installs this evening were RCs 31a426e40 and 435737148 in `_caches\tmp\v1-rc\orgodesk\`, built with `release\build-rc.ps1` from branch `v1-rc` run in a temporary worktree (placeholder bundle id, NOT FOR SALE).
- **Rex's layout restore (he asked):** with Orgodesk closed, the bounds of 10 Orgos (matched by id) were taken from the 21 Nov `layout.json.bak`; contents, rules, icons and new Orgos were kept. The layout from just before is `%APPDATA%\orgodesk\layout.json.before-restore-20261124-034312`. The 21 Nov `.bak` itself was replaced when the app started (it copies the loaded layout). Copies of the 1.0.1 exe and the whole data folder from before the first install, and of layout.json, settings.json and error.log from before any change, are in that session's scratchpad (`before-install-1.1.0`, `orgodesk-backup`), which a reboot may clear; v1.0.1 can be rebuilt from its tag.
- **After the restore** (between 03:50 and 04:34, in the app by hand, not through commands): a new "Orgo-Suite" Orgo holds the 8 Orgo shortcuts, and Places, Music and Sound, and AI & image gen sit stacked at the main screen's right edge. Left as found.
- **Tool note:** on this PC the Bash tool showed a stale copy of `%APPDATA%\orgodesk` (a 7 Nov layout.json); read Rex's live data through PowerShell.

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
