# Changelog

All notable changes to Orgodesk are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/), and version numbers follow [Semantic Versioning](https://semver.org/): MAJOR for changes that break saved layouts or settings, MINOR for new features, PATCH for fixes.

## [Unreleased]

### Fixed
- Orgos on a monitor that is unplugged for a moment (a cable pulled out and back in) go back to that monitor when it returns, instead of staying piled on the main screen at the wrong size (FC-67).

## [1.1.0] - unreleased

What OrgoAI v1 adds to Orgodesk. Version bump proposed by release prep (scorecard 2a); the date is set on release.

### Added
- Command mode for OrgoAI and scripts: `Orgodesk.exe --orgo-command` lists groups, presets, snapshots and rules and previews layout, rename_group, remove_group and move_icon changes, always as a preview first, with data-safety checks for the existing layout (FC-55, FC-57).
- The AI connection comes only with the Orgo suite bundle: its key, or the 30-day suite trial, then read-only (FC-56). The tray menu opens the key window.
- An installer (Inno Setup, per user, no admin), replacing the zip (FC-61, FC-62).

## [1.0.1] - 2026-11-18

Built from tag `v1.0.1`. Your Orgos, presets and settings carry over; install it over 1.0.0.

### Fixed
- Memory crept up by about 2 MB an hour while Orgodesk sat idle: the 2-second background check made throwaway memory that Windows rarely got back. It now makes none (FC-44).

## [1.0.0] - 2026-11-17

First public release, sold on Gumroad by CRC Solutions. Release build `Orgodesk.exe` SHA-256 `9f83ae3de3849933c96c972f592126ca51d119f748df0b743cf3a5331bf4a081`, built from tag `v1.0.0` with the Gumroad product id set. Pre-launch checks are at the end of `TESTING.md` (Defender scan, Gumroad download match, clean-VM install, on-screen pass).

### Added
- Paid version: 14-day trial, Gumroad license keys (5 PCs per key, weekly recheck, 30 days offline), license window and tray entries. Development builds (empty product id) have no trial or key.
- Layout backup: each clean start keeps `layout.json.bak`; a damaged layout is restored from it and the user is told.
- The layout records the display scale it was laid out at, and converts when the scale changes.
- `THIRD-PARTY-NOTICES.txt` for the bundled .NET runtime and Windows Forms.
- New-version notice: once a day Orgodesk reads a small version file and shows a tray notice with a link when a newer version is out. It never downloads or installs anything. Tick box in Settings; "Check for a new version now" in the tray menu.

- "Open log folder (for support)" in the tray menu; README-buyers.txt says where the log is and how to send it.
- The licence is also checked once a day and two minutes after the PC wakes; Orgodesk closes mid-session only when Gumroad answers that the key was refunded or the trial is over, never because the network is down.
- The trial start is also kept in the registry (paid builds), so deleting license.json no longer restarts the trial.

### Changed
- The seller and publisher name is CRC Solutions; support address support@crc-solutions.org.
- Error messages say what to do next; dialogs tab in on-screen order, have keyboard shortcuts, and close with Esc.
- An update link is only opened when it is on the Orgodesk download site.
- Rename refuses names containing \ or /; a portal's "Open folder" opens only an existing folder.
- Built on .NET 10 (was .NET 8, whose support ended 2026-11-10). The download is smaller (about 107 MB, was 145 MB).
- The tray menu shows the version.
- Windows 11 only; Windows 10 is not supported.
- Uninstall from Settings > Apps removes the program; it asks whether to keep your Orgos.

### Fixed
- Settings > Auto-sort rules cut off its help line; Orgo properties showed a needless scrollbar at 150%; disabled colour swatches were unreadable (FC-53).
- A failed desktop scan could have emptied every Orgo; it is now logged and the layout left alone (FC-51).
- Icons of files that came and went stayed in memory; the cache now drops them (FC-48).
- Each desktop refresh leaked two handles (four per new desktop file) until garbage collection (FC-44).
- A layout.json with `null` lists, or a licence-server reply that was not a JSON object, crashed Orgodesk.
- A rate-limit or other non-404 answer from Gumroad revoked a paying buyer's key.
- Licence success text and colour swatch labels were below 4.5:1 contrast.
- After Explorer restarted, Orgodesk could stop seeing desktop changes; it now registers for them again, including when the watchdog re-attaches (FC-45).
- Installing over a running copy could fail with a sharing violation and leave nothing running; the installer now waits up to 15 seconds for the old copy to release its file (FC-46).
- A refused activation at the limit still used up an activation, and so did every retry (FC-41).
- After a crash, the desktop could be left with no icons behind the license window (FC-42).
- License messages were hard to read in a high-contrast theme.
- Reinstalling on the same PC used up another license activation (FC-40).
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
