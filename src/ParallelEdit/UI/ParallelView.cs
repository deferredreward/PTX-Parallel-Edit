using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ParallelEdit.Core;

namespace ParallelEdit.UI
{
    /// <summary>
    /// The whole parallel window without any Paratext types: a thin toolbar, a header row
    /// with the text names, and the verse grid. Hosts feed it ITextSource objects.
    /// </summary>
    public class ParallelView : UserControl
    {
        readonly ToolStrip toolbar;
        readonly ToolStripButton textsButton, prevButton, nextButton, markersButton;
        readonly ToolStripTextBox refBox;
        readonly ToolStripLabel status;
        readonly Panel header;
        readonly VerseGrid grid;
        readonly ToolTip tips = new ToolTip();

        List<ITextSource> texts = new List<ITextSource>();
        readonly Dictionary<string, LoadedChapter> cache = new Dictionary<string, LoadedChapter>();
        readonly List<Font> fonts = new List<Font>();
        VerseRef current = new VerseRef(40, 1, 1);
        bool loaded;

        /// <summary>Supplies every text the user may choose from.</summary>
        public Func<IReadOnlyList<ITextSource>> AvailableTexts;

        /// <summary>Raised when the user moves to another verse inside the view (to keep Paratext windows in step).</summary>
        public event Action<VerseRef> ReferenceChangedByUser;

        /// <summary>Raised when the chosen texts change, so the host can save its state.</summary>
        public event Action TextsChanged;

        public ParallelView()
        {
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;

            toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Padding = new Padding(4, 1, 4, 1), BackColor = Color.White };
            textsButton = new ToolStripButton("Texts…") { ToolTipText = "Choose which texts to show, and their order" };
            prevButton = new ToolStripButton("◀") { ToolTipText = "Previous chapter" };
            refBox = new ToolStripTextBox { AutoSize = false, Width = 90, ToolTipText = "Type a reference (e.g. MRK 3 or JHN 3:16) and press Enter" };
            nextButton = new ToolStripButton("▶") { ToolTipText = "Next chapter" };
            markersButton = new ToolStripButton("Markers") { CheckOnClick = true, ToolTipText = "Show USFM markers in every cell (editable cells always show them while you type)" };
            status = new ToolStripLabel("") { Alignment = ToolStripItemAlignment.Right, ForeColor = Theme.LabelText };
            toolbar.Items.AddRange(new ToolStripItem[] { textsButton, new ToolStripSeparator(), prevButton, refBox, nextButton, new ToolStripSeparator(), markersButton, status });

            header = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.LabelBack };
            header.Paint += Header_Paint;
            grid = new VerseGrid { Dock = DockStyle.Fill };
            grid.Resize += (s, e) => header.Invalidate();
            grid.CellEntered += Grid_CellEntered;
            grid.CellCommitted += Grid_CellCommitted;
            grid.CellLeftUnchanged += Grid_CellLeftUnchanged;

            Controls.Add(grid);
            Controls.Add(header);
            Controls.Add(toolbar);

            textsButton.Click += (s, e) => ChooseTexts();
            prevButton.Click += (s, e) => MoveChapter(-1);
            nextButton.Click += (s, e) => MoveChapter(1);
            markersButton.CheckedChanged += (s, e) => grid.ShowMarkers = markersButton.Checked;
            refBox.KeyDown += RefBox_KeyDown;
        }

        public IReadOnlyList<ITextSource> Texts => texts;
        public VerseRef Current => current;

        public bool ShowMarkers
        {
            get => markersButton.Checked;
            set => markersButton.Checked = value;
        }

        /// <summary>Replaces the list of shown texts. The first one sets verse numbering (versification).</summary>
        public void SetTexts(IEnumerable<ITextSource> newTexts)
        {
            if (!SaveAll() && !ConfirmDiscard()) return;
            foreach (var t in texts) t.ScriptureChanged -= Source_ScriptureChanged;
            texts = newTexts.Where(t => t != null).GroupBy(t => t.Id).Select(g => g.First()).ToList();
            foreach (var t in texts) t.ScriptureChanged += Source_ScriptureChanged;
            cache.Clear();
            if (loaded) Rebuild(scroll: true);
            TextsChanged?.Invoke();
        }

        /// <summary>Shows a reference (from Paratext or the toolbar). Saves pending edits before changing chapter.</summary>
        public void GoTo(VerseRef r)
        {
            if (r.Book <= 0 || r.Chapter <= 0) return;
            bool sameChapter = loaded && r.SameChapter(current);
            if (!sameChapter)
            {
                if (!SaveAll() && !ConfirmDiscard()) return;
                cache.Clear();
            }
            current = r;
            refBox.Text = Books.Code(r.Book) + " " + r.Chapter + ":" + r.Verse;
            if (sameChapter) grid.HighlightVerse(r.Verse, scroll: true);
            else { loaded = true; Rebuild(scroll: true); }
        }

        LoadedChapter GetChapter(ITextSource src, int book, int chapter)
        {
            string key = src.Id + "|" + book + "|" + chapter;
            if (!cache.TryGetValue(key, out var lc))
                cache[key] = lc = LoadedChapter.Load(src, book, chapter);
            return lc;
        }

        void Rebuild(bool scroll, int focusRow = -1, int focusCol = -1, int selection = 0)
        {
            var rows = RowBuilder.Build(current.Book, current.Chapter, texts, GetChapter);
            foreach (var f in fonts) f.Dispose();
            fonts.Clear();
            var styles = texts.Select(t =>
            {
                var style = new TextColumnStyle
                {
                    Font = MakeFont(t.FontFamily, t.FontSize, FontStyle.Regular),
                    HeadingFont = MakeFont(t.FontFamily, t.FontSize, FontStyle.Bold),
                    RightToLeft = t.RightToLeft,
                };
                fonts.Add(style.Font);
                fonts.Add(style.HeadingFont);
                return style;
            }).ToArray();
            grid.SetRows(rows, styles);
            grid.HighlightVerse(current.Verse, scroll);
            if (focusRow >= 0) grid.FocusCell(focusRow, focusCol, selection);
            else if (ContainsFocus || ActiveControl == null) ActiveControl = grid;
            header.Invalidate();
            var errors = texts.Select(t => GetChapter(t, current.Book, current.Chapter))
                .Where(lc => lc.Error != null).Select(lc => lc.Source.ShortName + ": " + lc.Error).ToList();
            tips.SetToolTip(header, errors.Count > 0 ? string.Join("\n", errors) : "");
            if (errors.Count > 0) SetStatus("Could not read " + string.Join(", ", errors), true);
            prevButton.Enabled = current.Chapter > 1;
            nextButton.Enabled = texts.Count > 0 && current.Chapter < Math.Max(1, texts[0].LastChapter(current.Book));
            if (texts.Count == 0) SetStatus("Click “Texts…” to choose what to show.", false);
        }

        static Font MakeFont(string family, float size, FontStyle style)
        {
            float pt = size <= 0 ? 10f : Math.Max(8f, Math.Min(24f, size));
            try { return new Font(string.IsNullOrEmpty(family) ? "Segoe UI" : family, pt, style); }
            catch (ArgumentException) { return new Font("Segoe UI", pt, style); }
        }

        void Header_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var line = new Pen(Theme.GridLine))
                g.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
            if (texts.Count == 0) return;
            using (var nameFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var noteFont = new Font("Segoe UI", 8f))
            {
                for (int col = 0; col < texts.Count; col++)
                {
                    var t = texts[col];
                    var lc = GetChapter(t, current.Book, current.Chapter);
                    int x = grid.ColumnLeft(col) + 4;
                    var rect = new Rectangle(x, 0, grid.ColumnWidth - 8, header.Height - 1);
                    string note = lc.Error != null
                        ? (lc.Error.IndexOf("does not have access", StringComparison.OrdinalIgnoreCase) >= 0 ? "licensed: Paratext blocks plugins" : "unavailable") : lc.Text.Segments.Count == 0 ? "no text here" : lc.Editable ? "" : "read-only";
                    var nameSize = TextRenderer.MeasureText(g, t.ShortName, nameFont);
                    TextRenderer.DrawText(g, t.ShortName, nameFont, rect, Theme.Heading,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (note.Length > 0)
                    {
                        var noteRect = new Rectangle(x + nameSize.Width, 0, Math.Max(0, rect.Width - nameSize.Width), rect.Height);
                        TextRenderer.DrawText(g, note, noteFont, noteRect, Theme.Muted,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }
                }
            }
        }

        void Grid_CellEntered(VerseGrid.CellBox box)
        {
            var info = box.Row.Info;
            if (box.Info.Editable) box.Info.Chapter.Source.ActivateKeyboard();
            tips.SetToolTip(header, texts.Count > box.Column ? texts[box.Column].FullName : "");
            if (!info.Ref.Equals(current))
            {
                current = info.Ref;
                refBox.Text = Books.Code(current.Book) + " " + current.Chapter + ":" + current.Verse;
                grid.HighlightVerse(current.Verse);
                ReferenceChangedByUser?.Invoke(current);
            }
        }

        void Grid_CellCommitted(VerseGrid.CellBox box, string editedText)
        {
            var cell = box.Info;
            var lc = cell.Chapter;
            string newRaw = ChapterText.Reassemble(cell.Part, cell.Segment.Get(cell.Part), editedText, lc.Text.NewLine);
            try
            {
                lc.Edit(cell.Segment, cell.Part, newRaw);
            }
            catch (FormatException e)
            {
                MessageBox.Show(this, e.Message + "\n\nYour change to this verse was not kept.", "Parallel Edit",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                BeginInvoke((Action)grid.RefreshTexts);
                return;
            }
            if (!Save(lc, out bool structureChanged)) return;
            if (structureChanged) RebuildKeepingFocus();
        }

        void Grid_CellLeftUnchanged(VerseGrid.CellBox box)
        {
            var lc = box.Info.Chapter;
            if (lc != null && !lc.IsDirty && IsStale(lc)) RebuildKeepingFocus(reload: lc);
        }

        /// <summary>Rebuilds after the current focus change finishes, keeping focus on the same cell position.</summary>
        void RebuildKeepingFocus(LoadedChapter reload = null)
        {
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed) return;
                var focused = grid.FocusedCell;
                int row = -1, col = -1, sel = 0;
                if (focused != null)
                {
                    row = grid.Rows.ToList().IndexOf(focused.Row);
                    col = focused.Column;
                    sel = focused.SelectionStart;
                    grid.CommitFocused();
                }
                if (reload != null) cache.Remove(reload.Source.Id + "|" + reload.Book + "|" + reload.Chapter);
                Rebuild(scroll: false, focusRow: row, focusCol: col, selection: sel);
            }));
        }

        bool IsStale(LoadedChapter lc)
        {
            try { return lc.Source.GetChapterUsfm(lc.Book, lc.Chapter) != lc.LoadedUsfm; }
            catch { return false; }
        }

        /// <summary>Writes one chapter's pending edits. Returns false when nothing could be written.</summary>
        bool Save(LoadedChapter lc, out bool structureChanged)
        {
            structureChanged = false;
            if (!lc.IsDirty) return true;
            string written = null;
            bool cancelled = false;
            string error = lc.Source.WriteChapter(lc.Book, lc.Chapter, fresh =>
            {
                string merged = lc.Merge(fresh, out var conflicts);
                if (conflicts.Count > 0)
                {
                    string where = string.Join(", ", conflicts.Select(c => c.SegmentKey.Split('#')[0]).Distinct());
                    var answer = MessageBox.Show(this,
                        $"{lc.Source.ShortName} {Books.Code(lc.Book)} {lc.Chapter}: verse {where} was also changed somewhere else since this window loaded it.\n\n" +
                        "Yes = keep your version of that verse\nNo = keep the other version (your change to that verse is dropped)\nCancel = do not save now",
                        "Parallel Edit", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (answer == DialogResult.Cancel) { cancelled = true; return null; }
                    if (answer == DialogResult.Yes) merged = LoadedChapter.ForceApply(merged, conflicts);
                }
                written = merged;
                return merged;
            });

            if (cancelled) { SetStatus("Not saved yet", true); return false; }
            if (error != null)
            {
                SetStatus($"Could not save {lc.Source.ShortName}: {error}", true);
                MessageBox.Show(this, $"Could not save {lc.Source.ShortName} {Books.Code(lc.Book)} {lc.Chapter}:\n{error}\n\nYour change is kept in this window and will be tried again when you next leave a cell or save.",
                    "Parallel Edit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            structureChanged = written != lc.Text.ToUsfm();
            if (structureChanged) lc.MarkWritten(written);
            else lc.MarkWrittenSameText(written);
            SetStatus($"Saved {lc.Source.ShortName} {Books.Code(lc.Book)} {lc.Chapter}", false);
            return true;
        }

        /// <summary>Commits the cell being typed in and writes all pending edits. Returns false if something is still unsaved.</summary>
        public bool SaveAll()
        {
            grid.CommitFocused();
            bool ok = true, rebuild = false;
            foreach (var lc in cache.Values.Where(c => c.IsDirty).ToList())
            {
                ok &= Save(lc, out bool changed);
                rebuild |= changed;
            }
            if (rebuild && loaded) RebuildKeepingFocus();
            return ok;
        }

        public bool HasUnsavedEdits => cache.Values.Any(c => c.IsDirty) || (grid.FocusedCell is VerseGrid.CellBox b && b.Info.Editable && b.Text != b.EditStartText);

        void Source_ScriptureChanged(int book, int chapter)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || book != current.Book && book != 0) return;
                var focused = grid.FocusedCell;
                bool reload = false;
                foreach (var lc in cache.Values.ToList())
                {
                    if (lc.Book != current.Book || (chapter != 0 && lc.Chapter != chapter)) continue;
                    if (lc.IsDirty) continue;                               // our save will merge
                    if (focused != null && focused.Info.Chapter == lc) continue; // user is in it; checked on leave
                    if (IsStale(lc)) { cache.Remove(lc.Source.Id + "|" + lc.Book + "|" + lc.Chapter); reload = true; }
                }
                if (reload) RebuildKeepingFocus();
            }));
        }

        void MoveChapter(int delta)
        {
            int ch = current.Chapter + delta;
            if (ch < 1) return;
            var r = new VerseRef(current.Book, ch, 1);
            GoTo(r);
            ReferenceChangedByUser?.Invoke(r);
        }

        void RefBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (Books.TryParse(refBox.Text, out var r))
            {
                GoTo(r);
                ReferenceChangedByUser?.Invoke(r);
                grid.Focus();
            }
            else SetStatus("Could not read that reference. Try e.g. MRK 3 or JHN 3:16", true);
        }

        public void ChooseTexts()
        {
            var all = AvailableTexts?.Invoke() ?? new List<ITextSource>();
            using (var dlg = new TextPickerDialog(all, texts))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    SetTexts(dlg.Selected);
            }
        }

        bool ConfirmDiscard() =>
            MessageBox.Show(this, "Some changes could not be saved (see the message above).\n\nDiscard them and continue?", "Parallel Edit",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        void SetStatus(string text, bool isError)
        {
            status.Text = text;
            status.ForeColor = isError ? Theme.Error : Theme.LabelText;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var t in texts) t.ScriptureChanged -= Source_ScriptureChanged;
                foreach (var f in fonts) f.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
