using System.Collections.Generic;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// <see cref="ILabView"/> over this process's own <see cref="LabState"/>.
    /// <para>
    /// Forwarding, like <see cref="HostMachineView"/>, with one piece of bookkeeping: the per-machine
    /// adapters are built once and cached. <c>Interactable.Prompt</c> runs every frame a player is
    /// looking at a station, so allocating a wrapper per lookup would put a few hundred bytes a second
    /// of garbage on the floor to answer a question whose answer never changes.
    /// </para>
    /// </summary>
    public sealed class HostLabView : ILabView
    {
        private readonly LabState lab;
        private readonly Dictionary<string, HostMachineView> machines = new();

        public HostLabView(LabState lab) => this.lab = lab;

        /// <summary>The wrapped state, so <see cref="LabRuntime"/> can tell whose view this is.</summary>
        public LabState Lab => lab;

        public int Day => lab.Day;

        public float DaySecondsRemaining => lab.DaySecondsRemaining;

        public bool DayInProgress => lab.DayInProgress;

        public bool ShiftOver => lab.ShiftOver;

        public bool IsRunOver => lab.IsRunOver;

        public float Money => lab.Economy.Money;

        public float Reputation => lab.Economy.Reputation;

        public float SolventUnits => lab.Economy.SolventUnits;

        public int ReferenceStandards => lab.Economy.ReferenceStandards;

        public float CalibrationCost => lab.Tuning.CalibrationCost;

        public int OpenSampleCount
        {
            get
            {
                int open = 0;
                foreach (var s in lab.Samples.All)
                {
                    if (!s.FiledVerdict.HasValue) open++;
                }
                return open;
            }
        }

        /// <summary>Always true: a process that simulates is the process that spawned the bottles.</summary>
        public bool HasVialProps => true;

        public IMachineView Machine(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            if (machines.TryGetValue(instanceId, out var cached)) return cached;

            var instance = lab.FindMachine(instanceId);
            if (instance == null) return null;

            var view = new HostMachineView(instance);
            machines[instanceId] = view;
            return view;
        }
    }
}
