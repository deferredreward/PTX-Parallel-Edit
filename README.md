# Parallel Edit (Paratext 9 plugin)

Shows several Paratext texts side by side, one row per verse, and lets you edit
the texts you are allowed to edit, right in that grid.

- **Pick texts**: `Texts…` opens a list of every project and resource on this
  computer. Add them in the order you want them left to right. The first text
  sets the verse numbering (versification); the others are mapped onto it, so
  a Hebrew-numbered resource still lines up with an English-numbered project.
- **Read**: each row is one verse. Section headings get their own thin row
  above the verse they introduce. The view mode dropdown picks how text is
  shown:
  - **Clean** (default): plain text, footnotes/cross references/markers
    hidden, so the text reads cleanly.
  - **Standard**: text styled like Paratext's Standard view (font size, bold,
    italic, color, indents, etc. from the project's stylesheet), with every
    USFM marker shown in small grey text and notes shown inline. Headings
    appear inside the verse cells, as in Unformatted.
  - **Unformatted**: raw USFM in every cell; heading rows are hidden because
    headings then appear inside the verse cells.
  Whatever the mode, clicking into an editable cell always shows that verse's
  raw USFM as plain text for editing.
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

- `src/ParallelEdit` — the plugin. `Core/` has the USFM verse splitting, merge
  and tokenizing logic (no Paratext or UI types) — including `StyledText`,
  which turns USFM into paragraphs/runs for Standard mode, and `MarkerStyle`,
  the stylesheet-derived look for one marker; `UI/` has the grid and
  `RtfBuilder` (turns `StyledText` + marker styles into the RTF a cell shows);
  the files at the top connect it to Paratext, including
  `ParatextTextSource.MarkerStyles` (from `Project.ScriptureMarkerInformation`).
- `tests/ParallelEdit.Tests` — console tests:
  `dotnet run --project tests/ParallelEdit.Tests -- "C:\My Paratext 9 Projects"`
  also checks that every chapter of every local project splits and rebuilds
  byte-for-byte, and that `StyledText` round-trips every verse.
- `tests/TestHost` — runs the same view outside Paratext on project folders.
  Use copies of the folders; `--edit` really writes to them. `--mode
  clean|standard|unformatted` picks the view mode (`--markers` is a legacy
  alias for `--mode unformatted`). Standard mode reads `usfm.sty` from the
  projects folder and `custom.sty` from the project folder, if present.
