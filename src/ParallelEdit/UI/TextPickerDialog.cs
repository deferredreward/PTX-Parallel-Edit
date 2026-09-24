using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ParallelEdit.Core;

namespace ParallelEdit.UI
{
    /// <summary>Two lists: every available text on the left, the shown texts (in order) on the right.</summary>
    public class TextPickerDialog : Form
    {
        readonly IReadOnlyList<ITextSource> all;
        readonly TextBox filter = new TextBox();
        readonly ListBox available = new ListBox { IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
        readonly ListBox chosen = new ListBox { IntegralHeight = false };

        class Item
        {
            public ITextSource Source;
            public override string ToString() =>
                Source.ShortName + (Source.FullName != Source.ShortName && !string.IsNullOrEmpty(Source.FullName) ? " – " + Source.FullName : "") +
                (Source.IsResource ? "  (resource)" : "");
        }

        public TextPickerDialog(IReadOnlyList<ITextSource> allTexts, IEnumerable<ITextSource> current)
        {
            all = allTexts.OrderBy(t => t.IsResource).ThenBy(t => t.ShortName, StringComparer.OrdinalIgnoreCase).ToList();
            Text = "Choose texts";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 440);
            MinimumSize = new Size(560, 320);

            var addBtn = new Button { Text = "Add →", Width = 90 };
            var removeBtn = new Button { Text = "← Remove", Width = 90 };
            var upBtn = new Button { Text = "Move up", Width = 90 };
            var downBtn = new Button { Text = "Move down", Width = 90 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            AcceptButton = ok;
            CancelButton = cancel;

            var leftLabel = new Label { Text = "Available (type to filter)", AutoSize = true };
            var rightLabel = new Label { Text = "Shown, left to right. The first sets verse numbering.", AutoSize = true };

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(10) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            filter.Dock = DockStyle.Fill;
            available.Dock = DockStyle.Fill;
            chosen.Dock = DockStyle.Fill;
            var middle = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, Padding = new Padding(4, 30, 0, 0) };
            middle.Controls.AddRange(new Control[] { addBtn, removeBtn, new Label { Height = 16 }, upBtn, downBtn });
            var bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            bottom.Controls.AddRange(new Control[] { cancel, ok });

            table.Controls.Add(leftLabel, 0, 0);
            table.Controls.Add(rightLabel, 2, 0);
            table.Controls.Add(filter, 0, 1);
            table.Controls.Add(available, 0, 2);
            table.Controls.Add(middle, 1, 2);
            table.Controls.Add(chosen, 2, 1);
            table.SetRowSpan(chosen, 2);
            table.Controls.Add(bottom, 0, 3);
            table.SetColumnSpan(bottom, 3);
            Controls.Add(table);

            foreach (var t in current) chosen.Items.Add(new Item { Source = t });
            FillAvailable();

            filter.TextChanged += (s, e) => FillAvailable();
            filter.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && available.Items.Count > 0) { available.Focus(); available.SelectedIndex = 0; e.Handled = true; }
                if (e.KeyCode == Keys.Enter && available.Items.Count == 1) { available.SelectedIndex = 0; Add(); e.SuppressKeyPress = true; }
            };
            addBtn.Click += (s, e) => Add();
            available.DoubleClick += (s, e) => Add();
            removeBtn.Click += (s, e) => Remove();
            chosen.DoubleClick += (s, e) => Remove();
            upBtn.Click += (s, e) => MoveSelected(-1);
            downBtn.Click += (s, e) => MoveSelected(1);
        }

        public IEnumerable<ITextSource> Selected => chosen.Items.Cast<Item>().Select(i => i.Source).ToList();

        HashSet<string> ChosenIds => new HashSet<string>(chosen.Items.Cast<Item>().Select(i => i.Source.Id));

        void FillAvailable()
        {
            string f = filter.Text.Trim();
            var ids = ChosenIds;
            available.BeginUpdate();
            available.Items.Clear();
            foreach (var t in all)
            {
                if (ids.Contains(t.Id)) continue;
                var item = new Item { Source = t };
                if (f.Length == 0 || item.ToString().IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    available.Items.Add(item);
            }
            available.EndUpdate();
        }

        void Add()
        {
            foreach (Item item in available.SelectedItems.Cast<Item>().ToList()) chosen.Items.Add(item);
            FillAvailable();
        }

        void Remove()
        {
            if (chosen.SelectedItem == null) return;
            chosen.Items.Remove(chosen.SelectedItem);
            FillAvailable();
        }

        void MoveSelected(int delta)
        {
            int i = chosen.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= chosen.Items.Count) return;
            var item = chosen.Items[i];
            chosen.Items.RemoveAt(i);
            chosen.Items.Insert(j, item);
            chosen.SelectedIndex = j;
        }
    }
}
