using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ParallelEdit.Core;
using ParallelEdit.UI;

namespace TestHost
{
    /// <summary>
    /// Runs ParallelView outside Paratext against Paratext project folders (use copies!).
    /// TestHost.exe --ref "MRK 1:9" [--shot out.png] [--edit] [--readonly SHORTNAME] folder1 folder2 ...
    /// --edit types into verse 2 of the first editable text, leaves the cell, and checks the file changed.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            var folders = new List<string>();
            var readOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string shot = null, reference = "MRK 1:1";
            bool edit = false, markers = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--shot": shot = args[++i]; break;
                    case "--ref": reference = args[++i]; break;
                    case "--edit": edit = true; break;
                    case "--markers": markers = true; break;
                    case "--readonly": readOnly.Add(args[++i]); break;
                    default: folders.Add(args[i]); break;
                }
            }

            var sources = folders.Select(f => new FolderTextSource(f, readOnly.Contains(Path.GetFileName(f)))).ToList();
            var form = new Form { Text = "Parallel Edit test host", Width = 1200, Height = 800, StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            var view = new ParallelView { Dock = DockStyle.Fill, AvailableTexts = () => sources };
            form.Controls.Add(view);
            view.SetTexts(sources);
            view.ShowMarkers = markers;
            Books.TryParse(reference, out var r);
            view.GoTo(r);

            int exit = 0;
            if (shot != null || edit)
            {
                form.Shown += (s, e) =>
                {
                    var t = new System.Windows.Forms.Timer { Interval = 800 };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop();
                        try
                        {
                            if (edit) exit = RunEdit(form, view, sources, shot);
                            else Snap(form, shot);
                        }
                        catch (Exception ex) { Console.WriteLine("ERROR " + ex); exit = 2; }
                        form.Close();
                    };
                    t.Start();
                };
            }
            Application.Run(form);
            return exit;
        }

        static void Snap(Form form, string path)
        {
            form.TopMost = true;
            form.Activate();
            Application.DoEvents();
            Thread.Sleep(300);
            Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("Saved screenshot " + path);
        }

        static int RunEdit(Form form, ParallelView view, List<FolderTextSource> sources, string shot)
        {
            var grid = FindGrid(view);
            var target = grid.Rows.Where(row => !row.Info.IsHeading && row.Info.Label == "2")
                .SelectMany(row => row.Cells).First(c => c.Info.Editable);
            var src = (FolderTextSource)target.Info.Chapter.Source;
            string before = File.ReadAllText(src.FileFor(target.Info.Chapter.Book));
            target.Focus();
            Application.DoEvents();
            Console.WriteLine("Focused cell text (raw while editing): " + target.Text.Replace("\r\n", " / "));
            target.SelectionStart = target.TextLength;
            target.SelectedText = " [EDITED BY TEST]";
            Application.DoEvents();
            if (shot != null) Snap(form, shot.Replace(".png", "-editing.png"));
            // move focus to the next row to trigger the save
            var next = grid.Rows.SkipWhile(row => row != target.Row).Skip(1).SelectMany(row => row.Cells).First(c => c.TabStop);
            next.Focus();
            Application.DoEvents();
            Thread.Sleep(200);
            Application.DoEvents();
            string after = File.ReadAllText(src.FileFor(target.Info.Chapter.Book));
            var diff = Diff(before, after);
            Console.WriteLine("File changed lines:\n" + diff);
            if (shot != null) Snap(form, shot);
            bool ok = after.Contains(" [EDITED BY TEST]") && after.Replace(" [EDITED BY TEST]", "") == before;
            Console.WriteLine(ok ? "EDIT OK: only the edited verse changed" : "EDIT FAILED");
            return ok ? 0 : 1;
        }

        static VerseGrid FindGrid(Control c)
        {
            if (c is VerseGrid g) return g;
            foreach (Control child in c.Controls) { var f = FindGrid(child); if (f != null) return f; }
            return null;
        }

        static string Diff(string a, string b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n');
            var lines = new List<string>();
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i] : "", y = i < lb.Length ? lb[i] : "";
                if (x != y) lines.Add($"- {x.TrimEnd()}\n+ {y.TrimEnd()}");
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>ITextSource over a Paratext project folder's SFM files (chapter 1 includes the book header, like Paratext).</summary>
    class FolderTextSource : ITextSource
    {
        readonly string folder;
        readonly bool readOnly;
        public FolderTextSource(string folder, bool readOnly)
        {
            this.folder = folder;
            this.readOnly = readOnly;
            string settings = Path.Combine(folder, "Settings.xml");
            if (File.Exists(settings))
            {
                string xml = File.ReadAllText(settings);
                FontFamily = Regex.Match(xml, "<DefaultFont>([^<]*)").Groups[1].Value;
                float.TryParse(Regex.Match(xml, "<DefaultFontSize>([^<]*)").Groups[1].Value, out float size);
                FontSize = size;
                FullName = Regex.Match(xml, "<FullName>([^<]*)").Groups[1].Value;
            }
        }

        public string Id => folder;
        public string ShortName => Path.GetFileName(folder);
        public string FullName { get; }
        public bool IsResource => readOnly;
        public string FontFamily { get; }
        public float FontSize { get; }
        public bool RightToLeft => false;

        public string FileFor(int book) =>
            Directory.EnumerateFiles(folder, "*.SFM").FirstOrDefault(f => Path.GetFileName(f).Substring(2).StartsWith(Books.Code(book), StringComparison.OrdinalIgnoreCase));

        static readonly Regex ChapterStart = new Regex(@"(?m)^(?=\\c\s)");

        List<string> Chapters(int book)
        {
            string file = FileFor(book);
            if (file == null) return new List<string>();
            var parts = ChapterStart.Split(File.ReadAllText(file)).ToList();
            if (parts.Count > 1 && !parts[0].StartsWith("\\c")) { parts[1] = parts[0] + parts[1]; parts.RemoveAt(0); }
            return parts;
        }

        public string GetChapterUsfm(int book, int chapter)
        {
            var ch = Chapters(book);
            return chapter >= 1 && chapter <= ch.Count ? ch[chapter - 1] : "";
        }

        public bool CanEdit(int book, int chapter) => !readOnly;

        public string WriteChapter(int book, int chapter, Func<string, string> transform)
        {
            var ch = Chapters(book);
            if (chapter < 1 || chapter > ch.Count) return "no such chapter";
            string result = transform(ch[chapter - 1]);
            if (result == null) return null;
            ch[chapter - 1] = result;
            File.WriteAllText(FileFor(book), string.Concat(ch));
            ScriptureChanged?.Invoke(book, chapter);
            return null;
        }

        public int LastChapter(int book) => Chapters(book).Count;
        public int LastVerse(int book, int chapter) => ChapterText.Parse(GetChapterUsfm(book, chapter)).Segments.Select(s => s.End).DefaultIfEmpty(0).Max();

        public bool TryMapVerse(ITextSource from, int book, int chapter, int verse, out int mc, out int mv) { mc = chapter; mv = verse; return true; }
        public void ActivateKeyboard() { }
        public event Action<int, int> ScriptureChanged;
    }
}
