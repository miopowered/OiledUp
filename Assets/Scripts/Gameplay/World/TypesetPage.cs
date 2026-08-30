using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One printed page: the lines that fit on it, and the chapter whose running head it carries.
    ///
    /// <para>
    /// The chapter travels with the page rather than being looked up by the renderer, because a
    /// spread routinely straddles two chapters — the last page of one sits opposite the first page of
    /// the next, and during a turn the two halves come from different spreads entirely. A running
    /// head derived at draw time from "where we are in the book" would print the wrong chapter on
    /// exactly those pages.
    /// </para>
    /// </summary>
    public sealed class TypesetPage
    {
        /// <summary>The chapter this page belongs to. Printed as the recto running head.</summary>
        public string Section { get; }

        public IReadOnlyList<BookLine> Lines { get; }

        /// <summary>
        /// Front matter. A title page carries neither running head nor folio — those number the
        /// body of a book, and starting the count on the title is a thing no printer has ever done.
        /// </summary>
        public bool IsTitlePage { get; }

        public TypesetPage(string section, IReadOnlyList<BookLine> lines, bool isTitlePage = false)
        {
            Section = section ?? string.Empty;
            Lines = lines ?? new List<BookLine>();
            IsTitlePage = isTitlePage;
        }
    }
}
