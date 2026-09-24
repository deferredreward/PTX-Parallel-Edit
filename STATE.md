# STATE — PTX-Parallel-Edit

Paratext 9 plugin: several texts side by side, verse by verse, editable where
Paratext allows. See README for use and install.

## Gotchas (learned the hard way)

- **Plugins load only from `C:\Program Files\Paratext 9\plugins\<Name>\`**
  (confirmed in `%LOCALAPPDATA%\Paratext95\ParatextLog.log`). Every install
  needs an admin prompt; Paratext must be closed. `install.ps1` handles both.
- **Licensed resources are blocked**: `GetUSFM` on ESV/NIV/BHS etc. throws
  "Plugin does not have access to GetUSFM for this project" (issue #1).
- **Paratext normalizes whitespace on save**: the first plugin save of a chapter
  removed blank lines and joined `\ts \*` onto the previous line (text
  unchanged). Believed to be Paratext's `PutUSFM`, not our code (our merge only
  replaces edited verses), but not proven.
- **The plugin API does not expose Paratext's editor/renderer** for embedding;
  view modes must be built from `ScriptureMarkerInformation` (issue #3).
- **Swapping a TextBox's text during mouse-down turns the click into a drag
  selection**; raw USFM is revealed on mouse-up instead (`VerseGrid.Reveal`).
- **RichTextBox quirks** (cells are `RichTextBox` since view modes, issue #3):
  `.Text` uses bare `\n` for every line break, including `\par` from RTF — no
  `\r` at all, unlike `TextBox`'s `\r\n`. Setting `.Rtf` while the control is
  still its old (smaller) height scrolls to keep the caret in view, hiding
  earlier lines even after the control is later resized to fit — reset
  `SelectionStart = 0` and call `ScrollToCaret()` after both setting `.Rtf`
  and after any resize (`VerseGrid.SetRtf`, `LayoutRows`). `ContentsResized`
  (EN_REQUESTRESIZE) only fires once `EM_SETEVENTMASK` has the
  `ENM_REQUESTRESIZE` bit set — do that in `OnHandleCreated`, not before.
  `Form.DrawToBitmap` does not render `RichTextBox` content at all; screenshot
  by copying the real screen area instead (`TestHost.Snap`).
- **The plugin API reports stylesheet indents in thousandths of an inch**
  (`IParagraphMarkerInfo.FirstLineIndent`/`LeftMargin`/`RightMargin`: `\p`'s
  `.125` arrives as 125; Paratext's `ScrTag.ParseF` multiplies by 1000).
  `MarkerStyle` holds inches, so `ParatextTextSource` divides. The TestHost reads
  `.sty` files directly in inches, so it cannot catch a unit slip here; only a
  run inside Paratext does.
- **Build**: .NET SDK (user-local `~\.dotnet` on this laptop), targets net48,
  references the plugin DLLs from the Paratext install dir
  (`-p:ParatextInstallDir=...` to override).

## Verification habits

- `tests/ParallelEdit.Tests` round-trips every chapter of every local project
  byte-for-byte; run it with the projects folder as the argument.
- Before any in-Paratext edit test, back up the book `.SFM` and hash-compare
  afterwards. Never send keystrokes until a screenshot confirms focus.

## Test data

- `ULE` (ULTtoEdit) is the project Benjamin designated as safe to edit.
