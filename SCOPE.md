# Orgodesk

## What this is

Orgodesk (formerly Fences Clone), a personal desktop organizer in the style of Stardock Fences: fences, roll-up, quick-hide, folder portals, desktop pages, auto-sort rules
and snapshots, as a C# .NET 8 app that takes over desktop icon rendering. Personal use only.

## What's under it

| Folder | What | State |
|---|---|---|
| [src/Orgodesk.Core/](src/Orgodesk.Core/) | Models, grid layout, rules, reconciler, JSON persistence. No UI. | active |
| [src/Orgodesk.Windows/](src/Orgodesk.Windows/) | Shell COM interop and desktop window plumbing. | active |
| [src/Orgodesk.App/](src/Orgodesk.App/) | The app: surface control, renderer, tray, dialogs. | active |
| [tests/Orgodesk.Core.Tests/](tests/Orgodesk.Core.Tests/) | xUnit tests for Core. | active |

## Read first

- [README.md](README.md): how it works, how to run it, where the files live.

## Skim

- [ISSUES.md](ISSUES.md): known limits and what was not verified.

## Ignore

- `bin/`, `obj/` under every project.

## Rules that apply here

Inherited from [HQ/PRINCIPLES.md](../../HQ/PRINCIPLES.md), plus:

- Never copy Stardock code or assets. Features are fair game; their code and art are not.
- Core stays free of Windows dependencies so the layout engine can be reused.
- Anything that hides the real desktop icons must have a restore path (`--restore`, the exit handler, the flag file).

## Current state

- 2026-11-06: built to the brief and hand-tested with Rex. Builds clean, 38 Core tests pass. Verified live: attach,
  enumeration, icons, fence creation, move/resize with snapping, roll-up and auto-roll, icon drag in/out, lasso,
  quick hide, pages, context menus, clean exit with icon restore. Added at Rex's request: auto-roll (peek), fence
  snapping, double right-click switch off/on, a single packaged exe (`publish.bat` to `dist\`), and a self-installer (`install.bat` / `--install`:
  copies to `%LOCALAPPDATA%\Programs\Orgodesk`, Run key, Start Menu entry). Installed on Rex's PC 2026-11-06. Presets (named desktop arrangements, right-click > Presets) added the same day;
  further ideas ranked in UPGRADES.md. Not verified: DPI/monitor change, drops onto portals or from Explorer, inline rename. See ISSUES.md.
