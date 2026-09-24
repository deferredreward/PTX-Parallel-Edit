using System;
using System.Collections.Generic;
using System.Linq;

namespace ParallelEdit.Core
{
    /// <summary>One chapter of one text as loaded into the view, with the edits not yet written.</summary>
    public class LoadedChapter
    {
        public ITextSource Source;
        public int Book, Chapter;
        /// <summary>USFM as last read from (or written to) the project.</summary>
        public string LoadedUsfm = "";
        public ChapterText Text = new ChapterText();
        public bool Editable;
        public string Error;

        /// <summary>Edits since the last write, keyed by segment key + part, holding the raw text before editing.</summary>
        public readonly Dictionary<string, PendingEdit> Pending = new Dictionary<string, PendingEdit>();

        public static LoadedChapter Load(ITextSource src, int book, int chapter)
        {
            var lc = new LoadedChapter { Source = src, Book = book, Chapter = chapter };
            try
            {
                lc.LoadedUsfm = src.GetChapterUsfm(book, chapter) ?? "";
                lc.Text = ChapterText.Parse(lc.LoadedUsfm);
                lc.Editable = lc.Text.Segments.Count > 0 && !src.IsResource && src.CanEdit(book, chapter);
            }
            catch (Exception e)
            {
                lc.Error = e.Message;
                lc.Text = new ChapterText();
                lc.Editable = false;
            }
            return lc;
        }

        public bool IsDirty => Pending.Count > 0;

        /// <summary>Changes one part of a segment in memory and remembers the original for merging.</summary>
        public void Edit(VerseSegment seg, SegmentPart part, string newRaw)
        {
            string key = Text.KeyOf(seg) + "|" + part;
            string oldRaw = seg.Get(part);
            if (oldRaw == newRaw) return;
            seg.Set(part, newRaw); // throws (changing nothing) if a Whole edit lost its \v marker
            if (Pending.TryGetValue(key, out var existing))
            {
                existing.NewRaw = newRaw;
                if (existing.OriginalRaw == newRaw) Pending.Remove(key);
            }
            else
            {
                Pending[key] = new PendingEdit { SegmentKey = Text.KeyOf(seg), Part = part, OriginalRaw = oldRaw, NewRaw = newRaw };
            }
        }

        /// <summary>
        /// Produces the USFM to write, given what the project holds right now.
        /// If the project has not changed since loading, this is simply the edited chapter.
        /// Otherwise each pending edit is applied to the fresh text, but only where that verse
        /// still has the text we started from; other verses become conflicts.
        /// </summary>
        public string Merge(string fresh, out List<PendingEdit> conflicts)
        {
            conflicts = new List<PendingEdit>();
            if (fresh == LoadedUsfm) return Text.ToUsfm();

            var freshText = ChapterText.Parse(fresh);
            foreach (var edit in Pending.Values)
            {
                var seg = freshText.FindByKey(edit.SegmentKey);
                if (seg == null || seg.Get(edit.Part) != edit.OriginalRaw)
                    conflicts.Add(edit);
                else
                    seg.Set(edit.Part, edit.NewRaw);
            }
            return freshText.ToUsfm();
        }

        /// <summary>Forces conflicting edits onto fresh text (user chose "keep mine"). Edits whose verse vanished are dropped.</summary>
        public static string ForceApply(string mergedUsfm, IEnumerable<PendingEdit> edits)
        {
            var text = ChapterText.Parse(mergedUsfm);
            foreach (var edit in edits)
                text.FindByKey(edit.SegmentKey)?.Set(edit.Part, edit.NewRaw);
            return text.ToUsfm();
        }

        /// <summary>Call after a successful write of exactly the in-memory text; keeps segment objects so open cells stay valid.</summary>
        public void MarkWrittenSameText(string usfm)
        {
            LoadedUsfm = usfm;
            Pending.Clear();
        }

        /// <summary>Call after a successful write.</summary>
        public void MarkWritten(string usfm)
        {
            LoadedUsfm = usfm;
            Text = ChapterText.Parse(usfm);
            Pending.Clear();
        }
    }

    public class PendingEdit
    {
        public string SegmentKey;
        public SegmentPart Part;
        public string OriginalRaw;
        public string NewRaw;
    }

    public enum CellKind { Text, Continued, Empty }

    public class CellInfo
    {
        public CellKind Kind;
        public LoadedChapter Chapter;
        public VerseSegment Segment;
        public SegmentPart Part;
        public bool Editable => Kind == CellKind.Text && Chapter != null && Chapter.Editable;
        /// <summary>Raw USFM of the part shown in this cell (trailing whitespace removed).</summary>
        public string RawText
        {
            get
            {
                if (Segment == null) return "";
                ChapterText.SplitTrailingWhitespace(Segment.Get(Part), out string t, out _);
                return t;
            }
        }
        public string CleanText => Kind == CellKind.Continued ? "↑ " + Segment.Label : ChapterText.ToCleanText(Part == SegmentPart.Whole ? Segment?.Body : Segment?.Get(Part));
    }

    public class RowInfo
    {
        public bool IsHeading;
        public string Label;
        public VerseRef Ref;
        public CellInfo[] Cells;
    }

    /// <summary>Builds the verse-by-verse rows for one chapter of the first ("anchor") text.</summary>
    public static class RowBuilder
    {
        public static List<RowInfo> Build(int book, int chapter, IList<ITextSource> texts,
            Func<ITextSource, int, int, LoadedChapter> getChapter)
        {
            var rows = new List<RowInfo>();
            if (texts.Count == 0) return rows;
            var anchor = texts[0];
            var anchorChapter = getChapter(anchor, book, chapter);

            // verses: the versification's count plus any extra verses actually in the anchor text
            var verses = new SortedSet<int>();
            int last = anchor.LastVerse(book, chapter);
            for (int v = 1; v <= last; v++) verses.Add(v);
            foreach (var seg in anchorChapter.Text.Segments)
                for (int v = Math.Max(1, seg.Start); v <= seg.End; v++) verses.Add(v);

            var used = texts.Select(_ => new HashSet<VerseSegment>()).ToArray();
            var identity = texts.Select(_ => true).ToArray();

            foreach (int v in verses)
            {
                var verseCells = new CellInfo[texts.Count];
                var leadCells = new CellInfo[texts.Count];
                bool anyLead = false;
                for (int col = 0; col < texts.Count; col++)
                {
                    var src = texts[col];
                    int mc = chapter, mv = v;
                    if (col > 0 && !src.TryMapVerse(anchor, book, chapter, v, out mc, out mv))
                    {
                        verseCells[col] = new CellInfo { Kind = CellKind.Empty };
                        identity[col] = false;
                        continue;
                    }
                    if (mc != chapter || mv != v) identity[col] = false;
                    var lc = col == 0 ? anchorChapter : getChapter(src, book, mc);
                    var seg = lc.Text.FindVerse(mv);
                    if (seg == null)
                    {
                        verseCells[col] = new CellInfo { Kind = CellKind.Empty, Chapter = lc };
                    }
                    else if (!used[col].Add(seg))
                    {
                        verseCells[col] = new CellInfo { Kind = CellKind.Continued, Chapter = lc, Segment = seg };
                    }
                    else
                    {
                        verseCells[col] = new CellInfo { Kind = CellKind.Text, Chapter = lc, Segment = seg, Part = SegmentPart.Whole };
                        if (ChapterText.ToCleanText(seg.Lead).Length > 0)
                        {
                            leadCells[col] = new CellInfo { Kind = CellKind.Text, Chapter = lc, Segment = seg, Part = SegmentPart.Lead };
                            anyLead = true;
                        }
                    }
                }

                var r = new VerseRef(book, chapter, v);
                if (anyLead)
                {
                    for (int col = 0; col < texts.Count; col++)
                        if (leadCells[col] == null) leadCells[col] = new CellInfo { Kind = CellKind.Empty };
                    rows.Add(new RowInfo { IsHeading = true, Label = "", Ref = r, Cells = leadCells });
                }
                rows.Add(new RowInfo { Label = v.ToString(), Ref = r, Cells = verseCells });
            }

            // verses present in a later text's chapter that no anchor verse maps onto (e.g. an extra verse).
            // Only safe when that text maps verse-for-verse; otherwise its leftovers belong to other chapters.
            for (int col = 1; col < texts.Count; col++)
            {
                if (!identity[col]) continue;
                var lc = getChapter(texts[col], book, chapter);
                foreach (var seg in lc.Text.Segments.Where(s => !used[col].Contains(s)))
                {
                    var cells = new CellInfo[texts.Count];
                    for (int c = 0; c < texts.Count; c++) cells[c] = new CellInfo { Kind = CellKind.Empty };
                    cells[col] = new CellInfo { Kind = CellKind.Text, Chapter = lc, Segment = seg, Part = SegmentPart.Whole };
                    used[col].Add(seg);
                    rows.Add(new RowInfo { Label = seg.Label + "*", Ref = new VerseRef(book, chapter, Math.Max(1, seg.Start)), Cells = cells });
                }
            }
            return rows;
        }
    }
}
