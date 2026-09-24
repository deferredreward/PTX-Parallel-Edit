namespace ParallelEdit.Core
{
    /// <summary>What kind of USFM marker a style applies to.</summary>
    public enum MarkerKind { Paragraph, Character, Note, Other }

    /// <summary>Paragraph justification, matching the project stylesheet's values.</summary>
    public enum Alignment { Left, Center, Right, Justify }

    /// <summary>
    /// One marker's display style, taken from the project's stylesheet. Every field is nullable;
    /// a null value means the stylesheet did not say, so the renderer falls back to the column's
    /// base font/behavior. No System.Drawing or WinForms types here: Core stays UI-free.
    /// </summary>
    public class MarkerStyle
    {
        public string Marker;
        public MarkerKind Kind;

        public string FontFamily;
        /// <summary>Points, in a stylesheet where 12pt is the base size.</summary>
        public int? FontSize;
        /// <summary>ARGB, as System.Drawing.Color.ToArgb() would produce.</summary>
        public int? ColorArgb;
        public bool? Bold, Italic, Superscript, Subscript, Underline, SmallCaps;

        /// <summary>Paragraph-only.</summary>
        public Alignment? Justification;
        /// <summary>Paragraph-only, in inches.</summary>
        public float? FirstLineIndent, LeftMargin, RightMargin;
    }
}
