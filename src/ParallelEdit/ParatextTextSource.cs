using System;
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
                if (r == null) return true; // verse outside the anchor's versification: assume the same number
                var mapped = r.ChangeVersification(toVers);
                if (mapped == null || mapped.BookNum != book) return false;
                mappedChapter = mapped.ChapterNum;
                mappedVerse = mapped.VerseNum;
                return true;
            }
            catch (Exception)
            {
                return true;
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
