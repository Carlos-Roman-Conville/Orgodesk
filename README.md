# Orgodesk

Orgodesk (formerly "Fences Clone") is a personal, from-scratch desktop organizer for Windows 11 in the style of Stardock Fences. On screen each box is an **Orgo** (plural Orgos); the code keeps the word fence: titled, shaded groups ("fences") on the
desktop that hold icons, with roll-up, quick-hide, folder portals, desktop pages, auto-sort rules and layout
snapshots. Built for personal use only; no Stardock code or assets are used.

## How it works

Fences (3 and later) takes over icon rendering instead of moving Explorer's icons around. This clone does the same:

1. It hides Explorer's real desktop icon list window (instant, nothing persisted; a crash cannot leave the desktop
   empty after a reboot).
2. It creates a per-pixel-alpha layered window as a **child of Explorer's `SHELLDLL_DefView`**, covering the whole
   virtual screen, above the hidden icon list. The wallpaper shows through; the window gets all mouse input.
3. It enumerates the desktop namespace itself (user desktop + public desktop + Recycle Bin and the other special
   items) through `IShellFolder`, draws icons from the system image lists (with shortcut overlays), opens items with
   `ShellExecuteEx`, shows the real shell context menus through `IContextMenu`, and does drag-and-drop with the
   shell's own `IDataObject`, so dragging to and from Explorer windows works.
4. Shell change notifications (`SHChangeNotifyRegister`) keep the view in step with the file system.
5. On exit the real icons are shown again. `Orgodesk.exe --restore` does that on its own if the app ever dies.

## Requirements

- Windows 10/11, 64-bit. Tested on Windows 11 build 26200.
- .NET 8 SDK to build (`dotnet --version`), or the .NET 8 Desktop Runtime to run a published build.
- **Exit Stardock Fences first** if it is installed. Both programs take over the same desktop; running both at once
  makes them fight over the icon list. (Fences also leaves a hook DLL inside Explorer until Explorer restarts.)

## Install (recommended for daily use)

```bat
install.bat
```

builds a single self-contained `dist\Orgodesk.exe` (no .NET install needed), copies it to
`%LOCALAPPDATA%\Programs\Orgodesk\`, registers it to start with Windows, adds a Start Menu entry and
starts it. From then on it is simply on, every login. `uninstall.bat` removes the startup entry (files stay).
`publish.bat` alone just rebuilds `dist\Orgodesk.exe`.

## Build and run

```bat
run.bat
```

or by hand:

```bat
dotnet build Orgodesk.sln
start "" src\Orgodesk.App\bin\Debug\net8.0-windows\Orgodesk.exe
```

Tests:

```bat
dotnet test
```

Command-line switches:

| Switch | What |
|---|---|
| `--exit` | Asks the running instance to quit cleanly (restores the desktop icons). |
| `--restore` | Emergency: shows the real desktop icons again and exits. Use it if the app crashed and the desktop is empty. |
| `--diag` | Shows which Explorer windows the app finds and whether the real icon list is visible. |
| `--install` | Copies the exe to `%LOCALAPPDATA%\Programs\Orgodesk`, sets start-with-Windows, adds a Start Menu entry, starts it. |
| `--uninstall` | Stops it and removes the start-with-Windows entry. Files and layout stay. |

## Using it

- **New fence:** right-drag a rectangle on empty desktop, then *Create fence here*. Or right-click > *New fence here*.
  Icons under the new rectangle become its first members.
- **Move / resize:** drag the title bar; drag any edge or corner. Fences snap to each other and to the screen
  edges (Settings > Behavior to turn it off; hold Alt to skip snapping for one drag).
- **Roll up:** double-click the title bar (or the fence menu). A rolled fence shows only its title.
- **Auto-roll (peek):** fence menu > *Roll up automatically*. The fence stays rolled up and opens while the mouse
  is over it (or while you drag an icon over it), then rolls back when the mouse leaves.
- **Fence menu:** right-click a title bar: rename, properties (color, portal folder, sort order), sort by, remove.
- **Quick hide:** double-click empty desktop to hide every icon and fence; double-click again to show them.
- **Switch off / on:** double **right**-click the desktop. Off means the normal Windows desktop is back while the
  clone waits in the tray; double right-click again (or tray > *Switch off*) brings it back with the same layout.
- **Folder portal:** a fence that shows the contents of any folder (right-click > *New folder portal here*, or set
  the portal folder in fence properties). Dropping files on a portal moves them into that folder.
- **Colors per Orgo:** Settings > Appearance > *Colors apply to* picks one Orgo (or all); the Orgo and title bar
  colors then change only that one, and *Back to default colors* clears its override. Right-click > *Orgo
  properties...* has the same two colors.
- **Presets:** arrange the desktop, then right-click > *Presets* > *Save current desktop as preset...* and name
  it (Development, Gaming, Office...). Switch with right-click > *Presets* > name: the fences and icon positions
  change, files never move. Icons that appeared since the preset was saved go to free slots. *Update "name" from
  current desktop* overwrites a preset after you tweak it; *Delete preset* removes one. Also in the tray menu.
- **Desktop pages** (off by default, Settings > Behavior): right-click > *Desktop pages* to add, switch, rename or delete pages. Swipe by dragging with the
  middle mouse button, or left-drag starting at a screen edge, or press Ctrl+Left / Ctrl+Right. Dots at the bottom
  show the current page.
- **Auto-sort rules:** Settings > *Auto-sort rules*. Match by extension list (`jpg;png`), name wildcard (`Report*`)
  or item type, and pick the target fence. New items are sorted when they appear; *Sort icons by rules now* sorts
  everything.
- **Snapshots:** right-click > *Layout snapshots* (or the tray menu) to save and restore named layouts.
- **Keys:** Enter opens, Alt+Enter shows properties, F2 renames, Delete recycles (Shift+Delete deletes), Ctrl+A
  selects all, Esc clears the selection, F5 refreshes, mouse wheel scrolls a full fence.
- **Tray icon:** settings, snapshots, pages, quick hide, refresh, exit.

## Files

| Path | What |
|---|---|
| `%APPDATA%\orgodesk\layout.json` | Pages, fences, free icon positions, rules. Autosaved. |
| `%APPDATA%\orgodesk\settings.json` | Appearance and behavior. |
| `%APPDATA%\orgodesk\snapshots\*.json` | Saved layouts. |
| `%APPDATA%\orgodesk\error.log` | Anything that went wrong. |
| `%APPDATA%\orgodesk\icons-hidden.flag` | Present while the real icons are hidden by this app. |

Positions are physical pixels in virtual-screen coordinates, the same space Explorer uses. Icon and cell sizes are
set at 96 dpi and scaled per monitor.

## Modules

| Project | Folder | What |
|---|---|---|
| `Orgodesk.Core` (net8.0, no UI) | `Fences/` | Models (`Layout`, `Page`, `Fence`), grid layout engine, free-icon grid, reconciler (keeps the layout in step with the real desktop). |
| | `Rules/` | Rule model and matcher. |
| | `Persistence/` | JSON store with atomic writes, snapshots, settings. |
| `Orgodesk.Windows` | `Shell/` | COM interop: enumeration, icons, context menus, drag data objects, file operations, change notifications. |
| | `Desktop/` | Finding Explorer's desktop windows, hiding the real icons, DIB-backed layered surface, DPI, autostart. |
| `Orgodesk.App` | `UI/` | The desktop surface control, renderer, tray context, settings, rule and fence dialogs, snapshots. |
| `Orgodesk.Core.Tests` | | xUnit tests for layout, rules, reconciliation and persistence. |

Core has no Windows dependencies and is reusable for any grid-of-items UI. The `Shell/` and `Desktop/` folders are
reusable for anything that wants to live on the Windows desktop.

## Known limits

See [ISSUES.md](ISSUES.md).
