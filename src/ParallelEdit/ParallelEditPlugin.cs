using System;
using System.Collections.Generic;
using Paratext.PluginInterfaces;

namespace ParallelEdit
{
    /// <summary>
    /// Paratext 9 plugin entry point. Adds "Parallel Edit..." to the Tools menu of a Scripture text window.
    /// </summary>
    public class ParallelEditPlugin : IParatextWindowPlugin
    {
        public const string PluginName = "Parallel Edit";

        public string Name => PluginName;
        public Version Version => GetType().Assembly.GetName().Version;
        public string VersionString => Version.ToString(3);
        public string Publisher => "unfoldingWord";

        public IEnumerable<WindowPluginMenuEntry> PluginMenuEntries
        {
            get { yield return new WindowPluginMenuEntry(PluginName + "...", Run, PluginMenuLocation.ScrTextTools); }
        }

        public string GetDescription(string locale) =>
            "Shows several texts side by side, verse by verse, and lets you edit the ones you have permission to edit.";

        public IDataFileMerger GetMerger(IPluginHost host, string dataIdentifier) =>
            throw new NotImplementedException("Parallel Edit stores no plugin data.");

        static void Run(IWindowPluginHost host, IParatextChildState state)
        {
            host.ShowEmbeddedUi(new ParallelEditControl(), state.Project);
        }
    }
}
