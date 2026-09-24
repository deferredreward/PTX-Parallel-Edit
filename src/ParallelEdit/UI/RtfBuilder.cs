using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using ParallelEdit.Core;

namespace ParallelEdit.UI
{
    /// <summary>
    /// Turns StyledText paragraphs into an RTF document for a RichTextBox, using the project's
    /// marker styles (font size/bold/italic/color/superscript/justification/indents) where known,
    /// and the column's own font as the fallback for everything a style does not say.
    /// </summary>
    static class RtfBuilder
    {
        public static string Build(IList<StyledParagraph> paragraphs, IReadOnlyDictionary<string, MarkerStyle> styles, Font baseFont, Color baseColor, int columnWidthTwips, bool rightToLeft)
        {
            styles = styles ?? new Dictionary<string, MarkerStyle>();
            var colors = new List<Color> { baseColor, Theme.Muted };
            int ColorIndex(Color c)
            {
                int i = colors.IndexOf(c);
                if (i >= 0) return i + 1; // index 0 in \colortbl is the auto color
                colors.Add(c);
                return colors.Count;
            }
            int maxLeftMarginTwips = Math.Max(0, columnWidthTwips / 3);

            var body = new StringBuilder();
            for (int i = 0; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                MarkerStyle paraStyle = p.Marker != null && styles.TryGetValue(p.Marker, out var ps) ? ps : null;
                body.Append(i == 0 ? @"\pard" : @"\par\pard");
                body.Append(JustificationTag(paraStyle?.Justification, rightToLeft));
                int li = InchesToTwips(paraStyle?.LeftMargin);
                int fi = InchesToTwips(paraStyle?.FirstLineIndent);
                int ri = InchesToTwips(paraStyle?.RightMargin);
                li = System.Math.Min(li, maxLeftMarginTwips);
                fi = System.Math.Max(-li, System.Math.Min(fi, maxLeftMarginTwips)); // the first line stays inside the cell
                ri = System.Math.Min(ri, maxLeftMarginTwips);
                body.Append(@"\li").Append(li).Append(@"\fi").Append(fi).Append(@"\ri").Append(ri);
                body.Append(' ');

                int markerSize = Round(baseFont.SizeInPoints * 0.75f * 2), textSize = Round(baseFont.SizeInPoints * 2);
                for (int r = 0; r < p.Runs.Count; r++)
                {
                    var run = p.Runs[r];
                    if (run.Text.Length == 0) continue;
                    if (run.IsMarker)
                    {
                        // the marker is small, but the space after it keeps full text size plus a little, so it does not crowd the word
                        ChapterText.SplitTrailingWhitespace(run.Text, out string marker, out string space);
                        AppendRunProps(body, ColorIndex(Theme.Muted), markerSize, false, false, false, false, false);
                        body.Append(' ');
                        Escape(body, marker);
                        if (space.Length > 0)
                        {
                            AppendRunProps(body, ColorIndex(Theme.Muted), textSize, false, false, false, false, false, spacingTwips: MarkerGapTwips);
                            body.Append(' ');
                            Escape(body, space);
                        }
                    }
                    else
                    {
                        MarkerStyle style = run.CharMarker != null && styles.TryGetValue(run.CharMarker, out var cs) ? cs : null;
                        MarkerStyle effective = style ?? paraStyle;
                        float ptSize = effective?.FontSize != null ? effective.FontSize.Value * (baseFont.SizeInPoints / 12f) : baseFont.SizeInPoints;
                        Color color = effective?.ColorArgb != null ? Color.FromArgb(effective.ColorArgb.Value) : baseColor;
                        void Props(int spacing) => AppendRunProps(body, ColorIndex(color), Round(ptSize * 2),
                            effective?.Bold ?? false, effective?.Italic ?? false, effective?.Underline ?? false,
                            effective?.Superscript ?? false, effective?.Subscript ?? false, effective?.SmallCaps ?? false, spacing);
                        // a marker right after a word (e.g. "Una'omi,\f"): widen the word's last letter's spacing to leave a small gap
                        bool markerTouches = r + 1 < p.Runs.Count && p.Runs[r + 1].IsMarker && !char.IsWhiteSpace(run.Text[run.Text.Length - 1]);
                        Props(0);
                        body.Append(' ');
                        if (!markerTouches) { Escape(body, run.Text); continue; }
                        Escape(body, run.Text.Substring(0, run.Text.Length - 1));
                        Props(MarkerGapTwips);
                        body.Append(' ');
                        Escape(body, run.Text.Substring(run.Text.Length - 1));
                    }
                }
            }

            var header = new StringBuilder();
            header.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl{\f0 ").Append(EscapePlain(baseFont.Name)).Append(@";}}");
            header.Append(@"{\colortbl ;");
            foreach (var c in colors) header.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';');
            header.Append('}');
            header.Append(@"\viewkind4\f0\fs").Append(Round(baseFont.SizeInPoints * 2)).Append(@"\cf1 ");

            return header.ToString() + body + "}";
        }

        /// <summary>Extra space (twips) next to a grey marker, so it stands apart from the text.</summary>
        const int MarkerGapTwips = 50;

        static void AppendRunProps(StringBuilder sb, int colorIndex, int halfPoints, bool bold, bool italic, bool underline, bool superscript, bool subscript, bool smallCaps = false, int spacingTwips = 0)
        {
            sb.Append(@"\cf").Append(colorIndex);
            sb.Append(@"\expndtw").Append(spacingTwips);
            sb.Append(@"\fs").Append(halfPoints);
            sb.Append(bold ? @"\b" : @"\b0");
            sb.Append(italic ? @"\i" : @"\i0");
            sb.Append(underline ? @"\ul" : @"\ulnone");
            sb.Append(smallCaps ? @"\scaps" : @"\scaps0");
            if (superscript) sb.Append(@"\super");
            else if (subscript) sb.Append(@"\sub");
            else sb.Append(@"\nosupersub");
        }

        /// <summary>A minimal document showing plain text in one font/color, aligned to the start side, no indents.</summary>
        public static string PlainDocument(string text, Font font, Color color, bool rightToLeft)
        {
            var sb = new StringBuilder();
            sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl{\f0 ").Append(EscapePlain(font.Name)).Append(@";}}");
            sb.Append(@"{\colortbl ;\red").Append(color.R).Append(@"\green").Append(color.G).Append(@"\blue").Append(color.B).Append(";}");
            sb.Append(@"\viewkind4\f0\fs").Append(Round(font.SizeInPoints * 2)).Append(@"\cf1\pard").Append(JustificationTag(null, rightToLeft))
              .Append(@"\li0\fi0\ri0\b0\i0\ulnone\scaps0\nosupersub ");
            Escape(sb, text ?? "");
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Paragraph direction and alignment. For a right-to-left text the stylesheet's Left/Right are taken as
        /// start/end (assumption: Paratext mirrors them the same way), so the default start side is the right.
        /// </summary>
        static string JustificationTag(Alignment? a, bool rightToLeft)
        {
            string dir = rightToLeft ? @"\rtlpar" : @"\ltrpar";
            switch (a)
            {
                case Alignment.Center: return dir + @"\qc";
                case Alignment.Justify: return dir + @"\qj";
                case Alignment.Right: return dir + (rightToLeft ? @"\ql" : @"\qr");
                default: return dir + (rightToLeft ? @"\qr" : @"\ql");
            }
        }

        static int InchesToTwips(float? inches) => inches.HasValue ? (int)System.Math.Round(inches.Value * 1440) : 0;
        static int Round(float f) => (int)System.Math.Round(f);

        static string EscapePlain(string s)
        {
            var sb = new StringBuilder();
            Escape(sb, s ?? "");
            return sb.ToString();
        }

        /// <summary>Escapes \, {, } and encodes non-ASCII as \uN? (signed 16-bit).</summary>
        static void Escape(StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                if (c == '\\' || c == '{' || c == '}') { sb.Append('\\').Append(c); }
                else if (c == '\n') { sb.Append(@"\line "); }
                else if (c == '\r') { /* skip: \n carries the break */ }
                else if (c >= 32 && c < 127) { sb.Append(c); }
                else
                {
                    int code = c > 32767 ? c - 65536 : c;
                    sb.Append(@"\u").Append(code).Append('?');
                }
            }
        }
    }
}
