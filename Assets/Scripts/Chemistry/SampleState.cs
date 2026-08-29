using System.Collections.Generic;
using Residue.Data;

namespace Residue.Chemistry
{
    /// <summary>
    /// Everything about a sample that a client is allowed to know. Safe to replicate in full.
    /// Ground truth lives in <see cref="SampleGroundTruth"/>, which the host holds separately.
    /// </summary>
    public sealed class SampleState
    {
        /// <summary>No decision recorded yet. The value <see cref="RegisteredLine"/> starts at.</summary>
        public const int Unregistered = -1;

        /// <summary>
        /// The player looked and could not say. A recorded decision rather than the absence of one:
        /// declaring a pair of vials inseparable is the correct answer to §6.1's same-drum trap, and
        /// it has to be distinguishable from never having looked.
        /// </summary>
        public const int CannotTell = -2;

        public SampleId Id;

        /// <summary>
        /// Printed on the vial label, e.g. "RIG-7 COMPRESSOR B". This is also what the lab files the
        /// sample under: the tag arrives with the bottle rather than being transcribed (#73).
        /// </summary>
        public string EquipmentTag;

        public EquipmentProfileDef Profile;

        public float HoursSinceOilChange;

        /// <summary>Free text from the field. May be wrong, vague, or absent — that is the point.</summary>
        public string FieldTechNote;

        public int CollectedDay;

        /// <summary>
        /// The firm that sent this sample, or null for one that arrived without a sender (#29).
        /// <para>
        /// A definition reference rather than an id, matching <see cref="Profile"/> beside it — the
        /// catalog is the one source of truth for what a customer is, and holding the object means a
        /// renamed firm cannot leave a sample pointing at a name that no longer exists. What
        /// <i>persists</i> is the id: a save stores <c>Customer.Id</c> and resolves it on load, because
        /// a save that pinned its own copy of a customer would fork the balance tables the moment they
        /// were rebuilt.
        /// </para>
        /// This is provenance, not chemistry. Nothing about the sender changes what an instrument
        /// reads (hard rule 1); it changes what the paperwork claims and therefore what is worth
        /// checking.
        /// </summary>
        public CustomerDef Customer;

        /// <summary>
        /// The delivery this arrived on, e.g. <c>KH-04127</c>. Null for a sample with no paperwork.
        /// <para>
        /// Stored on the sample rather than only on the note because a vial outlives the day its
        /// carton was opened: a re-draw filed three days later still has to be traceable to the job it
        /// came in on, and the note itself is a runtime object that does not survive the shift.
        /// </para>
        /// </summary>
        public string JobNumber;

        /// <summary>
        /// The earlier sample this is a re-draw of, when the player filed MONITOR and the unit
        /// stayed in service (§5.4). <see cref="SampleId.None"/> for a first draw.
        /// <para>
        /// Equipment tags repeat legitimately across a contract, so identity has to be explicit —
        /// and §6.1 wants the player to be able to see a unit's history rather than infer it from
        /// a label they may have transcribed wrong.
        /// </para>
        /// </summary>
        public SampleId ResampleOf = SampleId.None;

        public bool IsResample => ResampleOf.IsValid;

        // ---- Reconciliation (#32) ----

        /// <summary>
        /// Why this vial cannot say for itself which line of its note it answers, or
        /// <see cref="SampleAmbiguity.None"/> — which is almost always.
        /// <para>
        /// Client-safe: it restates what somebody standing at the bench can already see. It does not
        /// say what the vial <i>is</i>; that is <see cref="SampleGroundTruth"/>'s and the player has to
        /// work it out.
        /// </para>
        /// </summary>
        public SampleAmbiguity Ambiguity;

        /// <summary>
        /// Which line of the delivery note the player says this vial answers.
        /// <see cref="Unregistered"/> until they say, <see cref="CannotTell"/> if they say they cannot.
        /// <para>
        /// Meaningless — and refused — while <see cref="Ambiguity"/> is
        /// <see cref="SampleAmbiguity.None"/>. That refusal is the whole of #73's settlement: the
        /// typed step exists for the two bottles a shift that cannot speak for themselves, never for
        /// the other fourteen.
        /// </para>
        /// </summary>
        public int RegisteredLine = Unregistered;

        /// <summary>
        /// The tank tag off the line they picked, copied at the moment they picked it. Null while
        /// unregistered or when they recorded <see cref="CannotTell"/>.
        /// <para>
        /// Copied rather than looked up, because a note is a runtime object that does not survive the
        /// shift and a verdict resolves days later. This is what the report ends up naming, and
        /// <see cref="RecordTag"/> marks it as the player's call rather than something read off a
        /// bottle.
        /// </para>
        /// </summary>
        public string RegisteredTag;

        /// <summary>A decision is outstanding on this vial. Never blocks anything; see <see cref="SampleAmbiguity"/>.</summary>
        public bool NeedsRegistering =>
            Ambiguity != SampleAmbiguity.None && RegisteredLine == Unregistered;

        // ---- Physical ----

        /// <summary>Remaining volume. Starts at 100 ml; the full panel costs more than that (§4.5).</summary>
        public float VolumeMl = 100f;

        public float TemperatureC = 20f;

        /// <summary>False until agitated. Running an unsettled sample skews heavy particulates.</summary>
        public bool IsSettled;

        public SampleLocation Location;

        // ---- Player-facing ----

        /// <summary>
        /// Results the player has actually filed at the terminal, by carrying the printout there.
        /// <para>
        /// An instrument finishing a run does NOT put anything here. A reading exists physically on
        /// the machine and on its slip; it becomes part of the record when someone walks it to the
        /// desk. A slip left on a bench is a test you paid for and cannot use.
        /// </para>
        /// </summary>
        public readonly List<TestResult> Results = new();

        public Verdict? FiledVerdict;

        /// <summary>Optional. A correct root cause pays a significant bonus (§5.4).</summary>
        public RootCauseDef FiledRootCause;

        public int FiledOnDay = -1;

        /// <summary>True once the consequence has been resolved and reported back to the player.</summary>
        public bool ConsequenceResolved;

        /// <summary>
        /// How far along the §5.1 chain this sample is. Derived from the fields above rather than
        /// stored — see <see cref="SampleLifecycle"/> for why.
        /// </summary>
        public SampleStage Stage => SampleLifecycle.StageOf(this);

        /// <summary>
        /// What the lab calls this sample, on a screen, on a printout and in an end-of-day report.
        /// <para>
        /// The same string as <see cref="EquipmentTag"/> since #73 removed booking-in: the tag comes
        /// off the label and there is no second, player-typed name it could disagree with. It stays
        /// a distinct member because "what the record is filed under" is what every caller actually
        /// means, and because a sample with no label still has to be nameable in a refusal.
        /// </para>
        /// <para>
        /// A vial whose label is unreadable is the one exception, and it is marked as one. Once the
        /// player has registered it (#32) the record is filed under the tank they named — with
        /// "(unlabelled vial)" after it, every time, on every screen and in every report. That suffix
        /// is not decoration: it is the difference between a name the customer printed and a name the
        /// lab decided on, and the whole cost of getting the decision wrong lands on that difference.
        /// </para>
        /// </summary>
        public string RecordTag =>
            !string.IsNullOrEmpty(EquipmentTag) ? EquipmentTag
            : !string.IsNullOrEmpty(RegisteredTag) ? $"{RegisteredTag} (unlabelled vial)"
            : $"UNLABELLED {Id}";

        public bool HasVolumeFor(MachineDef machine) => machine != null && VolumeMl >= machine.SampleVolumeMl;

        /// <summary>Most recent measured value for an element across all runs, newest first.</summary>
        public bool TryGetLatest(string elementId, out float value, out TestResult source)
        {
            for (int i = Results.Count - 1; i >= 0; i--)
            {
                var r = Results[i];
                if (r.IsBlank) continue;
                if (r.TryGet(elementId, out value))
                {
                    source = r;
                    return true;
                }
            }
            value = 0f;
            source = null;
            return false;
        }

        /// <summary>
        /// Score every element this sample has a reading for against its profile.
        /// This is what the terminal colours red/amber/green.
        /// </summary>
        public ReadingSeverity WorstReading()
        {
            var worst = ReadingSeverity.Normal;
            if (Profile == null) return worst;

            foreach (var t in Profile.Thresholds)
            {
                if (t?.Element == null) continue;
                if (!TryGetLatest(t.Element.Id, out float v, out _)) continue;
                var s = t.Evaluate(v);
                if (s > worst) worst = s;
            }
            return worst;
        }

        public override string ToString() => $"{Id} [{EquipmentTag}]";
    }
}
