using System;
using System.Text.RegularExpressions;

namespace ParallelEdit.Core
{
    /// <summary>Standard Paratext book numbering (1 = GEN ... 66 = REV, then deuterocanon and extras).</summary>
    public static class Books
    {
        static readonly string[] Codes = (
            "GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM " +
            "EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP " +
            "COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD REV TOB JDT ESG WIS SIR BAR LJE S3Y SUS " +
            "BEL 1MA 2MA 3MA 4MA 1ES 2ES MAN PS2 ODA PSS JSA JDB TBS SST DNT BLT XXA XXB XXC XXD XXE XXF XXG FRT " +
            "BAK OTH 3ES EZA 5EZ 6EZ INT CNC GLO TDX NDX DAG PS3 2BA LBA JUB ENO 1MQ 2MQ 3MQ REP 4BA LAO").Split(' ');

        public static string Code(int book) => book >= 1 && book <= Codes.Length ? Codes[book - 1] : "?" + book;

        public static int Number(string code)
        {
            int i = Array.IndexOf(Codes, (code ?? "").Trim().ToUpperInvariant());
            return i < 0 ? 0 : i + 1;
        }

        static readonly Regex RefRx = new Regex(@"^\s*([1-6]?[A-Za-z]{2,3})\s*(\d+)?(?:\s*[:.]\s*(\d+))?\s*$");

        /// <summary>Parses "MRK 1", "mrk 1:5", "1CO 13.4". Missing chapter/verse default to 1.</summary>
        public static bool TryParse(string text, out VerseRef result)
        {
            result = default;
            var m = RefRx.Match(text ?? "");
            if (!m.Success) return false;
            int book = Number(m.Groups[1].Value);
            if (book == 0) return false;
            int ch = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            int v = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1;
            result = new VerseRef(book, Math.Max(1, ch), Math.Max(1, v));
            return true;
        }
    }
}
