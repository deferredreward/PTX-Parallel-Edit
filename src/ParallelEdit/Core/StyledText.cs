using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ParallelEdit.Core
{
    /// <summary>One piece of a styled paragraph: either the marker token itself, or the text it governs.</summary>
    public class StyledRun
    {
        public string Text;
        /// <summary>True when this run is the marker token's own text (e.g. "\add ", "\add*", "\q1", "\ts \*").</summary>
        public bool IsMarker;
        /// <summary>The innermost active character/note marker for a text run (marker name, no backslash). Null for plain text.</summary>
        public string CharMarker;
    }

    /// <summary>One paragraph: everything from a line-start paragraph marker up to (not including) the next one.</summary>
    public class StyledParagraph
    {
        /// <summary>The paragraph/heading marker governing this paragraph (name only, no backslash), or null.</summary>
        public string Marker;
        public List<StyledRun> Runs = new List<StyledRun>();
    }

    /// <summary>
    /// Tokenizes USFM into paragraphs and runs for styled display, without needing Paratext's own renderer.
    /// Lossless: concatenating every run's Text of every paragraph reproduces the input text, minus only
    /// the single newline that separated it from the paragraph before it.
    /// </summary>
    public static class StyledText
    {
        // marker name at the very start of a line (no leading whitespace, as real USFM always has it)
        static readonly Regex ParaStartRx = new Regex(@"(?m)^\\([A-Za-z]+[0-9]*)(?![A-Za-z0-9*])", RegexOptions.Compiled);

        // "\ts \*" or "\qt-s |who=""Ruth""\*" : a whole milestone, ending in a bare "\*"
        static readonly Regex MilestoneRx = new Regex(@"\G\\[A-Za-z][A-Za-z0-9]*(?:-[se])?\b[^\\\n]*\\\*", RegexOptions.Compiled);
        // "\add*" or "\+nd*" : a character/note end marker
        static readonly Regex EndMarkerRx = new Regex(@"\G\\(\+)?([A-Za-z][A-Za-z0-9]*)\*", RegexOptions.Compiled);
        // a footnote/cross-reference opener with its caller field, e.g. "\f + " or "\x - "
        // (?![A-Za-z0-9]) so "\fr" or "\xt" inside a note is not read as a new "\f" / "\x" note
        static readonly Regex NoteOpenRx = new Regex(@"\G\\(f|fe|x|ef|ex)(?![A-Za-z0-9*])[ \t]+\S+[ \t]+", RegexOptions.Compiled);
        static readonly Regex NoteOpenBareRx = new Regex(@"\G\\(f|fe|x|ef|ex)(?![A-Za-z0-9*])[ \t]?", RegexOptions.Compiled);
        // a verse marker with its number, e.g. "\v 12 ": one marker token that opens no character scope
        static readonly Regex VerseRx = new Regex(@"\G\\v[ \t]+[^\s\\]+[ \t]?", RegexOptions.Compiled);
        // any other opening marker, e.g. "\add ", "\+nd ", "\q1"
        static readonly Regex OpenMarkerRx = new Regex(@"\G\\(\+)?([A-Za-z][A-Za-z0-9]*)[ \t]?", RegexOptions.Compiled);
        // a "|attr=""x""" attribute clause immediately preceding an end marker, folded into the end marker's text
        static readonly Regex TrailingAttributeRx = new Regex(@"\|[^\\]*$", RegexOptions.Compiled);

        static readonly HashSet<string> NoteContainers = new HashSet<string> { "f", "fe", "x", "ef", "ex" };
        static readonly HashSet<string> NoteInnerMarkers = new HashSet<string>(
            "fr ft fq fqa fv fk fl fw fp fdc xo xt xta xop xq".Split(' '));

        class Scope { public string Name; public bool IsNote; }

        public static List<StyledParagraph> Parse(string usfm, string initialParagraphMarker)
        {
            var result = new List<StyledParagraph>();
            usfm = usfm ?? "";
            var starts = new List<Match>();
            foreach (Match m in ParaStartRx.Matches(usfm))
            {
                string name = m.Groups[1].Value;
                if (ChapterText.ParagraphMarkers.Contains(name) || ChapterText.HeadingMarkers.Contains(name))
                    starts.Add(m);
            }

            int pos = 0;
            string marker = initialParagraphMarker;
            for (int i = 0; i <= starts.Count; i++)
            {
                int chunkEnd = i < starts.Count ? starts[i].Index : usfm.Length;
                string raw = usfm.Substring(pos, chunkEnd - pos);
                // the newline that separates this chunk from the paragraph marker after it is a break, not text
                if (i < starts.Count)
                {
                    if (raw.EndsWith("\r\n")) raw = raw.Substring(0, raw.Length - 2);
                    else if (raw.EndsWith("\n")) raw = raw.Substring(0, raw.Length - 1);
                }
                if (raw.Length > 0 || (i == 0 && (starts.Count == 0 || starts[0].Index > 0))) // no blank first line when the text opens with a paragraph marker
                    result.Add(new StyledParagraph { Marker = marker, Runs = TokenizeParagraph(raw) });
                if (i < starts.Count)
                {
                    marker = starts[i].Groups[1].Value;
                    pos = starts[i].Index;
                }
            }
            if (result.Count == 0) result.Add(new StyledParagraph { Marker = initialParagraphMarker });
            return result;
        }

        static List<StyledRun> TokenizeParagraph(string text)
        {
            var runs = new List<StyledRun>();
            var stack = new List<Scope>();
            int pos = 0;
            int textStart = 0;
            string CurrentMarker() => stack.Count > 0 ? stack[stack.Count - 1].Name : null;

            // Emits the plain text since textStart as one run. When foldAttribute is true and the text
            // ends with "|attr=..." (about to be followed by an end marker), that suffix is left unemitted
            // and returned instead, so the caller can prepend it to the end marker's own run.
            string EmitPendingText(int end, bool foldAttribute)
            {
                if (end <= textStart) { textStart = end; return ""; }
                string t = text.Substring(textStart, end - textStart);
                string attr = "";
                if (foldAttribute)
                {
                    var m = TrailingAttributeRx.Match(t);
                    if (m.Success) { attr = t.Substring(m.Index); t = t.Substring(0, m.Index); }
                }
                if (t.Length > 0) runs.Add(new StyledRun { Text = t, IsMarker = false, CharMarker = CurrentMarker() });
                textStart = end;
                return attr;
            }

            while (pos < text.Length)
            {
                if (text[pos] != '\\') { pos++; continue; }

                Match m = MilestoneRx.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    string attr = EmitPendingText(pos, foldAttribute: true);
                    runs.Add(new StyledRun { Text = attr + m.Value, IsMarker = true });
                    pos += m.Length;
                    textStart = pos;
                    continue;
                }

                m = EndMarkerRx.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    string name = m.Groups[2].Value;
                    // capture the closing scope's text before popping, so the text just before this
                    // end marker still attributes to the marker it was inside
                    string attr = EmitPendingText(pos, foldAttribute: true);
                    if (stack.Count > 0 && stack[stack.Count - 1].IsNote && NoteContainers.Contains(name))
                    {
                        while (stack.Count > 0) { bool wasNote = stack[stack.Count - 1].IsNote; stack.RemoveAt(stack.Count - 1); if (wasNote) break; }
                    }
                    else
                    {
                        int idx = stack.FindLastIndex(s => s.Name == name);
                        if (idx >= 0) stack.RemoveRange(idx, stack.Count - idx);
                    }
                    runs.Add(new StyledRun { Text = attr + m.Value, IsMarker = true });
                    pos += m.Length;
                    textStart = pos;
                    continue;
                }

                m = NoteOpenRx.Match(text, pos);
                if (!m.Success || m.Index != pos) m = NoteOpenBareRx.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    EmitPendingText(pos, foldAttribute: false);
                    stack.Add(new Scope { Name = m.Groups[1].Value, IsNote = true });
                    runs.Add(new StyledRun { Text = m.Value, IsMarker = true });
                    pos += m.Length;
                    textStart = pos;
                    continue;
                }

                m = VerseRx.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    EmitPendingText(pos, foldAttribute: false);
                    runs.Add(new StyledRun { Text = m.Value, IsMarker = true });
                    pos += m.Length;
                    textStart = pos;
                    continue;
                }

                m = OpenMarkerRx.Match(text, pos);
                if (m.Success && m.Index == pos)
                {
                    string name = m.Groups[2].Value;
                    EmitPendingText(pos, foldAttribute: false);
                    // a paragraph/heading marker (only possible at the very start of a chunk, by construction
                    // of Parse's splitting) governs the paragraph itself, not an inline character scope
                    if (!(ChapterText.ParagraphMarkers.Contains(name) || ChapterText.HeadingMarkers.Contains(name)))
                    {
                        if (stack.Count > 0 && !stack[stack.Count - 1].IsNote && NoteInnerMarkers.Contains(stack[stack.Count - 1].Name))
                            stack.RemoveAt(stack.Count - 1); // an inner note marker ends when the next one starts
                        stack.Add(new Scope { Name = name, IsNote = false });
                    }
                    runs.Add(new StyledRun { Text = m.Value, IsMarker = true });
                    pos += m.Length;
                    textStart = pos;
                    continue;
                }

                pos++; // a lone backslash that matched nothing recognizable: keep it as plain text
            }
            EmitPendingText(text.Length, foldAttribute: false);
            return runs;
        }
    }
}
