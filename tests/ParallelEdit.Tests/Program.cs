using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ParallelEdit.Core;

namespace ParallelEdit.Tests
{
    /// <summary>
    /// Plain console test runner (no test framework needed).
    /// Usage: ParallelEdit.Tests.exe [folder with Paratext projects for round-trip checks]
    /// </summary>
    static class Program
    {
        static int failures;

        static int Main(string[] args)
        {
            Run(nameof(SplitsHeadingsIntoNextVerse), SplitsHeadingsIntoNextVerse);
            Run(nameof(CleanTextDropsMarkersAndNotes), CleanTextDropsMarkersAndNotes);
            Run(nameof(BridgesAndLabels), BridgesAndLabels);
            Run(nameof(EditThenMergeUnchangedProject), EditThenMergeUnchangedProject);
            Run(nameof(MergeKeepsOtherPeoplesEdits), MergeKeepsOtherPeoplesEdits);
            Run(nameof(MergeReportsConflict), MergeReportsConflict);
            Run(nameof(ReassembleKeepsLineStructure), ReassembleKeepsLineStructure);
            Run(nameof(ParsesReferences), ParsesReferences);
            Run(nameof(RowsAlignBridgesAndHeadings), RowsAlignBridgesAndHeadings);
            Run(nameof(NoVersesMeansPrefixOnly), NoVersesMeansPrefixOnly);
            Run(nameof(CaretMapsFromCleanToRaw), CaretMapsFromCleanToRaw);
            Run(nameof(WholeVerseEditing), WholeVerseEditing);
            Run(nameof(SafetyChecksForSaving), SafetyChecksForSaving);

            if (args.Length > 0) RoundTripFolder(args[0]);

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }

        static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}: expected [{Show(expected)}] got [{Show(actual)}]");
        }

        static string Show(object o) => o?.ToString().Replace("\r", "\\r").Replace("\n", "\\n");

        const string Mark1 =
            "\\c 1\n\\s Joani o Muiminizi\n\\r (Mat 3:1-12)\n\\p\n" +
            "\\v 1 Aa, nga matatikizo.\n\\p\n" +
            "\\v 2 Shina omu kukwalilwe,\n\\b\n\\q1 “Nandi na tuma,\n\\q2 ozhu na tende.\\x - \\xo 1:2 \\xt Malaki 3:1\\x*\n\\q1\n" +
            "\\v 3 Ndi zhwi lyo zhwa.\n\\s Zo Kuiminizwa\n\\p\n" +
            "\\v 4 Chwale Joani \\w o|lemma=\"x\"\\w* Muiminizi.\n";

        static void SplitsHeadingsIntoNextVerse()
        {
            var t = ChapterText.Parse(Mark1);
            Eq(Mark1, t.ToUsfm(), "round trip");
            Eq("\\c 1\n", t.Prefix, "prefix");
            Eq(4, t.Segments.Count, "segments");
            Eq("\\s Joani o Muiminizi\n\\r (Mat 3:1-12)\n\\p\n", t.Segments[0].Lead, "v1 lead");
            Eq("Aa, nga matatikizo.\n", t.Segments[0].Body, "v1 body");
            Eq("\\p\n", t.Segments[1].Lead, "v2 lead");
            Eq(true, t.Segments[1].Body.EndsWith("\\x*\n"), "v2 body keeps poetry, loses trailing empty q1");
            Eq("\\q1\n", t.Segments[2].Lead, "v3 lead");
            Eq("\\s Zo Kuiminizwa\n\\p\n", t.Segments[3].Lead, "v4 lead");
        }

        static void CleanTextDropsMarkersAndNotes()
        {
            var t = ChapterText.Parse(Mark1);
            Eq("Shina omu kukwalilwe,\n“Nandi na tuma,\nozhu na tende.", ChapterText.ToCleanText(t.Segments[1].Body), "poetry");
            Eq("Chwale Joani o Muiminizi.", ChapterText.ToCleanText(t.Segments[3].Body), "word attributes");
            Eq("Joani o Muiminizi\n(Mat 3:1-12)", ChapterText.ToCleanText(t.Segments[0].Lead), "heading");
            Eq("", ChapterText.ToCleanText("\\p\n"), "empty paragraph");
            Eq("there.", ChapterText.ToCleanText("there.\n\n\\ts \\*\n"), "chunk milestone");
            Eq("a b", ChapterText.ToCleanText("a \\qt-s |who=\"Ruth\"\\*b"), "quote milestone");
            Eq("a b", ChapterText.ToCleanText("a\\f + \\fr 1:1 \\ft note\\f* b"), "footnote");
        }

        static void BridgesAndLabels()
        {
            var t = ChapterText.Parse("\\c 2\n\\p\n\\v 1-3 abc\n\\v 4a d\n\\v 4b e\n");
            Eq(1, t.Segments[0].Start, "start"); Eq(3, t.Segments[0].End, "end");
            Eq(t.Segments[0], t.FindVerse(2), "find inside bridge");
            Eq(4, t.Segments[1].Start, "4a");
            Eq("4a#0", t.KeyOf(t.Segments[1]), "key");
            Eq(t.Segments[2], t.FindByKey("4b#0"), "find by key");
        }

        static LoadedChapter Loaded(string usfm) =>
            new LoadedChapter { LoadedUsfm = usfm, Text = ChapterText.Parse(usfm), Editable = true };

        static void EditThenMergeUnchangedProject()
        {
            var lc = Loaded(Mark1);
            var seg = lc.Text.FindVerse(1);
            lc.Edit(seg, SegmentPart.Body, ChapterText.Reassemble(SegmentPart.Body, seg.Body, "Changed one.", "\n"));
            Eq(true, lc.IsDirty, "dirty");
            string merged = lc.Merge(Mark1, out var conflicts);
            Eq(0, conflicts.Count, "conflicts");
            Eq(Mark1.Replace("Aa, nga matatikizo.", "Changed one."), merged, "merged");
            // editing back to the original clears the pending edit
            lc.Edit(seg, SegmentPart.Body, "Aa, nga matatikizo.\n");
            Eq(false, lc.IsDirty, "not dirty after undoing");
        }

        static void MergeKeepsOtherPeoplesEdits()
        {
            var lc = Loaded(Mark1);
            var seg = lc.Text.FindVerse(3);
            lc.Edit(seg, SegmentPart.Body, "Mine.\n");
            string fresh = Mark1.Replace("Aa, nga matatikizo.", "Theirs.");
            string merged = lc.Merge(fresh, out var conflicts);
            Eq(0, conflicts.Count, "conflicts");
            Eq(fresh.Replace("Ndi zhwi lyo zhwa.", "Mine."), merged, "both edits kept");
        }

        static void MergeReportsConflict()
        {
            var lc = Loaded(Mark1);
            lc.Edit(lc.Text.FindVerse(1), SegmentPart.Body, "Mine.\n");
            string fresh = Mark1.Replace("Aa, nga matatikizo.", "Theirs.");
            string merged = lc.Merge(fresh, out var conflicts);
            Eq(1, conflicts.Count, "conflict count");
            Eq(fresh, merged, "their text kept until user decides");
            Eq(fresh.Replace("Theirs.", "Mine."), LoadedChapter.ForceApply(merged, conflicts), "force mine");
        }

        static void ReassembleKeepsLineStructure()
        {
            Eq("new text\r\n", ChapterText.Reassemble(SegmentPart.Body, "old\r\n", "new text", "\r\n"), "body crlf");
            Eq("line1\r\n\\q2 line2\r\n", ChapterText.Reassemble(SegmentPart.Body, "old\r\n", "line1\n\\q2 line2", "\r\n"), "multi-line body");
            Eq("", ChapterText.Reassemble(SegmentPart.Lead, "\\s Old\n\\p\n", "   ", "\n"), "cleared heading");
            Eq("\\s New\n", ChapterText.Reassemble(SegmentPart.Lead, "\\s Old\n", "\\s New", "\n"), "heading");
            Eq("x ", ChapterText.Reassemble(SegmentPart.Body, "old ", "x", "\n"), "same-line body");
        }

        static void ParsesReferences()
        {
            Eq(true, Books.TryParse("mrk 3:4", out var r), "parse");
            Eq(new VerseRef(41, 3, 4), r, "MRK 3:4");
            Books.TryParse("1CO 13", out r);
            Eq(new VerseRef(46, 13, 1), r, "1CO 13");
            Eq(false, Books.TryParse("ZZZ 1", out _), "bad book");
            Eq("REV", Books.Code(66), "REV");
            Eq("XXA", Books.Code(93), "XXA");
        }

        static void RowsAlignBridgesAndHeadings()
        {
            var a = new MemorySource("A", "\\c 1\n\\p\n\\v 1 a1\n\\v 2 a2\n\\s Head\n\\p\n\\v 3 a3\n", 3);
            var b = new MemorySource("B", "\\c 1\n\\p\n\\v 1-2 b12\n\\v 3 b3\n\\v 4 extra\n", 3);
            var cache = new Dictionary<string, LoadedChapter>();
            var rows = RowBuilder.Build(1, 1, new ITextSource[] { a, b }, (s, bk, ch) =>
            {
                string k = s.Id + ch;
                if (!cache.TryGetValue(k, out var lc)) cache[k] = lc = LoadedChapter.Load(s, bk, ch);
                return lc;
            });
            Eq("1,2,,3,4*", string.Join(",", rows.Select(r => r.Label)), "row labels");
            Eq(CellKind.Continued, rows[1].Cells[1].Kind, "bridge continues");
            Eq(true, rows[2].IsHeading, "heading row");
            Eq("Head", rows[2].Cells[0].CleanText, "heading text");
            Eq(CellKind.Empty, rows[2].Cells[1].Kind, "no heading in B");
            Eq("extra", rows[4].Cells[1].CleanText, "extra verse in B");
            Eq(true, rows[0].Cells[0].Editable, "editable");
        }

        static void CaretMapsFromCleanToRaw()
        {
            string raw = "Shina omu,\n\\b\n\\q1 “Nandi \\nd Lord\\nd* tuma,\\x - \\xo 1:2 \\xt Mal 3:1\\x*\n\\q2 ozhu";
            string clean = ChapterText.ToCleanText(raw);
            int at = clean.IndexOf("tuma");
            Eq("tuma", raw.Substring(ChapterText.MapCleanToRaw(clean, at + 1, raw) - 1, 4), "after footnote-free markers");
            at = clean.IndexOf("ozhu");
            Eq("ozhu", raw.Substring(ChapterText.MapCleanToRaw(clean, at + 1, raw) - 1, 4), "after cross reference");
            Eq(0, ChapterText.MapCleanToRaw(clean, 0, raw), "start");
            string plain = "Now it happened in the days";
            Eq(10, ChapterText.MapCleanToRaw(plain, 10, plain), "plain text maps 1:1");
        }

        static void WholeVerseEditing()
        {
            var lc = Loaded(Mark1);
            var v4 = lc.Text.FindVerse(4);
            string whole = v4.Get(SegmentPart.Whole);
            Eq("\\s Zo Kuiminizwa\n\\p\n\\v 4 Chwale Joani \\w o|lemma=\"x\"\\w* Muiminizi.\n", whole, "whole shows lead, marker, body");

            // change the paragraph marker and the text in one edit
            string edited = ChapterText.Reassemble(SegmentPart.Whole, whole, "\\s Zo Kuiminizwa\n\\m\n\\v 4 Chwale.", "\n");
            lc.Edit(v4, SegmentPart.Whole, edited);
            Eq("\\s Zo Kuiminizwa\n\\m\n", v4.Lead, "lead updated");
            Eq("Chwale.\n", v4.Body, "body updated");
            Eq(Mark1.Replace("\\p\n\\v 4 Chwale Joani \\w o|lemma=\"x\"\\w* Muiminizi.", "\\m\n\\v 4 Chwale."), lc.Merge(Mark1, out _), "chapter");

            // deleting the verse marker is refused and changes nothing
            string before = lc.Text.ToUsfm();
            bool threw = false;
            try { lc.Edit(v4, SegmentPart.Whole, "no marker here\n"); } catch (FormatException) { threw = true; }
            Eq(true, threw, "refused");
            Eq(before, lc.Text.ToUsfm(), "unchanged after refusal");

            // "\v 1" must not match inside "\v 10"
            var t = ChapterText.Parse("\\c 1\n\\v 1 a\n\\v 10 b\n");
            threw = false;
            try { t.Segments[0].Set(SegmentPart.Whole, "\\v 10 x\n"); } catch (FormatException) { threw = true; }
            Eq(true, threw, "v1 vs v10");
        }

        static void SafetyChecksForSaving()
        {
            var lc = Loaded(Mark1);
            lc.Edit(lc.Text.FindVerse(1), SegmentPart.Body, "Mine.\n");
            // verse 1 became part of a bridge elsewhere: the edit has nowhere to go and must be reported, not dropped
            string bridged = Mark1.Replace("\\v 1 Aa", "\\v 1-2 Aa").Replace("\\v 2 Shina", "Shina");
            Eq(1, LoadedChapter.Missing(bridged, lc.Pending.Values).Count, "missing after bridge");
            Eq(1, LoadedChapter.Missing("", lc.Pending.Values).Count, "missing when chapter is empty");
            Eq(0, LoadedChapter.Missing(Mark1, lc.Pending.Values).Count, "present");

            // whitespace normalized by Paratext counts as a different structure, so the view re-reads it
            var a = ChapterText.Parse(Mark1);
            Eq(true, LoadedChapter.SameStructure(a, ChapterText.Parse(Mark1)), "same");
            Eq(false, LoadedChapter.SameStructure(a, ChapterText.Parse(Mark1.Replace("\\p\n\\v 2", "\\p \\v 2"))), "whitespace differs");
            // a heading typed at the end of verse 3 belongs to verse 4 after re-parsing
            var edited = ChapterText.Parse(Mark1);
            edited.Segments[2].Body += "\\s New\n";
            Eq(false, LoadedChapter.SameStructure(ChapterText.Parse(edited.ToUsfm()), edited), "heading moves on re-parse");
        }

        static void NoVersesMeansPrefixOnly()
        {
            var t = ChapterText.Parse("\\id GEN\n\\c 1\n");
            Eq(0, t.Segments.Count, "no segments");
            Eq("\\id GEN\n\\c 1\n", t.ToUsfm(), "round trip");
            Eq("", ChapterText.Parse(null).ToUsfm(), "null");
        }

        /// <summary>Every chapter of every SFM file under the folder must survive Parse → ToUsfm unchanged.</summary>
        static void RoundTripFolder(string folder)
        {
            int chapters = 0, verses = 0, badLabels = 0, files = 0;
            var chapterStart = new Regex(@"(?m)^(?=\\c\s)");
            foreach (var file in Directory.EnumerateFiles(folder, "*.SFM", SearchOption.AllDirectories))
            {
                if (file.Contains("_Backups") || file.Contains("Temp Files")) continue;
                files++;
                string all = File.ReadAllText(file);
                var parts = chapterStart.Split(all);
                // Paratext's chapter 1 includes the book header, so join the header to the first chapter
                var chunks = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i == 1 && !parts[0].StartsWith("\\c")) { chunks[0] += parts[1]; continue; }
                    chunks.Add(parts[i]);
                }
                foreach (var chunk in chunks)
                {
                    var t = ChapterText.Parse(chunk);
                    chapters++;
                    verses += t.Segments.Count;
                    badLabels += t.Segments.Count(s => s.Start < 0);
                    if (t.ToUsfm() != chunk)
                    {
                        failures++;
                        Console.WriteLine("FAIL round trip " + file);
                        break;
                    }
                }
            }
            Console.WriteLine($"Round trip: {files} files, {chapters} chapters, {verses} verses, {badLabels} unparsed verse labels");
        }
    }

    class MemorySource : ITextSource
    {
        readonly string usfm; readonly int lastVerse;
        public MemorySource(string id, string chapterUsfm, int lastVerse) { Id = id; usfm = chapterUsfm; this.lastVerse = lastVerse; }
        public string Id { get; }
        public string ShortName => Id;
        public string FullName => Id;
        public bool IsResource => false;
        public string FontFamily => "Segoe UI";
        public float FontSize => 10;
        public bool RightToLeft => false;
        public string GetChapterUsfm(int book, int chapter) => chapter == 1 ? usfm : "";
        public bool CanEdit(int book, int chapter) => true;
        public string WriteChapter(int book, int chapter, Func<string, string> transform) => "not supported";
        public int LastChapter(int book) => 1;
        public int LastVerse(int book, int chapter) => lastVerse;
        public bool TryMapVerse(ITextSource from, int book, int chapter, int verse, out int mc, out int mv) { mc = chapter; mv = verse; return true; }
        public void ActivateKeyboard() { }
        public event Action<int, int> ScriptureChanged { add { } remove { } }
    }
}
