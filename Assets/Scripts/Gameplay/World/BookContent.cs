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
    /// <para>
    /// The prose is in <see cref="LabStrings"/> under <c>book.</c> (#55); the figures, element
    /// names, faults and root causes are not, because they come out of the content tables and are
    /// balance data with their own pipeline. What is localised here is the connective tissue — the
    /// words around the numbers — never a number.
    /// </para>
    /// </summary>
    public static class BookContent
    {
        public static string TitleFor(BookKind kind, MachineDef machine) => kind switch
        {
            BookKind.MachineManual => machine != null
                ? LabStrings.BookOperatorManualFor.Format(("instrument", machine.DisplayName))
                : LabStrings.BookOperatorManual.Text,
            BookKind.ElementIndex => LabStrings.BookElementIndex.Text,
            BookKind.DiagnosticGuide => LabStrings.BookDiagnosticGuide.Text,
            BookKind.ThresholdTables => LabStrings.BookThresholdTables.Text,
            _ => LabStrings.BookReference.Text
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
        public static string ShiftBriefTitle => LabStrings.BookShiftBriefTitle.Text;

        /// <summary>
        /// The last line of the brief, and the line that decides whether the rest of it is allowed to
        /// exist. It reads as the closing of the pages below because it is stored beside them, in the
        /// <c>book.shift_brief_</c> block of <see cref="LabStrings"/> — that block carries the rule
        /// nobody may edit one of these lines without.
        /// </summary>
        public static string ShiftBriefClosing => LabStrings.BookShiftBriefClosing.Text;

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
        /// The words themselves are the <c>book.shift_brief_</c> block of <see cref="LabStrings"/>
        /// (#55), and that block repeats the rule below at the point of editing, because the rule is
        /// about the sentences rather than about this method.
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
                Title = LabStrings.BookShiftBriefManualsTitle.Text,
                Body = LabStrings.BookShiftBriefManualsBody.Format(
                    ("elements", TitleFor(BookKind.ElementIndex, null)),
                    ("diagnostics", TitleFor(BookKind.DiagnosticGuide, null)),
                    ("thresholds", TitleFor(BookKind.ThresholdTables, null)))
            },
            new BookPage
            {
                Title = LabStrings.BookShiftBriefLoadingTitle.Text,
                Body = LabStrings.BookShiftBriefLoadingBody.Text
            },
            new BookPage
            {
                Title = LabStrings.BookShiftBriefFilingTitle.Text,
                Body = LabStrings.BookShiftBriefFilingBody.Text
            },
            new BookPage
            {
                Title = LabStrings.BookShiftBriefDirtyTitle.Text,
                Body = LabStrings.BookShiftBriefDirtyBody.Text
            },
            new BookPage
            {
                Title = LabStrings.BookShiftBriefDriftTitle.Text,
                Body = LabStrings.BookShiftBriefDriftBody.Text
            },
            new BookPage
            {
                Title = LabStrings.BookShiftBriefVerdictTitle.Text,
                Body = LabStrings.BookShiftBriefVerdictBody.Text
            }
        };

        // -- Machine manual --------------------------------------------------------------------------

        private static void BuildManual(List<BookPage> pages, MachineDef machine, ContentCatalog catalog)
        {
            if (machine == null) return;

            // Each paragraph is appended whole rather than a line at a time. The page is fixed-width
            // paper, so the breaks are part of the text — but a translator handed "in a direction
            // that is re-rolled each day. Run a certified" has been handed half a sentence and no way
            // to re-wrap it (#55). The breaks therefore live inside the line, not between appends.
            var sb = new StringBuilder();
            sb.AppendLine(LabStrings.BookManualRunTime.Format(
                ("seconds", machine.RunTimeSeconds.ToString("F0"))));
            sb.AppendLine(LabStrings.BookManualSampleUsed.Format(
                ("millilitres", machine.SampleVolumeMl.ToString("F0"))));
            sb.AppendLine(LabStrings.BookManualCostPerRun.Format(
                ("cost", machine.CostPerRun.ToString("F0"))));
            sb.AppendLine();
            sb.AppendLine(LabStrings.BookManualNoise.Format(
                ("percent", (machine.BaseNoisePercent * 100f).ToString("F0"))));
            sb.AppendLine(LabStrings.BookManualDrift.Format(
                ("percent", (machine.CalibrationDriftPerRun * 100f).ToString("F1"))));
            sb.AppendLine();
            sb.AppendLine(LabStrings.BookManualCarryover.Format(
                ("percent", (machine.ContaminationCarryoverPercent * 100f).ToString("F0"))));
            if (machine.RequiresFumeHood) sb.AppendLine().Append(LabStrings.BookManualFumeHood.Text);
            if (machine.RequiresPreheat)
            {
                sb.AppendLine().Append(LabStrings.BookManualPreheat.Format(
                    ("celsius", machine.PreheatTargetC.ToString("F0"))));
            }

            pages.Add(new BookPage
            {
                Title = LabStrings.BookManualOperationTitle.Text, Body = sb.ToString()
            });

            var measures = new StringBuilder();
            measures.AppendLine(LabStrings.BookManualReportsIntro.Text);
            measures.AppendLine();
            foreach (var e in machine.Measures)
            {
                if (e == null) continue;
                measures.AppendLine($"  {e.Id,-8} {e.DisplayName} ({e.Unit})");
            }
            pages.Add(new BookPage
            {
                Title = LabStrings.BookManualReportsTitle.Text, Body = measures.ToString()
            });

            // The blind-spot page is the whole reason these manuals exist.
            var blind = new StringBuilder();
            if (machine.CannotDetect.Count == 0)
            {
                blind.AppendLine(LabStrings.BookManualNoBlindSpots.Text);
            }
            else
            {
                blind.AppendLine(LabStrings.BookManualCannotDetect.Text);
                blind.AppendLine();
                foreach (var e in machine.CannotDetect)
                {
                    if (e == null) continue;
                    blind.AppendLine($"  {e.Id} — {e.DisplayName}");
                    if (!string.IsNullOrEmpty(e.SourceHint)) blind.AppendLine($"      {e.SourceHint}");
                    blind.AppendLine();
                }
                blind.AppendLine(LabStrings.BookManualCannotDetectClosing.Text);
            }
            pages.Add(new BookPage
            {
                Title = LabStrings.BookManualBlindSpotsTitle.Text, Body = blind.ToString()
            });
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
            ElementCategory.WearMetal => LabStrings.BookCategoryWearMetals.Text,
            ElementCategory.Contaminant => LabStrings.BookCategoryContaminants.Text,
            ElementCategory.Additive => LabStrings.BookCategoryAdditives.Text,
            _ => LabStrings.BookCategoryFluidProperties.Text
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
                sb.AppendLine(LabStrings.BookThresholdsGrade.Format(
                    ("grade", profile.BaseOilGrade),
                    ("hours", profile.DefaultOilChangeHours.ToString("F0"))));
                sb.AppendLine();
                sb.AppendLine(LabStrings.BookThresholdsColumns.Text);
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
                sb.AppendLine(LabStrings.BookThresholdsFooter.Text);

                pages.Add(new BookPage { Title = profile.DisplayName, Body = sb.ToString() });
            }
        }
    }
}
