# Orgodesk: design

**Standard check** ([architecture standard](../../../orgo-suite/ARCHITECTURE-STANDARD.md)): **partly written**: sections 4 and 7 (D-1, D-2) are filled in; 1 to 3, 5, 6, 8 and 9 are still placeholders. Until it is written, the design lives in: README.md, SCOPE.md, ISSUES.md and `HQ/handoffs/orgodesk`. The next agent that touches this tool fills in what it can and updates this line. Last checked 2026-11-23 by Claude (FC-55).

## 1. In plain English
What the tool does, who it is for, and how it works, in one short paragraph a non-programmer can follow.

## 2. The big picture
A plain-text diagram of the pieces and how data moves through them, then two or three sentences walking through
one real use from start to finish (for example: "you drop a file in Downloads → the watcher sees it → ...").

## 3. The pieces
| Piece | What it does | Why it is separate | Where in the code |
|---|---|---|---|

## 4. Shared parts and suite place
- **Shared parts it uses:** orgo-commands (command mode, `src/Orgodesk.Commands`, FC-55) and crc-appshell kit-1.10
  (the AI-connection gate `CommandSales`/`CommandLicense` and its key window, bundle-only, D-11). Orgodesk changes no
  files, so it does not use orgo-filecore (the library is handed an empty in-memory disk it never runs a plan on). Its
  own app licence (14-day trial, Gumroad key) is still the copy in `Orgodesk.Core.Licensing` (FC-56). Not used yet:
  Orgo.Windowing, orgo-recognize.
- **Features it owns** (SYSTEM.md section 3): desktop icon groups, layouts, presets, snapshots and desktop mouse
  input. It calls on no other tool.
- **Its commands for OrgoAI** (`Orgodesk.exe --orgo-command`, tool name `orgodesk`): list `groups`, `presets`,
  `snapshots`, `rules`; status (app running, page, counts, layout ok or damaged); preview `new_group`, `add_rule`,
  `sort_icons`, `quick_hide`, `preset`, `save_preset`, `portal`, `roll_up`, `rename_group`, `remove_group`, `move_icon`, `snapshot_save`, `snapshot_restore`.
  Every preview is a described change (orgo-commands OC-10). Apply makes it under the shared lock and keeps the layout
  as it was as a snapshot first; the answer names the undo (`preview(snapshot_restore, name="Before AI change ...")`,
  or `quick_hide show=true`). Usage text is in `DeskTool.Info`; test requests in `tests/commands/requests.jsonl`.

## 5. Data it keeps
| Data | Where | Format | Who may read it |
|---|---|---|---|
What happens to the data on a crash, an upgrade, and an uninstall.

## 6. Words explained
| Word | Meaning here |
|---|---|

## 7. Decisions
One record per big decision (rules 3 to 5). Newest last; never deleted.

### D-1: Command mode writes layout.json itself under the shared lock; the running app reloads it
- **Status:** proposed (Claude, 2026-11-23, FC-55)
- **Options considered:** (a) command mode hands each change to the running app over a pipe and the app makes it: the
  app's own code paths, but a second channel, a second apply path for when the app is not running, and nothing for
  quick hide when it is not; (b) command mode writes layout.json under orgo-commands' lock and signals the app, which
  holds the same lock from the person's edit to its save and never writes over a file that changed since it last read
  or wrote it.
- **Chosen because:** (b) needs one apply path whether or not the app runs, uses the lock every Orgo already shares,
  and is unit-tested end to end (`SharedLayoutTests`, each check proved by removing it).
- **Cost accepted:** an edit the app makes without its save hold (none known today) would be dropped in favour of a
  command's change, and logged.
- **Would change if:** the app starts keeping layout state that is not in layout.json, or a VM round shows a lost edit.

### D-2: Undo of a desktop change is a snapshot kept before it
- **Status:** proposed (Claude, 2026-11-23, FC-55); reported to the controller as an orgo-commands gap
- **Options considered:** (a) the library's `undo` verb: it undoes file runs only (orgo-filecore journal), so it finds
  nothing of Orgodesk's; (b) Orgodesk's own snapshots: every apply saves "Before AI change <time>" first, and the answer
  names `snapshot_restore` with that name; (c) wait for an orgo-commands change (undo for non-file changes).
- **Chosen because:** snapshots already exist, are shown in the Snapshots window, and restore the whole layout exactly
  (test `New_group_applies_and_its_undo_snapshot_puts_the_layout_back`).
- **Cost accepted:** restoring a before-snapshot also takes back later changes made by hand; its preview says which
  groups go away. Before-snapshots pile up (one small file per change; FC-55).
- **Would change if:** orgo-commands gains undo for non-file changes (then `undo run=last` should reach it).

## 8. Limits and risks
What it cannot do yet, what could go wrong, and what is not tested. Link ISSUES.md for details.

## 9. How it is checked
The tests and gates that prove it works (unit tests, VM rounds, independent tester rounds), and how to run them.
