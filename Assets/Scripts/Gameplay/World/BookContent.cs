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
            foreach (var category in new[]
                     {
                         ElementCategory.WearMetal, ElementCategory.Contaminant,
                         ElementCategory.Additive, ElementCategory.FluidProperty
                     })
            {
                var sb = new StringBuilder();
                foreach (var e in catalog.Elements)
                {
                    if (e == null || e.Category != category) continue;
                    sb.AppendLine($"{e.Id} — {e.DisplayName} ({e.Unit})");
                    if (!string.IsNullOrEmpty(e.SourceHint)) sb.AppendLine($"   {e.SourceHint}");
                    sb.AppendLine();
                }
                pages.Add(new BookPage { Title = Readable(category), Body = sb.ToString() });
            }
        }

        private static string Readable(ElementCategory c) => c switch
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
