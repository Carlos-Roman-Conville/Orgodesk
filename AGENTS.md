# AGENTS.md: Orgodesk

Orgodesk is a Windows 11 desktop icon organizer (C#, .NET 10, WinForms), sold as a paid app with a 14-day trial by
CRC Solutions. This repo is public. See README.md for how it works, TESTING.md for release status, ISSUES.md for
open issues, SALES.md for the store setup.

## Build and test

- Test: `dotnet test tests/Orgodesk.Core.Tests -c Release` (needs the .NET 10 SDK; 86 tests at last count).
- Build the exe: `publish.bat` (into `dist\`). `release.bat` is the sale build: it refuses while `Sales.cs` has an
  empty `ProductId`.
- Packages are pinned by `packages.lock.json` (see `Directory.Build.props`). Check with `dotnet restore --locked-mode`;
  after changing a package reference, run a plain `dotnet restore` and commit the lock files.

## Layout

- `src/Orgodesk.Core`: no Windows APIs, fully unit-tested. Fences (the Orgo model), Licensing, Persistence
  (layout.json, settings.json, backups), Rules, Updates.
- `src/Orgodesk.Windows`: the shell side. Desktop (the overlay window, Explorer's desktop view) and Shell
  (IShellFolder, context menus, change notifications). Not covered by unit tests; verify in a VM.
- `src/Orgodesk.App`: entry point, installer, logging, sales settings, and all UI (`UI/`).
- `tests/Orgodesk.Core.Tests`: xUnit tests for Core.

## Rules that apply here (from the HQ AGENTS.md)

- Build to release quality: handle bad input and failed dependencies, never lose user data, no secret in code, logs
  or repo. Never write "done" or "works" without evidence (the command and its output); if it was not run, say so.
  A failed requirement goes in `ISSUES.md` with a severity. Only the owner lowers a bar.
- Rules trace to a source; never invent one. The legal files in `legal/` are the owner's decisions.
- Never delete the owner's files; never push to git; commit only your own paths. Ask before downloading anything new.
- Change only the files your task names, plus their notes and records.
- Never commit the test-only Gumroad product id or the mock license server, and never put a Gumroad access token,
  password or personal email in the app or repo.

## Working notes

- A coding agent's shell may be sandboxed: its `%APPDATA%` can be a stale copy, and installs from it do not reach the
  real system. Orgodesk's real data is in `%APPDATA%\orgodesk\` (layout.json, error.log).
- Build in a copy of the repo (without bin, obj, dist, restore, release, .git) when the working tree is shared;
  back up `dist\` before replacing it.
- Edit with exact-match patches and keep CRLF line endings. Python text containing backslashes needs raw strings.
- Commit messages end with a `Co-Authored-By:` line for the model in use.
- Release verdicts come from a full release check, never from a green test run alone. A dated CHANGELOG release and
  a git tag are made only at release, on the tested commit, with the owner's go.
