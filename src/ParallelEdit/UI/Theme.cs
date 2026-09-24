using System.Drawing;

namespace ParallelEdit.UI
{
    /// <summary>Colors for the view: plain, with unfoldingWord blues as the only accents.</summary>
    static class Theme
    {
        public static readonly Color GridLine = Color.FromArgb(0xD5, 0xDB, 0xE1);
        public static readonly Color EditableBack = Color.White;
        public static readonly Color ReadOnlyBack = Color.FromArgb(0xF2, 0xF4, 0xF6);
        public static readonly Color LabelBack = Color.FromArgb(0xF7, 0xF9, 0xFA);
        public static readonly Color LabelText = Color.FromArgb(0x6B, 0x75, 0x80);
        public static readonly Color Text = Color.FromArgb(0x23, 0x1F, 0x20);        // Tech
        public static readonly Color Muted = Color.FromArgb(0x9A, 0xA3, 0xAC);
        public static readonly Color Heading = Color.FromArgb(0x01, 0x42, 0x63);     // Ocean
        public static readonly Color Current = Color.FromArgb(0x31, 0xAD, 0xE3);     // Inspire
        public static readonly Color CurrentBack = Color.FromArgb(0xE6, 0xF5, 0xFC);
        public static readonly Color FocusBack = Color.FromArgb(0xFF, 0xFD, 0xF3);
        public static readonly Color Error = Color.FromArgb(0xB0, 0x3A, 0x2E);
    }
}
