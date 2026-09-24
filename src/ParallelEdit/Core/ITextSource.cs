using System;
using System.Collections.Generic;

namespace ParallelEdit.Core
{
    /// <summary>
    /// One text (project or resource) as the parallel view sees it. The Paratext
    /// implementation wraps IProject; the test host wraps plain USFM files.
    /// </summary>
    public interface ITextSource
    {
        string Id { get; }
        string ShortName { get; }
        string FullName { get; }
        bool IsResource { get; }

        string FontFamily { get; }
        float FontSize { get; }
        bool RightToLeft { get; }

        /// <summary>Marker styles from the project's stylesheet, keyed by marker without its backslash. Never null.</summary>
        IReadOnlyDictionary<string, MarkerStyle> MarkerStyles { get; }

        /// <summary>USFM of one chapter, or "" when the book or chapter does not exist.</summary>
        string GetChapterUsfm(int book, int chapter);

        /// <summary>Whether the current user may edit this chapter (permissions, resource, locks by assignment).</summary>
        bool CanEdit(int book, int chapter);

        /// <summary>
        /// Locks the chapter, reads the current USFM, passes it to <paramref name="transform"/>
        /// and writes back what it returns. A null return from transform cancels the write.
        /// Returns null on success, otherwise a message saying why nothing was written.
        /// </summary>
        string WriteChapter(int book, int chapter, Func<string, string> transform);

        int LastChapter(int book);
        int LastVerse(int book, int chapter);

        /// <summary>
        /// Maps a verse given in <paramref name="from"/>'s versification into this text's versification.
        /// </summary>
        bool TryMapVerse(ITextSource from, int book, int chapter, int verse, out int mappedChapter, out int mappedVerse);

        /// <summary>Switches to the keyboard set up for this text, if any.</summary>
        void ActivateKeyboard();

        /// <summary>Raised (on any thread) when scripture in this text changes. Arguments: book, chapter (0 = several).</summary>
        event Action<int, int> ScriptureChanged;
    }

    /// <summary>A book/chapter/verse position, always in the versification of the first text.</summary>
    public struct VerseRef : IEquatable<VerseRef>
    {
        public readonly int Book, Chapter, Verse;
        public VerseRef(int book, int chapter, int verse) { Book = book; Chapter = chapter; Verse = verse; }
        public bool SameChapter(VerseRef o) => Book == o.Book && Chapter == o.Chapter;
        public bool Equals(VerseRef o) => Book == o.Book && Chapter == o.Chapter && Verse == o.Verse;
        public override bool Equals(object obj) => obj is VerseRef o && Equals(o);
        public override int GetHashCode() => Book * 1000000 + Chapter * 1000 + Verse;
        public override string ToString() => Books.Code(Book) + " " + Chapter + ":" + Verse;
    }
}
