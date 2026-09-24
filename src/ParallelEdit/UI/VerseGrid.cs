using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ParallelEdit.Core;

namespace ParallelEdit.UI
{
    /// <summary>Font and direction for one text column.</summary>
    public class TextColumnStyle
    {
        public Font Font;
        public Font HeadingFont;
        public bool RightToLeft;
    }

    /// <summary>
    /// Scrolling grid: one row per verse (plus thin rows for section headings), one column per text.
    /// Each cell is a borderless TextBox; the grid paints the cell backgrounds and 1px grid lines.
    /// </summary>
    public class VerseGrid : Panel
    {
        public class RowView
        {
            public RowInfo Info;
            public Label Label;
            public CellBox[] Cells;
            public int Top, Height;
        }

        /// <summary>A cell's text box. Knows its row/column and forwards the mouse wheel to the grid.</summary>
        public class CellBox : TextBox
        {
            public RowView Row;
            public int Column;
            public CellInfo Info;
            /// <summary>Text shown when editing began, to detect a change on leaving.</summary>
            public string EditStartText;
            /// <summary>Set when focusing swapped in the raw text, so the click that caused it can place the caret.</summary>
            public bool JustRevealed;
            internal VerseGrid Grid;

            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEWHEEL = 0x020A;
                if (m.Msg == WM_MOUSEWHEEL && Grid != null)
                {
                    Grid.ScrollByWheel((short)((long)m.WParam >> 16));
                    return;
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        const int EM_GETLINECOUNT = 0xBA;

        readonly List<RowView> rows = new List<RowView>();
        TextColumnStyle[] columns = new TextColumnStyle[0];
        readonly Font labelFont = new Font("Segoe UI", 8.25f);
        readonly Font labelFontBold = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        int currentVerse = -1;
        bool showMarkers;
        bool suppressEvents;

        public event Action<CellBox> CellEntered;
        /// <summary>Raised when an editable cell loses focus with changed text.</summary>
        public event Action<CellBox, string> CellCommitted;
        /// <summary>Raised when a cell loses focus without a change.</summary>
        public event Action<CellBox> CellLeftUnchanged;

        public VerseGrid()
        {
            AutoScroll = true;
            BackColor = Theme.GridLine;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // The grid itself can hold focus, so opening the window does not drop the caret into the first cell.
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int line = labelFont.Height * 2, page = Math.Max(line, ClientSize.Height - line);
            int delta = e.KeyCode == Keys.Up ? -line : e.KeyCode == Keys.Down ? line : e.KeyCode == Keys.PageUp ? -page : e.KeyCode == Keys.PageDown ? page : 0;
            if (delta != 0)
            {
                AutoScrollPosition = new Point(0, Math.Max(0, -AutoScrollPosition.Y + delta));
                Invalidate();
                e.Handled = true;
            }
        }

        public IReadOnlyList<RowView> Rows => rows;

        public int LabelWidth => Scale(40);
        int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96f);

        /// <summary>Width usable by columns; the scrollbar's width is always reserved so layout does not jump.</summary>
        public int ContentWidth => Math.Max(100, Width - SystemInformation.VerticalScrollBarWidth - 2);

        public int ColumnLeft(int col) => LabelWidth + 1 + col * (ColumnWidth + 1);
        public int ColumnWidth => columns.Length == 0 ? 0 : Math.Max(40, (ContentWidth - LabelWidth - 1 - columns.Length) / columns.Length);

        public bool ShowMarkers
        {
            get => showMarkers;
            set
            {
                if (showMarkers == value) return;
                showMarkers = value;
                foreach (var r in rows) foreach (var c in r.Cells) if (!c.Focused) c.Text = DisplayText(c, false);
                LayoutRows();
            }
        }

        public void SetRows(IList<RowInfo> newRows, TextColumnStyle[] styles)
        {
            suppressEvents = true;
            SuspendLayout();
            try
            {
                foreach (var r in rows)
                {
                    r.Label.Dispose();
                    foreach (var c in r.Cells) c.Dispose();
                }
                rows.Clear();
                Controls.Clear();
                AutoScrollPosition = Point.Empty;
                columns = styles;

                var controls = new List<Control>();
                foreach (var info in newRows)
                {
                    var rv = new RowView { Info = info, Cells = new CellBox[info.Cells.Length] };
                    rv.Label = new Label
                    {
                        Text = info.Label,
                        Font = labelFont,
                        ForeColor = Theme.LabelText,
                        BackColor = Theme.LabelBack,
                        TextAlign = ContentAlignment.TopRight,
                        Padding = new Padding(0, Scale(3), Scale(4), 0),
                    };
                    controls.Add(rv.Label);
                    for (int col = 0; col < info.Cells.Length; col++)
                    {
                        var cell = info.Cells[col];
                        var style = styles[col];
                        var box = new CellBox
                        {
                            Row = rv, Column = col, Info = cell, Grid = this,
                            Multiline = true, WordWrap = true, BorderStyle = BorderStyle.None,
                            ScrollBars = ScrollBars.None, AcceptsReturn = true,
                            ReadOnly = !cell.Editable,
                            Font = info.IsHeading ? style.HeadingFont : style.Font,
                            RightToLeft = style.RightToLeft ? RightToLeft.Yes : RightToLeft.No,
                            BackColor = cell.Editable ? Theme.EditableBack : Theme.ReadOnlyBack,
                            ForeColor = cell.Kind == CellKind.Continued ? Theme.Muted : info.IsHeading ? Theme.Heading : Theme.Text,
                            TabStop = cell.Kind == CellKind.Text,
                        };
                        box.Text = DisplayText(box, false);
                        box.Enter += Box_Enter;
                        box.Leave += Box_Leave;
                        box.TextChanged += Box_TextChanged;
                        box.MouseDown += Box_MouseDown;
                        box.MouseUp += Box_MouseUp;
                        rv.Cells[col] = box;
                        controls.Add(box);
                    }
                    rows.Add(rv);
                }
                Controls.AddRange(controls.ToArray());
            }
            finally
            {
                ResumeLayout(false);
                suppressEvents = false;
            }
            LayoutRows();
            HighlightVerse(currentVerse);
        }

        string DisplayText(CellBox box, bool focused)
        {
            var info = box.Info;
            if (info.Kind == CellKind.Empty) return "";
            if (info.Kind == CellKind.Continued) return info.CleanText;
            if (showMarkers || (focused && info.Editable)) return info.RawText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            return info.CleanText.Replace("\n", "\r\n");
        }

        /// <summary>Refreshes the shown text of every cell from the model (e.g. after a save changed the chapter).</summary>
        public void RefreshTexts()
        {
            suppressEvents = true;
            foreach (var r in rows) foreach (var c in r.Cells) if (!c.Focused) c.Text = DisplayText(c, false);
            suppressEvents = false;
            LayoutRows();
        }

        void Box_Enter(object sender, EventArgs e)
        {
            var box = (CellBox)sender;
            if (suppressEvents) return;
            if (box.Info.Editable)
            {
                box.BackColor = Theme.FocusBack;
                // Swapping the text while a mouse button is down turns the click into a drag-selection,
                // so a click waits for the button to come up (Box_MouseUp) before showing the raw text.
                if (Control.MouseButtons != MouseButtons.None) box.JustRevealed = true;
                else Reveal(box, 0);
            }
            else box.EditStartText = box.Text;
            CellEntered?.Invoke(box);
        }

        /// <summary>Shows the raw USFM for editing, putting the caret where it was in the clean text.</summary>
        void Reveal(CellBox box, int cleanCaret)
        {
            box.JustRevealed = false;
            string shown = box.Text;
            string raw = DisplayText(box, true);
            suppressEvents = true;
            if (shown != raw)
            {
                string rawN = raw.Replace("\r\n", "\n");
                int cleanN = cleanCaret - shown.Substring(0, Math.Min(cleanCaret, shown.Length)).Split('\r').Length + 1;
                int caret = ChapterText.MapCleanToRaw(shown.Replace("\r\n", "\n"), cleanN, rawN);
                // the box uses CRLF line breaks; add one position per line break before the caret
                int lines = rawN.Substring(0, caret).Split('\n').Length - 1;
                box.Text = raw;
                box.SelectionStart = Math.Min(raw.Length, caret + lines);
            }
            else box.SelectionStart = Math.Min(shown.Length, cleanCaret);
            box.SelectionLength = 0;
            suppressEvents = false;
            box.EditStartText = box.Text;
            LayoutRows();
        }

        void Box_MouseDown(object sender, MouseEventArgs e)
        {
            var box = (CellBox)sender;
            if (!box.Focused) box.Focus();
        }

        void Box_MouseUp(object sender, MouseEventArgs e)
        {
            var box = (CellBox)sender;
            if (box.JustRevealed) Reveal(box, box.SelectionStart);
        }

        void Box_Leave(object sender, EventArgs e)
        {
            var box = (CellBox)sender;
            if (suppressEvents || box.IsDisposed) return;
            if (!box.Info.Editable) return;
            if (box.JustRevealed)
            {
                // focus left before the raw text was ever shown: nothing was edited
                box.JustRevealed = false;
                box.BackColor = Theme.EditableBack;
                return;
            }
            box.BackColor = Theme.EditableBack;
            string edited = box.Text;
            bool changed = edited != box.EditStartText;
            if (changed) CellCommitted?.Invoke(box, edited);
            else CellLeftUnchanged?.Invoke(box);
            if (box.IsDisposed) return; // a commit may have rebuilt the grid
            suppressEvents = true;
            box.Text = DisplayText(box, false);
            suppressEvents = false;
            LayoutRows();
        }

        void Box_TextChanged(object sender, EventArgs e)
        {
            if (suppressEvents) return;
            var box = (CellBox)sender;
            if (!box.Focused) return;
            int before = box.Row.Height;
            if (MeasureRow(box.Row) != before) LayoutRows();
        }

        /// <summary>Commits the focused cell (if any) so pending typing is not lost, e.g. before saving or navigating.</summary>
        public void CommitFocused()
        {
            var box = FocusedCell;
            if (box == null || !box.Info.Editable || box.Text == box.EditStartText) return;
            string edited = box.Text;
            box.EditStartText = edited;
            CellCommitted?.Invoke(box, edited);
        }

        public CellBox FocusedCell => rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Focused);

        public void FocusCell(int row, int col, int selectionStart)
        {
            if (row < 0 || row >= rows.Count || col < 0 || col >= rows[row].Cells.Length) return;
            var box = rows[row].Cells[col];
            box.Focus();
            box.SelectionStart = Math.Min(selectionStart, box.TextLength);
            box.SelectionLength = 0;
        }

        int MeasureRow(RowView r)
        {
            int w = ColumnWidth - Scale(8);
            int h = 0;
            foreach (var c in r.Cells)
            {
                int lines;
                if (c.IsHandleCreated)
                {
                    if (c.Width != w) c.Width = w;
                    lines = (int)SendMessage(c.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
                    if (c.TextLength == 0) lines = 1;
                    h = Math.Max(h, lines * c.Font.Height);
                }
                else
                {
                    var size = TextRenderer.MeasureText(c.Text.Length == 0 ? " " : c.Text, c.Font, new Size(Math.Max(10, w - 4), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                    h = Math.Max(h, size.Height);
                }
            }
            r.Height = Math.Max(h + Scale(6), labelFont.Height + Scale(6));
            return r.Height;
        }

        public void LayoutRows()
        {
            if (columns.Length == 0) { AutoScrollMinSize = Size.Empty; Invalidate(); return; }
            SuspendLayout();
            int y = 0;
            int colW = ColumnWidth;
            int scrollY = AutoScrollPosition.Y;
            foreach (var r in rows)
            {
                // with markers shown, headings appear inside the verse cells, so heading rows are hidden
                bool visible = !(r.Info.IsHeading && showMarkers);
                if (r.Label.Visible != visible)
                {
                    r.Label.Visible = visible;
                    foreach (var c in r.Cells) c.Visible = visible;
                }
                r.Top = y;
                if (!visible) { r.Height = 0; continue; }
                MeasureRow(r);
                r.Label.Bounds = new Rectangle(0, y + scrollY, LabelWidth, r.Height);
                for (int col = 0; col < r.Cells.Length; col++)
                {
                    var box = r.Cells[col];
                    var bounds = new Rectangle(ColumnLeft(col) + Scale(4), y + scrollY + Scale(3), colW - Scale(8), r.Height - Scale(6));
                    if (box.Bounds != bounds) box.Bounds = bounds;
                }
                y += r.Height + 1;
            }
            ResumeLayout(false);
            AutoScrollMinSize = new Size(0, y);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // cell handles now exist, so row heights can use the real line wrapping
            BeginInvoke((Action)LayoutRows);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (rows.Count > 0) LayoutRows();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int scrollY = AutoScrollPosition.Y;
            int colW = ColumnWidth;
            foreach (var r in rows)
            {
                if (r.Height == 0) continue;
                int top = r.Top + scrollY;
                if (top > e.ClipRectangle.Bottom) break;
                if (top + r.Height < e.ClipRectangle.Top) continue;
                for (int col = 0; col < r.Cells.Length; col++)
                {
                    using (var b = new SolidBrush(r.Cells[col].BackColor))
                        e.Graphics.FillRectangle(b, ColumnLeft(col), top, colW, r.Height);
                }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // WS_EX_COMPOSITED: paint the grid and all its cells in one buffered pass,
                // so scrolling does not leave strips of stale background between rows
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate(true);
        }

        internal void ScrollByWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = (lines <= 0 ? 3 : lines) * labelFont.Height * delta / 120;
            int y = Math.Max(0, -AutoScrollPosition.Y - step);
            AutoScrollPosition = new Point(0, y);
            Invalidate(true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
        }

        /// <summary>Marks the verse number of the current verse and scrolls it into view if needed.</summary>
        public void HighlightVerse(int verse, bool scroll = false)
        {
            currentVerse = verse;
            RowView target = null;
            foreach (var r in rows)
            {
                bool isCurrent = !r.Info.IsHeading && r.Info.Ref.Verse == verse && !r.Info.Label.EndsWith("*");
                r.Label.BackColor = isCurrent ? Theme.CurrentBack : Theme.LabelBack;
                r.Label.ForeColor = isCurrent ? Theme.Current : Theme.LabelText;
                r.Label.Font = isCurrent ? labelFontBold : labelFont;
                if (isCurrent && target == null) target = r;
            }
            if (scroll && target != null)
            {
                int viewTop = -AutoScrollPosition.Y;
                int idx = rows.IndexOf(target);
                int top = idx > 0 && rows[idx - 1].Info.IsHeading ? rows[idx - 1].Top : target.Top;
                if (top < viewTop || target.Top + target.Height > viewTop + ClientSize.Height)
                {
                    AutoScrollPosition = new Point(0, Math.Max(0, top - Scale(4)));
                    Invalidate();
                }
            }
        }

        protected override Point ScrollToControl(Control activeControl)
        {
            // keep the current scroll position when a cell gets focus by mouse; only move when it is off screen
            var p = AutoScrollPosition;
            var b = activeControl.Bounds;
            if (b.Top >= 0 && b.Bottom <= ClientSize.Height) return p;
            return base.ScrollToControl(activeControl);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { labelFont.Dispose(); labelFontBold.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
