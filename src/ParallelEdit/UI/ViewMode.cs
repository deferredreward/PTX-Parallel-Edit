namespace ParallelEdit.UI
{
    /// <summary>How verse cells are displayed when not being edited.</summary>
    public enum ViewMode
    {
        /// <summary>Plain text: markers and notes hidden, heading rows shown. The default.</summary>
        Clean,
        /// <summary>Text styled per the project's stylesheet; every USFM marker shown in small grey text; headings inside verse cells.</summary>
        Standard,
        /// <summary>Raw USFM in every cell; heading rows hidden because headings appear inside verse cells.</summary>
        Unformatted
    }
}
