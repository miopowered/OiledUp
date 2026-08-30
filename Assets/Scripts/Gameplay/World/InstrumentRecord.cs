using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One instrument's paperwork, as the terminal prints it: how long since it was flushed, what its
    /// last solvent blank found, the certificate on file, and what the last recalibration cost in
    /// confidence.
    /// <para>
    /// Deliberately not a <see cref="MachineInstance"/>. That object is the host's simulation — it
    /// owns the residue map and the drift figure, which are the hidden state §5.2 and §5.3 exist to
    /// make discoverable, and a client has no business holding either even at zero. This is the far
    /// smaller thing a <i>screen</i> needs, and it is filled from a live instrument on the host and
    /// from replicated views on a client, so the terminal draws one set of rows either way.
    /// </para>
    /// <para>
    /// The <see cref="Check"/> is a real <see cref="CalibrationCheck"/> rather than a flattened copy
    /// because a client rebuilds it with the same <see cref="CalibrationCheck.From"/> the host used,
    /// from the replicated readout and the certificate its own content tables publish. Two screens
    /// showing a different average error for the same instrument is the one disagreement §5.3 cannot
    /// survive.
    /// </para>
    /// </summary>
    public sealed class InstrumentRecord
    {
        public string InstanceId;

        public MachineDef Def;

        /// <summary>Runs since the last solvent flush. The player's cue that carryover is building (§5.2).</summary>
        public int RunsSinceFlush;

        /// <summary>The last solvent blank, or null if this instrument has never had one.</summary>
        public TestResult LastBlank;

        /// <summary>Day of that blank, or -1. "Clean" and "unknown" are different answers.</summary>
        public int LastBlankDay = -1;

        /// <summary>The certificate on file, or null. Cleared by a recalibration, which consumes it.</summary>
        public CalibrationCheck Check;

        /// <summary>What the last recalibration corrected, and how much filed work it put in doubt.</summary>
        public CalibrationOutcome? LastCalibration;

        /// <summary>
        /// The instrument's name out of the content tables, or a stand-in for a record whose
        /// definition this process cannot see yet. Only the stand-in is a translated line; the
        /// definition's name is balance data with its own pipeline.
        /// </summary>
        public string DisplayName =>
            Def != null ? Def.DisplayName : ScreenStrings.ScreenInstrumentFallback;

        /// <summary>Read a live instrument. Host and single player; nothing here leaves the process.</summary>
        public static InstrumentRecord FromHost(MachineInstance machine) => machine == null
            ? null
            : new InstrumentRecord
            {
                InstanceId = machine.InstanceId,
                Def = machine.Def,
                RunsSinceFlush = machine.Runtime != null ? machine.Runtime.RunsSinceClean : 0,
                LastBlank = machine.LastBlank,
                LastBlankDay = machine.LastBlankDay,
                Check = machine.LastCheck,
                LastCalibration = machine.LastCalibration
            };
    }
}
