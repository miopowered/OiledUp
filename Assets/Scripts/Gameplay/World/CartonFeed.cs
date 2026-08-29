using System;
using System.Collections.Generic;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the delivery cartons come from, and the one place that answers "what is this box doing"
    /// for a host and a client alike.
    ///
    /// <para>
    /// The sixth static seam, and the same shape as the five before it:
    /// <see cref="LabCommands.Router"/> is how an action leaves, <see cref="LabView.Replicated"/> is
    /// how the instruments are read, <see cref="VialFeed"/> is where the bottles are,
    /// <see cref="RecordFeed"/> is where the numbers are, <see cref="SlipFeed"/> is where the printed
    /// paper is, and this is where the boxes are. In every case <c>Residue.Gameplay</c> declares the
    /// shape and <c>Residue.Net</c> fills it in, because the assembly dependency runs the other way and
    /// that direction is what keeps ground truth off the wire (CLAUDE.md's assembly diagram).
    /// </para>
    ///
    /// <para>
    /// <b>It has a lookup as well as a snapshot, and that is the difference from the other feeds.</b> A
    /// vial prop draws nothing about itself; a carton does — its prompt says how many bottles are left
    /// in it, its lid says whether it is still taped, and <see cref="CartonProp"/> has to ask that
    /// several times a frame while a player is looking at it. <see cref="TryFind"/> answers from this
    /// process's own bay when it has one and from the reconciler's last snapshot when it does not, so
    /// the prop contains no branch on session state — the argument <see cref="LabView.Current"/> makes,
    /// for the one kind of object that is a container as well as a thing you carry.
    /// </para>
    /// </summary>
    public static class CartonFeed
    {
        /// <summary>
        /// Fill <paramref name="into"/> with every carton this process has been told about and return
        /// true, or return false when this process is not the one being told — a host, single player,
        /// or a client whose session has not spawned yet.
        /// <para>
        /// False and "an empty list" are different answers, for the reason
        /// <see cref="VialFeed.Snapshot"/> gives: empty means the run has no boxes left and anything
        /// still standing in the room should go, false means nothing here should be touched at all.
        /// </para>
        /// </summary>
        public delegate bool Snapshot(List<CartonPlacement> into);

        /// <summary>Installed by <c>Residue.Net</c> at startup. Null in an Editor-only test run.</summary>
        public static Snapshot Source;

        private static readonly List<CartonPlacement> known = new();

        /// <summary>
        /// True once a snapshot has actually arrived. Distinguishes "this client has been told there
        /// are no boxes" from "this client has not been told anything yet", which matters to anybody
        /// deciding whether a box appearing is news (see <see cref="DeliveryBayStation"/>).
        /// </summary>
        public static bool HasSpoken { get; private set; }

        /// <summary>
        /// The boxes this process was last told about. Written once a frame by
        /// <see cref="CartonReconciler"/> and read by the props, so a prompt is a scan of a handful of
        /// rows rather than a fresh trip to the wire.
        /// </summary>
        public static IReadOnlyList<CartonPlacement> Known => known;

        /// <summary>Record a snapshot. Called by <see cref="CartonReconciler"/> and by nothing else.</summary>
        public static void Publish(IReadOnlyList<CartonPlacement> cartons)
        {
            known.Clear();
            HasSpoken = true;

            if (cartons == null) return;
            for (int i = 0; i < cartons.Count; i++) known.Add(cartons[i]);
        }

        /// <summary>
        /// Forget everything. Only for a test tearing down a fake session — the feed is static and
        /// process-wide, and rows left behind would be a box the next test's props resolved an id to.
        /// </summary>
        public static void Reset()
        {
            known.Clear();
            HasSpoken = false;
        }

        /// <summary>
        /// The box under that id as this process can see it: its own bay first, the last snapshot
        /// otherwise.
        /// <para>
        /// The host wins where both exist, for the reason <see cref="LabView.Current"/> gives — a host
        /// does publish these rows, but reading its own snapshot back would put the prompt on a box in
        /// its hands a publish behind the box.
        /// </para>
        /// </summary>
        public static bool TryFind(string cartonId, out CartonPlacement carton)
        {
            carton = default;
            if (string.IsNullOrEmpty(cartonId)) return false;

            var bay = HostBay;
            if (bay != null)
            {
                var box = bay.Find(cartonId);
                if (box == null) return false;

                carton = CartonPlacement.From(box, bay.RemainingIn(box));
                return true;
            }

            for (int i = 0; i < known.Count; i++)
            {
                if (!string.Equals(known[i].Id, cartonId, StringComparison.Ordinal)) continue;

                carton = known[i];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether there is anything left to unload, which is the whole of "is the truck still
        /// outside" (#30). Answered from the same two sources as <see cref="TryFind"/>, so the lorry
        /// leaves at the same moment on every screen in the session.
        /// </summary>
        public static bool TruckAtBay
        {
            get
            {
                var bay = HostBay;
                if (bay != null) return bay.TruckAtBay;

                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i].Stage == CartonStage.OnTheRoad) return true;
                    if (known[i].IsStandingInBay) return true;
                }
                return false;
            }
        }

        /// <summary>This process's own delivery bay, or null on a client and before the lab is built.</summary>
        private static DeliveryBay HostBay
        {
            get
            {
                var runtime = LabRuntime.Instance;
                var lab = runtime != null ? runtime.Lab : null;
                return lab != null ? lab.Deliveries : null;
            }
        }
    }
}
