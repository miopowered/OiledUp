using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// What a finished run is called on the instrument that produced it.
    /// <para>
    /// <b>Why this is a type and not two lines inside a screen (#56).</b> <see cref="MachineDisplay"/>
    /// used to have two <c>Show</c> overloads that captioned differently: the host had a
    /// <see cref="SampleState"/> in hand and printed its tag, a client had only a
    /// <see cref="SampleId"/> and printed that. Two players standing at the same instrument read
    /// different captions for the same run. The fix is not to make the two agree by inspection — that
    /// is what they were already trying to do — but to leave one place where the answer is computed,
    /// so there is nothing to disagree.
    /// </para>
    /// <para>
    /// The old objection was §5.1's: the paper label must not reach a screen, or a screen would diff
    /// it against the player's typed tag and hand over a mis-log for free. #73 removed booking-in.
    /// <see cref="SampleState.RecordTag"/> now simply <i>returns</i> <c>EquipmentTag</c>, there is no
    /// typed tag left for the label to be checked against, and so there is nothing to withhold. The
    /// instrument names the run the way the lab does, which is what the terminal and — since
    /// #38 — the printouts already did.
    /// </para>
    /// </summary>
    public static class RunCaption
    {
        /// <summary>
        /// A standard has to name itself: a full panel of plausible numbers with no sample named
        /// beside it reads as somebody else's sample.
        /// <para>
        /// Resolved rather than <c>const</c> since #55: these are words on a screen, not ids, and a
        /// compile-time constant cannot follow the active language. Everything that compares against
        /// them compares the string it was handed, which is still the string this returns.
        /// </para>
        /// </summary>
        public static string Blank => ScreenStrings.ScreenCaptionBlank.Text;

        public static string Standard => ScreenStrings.ScreenCaptionStandard.Text;

        /// <summary>
        /// A run whose sample nothing in this process can name yet. Never blank — see the type doc.
        /// A dash rather than a word, so there is nothing here to translate.
        /// </summary>
        public const string Unnamed = "-";

        /// <summary>
        /// The caption, given the name the lab files the sample under. Pure, and the only place the
        /// precedence between "this belongs to the instrument" and "this belongs to a sample" is
        /// decided.
        /// </summary>
        public static string For(TestResult result, string recordTag)
        {
            if (result == null) return Unnamed;
            if (result.IsBlank) return Blank;
            if (result.IsReference) return Standard;
            return string.IsNullOrEmpty(recordTag) ? Unnamed : recordTag;
        }

        /// <summary>
        /// The caption for a run on this process, whichever side of the wire it is. Resolves the tag
        /// through <see cref="RecordTagFor"/> and then answers exactly as above, so a host and a
        /// client that can both see the sample necessarily print the same string.
        /// </summary>
        public static string For(TestResult result, SampleId sample) =>
            For(result, RecordTagFor(sample));

        /// <summary>
        /// What this process calls that sample, or null if it cannot name it yet.
        /// <para>
        /// Both branches end at <see cref="SampleState.RecordTag"/>, which is the point. A process
        /// that simulates reads its own registry. One that does not reads the same
        /// <see cref="SampleState"/> objects the terminal draws, rebuilt from the host's publish — so
        /// the client is not deriving a second name from a different field, it is reading the same
        /// property off a copy of the same record.
        /// </para>
        /// <para>
        /// Null is reachable and is not a bug: a client whose first publish has not landed, or a
        /// sample the last run consumed outright, cannot be named by anyone. Both sides fall back to
        /// <see cref="Unnamed"/> together rather than one of them inventing something.
        /// </para>
        /// </summary>
        public static string RecordTagFor(SampleId sample)
        {
            if (!sample.IsValid) return null;

            var runtime = LabRuntime.Instance;
            var state = runtime != null ? runtime.SampleFor(sample) : null;

            // No lab here, so this process is reading the host's. ReadLab is only reached when a
            // caption is actually being drawn — once per new reading, not once per frame.
            if (state == null)
            {
                var records = RecordFeed.Source?.ReadLab();
                state = records?.Sample(sample);
            }

            return state?.RecordTag;
        }
    }
}
