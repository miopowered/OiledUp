using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The lab, as the room needs to see it: the clock, the books, and the instruments by id.
    /// <para>
    /// The companion to <see cref="IMachineView"/>, and the same argument. A prompt that has to say
    /// "shift over — no new runs" or grey out a £140 recalibration needs the day and the balance, and
    /// on a client both of those live in replicated views rather than in a <see cref="LabState"/>. So
    /// the world layer asks this, <see cref="LabView.Current"/> answers with whichever implementation
    /// this process has, and no call site contains the word "client".
    /// </para>
    /// <para>
    /// <b>Read-only by construction.</b> There is no method here that changes anything; every mutation
    /// in the game is a <see cref="LabCommand"/>. That is what makes it safe for a client to hold —
    /// and it is the reason this is a separate interface from <see cref="ILabStations"/>, which the
    /// host's validator uses and a client has no business implementing.
    /// </para>
    /// </summary>
    public interface ILabView
    {
        /// <summary>1-based. Zero before the first day begins.</summary>
        int Day { get; }

        float DaySecondsRemaining { get; }

        bool DayInProgress { get; }

        /// <summary>Instruments refuse to start new runs. Anything already running still finishes.</summary>
        bool ShiftOver { get; }

        /// <summary>Contract finished, or the money ran out (§1.2).</summary>
        bool IsRunOver { get; }

        float Money { get; }

        float Reputation { get; }

        /// <summary>Solvent left for flushing (§5.2).</summary>
        float SolventUnits { get; }

        /// <summary>Certified ampoules left (§5.3).</summary>
        int ReferenceStandards { get; }

        /// <summary>
        /// What a recalibration costs. Part of the run's balance rather than of its state, but a
        /// button that says the price has to get it from somewhere, and guessing it from a default
        /// <see cref="EconomyTuning"/> would silently disagree with the host the day one is tuned.
        /// </summary>
        float CalibrationCost { get; }

        /// <summary>Samples that have arrived and have no verdict filed yet.</summary>
        int OpenSampleCount { get; }

        /// <summary>
        /// Whether physical vials exist in this process.
        /// <para>
        /// False on a joined client. §3.2 makes a vial a local prop rather than a NetworkObject — 200+
        /// of them would drown the connection — and nothing replicates <c>SampleLocation</c> yet, so a
        /// client's crate and racks are empty however full the host's are. Call sites read this to
        /// <i>say so</i>: hard rule 3 forbids showing a player a shelf that is only empty because the
        /// bottles did not travel.
        /// </para>
        /// </summary>
        bool HasVialProps { get; }

        /// <summary>The instrument placed under that id, or null if this process has not heard of it.</summary>
        IMachineView Machine(string instanceId);
    }
}
