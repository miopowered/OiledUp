using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// A whole run, flattened into plain data so it can be written to disk and read back exactly
    /// (#49). This is the format <see cref="RunSaveStore"/> was built to carry.
    ///
    /// <para>
    /// <b>State, not a seed.</b> The tempting shape for this type is a seed and a day number: the
    /// generator is deterministic, so replay it. It does not work, and the reason is not subtle — a
    /// run diverges the moment a player acts. <c>Rng</c> reproduces <i>generation</i>, never
    /// decisions, so which vial got agitated, which instrument it went into, and what was filed
    /// against it are facts no seed contains. Everything that would change what happens next is
    /// written out here, and the generator state travels beside the seed rather than instead of it.
    /// </para>
    ///
    /// <para>
    /// <b>Content is referenced by id and never embedded.</b> A <c>FaultDef</c> is balance data,
    /// regenerated from <c>ContentTables.cs</c> whenever the chemistry is retuned. A save that
    /// carried its own copy would silently fork: the loaded run would score against last week's
    /// thresholds while the manual on the wall printed this week's, and hard rule 1 would be broken
    /// by a file on the player's disk. So every definition is an id, resolved against the live
    /// <c>ContentCatalog</c> at load — and an id that no longer resolves refuses the load rather
    /// than dropping the sample it named. See <see cref="RunSnapshotRestore"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Ground truth is in here, and that is deliberate.</b> The host legitimately holds it, and a
    /// save with the truth stripped out could not resolve a single pending verdict — the whole §5.4
    /// delayed consequence would evaporate over a quit. The file is on the player's disk and readable
    /// by anyone who opens it; for a co-op PvE game that is an acceptable trade and it is recorded
    /// here rather than discovered later. What is <i>not</i> acceptable is a path from a save to a
    /// client, so <see cref="TruthRecord"/> and <see cref="Truths"/> are <c>internal</c> to
    /// <c>Residue.Gameplay</c>. <c>Residue.Net</c> references this assembly and still cannot name
    /// them, which is the same structural argument that keeps
    /// <c>SampleRegistry</c>'s truth map private — not a comment asking to be remembered.
    /// </para>
    /// </summary>
    public sealed class RunSnapshot
    {
        /// <summary>
        /// Shape of this snapshot, versioned from the first commit.
        /// <para>
        /// Distinct from <see cref="RunSaveStore.CurrentFormatVersion"/> on purpose: that one
        /// versions the envelope — magic, length, checksum — and this one versions what the payload
        /// inside it means. They change for unrelated reasons and folding them together would force
        /// a rewrite of every save whenever the checksum scheme moved.
        /// </para>
        /// A reader refuses anything it does not recognise, with both numbers in the message. See
        /// <see cref="RunSnapshotCodec"/> for why refusing beats guessing.
        /// </summary>
        public const int SchemaVersion = 3;

        public int Schema = SchemaVersion;

        /// <summary>When the save was written, for the menu to date it. <c>DateTime.UtcNow.Ticks</c>.</summary>
        public long SavedUtcTicks;

        // -- Contract and clock --------------------------------------------------------------------

        public string ContractId;
        public string ContractName;

        public int Day;
        public bool DayInProgress;
        public float DaySecondsRemaining;

        // -- Generation ----------------------------------------------------------------------------

        /// <summary>The run seed. Kept for diagnostics and for reproducing a report, not for replay.</summary>
        public int Seed;

        /// <summary>Live xorshift128 state — see <c>Rng.CaptureState</c> for why the seed alone is not enough.</summary>
        public uint RngA, RngB, RngC, RngD;

        /// <summary>Next sample id the generator will mint.</summary>
        public int NextSampleId = 1;

        // -- Books ---------------------------------------------------------------------------------

        public float Money;
        public float Reputation;
        public float SolventUnits;
        public int ReferenceStandards;
        public float TotalEarned;
        public float TotalLost;

        // -- Contents ------------------------------------------------------------------------------

        public readonly List<SampleRecord> Samples = new();
        public readonly List<MachineRecord> Machines = new();
        public readonly List<SlipRecord> Slips = new();
        public readonly List<BottleRecord> Bottles = new();

        /// <summary>Verdicts filed and not yet due. §5.4's whole point is that these outlive the day.</summary>
        public readonly List<PendingRecord> Pending = new();

        /// <summary>Units the player filed MONITOR on, waiting for the next day to re-draw them.</summary>
        public readonly List<int> Requeues = new();

        /// <summary>The summary the player is looking at when they quit at a day boundary.</summary>
        public readonly List<ReportRecord> LastReports = new();

        public int NextSlipTicket = 1;

        /// <summary>
        /// SERVER ONLY. Parallel to <see cref="Samples"/> by <see cref="TruthRecord.Id"/>.
        /// <c>internal</c> so no assembly that talks to a client can name the type, let alone read it.
        /// </summary>
        internal readonly List<TruthRecord> Truths = new();

        // -- Records -------------------------------------------------------------------------------

        /// <summary>One element's measured or true concentration. Element ids are strings and need no
        /// catalog lookup, so an element removed from the tables costs a reading nobody reads rather
        /// than a failed load.</summary>
        public struct Reading
        {
            public string ElementId;
            public float Value;
        }

        /// <summary>A <c>SampleLocation</c>, flattened.</summary>
        public struct PlaceRecord
        {
            public int Kind;
            public ulong HolderClientId;
            public string ContainerId;
            public int SlotIndex;
        }

        /// <summary>One completed run's player-facing numbers. Never ground truth — see <c>TestResult</c>.</summary>
        public sealed class ResultRecord
        {
            public string MachineId;
            public int DayRun;
            public int MachineRunIndex;
            public float VolumeConsumedMl;
            public float Cost;
            public bool Suspect;
            public bool IsBlank;
            public bool IsReference;
            public readonly List<Reading> Values = new();
        }

        /// <summary>Everything about a sample a client is allowed to know. Mirrors <c>SampleState</c>.</summary>
        public sealed class SampleRecord
        {
            public int Id;
            public string EquipmentTag;
            public string ProfileId;

            /// <summary>Who sent it (#29), by id. Null for a sample with no sender on file.</summary>
            public string CustomerId;

            /// <summary>The delivery it arrived on, e.g. KH-04127. Null when there was no paperwork.</summary>
            public string JobNumber;

            public float HoursSinceOilChange;
            public string FieldTechNote;
            public int CollectedDay;
            public int ResampleOf;

            public float VolumeMl;
            public float TemperatureC;
            public bool IsSettled;
            public PlaceRecord Location;

            public readonly List<ResultRecord> Results = new();

            /// <summary>The filed <c>Verdict</c> as an int, or -1 for "no verdict on file".</summary>
            public int FiledVerdict = -1;

            public string FiledRootCauseId;
            public int FiledOnDay = -1;
            public bool ConsequenceResolved;

            /// <summary><c>SampleAmbiguity</c> as an int (#32).</summary>
            public int Ambiguity;

            /// <summary>
            /// What the player recorded about an ambiguous vial. Saved because a verdict resolves days
            /// after it was filed: a decision made on day 3 has to still be the decision being scored
            /// on day 9, across however many quits happen in between.
            /// </summary>
            public int RegisteredLine = SampleState.Unregistered;

            public string RegisteredTag;
        }

        /// <summary>SERVER ONLY. What is actually wrong with a sample. See the type doc.</summary>
        internal sealed class TruthRecord
        {
            public int Id;
            public readonly List<string> FaultIds = new();
            public readonly List<float> Severities = new();
            public readonly List<Reading> TrueValues = new();
            public readonly List<Reading> Contamination = new();

            // -- Provenance (#32) --
            //
            // Plain values rather than content ids: a tank tag is a string the customer printed, not
            // a definition, so there is nothing here for a rebuilt ContentTables to fork.

            public string TrueTankTag;
            public int TrueNoteLine = -1;

            /// <summary>The other half of a split draw, or 0 for none.</summary>
            public int SameDrumAs;
        }

        /// <summary>One installed instrument: what it is, what is in it, and what it has drifted to.</summary>
        public sealed class MachineRecord
        {
            public string InstanceId;
            public string DefId;

            public int LoadedSample;

            /// <summary><c>RunKind</c> as an int.</summary>
            public int ActiveRun;

            public float SecondsRemaining;

            /// <summary>Length of the run in progress, so the progress bar does not jump on load.</summary>
            public float RunDuration;

            public bool HasResultWaiting;

            public readonly List<Reading> Residue = new();
            public float DriftPercent;
            public int DriftSign = 1;
            public int RunIndex;
            public int RunsSinceClean;
            public int RunsSinceCalibration;
            public int DriftStartedAtRunIndex;
            public int LastCalibratedDay = -1;

            public ResultRecord LastResult;
            public ResultRecord LastBlank;
            public int LastBlankDay = -1;

            /// <summary>
            /// The certified run behind <c>MachineInstance.LastCheck</c>, rather than the check itself.
            /// <para>
            /// A <c>CalibrationCheck</c> is a reading of a result against a certificate, and the
            /// certificate is itself derived from the profiles the catalog already holds. Saving the
            /// run and rebuilding the check on load means the two can never disagree — a saved check
            /// would be a second copy of a number that has one source.
            /// </para>
            /// </summary>
            public ResultRecord LastCheckResult;

            public int LastCheckDay = -1;

            public bool HasLastCalibration;
            public int CalibrationDay;
            public float CalibrationCorrectedDrift;
            public int CalibrationFlaggedResults;
            public int CalibrationAffectedSamples;
            public int CalibrationAffectedArchived;
        }

        /// <summary>A printed slip nobody has filed yet — a test that was paid for and still counts.</summary>
        public sealed class SlipRecord
        {
            public int Ticket;
            public int Sample;
            public string MachineInstanceId;
            public ResultRecord Result;
            public PlaceRecord Location;
        }

        public sealed class BottleRecord
        {
            public string Id;
            public int Capacity;
            public int Charges;
            public PlaceRecord Location;
        }

        public struct PendingRecord
        {
            public int Sample;
            public int ResolveOnDay;
        }

        /// <summary>A resolved verdict, already shown to the player. Nothing here is still secret.</summary>
        public sealed class ReportRecord
        {
            public int Sample;
            public string RecordTag;
            public int Filed;
            public int Outcome;
            public float MoneyDelta;
            public float ReputationDelta;
            public bool RootCauseCorrect;
            public string FaultName;
            public string ActualRootCause;
            public bool RequeueSample;
            public string Headline;

            /// <summary><c>RegistrationOutcome</c> as an int (#32). Already shown; nothing secret.</summary>
            public int Registration;
        }
    }
}
