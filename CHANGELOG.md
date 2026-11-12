# Changelog

All notable changes to Orgodesk are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/), and version numbers follow [Semantic Versioning](https://semver.org/): MAJOR for changes that break saved layouts or settings, MINOR for new features, PATCH for fixes.

## [Unreleased]

Work toward the first public release, 1.0.0. The version number in `Orgodesk.App.csproj` is 1.0.0; it is released only after the production gate in `TESTING.md` says GO.

### Added
- Paid version: 14-day trial, Gumroad license keys (5 PCs per key, weekly recheck, 30 days offline), license window and tray entries. Development builds (empty product id) have no trial or key.
- Layout backup: each clean start keeps `layout.json.bak`; a damaged layout is restored from it and the user is told.
- The layout records the display scale it was laid out at, and converts when the scale changes.
- `THIRD-PARTY-NOTICES.txt` for the bundled .NET runtime and Windows Forms.

### Fixed
- A paying buyer was locked out after the PC clock had once been set to a future date (a successful key check now resets the clock guard) (FC-36).
- Dialogs (license, settings, snapshots, rule edit, Orgo properties) were clipped at 125–200% display scaling (FC-35).
- A damaged layout.json started Orgodesk with an empty layout and told no one (FC-28).
- A damaged settings.json was copied aside again at every start (FC-29).
- A failed save was only logged; the user is now told once, and no temp file is left (FC-31).
- Emoji in file names drew as empty boxes (FC-30).
- Icons overlapped after changing display scaling to 125–150% (FC-32).
- An icon pulled back on screen after a resolution change could land on another icon (FC-33).
- On a fresh Windows, navigation-pane items (Home, Gallery, user folders, drives) showed on the desktop (FC-25).
- Losing settings.json turned off start-with-Windows (FC-26).
- Orgodesk did not start with Windows when installed from a sandboxed shell (FC-22).

## Earlier development (not released)

- 2026-11-06: first working version (Orgos, portals, presets, rules); per-Orgo colors; renamed from Fences Clone to Orgodesk.
