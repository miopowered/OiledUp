using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// <see cref="IMachineView"/> over the host's own <see cref="MachineInstance"/>.
    /// <para>
    /// Pure forwarding, and that is the point: on a process that simulates, the world layer must
    /// behave exactly as it did before the interface existed. Every member below is the expression
    /// that used to sit at the call site, moved one level down and nowhere else.
    /// </para>
    /// Holds the instance rather than copying from it, so the readings are always this frame's.
    /// </summary>
    public sealed class HostMachineView : IMachineView
    {
        private readonly MachineInstance machine;

        public HostMachineView(MachineInstance machine) => this.machine = machine;

        /// <summary>The wrapped instance, for host-only code that needs the real object.</summary>
        public MachineInstance Instance => machine;

        public string InstanceId => machine.InstanceId;

        public MachineDef Def => machine.Def;

        public string DisplayName => machine.Def != null ? machine.Def.DisplayName : "Instrument";

        public bool IsRunning => machine.IsRunning;

        public bool IsEmpty => machine.IsEmpty;

        public bool HasResultWaiting => machine.HasResultWaiting;

        public float SecondsRemaining => machine.SecondsRemaining;

        public float Progress => machine.Progress;

        public float RunSeconds => machine.RunSeconds;

        public float CalibrationSeconds => machine.CalibrationSeconds;

        public bool HasFreshCheck(int day) => machine.HasFreshCheck(day);

        /// <summary>
        /// The real gateway. A null sample here means "no such sample", which is a refusal — unlike on
        /// a client, where it means "cannot be checked from this process".
        /// </summary>
        public LoadRefusal CanAccept(SampleState sample) => machine.CanAccept(sample);
    }
}
