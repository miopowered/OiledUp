using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One instrument, as the thing standing in front of it needs to see it.
    /// <para>
    /// <b>Why this exists.</b> <see cref="MachineStation"/> and <see cref="MachineActionButton"/> used
    /// to hold a <see cref="MachineInstance"/>, which is a piece of the host's simulation. A client has
    /// no <see cref="LabState"/> — deliberately, and permanently (see
    /// <see cref="LabRuntime.SimulatesLocally"/>) — so on a client those components found nothing and
    /// switched themselves off. The instrument then had no prompt, no status light and no display, and
    /// a connected player could not so much as read how long a run had left.
    /// </para>
    /// <para>
    /// The fix is not to hand a client a lab. It is to notice that <i>drawing</i> an instrument needs
    /// far less than simulating one: a name, whether it is busy, how long is left, and the two §5.2 /
    /// §5.3 tells. All of that is either replicated in <c>MachineView</c> or looked up in the
    /// <see cref="ContentCatalog"/> both sides already ship. So this interface is the shape of "what
    /// the world layer may know about an instrument", and it has two implementations — one over the
    /// host's live object, one over a replicated snapshot — which the world layer cannot tell apart.
    /// </para>
    /// <para>
    /// <b>Nothing here decides anything.</b> Every member is a read, and the answers feed prompts and
    /// pixels only. Whether an action is <i>allowed</i> is still settled once, on the host, by
    /// <see cref="LabCommandExecutor"/> — the client half stays optimistic on purpose, because a rule
    /// duplicated on the drawing side is a rule that can drift from the one that is enforced. See
    /// <see cref="LabCommands"/> for why an optimistic prompt is safe.
    /// </para>
    /// </summary>
    public interface IMachineView
    {
        /// <summary>The placed instrument's id, matching <c>LabRuntime.installedMachineIds</c>.</summary>
        string InstanceId { get; }

        /// <summary>
        /// The definition. Content, not state: run time, sample volume, display name and what the
        /// instrument can detect are all shipped in every process, so a client resolves this from its
        /// own catalog rather than being sent a copy that could go stale against the tables.
        /// </summary>
        MachineDef Def { get; }

        /// <summary>The instrument's name, or a neutral stand-in before the definition resolves.</summary>
        string DisplayName { get; }

        bool IsRunning { get; }

        /// <summary>True when there is no vial in it. Says nothing about <i>which</i> vial when there is.</summary>
        bool IsEmpty { get; }

        /// <summary>
        /// A sample run has finished and its vial is still in the instrument — see
        /// <see cref="MachineInstance.HasResultWaiting"/>. Read rather than remembered so that every
        /// player at the machine is offered the same thing.
        /// </summary>
        bool HasResultWaiting { get; }

        /// <summary>Seconds left on whatever is running. Zero when idle.</summary>
        float SecondsRemaining { get; }

        /// <summary>Fraction of the current run completed, for a progress bar. Zero when idle.</summary>
        float Progress { get; }

        /// <summary>How long a sample run takes here, after the testing time scale.</summary>
        float RunSeconds { get; }

        /// <summary>How long a recalibration occupies the instrument (§5.3).</summary>
        float CalibrationSeconds { get; }

        /// <summary>
        /// Whether a certificate from <paramref name="day"/> is on file. §5.3 only lets a
        /// recalibration proceed against a standard run today, so a button greys itself out on this
        /// rather than offering an action the host is about to refuse.
        /// </summary>
        bool HasFreshCheck(int day);

        /// <summary>
        /// Would this instrument take that vial?
        /// <para>
        /// Advisory, and knowingly incomplete on a client — see the implementations. The authoritative
        /// answer is <see cref="MachineInstance.CanAccept"/>, re-run by the executor when the request
        /// lands, and its refusal is the sentence the player is actually shown.
        /// </para>
        /// </summary>
        LoadRefusal CanAccept(SampleState sample);
    }
}
