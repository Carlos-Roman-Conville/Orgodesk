# Test round: Orgodesk

Follows the standard guide in `E:\AI Programs\HQ\testing\` (BASELINE, SPECIALIZED A, PRODUCTION).

- **Program:** Orgodesk, commit `b4fe73b` (2026-11-11). Paid version (trial and Gumroad license keys) is **in progress** in the working tree, not committed, not yet tested.
- **Kind:** [x] Windows desktop app  [ ] website  [ ] game  [ ] script  [ ] uses AI models  [x] keeps important data (layouts, presets)
- **Where tested:** VirtualBox VM `Orgodesk-Test` (Windows 11 Home build 26300, standard local account, fresh install) at `G:\VMs\orgodesk-test\`; Rex's PC (Windows 11 build 26200).
- **Smoke test steps:**
  1. Install (`Orgodesk.exe --install`), or start the installed exe.
  2. Desktop icons appear in Orgodesk's surface; error.log gets a `Started` line.
  3. Switch to another preset and back; Orgos and icons match.
  4. Tray > Exit: the process ends and Windows' own icons come back.

## Severity and exit criteria (set 2026-11-11, before any release round)

Severity levels and exit criteria exactly as in `HQ\testing\PRODUCTION.md` sections 1 and 2: zero open Critical, zero open Major (or a Rex-approved workaround in the release notes), every Minor and Low listed in Known issues, every fix verified.

---

## Round 1 — 2026-11-11 (VM, commit b4fe73b and the Nov 7 build before it)

### Baseline

| # | Test | Result | Evidence |
|---|---|---|---|
| 1 | Smoke test | PASS | VM `results.log` 15:48 [install]; `shots/stage-install.png` |
| 2a | Functional: happy path | PARTIAL | Presets switch (FC-20 test), Orgo create/move/resize/roll-up verified by Rex 2026-11-06 (FC-7). Not yet re-run on the RC. |
| 2b | Boundaries | PARTIAL | ~180 icons on Rex's PC (Organized preset scroll fix). Empty desktop and one icon not tried. |
| 2c | Invalid / odd input | PASS | `[edge-odd-names]`: emoji, accents, 180-char name, no extension, leading dot, quotes, brackets, odd folder: all kept in layout, no errors. Emoji label draws as a box (FC-30, Low). |
| 3 | Automated tests | PASS | 69 of 69 (2026-11-12, includes the licensing tests and the new FC-31/FC-36 tests). |
| 4 | Confirmation + regression | PASS | FC-25 and FC-26 retested in VM (`[fix-*]` lines, 16:51–16:52); install, presets, reinstall rerun. |
| 5 | Clean-machine run | PASS | Fresh Windows 11 VM, standard user. |
| 6a | Save, close, reopen | PASS | `[seed-presets]` 15:56: presets kept after Orgodesk saves. |
| 6b | Missing data folder | PASS | `[data-deleted]` 16:05 and `[fix-data-deleted]` 16:52; start-with-Windows kept (FC-26 fixed). |
| 6c | Damaged layout.json | PASS (after fix) | Fix build 23:05: Orgos restored from `layout.json.bak`, logged, warning shown (`shots/fix2-damaged-message.png`). Damaged settings reset once and logged. Before the fix (19:49): 0 Orgos, nothing logged, user not told (FC-28, FC-29, both fixed). |
| 6d | Killed mid-save | PASS | `[edge-kill-mid-save]`: 20 hard kills at random moments, layout.json valid every time (atomic temp-file save). |
| 6e | Previous version's data | PASS | Nov 7 build's layout opened by b4fe73b on Rex's PC (152 KB layout kept, 2026-11-11 19:34). |
| 7 | Errors, logs, no secrets | PASS (after fix) | Failed save now logged **and** shown once (FC-31 fixed); damaged data logged and shown (FC-28/29). |
| 8 | Speed and memory | PASS (8-h soak running) | `[edge-memory]` 21:39: working set 81.5 MB → 82.1 MB over 30 idle minutes (a slow creep of about 1 MB an hour; recheck over 8+ hours in the soak round), 2.2 CPU seconds total. |
| 9 | Secrets, input, access | PARTIAL | No keys in code (dev build). Licensing code not reviewed yet. Runs without admin. |
| — | Exploratory session | PASS | 2026-11-12, charter: *explore desktop file churn (150 creates, 50 renames, 30 moves, 70 deletes) to discover sync errors, crashes or leaks*. Still running, layout matched the desktop exactly, no errors (`[x-churn]`). A mouse-driven session is still open (needs Rex). |

### Specialized A: Windows desktop app

| Test | Result | Evidence |
|---|---|---|
| Install on clean VM as standard user | PASS | `[install]` 15:48 and `[fix-fresh-install]` 16:51 |
| Shortcut, Start Menu, startup entry on the real system | PASS | Run value and Start Menu link in VM; Run value on Rex's PC checked through StdRegProv 2026-11-11 |
| First run with no data | PASS | `[fix-data-deleted]` |
| Starts by itself after restart | PASS | VM: 33 s and 47 s after boot (Nov 7 build); 17:01:29 after restart with fresh settings (b4fe73b) |
| Shows only Windows' own desktop items on a fresh PC | PASS | `[fix-fresh-install]`; `shots/fix-placed-kept.png` (placed items kept) |
| Uninstall leaves nothing behind | PASS | 2026-11-12 VM: uninstall (from the installed copy, as Settings > Apps runs it) removes the program folder, startup entry, Settings > Apps entry and Start Menu shortcut; asks about data (No = keep, default) and keeps it. Install offer from Downloads: Yes installs, registers Orgodesk 1.0.0 in Settings > Apps, starts the trial, and the kept Orgos come back. |
| Upgrade over previous version keeps data | PASS | Rex's PC, Nov 7 build to b4fe73b |
| Delete program folder + reinstall keeps data | PASS | `[reinstall]` 15:57 |
| Resolution change while running | PASS | 1024×768 → 800×600 → back; Orgos stay on screen. Icon landing on another (FC-33) fixed: stacked icons spread out after a display change. |
| Second monitor | PASS | Orgo on monitor 2 draws there; unplugging monitor 2 pulls it back onto monitor 1 (FC-34). Doesn't return to monitor 2 when replugged. |
| 150% display scaling | PASS (after fix) | Dialogs at 150% (real mouse, 2026-11-12): Orgo properties, Settings, Rule edit and License all fit with their buttons visible after the FC-35 fixes. Desktop: Fix build: 100% → 150% at 1024×768, 11 icons in 11 cells, Places scaled (`shots/fix2-150pct-1024.png`). Before the fix labels overlapped (FC-32, fixed). |
| Keyboard across processes | PASS | FC-9, 2026-11-12 with a real mouse in the VM (150%): click selects; F2 opens the rename box with the name selected; typing + Enter renames (extension kept); Ctrl+A selects every icon including inside Orgos; Esc clears; Delete sends the file to the Recycle Bin; right-click shows the Orgo menu and Windows' item menu. Found and fixed FC-38 on the way (dialogs disabling Explorer's desktop). |
| Path with spaces / non-English user name | NOT RUN | |

### Production gate

| # | Item | Result | Evidence / what's missing |
|---|---|---|---|
| P1 | Open issues by severity | PROPOSED | From the 2026-11-11 VM round: Majors FC-28 and FC-32 **fixed** (with FC-29, FC-33); still open: **Minor** FC-31; **Low** FC-30. Older open issues (FC-5, 6, 10, 12, 13, 23) and FC-9 still need a severity. Rex confirms. |
| P2 | Release candidate built from a tag, SHA-256 recorded | NOT DONE | Version set to 1.0.0 in the project; RC not built yet. |
| P3 | CHANGELOG.md, known issues | DONE | `CHANGELOG.md` (Keep a Changelog form), Unreleased section for 1.0.0. |
| P4 | Full regression on the RC | NOT DONE | Needs the RC. |
| P5 | Alpha: Rex uses the RC for an agreed period | NOT DONE | Rex sets the period. |
| P6 | Beta: outside users | NOT DONE | Or Rex's written skip. |
| P7 | Operational: logs readable, update path | PARTIAL | Logs fine; no "open log folder" menu item; no update path written. |
| P8 | Business: store claims tested | PARTIAL | Rex 2026-11-12: Windows 11 only, no Windows 10 version. README and store copy now say Windows 11 (64-bit) only, which matches what was tested. Rest of the store page checked at the RC. |
| P9 | Signed, scanned, download tested | NOT DONE | Unsigned for now; PRODUCTION.md now allows shipping unsigned only as Rex's written decision with SmartScreen steps for buyers (README-buyers.txt has them). Defender scan and real-download test at the RC. |
| P10 | Update and rollback tried | PARTIAL | Upgrade Nov 7 → b4fe73b works; rollback install not tried. |
| P11 | Third-party licenses, THIRD-PARTY-NOTICES | DONE | `THIRD-PARTY-NOTICES.txt` (MIT licenses of the .NET 8 runtime and Windows Desktop runtime, plus the runtime's own notices); `release.bat` copies it next to the exe. No other third-party code or assets. |
| P12 | EULA, refund, privacy policy | DRAFTED | `legal/EULA.md`, `PRIVACY.md`, `REFUNDS.md` written from what the code does (key + product id to Gumroad, nothing else). Rex fills [brackets] (name, state, refund window, support email) and decides on a lawyer. EULA ships as EULA.txt. |
| P13 | Paid features: trial, keys, PC limit, offline, clock, refund | PARTIAL | VM 2026-11-12 with a test-only product id: trial start, 2 days left, trial ended (window before desktop), wrong key, no internet, clock back, close exits: all PASS; license window at 150% fixed (FC-35); future-clock lockout fixed (FC-36). Needs Rex's Gumroad product: valid key, 6th PC, refund. |
| P14 | Support path, watch period | NOT DONE | SupportEmail empty in Sales.cs. |
| **GO / NO-GO** | | **NO-GO** | Open items above. |

### Gaps to close before release (in order)

1. Licensing work committed, then run its tests and the P13 loop in the VM.
2. Decide uninstall behaviour: register in Settings > Apps and remove program files on uninstall (data kept or asked), or document clearly. The Windows App Certification Kit's "clean reversible install" test expects nothing left behind.
3. ~~Windows 10~~: Rex decided Windows 11 only (2026-11-12); claims removed.
4. ~~Fix FC-28 and FC-32~~ done 2026-11-11 (with FC-29, FC-33). Still open: FC-31 (failed save not shown), FC-30 (emoji label).
5. Still not run: FC-9 (keyboard across processes, needs hands-on input), the exploratory session, an 8-hour soak.

VM state after this round: VBoxSVGA graphics, two monitors, test user at 150% scaling (`LogPixels` 144), odd-name test files on its desktop.
6. Version number, CHANGELOG.md, THIRD-PARTY-NOTICES, severity for each open FC issue.
7. Code signing (Rex: Azure Artifact Signing or OV certificate), EULA, refund and privacy policy, support email.
8. Release candidate, full regression on it, alpha period, beta or written skip, go/no-go.


## Round 2 — 2026-11-12

Fixes: FC-30 (emoji labels), FC-31 (failed save shown), FC-35 (dialogs at 150%), FC-36 (paying buyer locked out after a future clock). Version 1.0.0, CHANGELOG.md, THIRD-PARTY-NOTICES.txt. Paid version tested in the VM (FC-37). Exploratory churn session passed. 8-hour soak started 00:37 on the final development build (results in the VM at `C:\orgodesk-test\soak.log` and `[x-soak]` lines).

Round 2 continued (2026-11-12, later): Windows 10 dropped (Rex). Built: real uninstaller + Settings > Apps entry, first-run install offer for sold builds, README-buyers.txt, legal drafts. FC-9 still needs a real mouse.

Round 2, late (2026-11-12 01:20–02:05): real-mouse session in the VM. FC-9 keyboard PASS. Found FC-38 (Orgodesk dialogs could leave Explorer's desktop disabled) and fixed it; Orgo properties and Rule edit were clipped even at 100% and Settings ran behind the taskbar on a small screen: fixed; uninstaller left an empty program folder: fixed. New 8-hour soak started 02:02 on the installed sold-test build.
