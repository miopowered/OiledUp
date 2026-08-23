using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Chemistry
{
    /// <summary>
    /// What a certified reference run says about an instrument: the certificate against the readout,
    /// element by element, and the average error those two columns imply (§5.3).
    /// <para>
    /// The average is computed for the player rather than left as an exercise, because both columns
    /// it comes from are on the same screen — anyone who wants to check the arithmetic can. What this
    /// deliberately does <b>not</b> report is <i>why</i> the error is there. Residue from the previous
    /// sample inflates a standard exactly as it inflates a real one, so an unflushed instrument reads
    /// out of tolerance while its calibration is fine. Separating the two is what the solvent blank is
    /// for (§5.2); folding the machine's residue into this readout would hand over that answer free
    /// and delete the flush decision along with it.
    /// </para>
    /// </summary>
    public sealed class CalibrationCheck
    {
        /// <summary>One element's certificate value beside what the instrument made of it.</summary>
        public readonly struct Line
        {
            public readonly ElementDef Element;
            public readonly float Certified;
            public readonly float Measured;

            public Line(ElementDef element, float certified, float measured)
            {
                Element = element;
                Certified = certified;
                Measured = measured;
            }

            /// <summary>Signed fractional error. 0.18 means the instrument read this element 18% high.</summary>
            public float ErrorFraction =>
                Mathf.Approximately(Certified, 0f) ? 0f : Measured / Certified - 1f;
        }

        /// <summary>
        /// Below this an error is instrument noise rather than drift, and calling it drift would put
        /// perfectly good records in doubt. Shared with
        /// <see cref="MeasurementPipeline.FlagSuspectResults"/> so the number the terminal calls
        /// "in tolerance" and the number that decides what gets flagged cannot come apart.
        /// </summary>
        public const float Tolerance = 0.05f;

        private readonly List<Line> lines = new();

        public string MachineId { get; private set; }
        public string StandardId { get; private set; }

        /// <summary>In-game day the standard was run. A certificate goes stale overnight — drift walks a fresh direction each day (§5.3).</summary>
        public int Day { get; private set; }

        public IReadOnlyList<Line> Lines => lines;

        /// <summary>Mean signed error across the panel. This is the number the player recalibrates on.</summary>
        public float ErrorFraction { get; private set; }

        public bool IsOutOfTolerance => Mathf.Abs(ErrorFraction) >= Tolerance;

        /// <summary>
        /// Read a finished reference run against the certificate it was blended to. Elements the
        /// instrument cannot see, and elements the standard does not carry, are absent rather than
        /// scored as zero error — an instrument must not be judged on a number nobody measured.
        /// </summary>
        public static CalibrationCheck From(ReferenceStandard standard, TestResult result,
                                            MachineDef def, int day)
        {
            if (standard == null || result == null || def == null) return null;

            var check = new CalibrationCheck
            {
                MachineId = def.Id,
                StandardId = standard.Id,
                Day = day
            };

            float total = 0f;
            foreach (var element in def.Measures)
            {
                if (element == null || def.IsBlindTo(element.Id)) continue;
                if (!standard.TryGet(element.Id, out float certified) || certified <= 0f) continue;
                if (!result.TryGet(element.Id, out float measured)) continue;

                var line = new Line(element, certified, measured);
                check.lines.Add(line);
                total += line.ErrorFraction;
            }

            check.ErrorFraction = check.lines.Count == 0 ? 0f : total / check.lines.Count;
            return check;
        }
    }
}
