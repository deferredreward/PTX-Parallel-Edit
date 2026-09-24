using System;
using System.Collections.Generic;
using System.Linq;
using Paratext.PluginInterfaces;
using ParallelEdit.Core;

namespace ParallelEdit
{
    /// <summary>ITextSource over a Paratext project or resource.</summary>
    public class ParatextTextSource : ITextSource
    {
        public readonly IProject Project;
        readonly IPluginObject owner;
        Action<int, int> scriptureChanged;
        IReadOnlyDictionary<string, MarkerStyle> markerStyles;

        public ParatextTextSource(IProject project, IPluginObject owner)
        {
            Project = project;
            this.owner = owner;
        }

        public string Id => Project.ID;
        public string ShortName => Project.ShortName;
        public string FullName => Project.LongName;
        public bool IsResource => Project.IsResource;

        public string FontFamily => Safe(() => Project.Language?.Font?.FontFamily, null);
        public float FontSize => Safe(() => Project.Language?.Font?.Size ?? 0f, 0f);
        public bool RightToLeft => Safe(() => Project.Language?.IsRtoL ?? false, false);

        public IReadOnlyDictionary<string, MarkerStyle> MarkerStyles => markerStyles ?? (markerStyles = BuildMarkerStyles());

        Dictionary<string, MarkerStyle> BuildMarkerStyles()
        {
            var result = new Dictionary<string, MarkerStyle>();
            try
            {
                foreach (var info in Project.ScriptureMarkerInformation)
                {
                    var style = new MarkerStyle { Marker = info.Marker };
                    if (info is IParagraphMarkerInfo para)
                    {
                        style.Kind = MarkerKind.Paragraph;
                        style.Justification = ToAlignment(para.Justification);
                        style.FirstLineIndent = Inches(para.FirstLineIndent);
                        style.LeftMargin = Inches(para.LeftMargin);
                        style.RightMargin = Inches(para.RightMargin);
                    }
                    else if (info is INoteMarkerInfo)
                    {
                        style.Kind = MarkerKind.Note;
                    }
                    else if (info is ICharacterMarkerInfo)
                    {
                        style.Kind = MarkerKind.Character;
                    }
                    else
                    {
                        style.Kind = MarkerKind.Other;
                    }
                    if (info is IStyledMarkerInfo styled)
                    {
                        style.FontFamily = styled.FontFamily;
                        style.FontSize = styled.FontSize;
                        style.ColorArgb = styled.Color?.ToArgb();
                        style.Bold = styled.Bold;
                        style.Italic = styled.Italic;
                        style.Superscript = styled.Superscript;
                        style.Subscript = styled.Subscript;
                        style.Underline = styled.Underline;
                        style.SmallCaps = styled.SmallCaps;
                    }
                    result[info.Marker] = style;
                }
            }
            catch (Exception) { /* fall back to no styling */ }
            return result;
        }

        // Paratext stores stylesheet indents as thousandths of an inch (ScrTag.ParseF multiplies the .sty value by 1000)
        static float? Inches(float? thousandths) => thousandths / 1000f;

        static Alignment? ToAlignment(Justification? j)
        {
            switch (j)
            {
                case Justification.Left: return Alignment.Left;
                case Justification.Center: return Alignment.Center;
                case Justification.Right: return Alignment.Right;
                case Justification.Both: return Alignment.Justify;
                default: return null;
            }
        }

        bool HasBook(int book) => Project.AvailableBooks.Any(b => b.Number == book);

        public string GetChapterUsfm(int book, int chapter)
        {
            if (!HasBook(book)) return "";
            return Project.GetUSFM(book, chapter) ?? "";
        }

        public bool CanEdit(int book, int chapter) =>
            !Project.IsResource && HasBook(book) && Safe(() => Project.CanEdit(owner, book, chapter), false);

        public string WriteChapter(int book, int chapter, Func<string, string> transform)
        {
            IWriteLock writeLock;
            try
            {
                // We hold the lock only for this call, so a release request needs no action.
                writeLock = Project.RequestWriteLock(owner, l => { }, book, chapter);
            }
            catch (Exception e) { return e.Message; }
            if (writeLock == null)
                return "Paratext did not allow a lock on this chapter (it may be open for editing elsewhere, or you may not have permission).";
            try
            {
                string fresh = Project.GetUSFM(book, chapter) ?? "";
                string result = transform(fresh);
                if (result == null) return null; // caller cancelled; it reports that itself
                if (result != fresh)
                    Project.PutUSFM(writeLock, result, book);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                writeLock.Dispose(); // sends change notifications to the other Paratext windows
            }
        }

        public int LastChapter(int book) => Safe(() => Project.Versification.GetLastChapter(book), 0);
        public int LastVerse(int book, int chapter) => Safe(() => Project.Versification.GetLastVerse(book, chapter), 0);

        public bool TryMapVerse(ITextSource from, int book, int chapter, int verse, out int mappedChapter, out int mappedVerse)
        {
            mappedChapter = chapter;
            mappedVerse = verse;
            if (!(from is ParatextTextSource other) || ReferenceEquals(other.Project.Versification, Project.Versification))
                return true;
            try
            {
                var fromVers = other.Project.Versification;
                var toVers = Project.Versification;
                if (fromVers.Type == toVers.Type && !fromVers.IsCustomized && !toVers.IsCustomized)
                    return true;
                var r = fromVers.CreateReference(book, chapter, verse);
                // verse outside the anchor's versification: no safe mapping, so show nothing rather than a guess
                if (r == null) return false;
                var mapped = r.ChangeVersification(toVers);
                if (mapped == null || mapped.BookNum != book) return false;
                mappedChapter = mapped.ChapterNum;
                mappedVerse = mapped.VerseNum;
                return true;
            }
            catch (Exception)
            {
                return false; // a failed mapping must not put another verse's text (and edits) in this row
            }
        }

        public void ActivateKeyboard()
        {
            try { Project.VernacularKeyboard?.Activate(); } catch (Exception) { /* keyboard switching is a convenience */ }
        }

        public event Action<int, int> ScriptureChanged
        {
            add
            {
                if (scriptureChanged == null) Project.ScriptureDataChanged += OnScriptureDataChanged;
                scriptureChanged += value;
            }
            remove
            {
                scriptureChanged -= value;
                if (scriptureChanged == null) Project.ScriptureDataChanged -= OnScriptureDataChanged;
            }
        }

        void OnScriptureDataChanged(IProject sender, int book, int chapter) => scriptureChanged?.Invoke(book, chapter);

        static T Safe<T>(Func<T> f, T fallback)
        {
            try { return f(); } catch (Exception) { return fallback; }
        }
    }
}
