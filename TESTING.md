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
| 3 | Automated tests | PASS | 69 of 69 on .NET 10 (2026-11-12). |
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
| Explorer restart | PASS | 2026-11-12 VM (.NET 10 build): explorer.exe killed and restarted; Orgodesk kept running, re-attached to the new desktop within seconds, clicks route to it, Windows' icons stay hidden (`[hittest]` 03:58, `shots/explorer-restart.png`). |
| High-contrast theme | PASS (after fix) | 2026-11-12 VM: desktop labels and the license window readable; the red status text was dark red on black, now uses the theme text color (`shots/hc-license-fixed.png`). Other dialogs not opened in high contrast. |
| Sleep/resume, second user account, Remote Desktop | NOT RUN | Second account needs an admin-created user; sleep and Remote Desktop are unreliable inside VirtualBox: best on Rex's PC. |
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
| P9 | Signed, scanned, download tested | DECIDED: UNSIGNED | **Rex's written decision, 2026-11-12: ship unsigned until sales cover the signing cost (about $10 a month).** Conditions met in the docs: README-buyers.txt and the store copy explain the SmartScreen steps and say Smart App Control blocks it with no override; the refund policy promises a refund in that case. Still to do at the RC: Defender and VirusTotal scan of the exe and zip, and a real-download test. |
| P10 | Update and rollback tried | PARTIAL | Upgrade Nov 7 → b4fe73b works; rollback install not tried. |
| P11 | Third-party licenses, THIRD-PARTY-NOTICES | DONE | `THIRD-PARTY-NOTICES.txt` (MIT licenses of the .NET 8 runtime and Windows Desktop runtime, plus the runtime's own notices); `release.bat` copies it next to the exe. No other third-party code or assets. |
| P12 | EULA, refund, privacy policy | DRAFTED (v2) | 2026-11-12: rewritten from standard clauses, GDPR Article 13 and CalOPPA items, and Gumroad's refund options: 14-section EULA with a consumer-rights carve-out; privacy policy with a data table, legal basis, retention, rights and Do Not Track; 30-day money-back refund policy covering the Smart App Control case. Blanks for Rex are listed in `legal/README.md`. Not reviewed by a lawyer (Rex's call). |
| P13 | Paid features: trial, keys, PC limit, offline, clock, refund | PARTIAL | VM 2026-11-12 with a test-only product id: trial start, 2 days left, trial ended (window before desktop), wrong key, no internet, clock back, close exits: all PASS; license window at 150% fixed (FC-35); future-clock lockout fixed (FC-36). Needs Rex's Gumroad product: valid key, 6th PC, refund. |
| P14 | Support path, watch period | PARTLY | support@crc-solutions.org set in Sales.cs and forwarding (Cloudflare log: 1 received, 1 forwarded, 2026-11-12). Still to do: a test from an outside account seen in the inbox; the watch period after release. |
| P15 | Runtime and OS in support; app shows its version | PASS (after fix) | 2026-11-12: moved from .NET 8 (support ended 2026-11-10) to .NET 10.0.15 (supported to Nov 2028), built with SDK 10.0.404 kept in `Toolchain\dotnet10\sdk` (Microsoft zip, SHA-512 verified). 69 of 69 tests pass on .NET 10; VM regression on the .NET 10 build: install, data, edge and paid-version stages pass. Tray menu now shows "Orgodesk 1.0.0". Still open: publisher name in the exe (Rex picks the name); real-mouse recheck on .NET 10 at the RC. The seller must rebuild when .NET 10 gets security fixes. |
| P16 | Bare exe and zip scanned (Defender + VirusTotal) | NOT DONE | Do at the RC, after the .NET 10 move. |
| P17 | Tax, EU withdrawal consent, name search | PARTIAL | Gumroad is merchant of record (EU/UK VAT certain; Rex confirms US sales tax on Gumroad's tax page). Web search 2026-11-12 found no product or mark named "Orgodesk"; nearest is a registered Class 9 mark "ORGO" (Rex decides whether to check USPTO/EUIPO himself or with a lawyer). Refund draft doesn't undercut EU rights. |
| P18 | Repo visibility decided; no secrets | **NEEDS DECISION** | The GitHub repo is PUBLIC and has no license file: the full source, the license check and SALES.md (launch plan) are readable by anyone, and a development build has no trial or key. No secrets are in it (product id empty). Rex decides: make it private, or keep it public on purpose. |
| P19 | Update notice, 2FA, backups | PARTLY | 2026-11-13: in-app new-version notice built (reads a version file once a day; tray balloon and menu entry; Settings tick box; never installs). 15 unit tests. Not yet tested in the VM, and `UpdateUrl` in Sales.cs is empty until Rex picks where the file lives. Account 2FA and off-PC backup are Rex's to confirm. |
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

Round 3 (2026-11-12 03:20–03:35): moved to .NET 10. Regression in the VM on the .NET 10 build: fix-tests (fresh install, placed kept, data deleted), edge-tests (odd names, damaged layout and settings, 20 kills, failed save) and lic-tests (trial start, near end, expired, clock back, close exits) all pass; the FAIL lines in results.log for odd names and "only what Windows shows" are test-script artifacts, checked by hand (8 of 8 files in the layout, no unplaced navigation items). The first soak (on .NET 8, stopped at 80 min for the move) rose from 78.6 to 85.1 MB; a new 8-hour soak on the .NET 10 build started 03:30.

Research pass 3 (2026-11-12 04:00). New facts: (1) **Smart App Control** blocks unsigned apps with no "Run anyway"; an unsigned Orgodesk cannot run on those PCs unless the user turns the feature off. (2) **Gumroad's activation counter is global and counts every activation**, so a buyer who reinstalls (or deletes their data) uses another of the 5 activations on the same PC; resetting it is a seller action through Gumroad's API (decrement needs an access token). Needs a support procedure, or a higher limit. (3) Selling to EU buyers brings GDPR; the privacy draft already states the only data sent.

2026-11-12 04:15: activation limits brought in line with common practice (SALES.md "Activation limits"): same-PC reactivation no longer counts (FC-40). To test with the real Gumroad product: activate, delete the data folder, activate again, and confirm the uses count on Gumroad stays the same.

Round 4 (2026-11-12 04:20–04:45, .NET 10 build): paid paths end to end against a stand-in license server; found and fixed FC-41 (refused activations counted) and FC-42 (desktop left without icons behind the license window after a kill). Install offer from Downloads, uninstall (everything removed, data kept) and reinstall re-run on .NET 10: pass. High contrast: pass after a color fix. 71 of 71 automated tests. Not rechecked on .NET 10 with a real mouse: click/F2/menus and the tray version line. Soak restarted 04:43.

Real-mouse recheck on the .NET 10 build (2026-11-12 04:51, VM at 150%): click selects; F2 opens the rename box; typing + Enter renames (`net10 renamed.txt`); desktop right-click shows Orgodesk's items with Windows' own; "New Orgo here" creates an Orgo; dragging a file into it works; double-click on the title rolls it up; the tray menu shows the trial line, License... and "Orgodesk 1.0.0". All pass. Not re-opened on .NET 10: Settings, Orgo properties and Rule edit dialogs (passed on .NET 8, same code).

2026-11-12: seller and publisher name set to CRC Solutions (Rex). Shown in the legal drafts, the exe file details and Settings > Apps. Still blank: state, support email, effective date.

2026-11-12: support email filled (support@crc-solutions.org, Cloudflare Email Routing to Rex's Gmail) and forwarding confirmed in Cloudflare's log.

2026-11-13: legal blanks filled (New Jersey; effective 13 November 2026; no update emails). New-version notice added (86 unit tests pass). Still blank: `[2]` years for support emails, `[UPDATE HOST]` and `UpdateUrl`. The sleep, lock and second-account checks move to the VM (after the soak); Remote Desktop into the PC is not possible on Windows 11 Home, so that check is not applicable there.


### Quality standard (every round; see [QUALITY.md](../../../QUALITY.md))

Stage now: prototype (2026-11-14 check on d70b106)  Stage aimed at: shipped  (the bar for each stage is QUALITY.md section 3)

PASS / FAIL / SKIP with one line of evidence, usually the row above that proves it. No evidence = not met.

| # | Requirement | Result | Evidence |
|---|---|---|---|
| Q1 | Does every job the README or spec says |  | Not re-run on d70b106. Last full hands-on pass of the README jobs: 2026-11-12 real-mouse session on an earlier build; Rex's PC runs bba5714 daily with 15 Orgos. |
| Q2 | Every rule traces to a source; nothing invented | PASS | Licence rules in the tests (14-day trial, 5 PCs per key, 30 days offline) match SALES.md lines 8, 15, 18, set by Rex; update-file rules match SALES.md "Update notice". No test found asserting an unsourced rule (spot check 2026-11-14, not a full audit). |
| Q3 | Automated tests run with one command and all pass | PASS | `dotnet test tests/Orgodesk.Core.Tests -c Release` on d70b106, 2026-11-14: Failed 0, Passed 86. Gap: the FC-45 and FC-46 fixes have no automated test (Windows shell and installer). |
| Q4 | Invalid, empty, oversized and odd input: clear message, no crash, no silent wrong result | PASS | VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74): 8 odd-named files (emoji, accents, 180 chars, quotes, no extension) all in the layout, no errors; damaged layout.json and settings.json start with a logged message; unit tests cover an empty, malformed and 20 KB version file. The 4 FAIL lines in `[edge-odd-names]` are the test script's own text encoding, checked by hand. Empty desktop not tried. |
| Q5 | Each dependency failing (network, file, disk, service, model): says so, carries on or stops cleanly | | Not complete on one build. Explorer restart: PASS after the FC-45 fix, 4 of 4 restarts in the VM on 2026-11-14 (build 73f4ef13). Disk: failed save logged (`[edge-log-error]`, d70b106). Version-file network: unit tests (offline, 404). Licence-check network: not re-run since 2026-11-12. |
| Q6 | Closed or killed mid-work: next start is fine | PASS | VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74) `[edge-kill-mid-save]` 01:33: layout.json valid after 20 hard kills, starts normally. |
| Q7 | Works at real size and over a long run | FAIL | FC-44: no full 8-hour soak has finished; the cut-off run rose about 2 MB an hour and 4 handles per desktop file creation. |
| Q8 | Data survives save and reopen, missing folder, damaged file, upgrade | PASS | VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74): save and reopen (`[fix-placed-kept]`), missing data folder (`[fix-data-deleted]`), damaged layout restored from the last good copy and damaged settings reset (`[edge-corrupt-*]`). Upgrade: Rex's PC went da01808 to bba5714 on 2026-11-13 with all 15 Orgos kept (bba5714 differs from d70b106 only in Installer.cs). |
| Q9 | Data backed up and a restore tried |  | Automatic restore from layout.json.bak passed today (Q8). Not run on this build: restoring a snapshot by hand. Never done: the independent data-loss attack in SPECIALIZED F. |
| Q10 | Previous release obtainable; rollback tried |  | Not run. Older builds are kept in `restore\`, but installing one over a newer build has never been tried. |
| Q11 | Open Critical 0, Major 0; Minor and Low listed as known issues | FAIL | Open: FC-44 (leak) and FC-45 (reopened today), both proposed Major; FC-47. Rex has not confirmed the proposed severities (P1). |
| Q12 | No secret in code, repo history, build, logs or errors | PASS | 2026-11-14 on d70b106: grep over `git log -p --all` for keys, tokens and passwords: 0 hits; no commit ever set a non-empty ProductId; no personal path or email in tracked files. |
| Q13 | Outside input treated as data; least access | PASS | Version file and layout are parsed as JSON data only; a non-https address is never called and a non-https link is refused (UpdateCheckerTests, pass today). Installs and runs as a standard user with no admin prompt (VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74)). |
| Q14 | Security rules and the kind section passed; dependencies pinned and in support | FAIL | FC-47: no lock file, package versions not pinned by one. `dotnet list package --vulnerable --include-transitive` 2026-11-14: none. .NET 10 in support. Build is unsigned (Rex's written decision, P9). 2FA on the Gumroad, GitHub and Cloudflare accounts not confirmed. |
| Q15 | A stranger installs and does the main job from the README alone |  | Not run. No stranger has installed it from README-buyers.txt, and no release zip exists yet. |
| Q16 | Every error says what went wrong and what to do |  | Not reviewed on this build. Damaged-data and failed-save messages are logged today; the on-screen wording was last seen 2026-11-11/12. |
| Q17 | Keyboard alone, visible focus, contrast 4.5:1, never colour alone |  | Not run. Keyboard use passed 2026-11-12 on an earlier build; focus visibility and 4.5:1 contrast have never been measured. |
| Q18 | Start and main action not slow; idle use settles | FAIL | FC-44: memory does not settle when idle (107.0 to 114.5 MB over 3.5 idle hours in the cut-off soak). |
| Q19 | Clean machine from the real installer, zip or URL; platforms named; removes cleanly |  | Partly. VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74), Windows 11 Home build 26300, standard user: `--install` of the dev exe works, 3 installs over a running copy pass (FC-46). Not run: the real release zip (none built), uninstall on this build. |
| Q20 | Repo meets the repo standard | FAIL | FC-47: no lock file, no LICENSE file, no AGENTS.md, no release tag, CHANGELOG has no dated release. Present: README, CHANGELOG, ISSUES, one-command build and test, no personal paths. |
| Q21 | Separable parts; content and numbers in data | PASS | Orgodesk.sln: Core (no Windows code, unit-tested), Windows (shell interop), App (UI). Sale settings in Sales.cs, user settings in settings.json, rules and layout in layout.json. |
| Q22 | Log in a known place shows every failure; version shown | PASS | VM 2026-11-14, build d70b106 (SHA-256 cf188dc3...5a74): every failure caused today is in `%APPDATA%\orgodesk\error.log` (damaged layout 01:29:07, damaged settings 01:29:36, failed save 01:30:22). Version line in the tray menu last seen 2026-11-12, not re-seen today. FC-45 leaves no log line. |
| Q23 | Support path; user can send the log; health check and heartbeats for a service |  | Not complete. support@crc-solutions.org forwards (Cloudflare log 2026-11-12); a mail from an outside account has not been seen arriving. No "open log folder" menu item; README-buyers.txt does not say where the log is. |
| Q24 | Cannot harm machine, data or money; destructive actions shown first and cancellable |  | Not re-run on this build. 2026-11-12: Delete sends files to the Recycle Bin; uninstall asks before removing data. |
| Q25 | Promises match what was tested; every licence allows the use |  | Not run. No store page exists yet. THIRD-PARTY-NOTICES.txt covers the .NET runtime (MIT). |
| Q26 | All checked on the exact release build; Rex's go |  | No release build, no go from Rex. |

**Requirements met: 9 of 26.** Failure tests run for Q5: 3 of 4 (disk, version-file network, Explorer; licence network not re-run). Last clean-machine run: 2026-11-14, VM, dev exe (not a release zip).

2026-11-14 release check (first use of the release-check skill), on d70b106. Only results from this build count as PASS; a blank row means not run on this build, not failed. VM runs: `[fix-*]`, `[edge-*]`, `[fc45]`, `[fc46]` lines dated 01:25 to 01:33 in the VM's `C:\orgodesk-test\results.log`. The "Explorer restart" PASS in Specialized A above only checked that the Orgos came back; see FC-45.

2026-11-14, later: FC-45 fixed again and verified in the VM (4 Explorer restarts, 8 of 8 PASS). Q5 moves from FAIL to not complete (licence-check network still to re-run). 86 of 86 unit tests on the fix.

2026-11-15, sub-agent pass on the working tree (after 73a2af5, not yet committed): `dotnet test tests/Orgodesk.Core.Tests -c Release` Failed 0, Passed 303 (was 86; Q3 holds). Q7 and Q18: FC-44 handle leak fixed (VM A/B, 20 file changes: handles +83 before, -6 after; GDI and USER flat); memory still open until an 8-hour soak on the fixed build, so both stay FAIL. Q11: FC-51 (possible layout wipe on a failed desktop scan) newly proposed Major. Q14: lock files in. Q16, Q17, Q23: wording, tab order, mnemonics, contrast (licence success text 3.85 to about 6.5:1, swatch text by luminance) and "Open log folder" done in code, not yet seen on screen (FC-52), so still blank. Q20: LICENSE, AGENTS.md, lock files in; tag and dated release left (FC-47). Security audit and code review results: FC-49, FC-50.

2026-11-17, pre-launch checks on the release build (paid, ProductId set; exe SHA-256 3213698F..., zip B86B1D76...): Defender scan of `release\` found no threats. Gumroad download of the attached zip is byte-identical to the built zip (same SHA-256) and carries the internet mark (ZoneId=3). Clean VM (snapshot `before-desktop-kit`, all earlier Orgodesk data, registry and files removed): the downloaded exe shows "Windows protected your PC / More info" as README-buyers.txt and the store page say ("Run anyway" not clicked: VM keyboard did not reach that window). `--install`: exe = release build, Run value, Start menu shortcut, Settings > Apps entry "Orgodesk, CRC Solutions, 1.0.0", running, trial 14 days, TrialStart in HKCU\Software\Orgodesk. license.json deleted + restart 70 s later: FirstRunUtc unchanged (trial not reset). `--uninstall --quiet`: program folder, Run value, shortcut and Apps entry removed; TrialStart and layout kept. Not yet: VirusTotal (zip over the 10 MB browser-upload limit), clicking Run anyway, FC-52 on-screen dialog checks, sleep/resume licence check.

2026-11-17: VirusTotal skipped by Rex's decision (Defender scan clean).

2026-11-17, on-screen pass in the VM (release build, 150%): tray menu + Open log folder, Settings (readable, Esc), Orgo properties portal guard and unplugged-drive portal, rule editor message, licence window with a live Gumroad "not found": all PASS. Details FC-52; cosmetic findings FC-53. Q16/Q17/Q23 now have on-screen evidence at 150% (100%/200% and sleep/resume not run).

2026-11-17: FC-53 fixed and verified on screen (VM, 150%), build 9F83AE3D; new release zip in release\ (Defender clean). The zip on Gumroad (3213698F) is older and needs replacing.

2026-11-17: release 1.0.0 recorded: exe SHA-256 9f83ae3d...a081 (zip on Gumroad 6451c59f...e2a3, its CHANGELOG still headed Unreleased); tag v1.0.0; VirusTotal skipped (Rex).

2026-11-18: release 1.0.1 built (FC-44 idle memory fix): exe SHA-256 c61fd156...b707, zip da3d11b5...2960, from tag v1.0.1 (38fee0c). E: was full (0 MB free), so the tag was cloned to `G:\orgodesk-build1.0.1` and built there with the release.bat steps; the files are in its `release\` folder, not in this repo's. 303 of 303 unit tests on the clone; leak probe on the host before the build: flat, 0 bytes of garbage per watchdog tick; Defender scan of the release folder: no threats. Not done: the build has not been started (not in the VM, not on Rex's PC), no install-over-1.0.0 check, no Gumroad upload.

## Command mode for OrgoAI (FC-55, FC-56), 2026-11-23

Not a release check. `dotnet test tests/Orgodesk.Commands.Tests`: 47 passed; `dotnet test tests/Orgodesk.Core.Tests -c Release`: 303 passed (both rerun on crc-appshell f5e596d). Release build of the app stops at CS0020 until the bundle id exists. VM round on Orgo-Test-2 with the real `orgo` CLI: preview, apply and undo of a group, a rule, a sort and quick hide, data kept over a restart, no change to the real PC. Data safety (BASELINE 6): save, close, reopen and a damaged layout.json (refused, left as it is) are covered by tests and the round; kill mid-save, a read-only layout.json under command mode and upgrade from older data were not run for command mode (layout writes use the existing atomic LayoutStore.SaveLayout).

Update 2026-11-23 (FC-57): `dotnet test tests/Orgodesk.Commands.Tests`: 66 passed. Data safety for command mode (BASELINE 6 and 7): v1.0.1 data opens and takes changes; damaged layout.json refused and left as it is; half-written .tmp harmless; read-only layout a plain refusal with no stray .tmp; kill mid-apply 70 times in 80 rounds on the real exe: layout.json and snapshots intact every time. VM round of rename_group, remove_group and move_icon run 2026-11-23 on Orgo-Test-2 (see FC-57). Not run: kill mid-save of the app's own save (the window), which uses the existing atomic LayoutStore.SaveLayout.

Update 2026-11-23 (FC-62, installer): VM round on Orgo-Test-4 with a Debug installer: 41 of 41 scripted checks pass (install over a legacy self-install, upgrade over the running app, uninstall keeping layouts, reinstall, uninstall deleting data; trial key kept every time), run twice. Not run: Release installer, Defender/SmartScreen, Windows 10.
