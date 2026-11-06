# Upgrades

Ideas beyond the Fences feature set, ranked. Effort is a working estimate against the current architecture
(the clone owns the real desktop icons and draws them itself, so none of these need a redesign).
Status: idea / planned / building / done.

| # | Upgrade | What it solves | Value | Effort | Status |
|---|---|---|---|---|---|
| U-1 | **Presets** (named desktop arrangements picked from the right-click menu) | Re-setting up the same working desktop by hand. Arrange, save as preset, switch in two clicks. Files never move. | High | Half a day | done 2026-11-06 (right-click > Presets; tray menu) |
| U-2 | Auto-switch presets by running program (`Code.exe` → Development) | Switching by hand. Map exe names to presets yourself; last switch wins, with a cooldown. Needs U-1. | High | 1 day | idea |
| U-3 | Per-preset extras: launch a list of apps, set a wallpaper | A preset becomes a whole environment, not only an arrangement. Needs U-1. | Medium | Half a day | idea |
| U-4 | Multi-monitor layouts: one saved layout per monitor arrangement, restored when monitors change (dock, undock, resolution) | Arrangements shifting when the display setup changes. Also closes ISSUES FC-8. | High for laptop users | 1 to 2 days | idea |
| U-5 | Project containers: a fence that pins items from several folders and anywhere else, without copying files | Living in File Explorer to reach a project's scattered files. Portals today show one folder each. | Medium | 1 to 2 days | idea |
| U-6 | Export and import of layout, presets and settings as one file | Moving the setup to another PC; portable use. The data is already two JSON files. | Medium | 1 hour | idea |
| U-7 | Fence exclusion from quick-hide, roll animations, per-fence icon size and transparency | Fences parity leftovers (ISSUES FC-13). | Low | Half a day | idea |
| U-8 | Background icon loading with placeholders | First paint stalls on shortcuts to unreachable network targets (ISSUES FC-6). | Low | Half a day | idea |
| U-9 | Technician profile: presets plus export/import packaged for a support-desk audience | Standard desktops across machines. Nothing extra to build beyond U-1 and U-6; it is packaging. | Niche | 0 | idea |
| U-10 | AI or content-based file sorting | Finding files by what they are rather than their name or type. Large effort for little gain over the rules engine. | Low | Weeks | not planned |

Product note (from HQ/PRINCIPLES.md): as a product this is copyable, as the build itself showed. It is tooling and
portfolio unless tied to something ongoing.
