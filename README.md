# Parallel Edit (Paratext 9 plugin)

Shows several Paratext texts side by side, one row per verse, and lets you edit
the texts you are allowed to edit, right in that grid.

- **Pick texts**: `Texts…` opens a list of every project and resource on this
  computer. Add them in the order you want them left to right. The first text
  sets the verse numbering (versification); the others are mapped onto it, so
  a Hebrew-numbered resource still lines up with an English-numbered project.
- **Read**: each row is one verse. Section headings get their own thin row
  above the verse they introduce. Footnotes, cross references and markers are
  hidden so the text reads cleanly. Turn on `Markers` to see raw USFM
  everywhere.
- **Edit**: white cells are editable, grey cells are read-only (resources, or
  projects/books where you lack edit permission). Click into a white cell and
  it shows that verse's raw USFM, so footnotes and poetry markers are kept.
  Leaving the cell saves the verse to Paratext (Ctrl+S in Paratext also saves).
- **Navigate**: follows the verse you are on in other Paratext windows (same
  scroll group), and moves them when you click a verse here. `◀` / `▶` change
  chapter; type a reference such as `MRK 3` or `JHN 3:16` in the box.

## How saving works

Saving goes through Paratext's plugin API: the plugin takes Paratext's write
lock on that one chapter, re-reads the chapter, replaces only the verse you
changed, writes it with `PutUSFM`, and releases the lock. If someone changed
the same verse elsewhere since this window loaded it, you are asked which
version to keep. Edits to other verses are always kept. Paratext's normal
permission checks apply.

## Install

Requirements: Paratext 9.2 or later (built and tested against 9.5), Windows,
and the .NET SDK (6 or later) to build.

1. Close Paratext.
2. In PowerShell: `pwsh -File install.ps1`
   It builds the plugin and copies it to
   `C:\Program Files\Paratext 9\plugins\ParallelEdit\ParallelEdit.ptxplg`.
   Windows asks for administrator permission because that folder is protected.
3. Start Paratext, open a project, and choose **Tools ▸ Parallel Edit…** from
   that project window's menu.

Uninstall: `pwsh -File install.ps1 -Uninstall`.

## Limits

- The book header and introduction (everything before verse 1 of a chapter,
  such as `\id`, `\mt`, `\ip`) are kept but not shown.
- Verse text is edited as USFM while you type; the plugin does not check the
  markers. Paratext's own checks will flag mistakes as usual.
- A verse label that is not a number (malformed data such as `\v I`) is kept
  untouched but not shown.

## Development

- `src/ParallelEdit` — the plugin. `Core/` has the USFM verse splitting and
  merge logic (no Paratext or UI types); `UI/` has the grid; the files at the
  top connect it to Paratext.
- `tests/ParallelEdit.Tests` — console tests:
  `dotnet run --project tests/ParallelEdit.Tests -- "C:\My Paratext 9 Projects"`
  also checks that every chapter of every local project splits and rebuilds
  byte-for-byte.
- `tests/TestHost` — runs the same view outside Paratext on project folders.
  Use copies of the folders; `--edit` really writes to them.
