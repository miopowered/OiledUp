using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Every action a player can ask the lab to perform.
    /// <para>
    /// One flat enum rather than a method per action, because §3.1 makes every one of these the same
    /// thing — a request the host validates — and the netcode layer should have one message to carry
    /// and one place to authorise, not twenty. Adding an action here costs a case in
    /// <see cref="LabCommandExecutor"/> and nothing on the wire.
    /// </para>
    /// Reading is deliberately absent. Nothing here answers a question; a command that only wanted to
    /// know something would be a prompt, and prompts are asked every frame.
    /// </summary>
    public enum LabCommandKind
    {
        /// <summary>Unset. Refused, so a default-constructed command cannot do anything.</summary>
        None = 0,

        /// <summary>Pick a vial up. §5.1's unload when it comes out of the delivery crate.</summary>
        TakeVial,

        /// <summary>Pick a results slip up out of a tray.</summary>
        TakeSlip,

        /// <summary>Pick a manual up. Changes nothing but whose hands are full.</summary>
        TakeBook,

        /// <summary>Pick a solvent bottle up off its cradle or a rack (§5.2).</summary>
        TakeBottle,

        /// <summary>Set whatever is in your hands down on a surface.</summary>
        PutDown,

        /// <summary>Select one of the items already owned in the three-slot inventory.</summary>
        SelectInventory,

        /// <summary>Shake the carried vial back to homogeneous (§4.5).</summary>
        Agitate,

        /// <summary>Put the carried vial into an instrument.</summary>
        LoadMachine,

        /// <summary>Start the loaded sample running.</summary>
        StartRun,

        /// <summary>Take the vial back out of an instrument.</summary>
        TakeFromMachine,

        /// <summary>
        /// Flush an instrument. Zeroes residue and spends a charge out of the bottle in your hands
        /// (§5.2). The solvent came from the wash station; the flush happens here, because this is
        /// where the carryover is.
        /// </summary>
        FlushMachine,

        /// <summary>Top the carried bottle up from the wash station's drum (§5.2, §5.5).</summary>
        FillBottle,

        /// <summary>Run a solvent blank — the residue tell, not the fix (§5.2).</summary>
        RunBlank,

        /// <summary>Run a certified standard, which is the only way to measure drift (§5.3).</summary>
        RunReference,

        /// <summary>Recalibrate against today's certificate (§5.3).</summary>
        Calibrate,

        /// <summary>Transcribe the carried slip into a sample's record at the terminal.</summary>
        FileSlip,

        /// <summary>File the verdict and close the record (§5.4).</summary>
        FileVerdict,

        /// <summary>Order more solvent.</summary>
        OrderSolvent,

        /// <summary>Order more certified ampoules.</summary>
        OrderStandards,

        /// <summary>Withdraw a verdict filed on numbers a drifting instrument produced (§5.3).</summary>
        ReopenSuspect,

        /// <summary>Close the shift and settle everything due.</summary>
        EndDay,

        /// <summary>Begin the next contracted day.</summary>
        StartNextDay
    }

    /// <summary>
    /// One request, as a value.
    /// <para>
    /// Five primitive fields rather than a payload per action, so the whole command set crosses the
    /// wire as a single fixed-shape message (see <c>Residue.Net.LabCommandMessage</c>) and there is
    /// exactly one thing for the host to authorise. The fields are general on purpose and are given
    /// meaning by the static factories below — no call site ever fills one in by hand, so the
    /// generality never reaches anywhere it could be got wrong.
    /// </para>
    /// <para>
    /// Note what a command may <b>not</b> carry: a measured value. A slip is named by its ticket, not
    /// by its numbers, because §3.1's "never let a client compute a test result" is worth very little
    /// if a client may instead post one. See <see cref="Residue.Gameplay.Simulation.ResultSlips"/>.
    /// </para>
    /// </summary>
    public readonly struct LabCommand
    {
        public readonly LabCommandKind Kind;

        /// <summary>
        /// Which placed thing this is aimed at: a machine instance id, a rack id. Null for the
        /// actions that are aimed at whatever is in your hands, and for terminal paperwork, which is
        /// always aimed at <see cref="TerminalStation.FixtureId"/>.
        /// </summary>
        public readonly string FixtureId;

        /// <summary>The sample this concerns, where the action names one rather than deriving it.</summary>
        public readonly SampleId Sample;

        /// <summary>
        /// The one number an action needs. A rack slot, a slip ticket, a pack size, or a
        /// <see cref="Verdict"/> cast to its underlying value. Meaningless unless a factory set it.
        /// </summary>
        public readonly int Amount;

        /// <summary>A <c>RootCauseDef</c> id, or an item id for a grip. Null otherwise.</summary>
        public readonly string Text;

        public LabCommand(LabCommandKind kind, string fixtureId = null, SampleId sample = default,
                          int amount = 0, string text = null)
        {
            Kind = kind;
            FixtureId = string.IsNullOrEmpty(fixtureId) ? null : fixtureId;
            Sample = sample;
            Amount = amount;
            Text = string.IsNullOrEmpty(text) ? null : text;
        }

        // -- Hands ---------------------------------------------------------------------------------

        public static LabCommand TakeVial(SampleId sample) =>
            new(LabCommandKind.TakeVial, sample: sample);

        public static LabCommand TakeSlip(int ticket) =>
            new(LabCommandKind.TakeSlip, amount: ticket);

        public static LabCommand TakeBook(string bookId = null) =>
            new(LabCommandKind.TakeBook, bookId);

        /// <summary>
        /// A bottle is named in <see cref="FixtureId"/> rather than in <see cref="Text"/> because it
        /// is a placed thing with an id, exactly like a rack or an instrument — and because the field
        /// already crosses the wire in that role.
        /// </summary>
        public static LabCommand TakeBottle(string bottleId) =>
            new(LabCommandKind.TakeBottle, bottleId);

        public static LabCommand PutDown(string surfaceId, int slot) =>
            new(LabCommandKind.PutDown, surfaceId, amount: slot);

        public static LabCommand SelectInventory(LabGrip grip) =>
            new(LabCommandKind.SelectInventory, ((int)grip.Kind).ToString(), grip.Sample,
                grip.Ticket, grip.ItemId);

        public static LabCommand Agitate() => new(LabCommandKind.Agitate);

        // -- Instruments ---------------------------------------------------------------------------

        public static LabCommand LoadMachine(string machineInstanceId) =>
            new(LabCommandKind.LoadMachine, machineInstanceId);

        public static LabCommand StartRun(string machineInstanceId) =>
            new(LabCommandKind.StartRun, machineInstanceId);

        public static LabCommand TakeFromMachine(string machineInstanceId) =>
            new(LabCommandKind.TakeFromMachine, machineInstanceId);

        public static LabCommand FlushMachine(string machineInstanceId) =>
            new(LabCommandKind.FlushMachine, machineInstanceId);

        public static LabCommand RunBlank(string machineInstanceId) =>
            new(LabCommandKind.RunBlank, machineInstanceId);

        public static LabCommand RunReference(string machineInstanceId) =>
            new(LabCommandKind.RunReference, machineInstanceId);

        public static LabCommand Calibrate(string machineInstanceId) =>
            new(LabCommandKind.Calibrate, machineInstanceId);

        // -- Wash station --------------------------------------------------------------------------

        /// <summary>
        /// Which station, not which bottle: the bottle is whichever one the host says is in your
        /// hands, and letting a request name one would be letting a client fill a bottle across the
        /// room from a drum it is not standing at.
        /// </summary>
        public static LabCommand FillBottle(string stationId) =>
            new(LabCommandKind.FillBottle, stationId);

        // -- Terminal ------------------------------------------------------------------------------

        public static LabCommand FileSlip(int ticket) =>
            new(LabCommandKind.FileSlip, amount: ticket);

        public static LabCommand FileVerdict(SampleId sample, Verdict verdict, string rootCauseId) =>
            new(LabCommandKind.FileVerdict, sample: sample, amount: (int)verdict, text: rootCauseId);

        public static LabCommand OrderSolvent(int units) =>
            new(LabCommandKind.OrderSolvent, amount: units);

        public static LabCommand OrderStandards(int count) =>
            new(LabCommandKind.OrderStandards, amount: count);

        public static LabCommand ReopenSuspect(SampleId sample) =>
            new(LabCommandKind.ReopenSuspect, sample: sample);

        public static LabCommand EndDay() => new(LabCommandKind.EndDay);

        public static LabCommand StartNextDay() => new(LabCommandKind.StartNextDay);

        public override string ToString() =>
            $"{Kind}({FixtureId ?? "-"}, {Sample}, {Amount}, {Text ?? "-"})";
    }
}
