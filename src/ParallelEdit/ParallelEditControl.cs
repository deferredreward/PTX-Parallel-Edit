using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Paratext.PluginInterfaces;
using ParallelEdit.Core;
using ParallelEdit.UI;

namespace ParallelEdit
{
    /// <summary>The control Paratext docks in its window. Connects the ParallelView to Paratext.</summary>
    public class ParallelEditControl : EmbeddedPluginControl
    {
        readonly ParallelView view;
        readonly Dictionary<string, ParatextTextSource> wrappers = new Dictionary<string, ParatextTextSource>();
        IPluginChildWindow parent;
        IWindowPluginHost host;
        bool promptForTexts;

        public ParallelEditControl()
        {
            view = new ParallelView { Dock = DockStyle.Fill };
            Controls.Add(view);
        }

        public override void OnAddedToParent(IPluginChildWindow parentWindow, IWindowPluginHost pluginHost, string state)
        {
            parent = parentWindow;
            host = pluginHost;
            parent.SetTitle(ParallelEditPlugin.PluginName, true, false);

            view.AvailableTexts = () => host.GetAllProjects(true).Select(Wrap).ToList();
            view.ReferenceChangedByUser += ToParatext;

            parent.VerseRefChanged += (w, oldRef, newRef) => OnUi(() => FromParatext(newRef));
            parent.SaveRequested += w => OnUi(() => view.SaveAll());
            parent.WindowClosing += Parent_WindowClosing;
            parent.ProjectChanged += (w, project) => OnUi(() =>
            {
                var list = view.Texts.Where(t => t.Id != project.ID).ToList();
                list.Insert(0, Wrap(project));
                view.SetTexts(list);
            });
            host.ShuttingDown += (s, e) =>
            {
                // must run now (not posted) so a failed save can still cancel the shutdown
                bool ok = true;
                if (IsDisposed) return;
                if (InvokeRequired) Invoke((Action)(() => ok = SaveOrConfirmLoss()));
                else ok = SaveOrConfirmLoss();
                if (!ok) e.Cancel = true;
            };

            var saved = PluginState.Parse(state);
            var all = host.GetAllProjects(true);
            var texts = saved.TextIds.Select(id => all.FirstOrDefault(p => p.ID == id)).Where(p => p != null).Select(Wrap).ToList();
            var windowProject = parent.CurrentState?.Project;
            if (texts.Count == 0 && windowProject != null) texts.Add(Wrap(windowProject));
            promptForTexts = state == null;
            view.Mode = saved.Mode;
            view.SetTexts(texts);

            var start = parent.CurrentState?.VerseRef;
            if (start != null) FromParatext(start);
            else if (saved.Reference.HasValue) view.GoTo(saved.Reference.Value);
            else view.GoTo(new VerseRef(40, 1, 1));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (promptForTexts)
            {
                promptForTexts = false;
                // first time this window opens: go straight to choosing the texts
                BeginInvoke((Action)(() => view.ChooseTexts()));
            }
        }

        public override string GetState() =>
            new PluginState { TextIds = view.Texts.Select(t => t.Id).ToList(), Reference = view.Current, Mode = view.Mode }.ToString();

        public override void DoLoad(IProgressInfo progressInfo)
        {
            // Chapters load on demand on the UI thread; nothing slow to do up front.
        }

        ParatextTextSource Wrap(IProject project)
        {
            if (!wrappers.TryGetValue(project.ID, out var w))
                wrappers[project.ID] = w = new ParatextTextSource(project, this);
            return w;
        }

        IProject AnchorProject => (view.Texts.FirstOrDefault() as ParatextTextSource)?.Project ?? parent?.CurrentState?.Project;

        void FromParatext(IVerseRef r)
        {
            if (r == null) return;
            var anchor = AnchorProject;
            try
            {
                if (anchor != null && !ReferenceEquals(r.Versification, anchor.Versification))
                    r = r.ChangeVersification(anchor.Versification) ?? r;
            }
            catch (Exception) { /* keep the reference as given */ }
            view.GoTo(new VerseRef(r.BookNum, r.ChapterNum, Math.Max(1, r.VerseNum)));
        }

        void ToParatext(VerseRef r)
        {
            var anchor = AnchorProject;
            if (anchor == null || parent == null) return;
            try
            {
                var pr = anchor.Versification.CreateReference(r.Book, r.Chapter, r.Verse);
                if (pr != null) parent.SetReference(pr);
            }
            catch (Exception) { /* reference sync is best effort */ }
        }

        void Parent_WindowClosing(IPluginChildWindow sender, CancelEventArgs args)
        {
            if (!SaveOrConfirmLoss()) args.Cancel = true;
        }

        /// <summary>Saves everything; if something could not be saved, asks whether to lose it. False = stay open.</summary>
        bool SaveOrConfirmLoss()
        {
            if (view.SaveAll()) return true;
            var answer = MessageBox.Show(this, "Some changes could not be saved. Close anyway and lose them?", ParallelEditPlugin.PluginName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return answer == DialogResult.Yes;
        }

        void OnUi(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a); else a();
        }
    }

    /// <summary>What the window remembers between Paratext sessions: "texts=ID1,ID2;ref=41.1.1;mode=clean".</summary>
    public class PluginState
    {
        public List<string> TextIds = new List<string>();
        public VerseRef? Reference;
        public ViewMode Mode;

        public override string ToString() =>
            "texts=" + string.Join(",", TextIds) +
            (Reference.HasValue ? $";ref={Reference.Value.Book}.{Reference.Value.Chapter}.{Reference.Value.Verse}" : "") +
            ";mode=" + Mode.ToString().ToLowerInvariant();

        public static PluginState Parse(string state)
        {
            var s = new PluginState();
            if (string.IsNullOrEmpty(state)) return s;
            foreach (var part in state.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = part.Substring(0, eq), value = part.Substring(eq + 1);
                if (key == "texts") s.TextIds = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                else if (key == "markers") s.Mode = value == "1" ? ViewMode.Unformatted : ViewMode.Clean; // legacy state
                else if (key == "mode")
                {
                    if (Enum.TryParse(value, true, out ViewMode m) && Enum.IsDefined(typeof(ViewMode), m)) s.Mode = m; // "mode=7" also parses
                }
                else if (key == "ref")
                {
                    var n = value.Split('.');
                    if (n.Length == 3 && int.TryParse(n[0], out int b) && int.TryParse(n[1], out int c) && int.TryParse(n[2], out int v))
                        s.Reference = new VerseRef(b, c, v);
                }
            }
            return s;
        }
    }
}
