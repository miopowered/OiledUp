using System.Collections.Generic;
using System.Text;
using Residue.Data;

namespace Residue.Gameplay.World
{
    public enum BookKind
    {
        /// <summary>One instrument: what it measures, and crucially what it cannot see.</summary>
        MachineManual,

        /// <summary>Every element: where it comes from and what raises it.</summary>
        ElementIndex,

        /// <summary>Root causes and how to tell the confusable ones apart.</summary>
        DiagnosticGuide,

        /// <summary>Threshold tables per equipment profile.</summary>
        ThresholdTables
    }

    public sealed class BookPage
    {
        public string Title;
        public string Body;
    }

    /// <summary>
    /// Builds reference pages from the content definitions.
    /// <para>
    /// The teaching material was already authored — <see cref="ElementDef.SourceHint"/>,
    /// <see cref="RootCauseDef.Explanation"/>, <see cref="MachineDef.CannotDetect"/> — and was
    /// completely unreachable in game. §1.1.1 says the diagnostic tree must be genuinely learnable,
    /// and a player who has to read ContentTables.cs to find out what silicon means is not learning
    /// the game, they are reading its source.
    /// </para>
    /// Generated rather than hand-written so a balance change updates the manual automatically. A
    /// book that disagrees with the chemistry is worse than no book.
    /// </summary>
    public static class BookContent
    {
        public static string TitleFor(BookKind kind, MachineDef machine) => kind switch
        {
            BookKind.MachineManual => machine != null ? $"{machine.DisplayName} — Operator Manual" : "Operator Manual",
            BookKind.ElementIndex => "Elements & Sources",
            BookKind.DiagnosticGuide => "Diagnostic Guide",
            BookKind.ThresholdTables => "Threshold Tables",
            _ => "Reference"
        };

        public static List<BookPage> Build(BookKind kind, MachineDef machine, ContentCatalog catalog)
        {
            var pages = new List<BookPage>();
            if (catalog == null) return pages;

            switch (kind)
            {
                case BookKind.MachineManual: BuildManual(pages, machine, catalog); break;
                case BookKind.ElementIndex: BuildElements(pages, catalog); break;
                case BookKind.DiagnosticGuide: BuildCauses(pages, catalog); break;
                case BookKind.ThresholdTables: BuildThresholds(pages, catalog); break;
            }
            return pages;
        }

        // -- Shift brief -----------------------------------------------------------------------------

        /// <summary>Heading on the brief. Standing orders, not a tutorial: the lab's own words.</summary>
        public const string ShiftBriefTitle = "STANDING ORDERS";

        /// <summary>
        /// The last line of the brief, and the line that decides whether the rest of it is allowed to
        /// exist. Kept next to the pages it closes so nobody edits one without seeing the other.
        /// </summary>
        public const string ShiftBriefClosing =
            "None of the above tells you what any sample is. That part is the job.";

        /// <summary>
        /// The procedure the lab expects of you, and nothing else (#47).
        /// <para>
        /// This lives in <see cref="BookContent"/> because it is the same material as the manuals and
        /// must not drift from them — it names the reference books through
        /// <see cref="TitleFor(BookKind, MachineDef)"/> so renaming one renames it here too, and it
        /// repeats the manual's own wording for blanks, flushes and drift rather than inventing a
        /// second vocabulary for them. What it is <i>not</i> is a second copy of the manuals: it holds
        /// no numbers, because <see cref="BuildManual"/> already prints the real ones per instrument,
        /// and a duplicated figure is a figure that will eventually disagree with the chemistry.
        /// </para>
        /// <para>
        /// <b>Every line says where to look. No line says what you will find.</b> Not one element,
        /// fault or root cause is named anywhere in it, and
        /// <c>OnboardingTests.TheShiftBrief_NamesNoElementFaultOrRootCause</c> holds that shut against
        /// the real content tables — because hard rule 1 says a player who understands cause must beat
        /// one who memorised a table, and a brief that started listing symptoms would hand out the
        /// table on day one. Hard rule 3 is the other half: contamination and drift are only fair
        /// because a blank and a standard reveal them, and this is where the player is told those two
        /// tools exist at all.
        /// </para>
        /// <para>
        /// Deliberately free, unlike a <c>ReferenceBook</c>, which occupies your hands and costs shift
        /// time on purpose. That exemption is affordable only because there is no chemistry in here to
        /// look up — the moment a line of this would save someone a trip to the rack, it belongs in
        /// the rack instead.
        /// </para>
        /// </summary>
        public static List<BookPage> ShiftBrief() => new()
        {
            new BookPage
            {
                Title = "The manuals are not decoration",
                Body =
                    "Every instrument has its operator manual lying on the bench beside it, and the " +
                    $"rack by the terminal holds {TitleFor(BookKind.ElementIndex, null)}, " +
                    $"{TitleFor(BookKind.DiagnosticGuide, null)} and " +
                    $"{TitleFor(BookKind.ThresholdTables, null)}. A manual says what its instrument " +
                    "reports and — the part that matters — what it cannot see. Look at one and press " +
                    "[E] to pick it up."
            },
            new BookPage
            {
                Title = "Loading is a hold, and the hold is the shake",
                Body =
                    "Hold [E] at an instrument to load a vial. That hold is where the sample gets " +
                    "shaken, so it costs seconds you do not get back. A vial that came in cold has to " +
                    "be warmed first; the instrument will say so when it refuses."
            },
            new BookPage
            {
                Title = "Nothing files itself",
                Body =
                    "A finished run prints a slip into the tray on the instrument. The reading joins " +
                    "the record only when you carry that slip to the terminal. A slip left on a bench " +
                    "is a test you paid for and cannot use."
            },
            new BookPage
            {
                Title = "An instrument is dirty until you prove it clean",
                Body =
                    "Some of the last sample stays behind and turns up in the next one. Pushing a " +
                    "solvent blank through reads back what is in there. To clear it, fill a bottle at " +
                    "the wash station and hold FLUSH at the instrument. The terminal marks every " +
                    "instrument that has had no blank today."
            },
            new BookPage
            {
                Title = "An instrument drifts until you prove it has not",
                Body =
                    "Calibration wanders a little every run, in a direction re-rolled each day, and it " +
                    "quietly scales everything the instrument tells you. A certified reference " +
                    "standard is what measures it. The terminal marks every instrument that has had " +
                    "no standard today."
            },
            new BookPage
            {
                Title = "A verdict is a bill that arrives later",
                Body =
                    "Filing closes a sample, but the consequence lands days afterwards. Both " +
                    "directions cost: condemning a serviceable tank is expensive, and passing a bad " +
                    "one is worse. Naming the cause correctly is what pays."
            }
        };

        // -- Machine manual --------------------------------------------------------------------------

        private static void BuildManual(List<BookPage> pages, MachineDef machine, ContentCatalog catalog)
        {
            if (machine == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Run time      {machine.RunTimeSeconds:F0} s");
            sb.AppendLine($"Sample used   {machine.SampleVolumeMl:F0} ml");
            sb.AppendLine($"Cost per run  £{machine.CostPerRun:F0}");
            sb.AppendLine();
            sb.AppendLine($"Typical spread on a reading is about {machine.BaseNoisePercent * 100f:F0}%.");
            sb.AppendLine($"Calibration drifts roughly {machine.CalibrationDriftPerRun * 100f:F1}% per run,");
            sb.AppendLine("in a direction that is re-rolled each day. Run a certified");
            sb.AppendLine("reference sample if you suspect it.");
            sb.AppendLine();
            sb.AppendLine($"Carryover: about {machine.ContaminationCarryoverPercent * 100f:F0}% of whatever went");
            sb.AppendLine("through last stays behind. Push a solvent blank to see it.");
            sb.AppendLine("To clear it, fill a bottle at the wash station and hold");
            sb.AppendLine("the FLUSH button here. One charge per instrument.");
            if (machine.RequiresFumeHood) sb.AppendLine().Append("Requires a fume hood.");
            if (machine.RequiresPreheat) sb.AppendLine().Append($"Requires preheat to {machine.PreheatTargetC:F0} C.");

            pages.Add(new BookPage { Title = "Operation", Body = sb.ToString() });

            var measures = new StringBuilder();
            measures.AppendLine("This instrument reports:");
            measures.AppendLine();
            foreach (var e in machine.Measures)
            {
                if (e == null) continue;
                measures.AppendLine($"  {e.Id,-8} {e.DisplayName} ({e.Unit})");
            }
            pages.Add(new BookPage { Title = "Reports", Body = measures.ToString() });

            // The blind-spot page is the whole reason these manuals exist.
            var blind = new StringBuilder();
            if (machine.CannotDetect.Count == 0)
            {
                blind.AppendLine("No known blind spots for the quantities this instrument reports.");
                blind.AppendLine();
                blind.AppendLine("That does not mean a clean result clears the sample. It only");
                blind.AppendLine("clears what this instrument measures. Check what it does NOT");
                blind.AppendLine("report on the previous page.");
            }
            else
            {
                blind.AppendLine("CANNOT DETECT");
                blind.AppendLine();
                foreach (var e in machine.CannotDetect)
                {
                    if (e == null) continue;
                    blind.AppendLine($"  {e.Id} — {e.DisplayName}");
                    if (!string.IsNullOrEmpty(e.SourceHint)) blind.AppendLine($"      {e.SourceHint}");
                    blind.AppendLine();
                }
                blind.AppendLine("These will be absent from the report even when present in");
                blind.AppendLine("the sample. A clean result here is not a clean sample.");
            }
            pages.Add(new BookPage { Title = "Blind spots", Body = blind.ToString() });
        }

        // -- Element index ---------------------------------------------------------------------------

        private static void BuildElements(List<BookPage> pages, ContentCatalog catalog)
        {
            foreach (var category in CategoryOrder)
            {
                var sb = new StringBuilder();
                int count = 0;

                foreach (var e in catalog.Elements)
                {
                    if (e == null || e.Category != category) continue;
                    count++;
                    sb.AppendLine($"{e.Id} — {e.DisplayName} ({e.Unit})");
                    if (!string.IsNullOrEmpty(e.SourceHint)) sb.AppendLine($"   {e.SourceHint}");
                    sb.AppendLine();
                }

                // A category with nothing in it gets no page. WearMetal is empty in the
                // heat-treatment domain, and a blank chapter in a reference book reads as a bug.
                if (count > 0) pages.Add(new BookPage { Title = Readable(category), Body = sb.ToString() });
            }
        }

        /// <summary>
        /// The order categories are presented in, everywhere. The terminal groups results by this
        /// and the manual indexes elements by it; if the two disagreed the manual would be teaching
        /// an organisation the results screen contradicts, which is worse than either order alone.
        /// </summary>
        public static readonly ElementCategory[] CategoryOrder =
        {
            ElementCategory.WearMetal, ElementCategory.Contaminant,
            ElementCategory.Additive, ElementCategory.FluidProperty
        };

        /// <summary>Display name for a category. Shared with the terminal for the reason above.</summary>
        public static string Readable(ElementCategory c) => c switch
        {
            ElementCategory.WearMetal => "Wear metals",
            ElementCategory.Contaminant => "Contaminants",
            ElementCategory.Additive => "Additives",
            _ => "Fluid properties"
        };

        // -- Diagnostic guide ------------------------------------------------------------------------

        private static void BuildCauses(List<BookPage> pages, ContentCatalog catalog)
        {
            foreach (var cause in catalog.Causes)
            {
                if (cause == null) continue;
                var sb = new StringBuilder();
                sb.AppendLine(cause.Explanation);
                pages.Add(new BookPage { Title = cause.DisplayName, Body = sb.ToString() });
            }
        }

        // -- Threshold tables ------------------------------------------------------------------------

        private static void BuildThresholds(List<BookPage> pages, ContentCatalog catalog)
        {
            foreach (var profile in catalog.Profiles)
            {
                if (profile == null) continue;

                var sb = new StringBuilder();
                sb.AppendLine($"Oil grade {profile.BaseOilGrade}   change interval {profile.DefaultOilChangeHours:F0} h");
                sb.AppendLine();
                sb.AppendLine("ELEMENT   NORMAL          CRITICAL");
                sb.AppendLine();

                foreach (var t in profile.Thresholds)
                {
                    if (t?.Element == null) continue;

                    string normal, critical;
                    switch (t.Mode)
                    {
                        case ThresholdMode.LowerLimit:
                            normal = $">= {t.NormalLimit:0.###}";
                            critical = $"<= {t.CautionLimit:0.###}";
                            break;
                        case ThresholdMode.DeviationBand:
                            normal = $"{t.Baseline:0.#} +/-{t.NormalLimit * 100f:0}%";
                            critical = $"+/-{t.CautionLimit * 100f:0}%";
                            break;
                        default:
                            normal = $"<= {t.NormalLimit:0.###}";
                            critical = $">= {t.CautionLimit:0.###}";
                            break;
                    }

                    sb.AppendLine($"{t.Element.Id,-9} {normal,-15} {critical}");
                }

                sb.AppendLine();
                sb.AppendLine("Limits are per equipment type. The same iron figure can be");
                sb.AppendLine("routine on one unit and cause to pull another.");

                pages.Add(new BookPage { Title = profile.DisplayName, Body = sb.ToString() });
            }
        }
    }
}
