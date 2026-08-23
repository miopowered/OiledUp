using System.Collections.Generic;
using Residue.Gameplay.World;

namespace Residue.Net.Views
{
    /// <summary>
    /// <see cref="ILabView"/> over the views <see cref="LabNetwork"/> replicates. The client half of
    /// the read seam.
    /// <para>
    /// Everything here is a projection of a projection: the host wrote <c>DayView</c>,
    /// <c>EconomyView</c> and the two lists in <see cref="LabNetwork.PublishAll"/>, and this reshapes
    /// them into the vocabulary the room speaks. It adds nothing and computes nothing that the host
    /// did not already decide — which is what stops "the client thinks the shift is over" from
    /// becoming a class of bug (see <c>DayView</c> for why the derived flags travel rather than being
    /// re-derived from the clock).
    /// </para>
    /// <para>
    /// Installed into <see cref="LabView.Replicated"/> on spawn and cleared on despawn, so a station
    /// that outlives the session goes back to reading nothing rather than reading a dead list.
    /// </para>
    /// </summary>
    public sealed class ReplicatedLabView : ILabView
    {
        private readonly LabNetwork network;

        /// <summary>
        /// One adapter per instrument, kept because <c>Interactable.Prompt</c> asks for one every
        /// frame a player is looking at a station. The adapters hold no state of their own beyond a
        /// resolved definition, so caching them cannot make anything stale.
        /// </summary>
        private readonly Dictionary<string, ReplicatedMachineView> machines = new();

        public ReplicatedLabView(LabNetwork network) => this.network = network;

        public int Day => network != null ? network.Day.Day : 0;

        public float DaySecondsRemaining => network != null ? network.Day.SecondsRemaining : 0f;

        public bool DayInProgress => network != null && network.Day.DayInProgress;

        public bool ShiftOver => network != null && network.Day.ShiftOver;

        public bool IsRunOver => network != null && network.Day.IsRunOver;

        public float Money => network != null ? network.Economy.Money : 0f;

        public float Reputation => network != null ? network.Economy.Reputation : 0f;

        public float SolventUnits => network != null ? network.Economy.SolventUnits : 0f;

        public int ReferenceStandards => network != null ? network.Economy.ReferenceStandards : 0;

        public float CalibrationCost => network != null ? network.Economy.CalibrationCost : 0f;

        public int OpenSampleCount
        {
            get
            {
                var list = network != null ? network.Samples : null;
                if (list == null) return 0;

                int open = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].HasVerdict) open++;
                }
                return open;
            }
        }

        /// <summary>
        /// False, and that is the honest answer rather than a placeholder. §3.2 keeps a vial a local
        /// prop and nothing replicates <c>SampleLocation</c>, so this process has no bottles in it at
        /// all — see <see cref="ILabView.HasVialProps"/> for what the world layer does about that.
        /// </summary>
        public bool HasVialProps => false;

        /// <summary>
        /// The adapter for a placed instrument. Never null for a well-formed id, even before the first
        /// publish arrives: the adapter reads the list live, so one built early simply reports an idle
        /// nameless instrument until the snapshot lands and then starts telling the truth.
        /// </summary>
        public IMachineView Machine(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            if (machines.TryGetValue(instanceId, out var cached)) return cached;

            var view = new ReplicatedMachineView(network, instanceId);
            machines[instanceId] = view;
            return view;
        }
    }
}
