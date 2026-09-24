using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ParallelEdit.Core
{
    /// <summary>Which editable piece of a verse segment a cell shows.</summary>
    public enum SegmentPart
    {
        /// <summary>Headings and paragraph markers before the \v marker.</summary>
        Lead,
        /// <summary>Text after the \v marker.</summary>
        Body,
        /// <summary>The verse's complete USFM: Lead + "\v N " + Body.</summary>
        Whole
    }

    /// <summary>
    /// One verse of a chapter, split so that it can be shown and edited in a cell.
    /// Lead + VerseMarker + Body reproduces the original USFM exactly.
    /// </summary>
    public class VerseSegment
    {
        /// <summary>Headings and empty paragraph markers that come right before the \v marker.</summary>
        public string Lead = "";
        /// <summary>The "\v 12 " marker text itself. Never edited.</summary>
        public string VerseMarker = "";
        /// <summary>Everything after the verse marker up to the next verse's lead.</summary>
        public string Body = "";
        /// <summary>The verse label as written, e.g. "12", "1-2", "3a".</summary>
        public string Label = "";
        public int Start;
        public int End;

        public string Get(SegmentPart part) =>
            part == SegmentPart.Lead ? Lead : part == SegmentPart.Body ? Body : Lead + VerseMarker + Body;

        /// <summary>Changes a part. For Whole, the text must still contain this verse's \v marker.</summary>
        public void Set(SegmentPart part, string value)
        {
            if (part == SegmentPart.Lead) { Lead = value; return; }
            if (part == SegmentPart.Body) { Body = value; return; }

            string marker = VerseMarker.TrimEnd();
            var m = Regex.Match(value, Regex.Escape(marker) + @"(?=\s|\\|$)");
            if (!m.Success)
                throw new FormatException($"The verse marker \"{marker}\" must stay in the text.");
            string lead = value.Substring(0, m.Index);
            string after = value.Substring(m.Index + m.Length);
            // keep the marker's own trailing space exactly as it was
            if (VerseMarker.Length > marker.Length && after.StartsWith(VerseMarker.Substring(marker.Length)))
                after = after.Substring(VerseMarker.Length - marker.Length);
            Lead = lead;
            Body = after;
        }

        public bool Covers(int verse) => verse >= Start && verse <= End;
    }

    /// <summary>
    /// A chapter of USFM split into a hidden prefix (\c line, book headers, intro)
    /// and a list of verse segments. ToUsfm() always returns the exact input of Parse()
    /// until a segment part is changed.
    /// </summary>
    public class ChapterText
    {
        public string Prefix = "";
        public List<VerseSegment> Segments = new List<VerseSegment>();

        static readonly Regex VerseMarkerRx = new Regex(@"\\v[ \t]+([^\s\\]+)[ \t]?", RegexOptions.Compiled);
        // A marker at the start of a line (after optional spaces).
        static readonly Regex LineMarkerRx = new Regex(@"(?m)^[ \t]*\\([a-z]+[0-9]*)(?![a-z0-9*])", RegexOptions.Compiled);
        static readonly Regex LeadingNumberRx = new Regex(@"^(\d+)(?:[a-z]?)(?:[-\u2013,](\d+))?", RegexOptions.Compiled);

        /// <summary>Section heading style markers: they belong to the verse that follows them.</summary>
        internal static readonly HashSet<string> HeadingMarkers = new HashSet<string>(
            "s s1 s2 s3 s4 ms ms1 ms2 ms3 mr r sr sp d cl qa sd sd1 sd2 sd3 sd4".Split(' '));

        /// <summary>Paragraph markers; when they carry no text they belong to the verse that follows.</summary>
        internal static readonly HashSet<string> ParagraphMarkers = new HashSet<string>(
            ("p m po pr cls pmo pm pmc pmr pi pi1 pi2 pi3 pi4 mi nb pc ph ph1 ph2 ph3 b " +
             "q q1 q2 q3 q4 qr qc qm qm1 qm2 qm3 qd lh li li1 li2 li3 li4 lf lim lim1 lim2 lim3 lim4").Split(' '));

        public static ChapterText Parse(string usfm)
        {
            usfm = usfm ?? "";
            var result = new ChapterText();
            var matches = VerseMarkerRx.Matches(usfm);
            if (matches.Count == 0)
            {
                result.Prefix = usfm;
                return result;
            }

            string pending = usfm.Substring(0, matches[0].Index);
            SplitTrailingLead(pending, out string prefix, out string lead);
            result.Prefix = prefix;

            for (int i = 0; i < matches.Count; i++)
            {
                Match m = matches[i];
                int bodyStart = m.Index + m.Length;
                int bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : usfm.Length;
                string rawBody = usfm.Substring(bodyStart, bodyEnd - bodyStart);

                var seg = new VerseSegment { Lead = lead, VerseMarker = m.Value, Label = m.Groups[1].Value };
                ParseLabel(seg.Label, out seg.Start, out seg.End);

                if (i + 1 < matches.Count)
                {
                    SplitTrailingLead(rawBody, out string body, out lead);
                    seg.Body = body;
                }
                else
                {
                    seg.Body = rawBody;
                }
                result.Segments.Add(seg);
            }
            return result;
        }

        public string ToUsfm()
        {
            var sb = new StringBuilder(Prefix);
            foreach (var seg in Segments)
                sb.Append(seg.Lead).Append(seg.VerseMarker).Append(seg.Body);
            return sb.ToString();
        }

        /// <summary>Finds the segment for a verse number, or null.</summary>
        public VerseSegment FindVerse(int verse) => Segments.FirstOrDefault(s => s.Covers(verse));

        /// <summary>
        /// A stable key for a segment: its label plus which occurrence of that label it is
        /// (labels repeat only in malformed text, but the key must still be unique).
        /// </summary>
        public string KeyOf(VerseSegment seg)
        {
            int n = 0;
            foreach (var s in Segments)
            {
                if (s == seg) return seg.Label + "#" + n;
                if (s.Label == seg.Label) n++;
            }
            throw new ArgumentException("Segment is not part of this chapter");
        }

        public VerseSegment FindByKey(string key)
        {
            var counts = new Dictionary<string, int>();
            foreach (var s in Segments)
            {
                counts.TryGetValue(s.Label, out int n);
                counts[s.Label] = n + 1;
                if (s.Label + "#" + n == key) return s;
            }
            return null;
        }

        static void ParseLabel(string label, out int start, out int end)
        {
            var m = LeadingNumberRx.Match(label);
            if (!m.Success) { start = end = -1; return; }
            start = int.Parse(m.Groups[1].Value);
            end = m.Groups[2].Success ? Math.Max(start, int.Parse(m.Groups[2].Value)) : start;
        }

        /// <summary>
        /// Splits text so that trailing headings and empty paragraph markers
        /// (which introduce the next verse) go into <paramref name="lead"/>.
        /// keep + lead == text.
        /// </summary>
        static void SplitTrailingLead(string text, out string keep, out string lead)
        {
            var markers = LineMarkerRx.Matches(text);
            int split = text.Length;
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                Match m = markers[i];
                int unitEnd = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
                // only a contiguous run of movable units at the very end may move
                if (unitEnd != split) break;
                string name = m.Groups[1].Value;
                string content = text.Substring(m.Index + m.Length, unitEnd - m.Index - m.Length);
                bool movable = HeadingMarkers.Contains(name) ||
                               (ParagraphMarkers.Contains(name) && content.Trim().Length == 0);
                if (!movable) break;
                split = m.Index;
            }
            keep = text.Substring(0, split);
            lead = text.Substring(split);
        }

        // ---- display helpers ----

        static readonly Regex NoteRx = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex AttributesRx = new Regex(@"\|[^\\]*(?=\\\+?[a-z0-9]+\*)", RegexOptions.Compiled);
        // milestones such as "\ts \*" or "\qt-s |who=""x""\*"
        static readonly Regex MilestoneRx = new Regex(@"\\[a-z0-9]+(?:-[se])?\b[^\\\n]*\\\*", RegexOptions.Compiled);
        static readonly Regex EndMarkerRx = new Regex(@"\\\+?[a-z0-9]+\*", RegexOptions.Compiled);
        static readonly Regex LineStartMarkerRx = new Regex(@"(?m)^[ \t]*\\[a-z]+[0-9]*(?![a-z0-9*])[ \t]*", RegexOptions.Compiled);
        static readonly Regex InlineMarkerRx = new Regex(@"\\\+?[a-z]+[0-9]*(?![a-z0-9*])[ \t]?", RegexOptions.Compiled);
        static readonly Regex SpacesRx = new Regex(@"[ \t]+", RegexOptions.Compiled);
        static readonly Regex BlankLinesRx = new Regex(@"\n{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Readable text without USFM markers: notes and cross references removed,
        /// each paragraph or poetry line on its own line.
        /// </summary>
        public static string ToCleanText(string usfm)
        {
            if (string.IsNullOrEmpty(usfm)) return "";
            string s = usfm.Replace("\r\n", "\n");
            s = NoteRx.Replace(s, "");
            s = MilestoneRx.Replace(s, "");
            s = AttributesRx.Replace(s, "");
            s = EndMarkerRx.Replace(s, "");
            s = LineStartMarkerRx.Replace(s, "");
            s = InlineMarkerRx.Replace(s, "");
            s = SpacesRx.Replace(s, " ");
            s = string.Join("\n", s.Split('\n').Select(l => l.Trim()));
            s = BlankLinesRx.Replace(s, "\n");
            return s.Trim();
        }

        // parts of raw USFM that never show in the clean text
        static readonly Regex HiddenRx = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*|\\[a-z0-9]+(?:-[se])?\b[^\\\n]*\\\*|\|[^\\]*(?=\\\+?[a-z0-9]+\*)|\\\+?[a-z]+[0-9]*\*?",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Maps a caret position in the clean text to the matching position in the raw USFM,
        /// by counting letters and digits and skipping markers, notes and attributes in the raw text.
        /// </summary>
        public static int MapCleanToRaw(string clean, int cleanIndex, string raw)
        {
            int letters = 0;
            for (int i = 0; i < Math.Min(cleanIndex, clean.Length); i++)
                if (char.IsLetterOrDigit(clean[i])) letters++;
            if (letters == 0) return 0;

            var hidden = new bool[raw.Length];
            foreach (Match m in HiddenRx.Matches(raw))
                for (int i = m.Index; i < m.Index + m.Length; i++) hidden[i] = true;
            int seen = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (hidden[i] || !char.IsLetterOrDigit(raw[i])) continue;
                if (++seen == letters) return i + 1;
            }
            return raw.Length;
        }

        /// <summary>Splits trailing whitespace off so the editable text stays tidy.</summary>
        public static void SplitTrailingWhitespace(string raw, out string text, out string trailing)
        {
            int end = raw.Length;
            while (end > 0 && char.IsWhiteSpace(raw[end - 1])) end--;
            text = raw.Substring(0, end);
            trailing = raw.Substring(end);
        }

        /// <summary>
        /// Rebuilds a raw part from edited text, keeping the whitespace that followed the old text
        /// so the next marker still starts on its own line.
        /// </summary>
        public static string Reassemble(SegmentPart part, string oldRaw, string editedText, string newLine)
        {
            SplitTrailingWhitespace(oldRaw, out _, out string trailing);
            string text = (editedText ?? "").Replace("\r\n", "\n").TrimEnd().Replace("\n", newLine);
            if (part == SegmentPart.Lead)
            {
                if (text.Length == 0) return "";
                return text + (trailing.Contains("\n") ? trailing : newLine);
            }
            return text + trailing;
        }

        /// <summary>The line ending this chapter uses, so edited text matches it.</summary>
        public string NewLine => ToUsfm().Contains("\r\n") ? "\r\n" : "\n";
    }
}
