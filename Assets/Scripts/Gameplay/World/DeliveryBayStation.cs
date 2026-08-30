using System;
using System.Collections.Generic;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The loading bay: the marked places a carton stands in, and the truck that puts them there
    /// (#30).
    ///
    /// <para>
    /// A plain <c>MonoBehaviour</c> rather than an <see cref="Interactable"/>, like
    /// <see cref="DropSpot"/>. There is nothing to press here — you interact with the boxes standing
    /// on it, and a prompt on the floor would sit between the crosshair and the thing you were aiming
    /// at.
    /// </para>
    ///
    /// <para>
    /// <b>Two halves, and only one of them is host-side.</b> Building the props from
    /// <see cref="LabState.Deliveries"/> and announcing the delivery events are things only a process
    /// with a lab can do; a client's boxes are placed by <see cref="CartonReconciler"/> instead, from
    /// the same rows (#80). What is shared is everything a player standing here can see: the standing
    /// places, and whether the lorry is still outside. Both go through <see cref="CartonFeed"/>, which
    /// answers from this process's own bay or from the wire, so the truck leaves at the same moment on
    /// every screen in the session.
    /// </para>
    /// </summary>
    public sealed class DeliveryBayStation : MonoBehaviour, IVialSlots
    {
        /// <summary>
        /// What <c>Carton.Location</c> names the bay, and what the host locates when it checks that a
        /// player is standing here. Taken from the simulation rather than declared again — see
        /// <see cref="DeliveryBay.BayId"/>.
        /// </summary>
        public const string FixtureId = DeliveryBay.BayId;

        [Tooltip("Parent for the generated standing places. Falls back to this transform.")]
        [SerializeField] private Transform standingRoot;

        [Tooltip("The truck. Starts inactive and is shown only while there is something to unload.")]
        [SerializeField] private GameObject truck;

        [SerializeField] private float spacing = 0.8f;
        [SerializeField] private int columns = 2;

        private readonly List<Transform> standings = new();

        private LabState lab;
        private bool subscribed;
        private bool truckShown;

        // Registered with its places, not just its position: a carton's location is
        // OnSurface("bay", 2) and something has to be able to turn that back into a transform.
        private void OnEnable()
        {
            EnsureStandings();
            LabRuntime.RegisterFixture(FixtureId, transform, this);
            ShowTruck(false);
        }

        private void OnDisable()
        {
            LabRuntime.ForgetFixture(FixtureId, transform);
            Unsubscribe();
        }

        private void Start()
        {
            lab = LabRuntime.Instance != null ? LabRuntime.Instance.Lab : null;
            if (lab == null) return;

            lab.DeliveryDue += OnDeliveryDue;
            lab.DeliveryArrived += OnDeliveryArrived;
            lab.DeliveryHeld += OnDeliveryHeld;
            lab.DayEnded += OnDayEnded;
            subscribed = true;

            // A continued run opens with boxes already standing in the bay — see
            // DeliveryBay.RebuildFrom — and no arrival will ever be announced for them.
            BuildMissingProps();
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (!subscribed || lab == null) return;

            lab.DeliveryDue -= OnDeliveryDue;
            lab.DeliveryArrived -= OnDeliveryArrived;
            lab.DeliveryHeld -= OnDeliveryHeld;
            lab.DayEnded -= OnDayEnded;
            subscribed = false;
        }

        /// <summary>
        /// The truck is here while there is anything left to unload, and gone once there is not.
        /// Polled rather than evented because it goes false as a consequence of the player picking a
        /// box up, which is a command result rather than a delivery event — and because on a client
        /// there is no delivery event to hang it on at all.
        /// </summary>
        private void Update()
        {
            ShowTruck(CartonFeed.TruckAtBay);

            if (lab == null) NoticeArrivals();
        }

        // -- Announcements ------------------------------------------------------------------------------

        private void OnDeliveryDue(DeliveryBay bay) =>
            Announce(PromptStrings.BayDeliveryDue.Format(
                ("seconds", Mathf.RoundToInt(bay.SecondsUntilArrival))));

        private void OnDeliveryArrived(IReadOnlyList<Carton> arrived)
        {
            BuildMissingProps();

            if (arrived == null || arrived.Count == 0) return;

            Announce(Arrival(arrived[0].JobNumber, arrived.Count));
        }

        /// <summary>
        /// One sentence for a single box and another for several (#55). It used to be one sentence
        /// with <c>" and 2 more"</c> spliced into the middle of it, which is the fragment a
        /// translator cannot place — and the same fragment existed twice, here and in
        /// <see cref="NoticeArrivals"/>.
        /// </summary>
        private static string Arrival(string jobNumber, int count) => count <= 1
            ? PromptStrings.BayArrived.Format(("job", jobNumber))
            : PromptStrings.BayArrivedMore.Format(("job", jobNumber), ("count", count - 1));

        /// <summary>
        /// #30's "before the bay blocks", said out loud. The truck does not leave and nothing is lost;
        /// what is lost is the shift time spent unloading two deliveries at once later. Hard rule 3
        /// does not permit that cost to arrive as a surprise, so it is stated the moment it applies and
        /// the truck sitting outside with boxes on it is the standing reminder.
        /// </summary>
        private void OnDeliveryHeld(DeliveryBay bay) =>
            Announce(PromptStrings.BayFull.Format(("count", bay.OnTheRoadCount)));

        /// <summary>
        /// Cardboard does not survive the night (#31). <c>LabState.EndDay</c> has already flattened
        /// whatever was spent; this is the half that removes the objects.
        /// </summary>
        private void OnDayEnded(IReadOnlyList<ConsequenceReport> reports) => RetireDiscarded();

        // -- Announcements, on a process with no events to hear ------------------------------------------

        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private bool primed;

        /// <summary>
        /// Tell a joined player the lorry has turned up.
        /// <para>
        /// <c>LabState.DeliveryArrived</c> is an event on a lab a client does not have, so without this
        /// a delivery lands in silence while the player is head-down at an instrument — which is
        /// exactly the moment #30 wants them to have to choose. Derived from the boxes appearing rather
        /// than replicated as a message, because the boxes are the fact and a second channel saying the
        /// same thing is a second channel to keep in step.
        /// </para>
        /// <para>
        /// The first snapshot is swallowed. A client that joins mid-shift is told about boxes that have
        /// been standing there for ten minutes, and greeting it with "delivery at the bay" would be an
        /// announcement about the past. <see cref="CartonFeed.HasSpoken"/> is what makes that the
        /// <i>first</i> snapshot rather than the first frame — the room exists before the session does.
        /// </para>
        /// </summary>
        private void NoticeArrivals()
        {
            if (!CartonFeed.HasSpoken) return;

            bool first = !primed;
            primed = true;

            int arrived = 0;
            string firstJob = null;

            var known = CartonFeed.Known;
            for (int i = 0; i < known.Count; i++)
            {
                var carton = known[i];
                if (carton.Stage != CartonStage.Delivered) continue;
                if (!seen.Add(carton.Id) || first) continue;

                arrived++;
                firstJob ??= carton.JobNumber;
            }

            if (arrived == 0) return;

            Announce(Arrival(firstJob, arrived));
        }

        /// <summary>
        /// Say something to everyone in this process. Found rather than wired because a lab may have
        /// one player or four and the bay has no business knowing which — the same reason
        /// <see cref="LabHud"/> resolves its own interactor. Twice a day at most, so the search costs
        /// nothing worth avoiding.
        /// </summary>
        private static void Announce(string message)
        {
            var players = FindObjectsByType<PlayerInteractor>();
            for (int i = 0; i < players.Length; i++) players[i].Say(message, 6f);

            Debug.Log($"[DeliveryBay] {message}");
        }

        // -- Props --------------------------------------------------------------------------------------
        //
        // Host-side only. A client's boxes are built by CartonReconciler from the replicated rows, which
        // is the same argument VialReconciler makes: one prop system with two callers, and the caller is
        // whichever side actually knows what exists.

        /// <summary>
        /// Build a box for every carton the host says has been delivered and has no prop yet.
        /// Idempotent, so an arrival, a restore and a second arrival all take the same path.
        /// </summary>
        private void BuildMissingProps()
        {
            var runtime = LabRuntime.Instance;
            if (runtime == null || lab == null) return;

            foreach (var carton in lab.Deliveries.Cartons)
            {
                if (carton.Stage != CartonStage.Delivered) continue;
                if (runtime.CartonPropFor(carton.Id) != null) continue;

                var socket = PropSockets.For(carton.Location, null, out bool reachable);
                if (socket == null) continue;   // still on the truck, or a place this scene lacks

                runtime.SpawnCarton(carton, socket, reachable);
            }
        }

        private void RetireDiscarded()
        {
            var runtime = LabRuntime.Instance;
            if (runtime == null || lab == null) return;

            foreach (var carton in lab.Deliveries.Cartons)
            {
                if (carton.Stage == CartonStage.Discarded) runtime.RetireCarton(carton.Id);
            }
        }

        private void ShowTruck(bool visible)
        {
            if (truck == null || truckShown == visible) return;
            truckShown = visible;
            truck.SetActive(visible);
        }

        // -- IVialSlots ---------------------------------------------------------------------------------
        //
        // The bay holds cartons rather than vials, but a place to put a thing down is a place to put a
        // thing down: PropSockets already resolves OnSurface(id, slot) through this interface and a
        // second one would be the same table twice.

        private void EnsureStandings()
        {
            if (standings.Count > 0) return;

            for (int i = 0; i < DeliveryBay.DefaultCapacity; i++)
            {
                var go = new GameObject($"Standing_{i:D2}");
                go.transform.SetParent(standingRoot != null ? standingRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * spacing,
                    0f,
                    (i / columns) * spacing);
                standings.Add(go.transform);
            }
        }

        public Transform Slot(int index)
        {
            EnsureStandings();
            if (standings.Count == 0) return transform;
            return standings[Mathf.Clamp(index, 0, standings.Count - 1)];
        }

        public int FreeSlot()
        {
            EnsureStandings();
            for (int i = 0; i < standings.Count; i++)
            {
                if (VialSlot.Occupant(standings[i]) == null) return i;
            }
            return -1;
        }

        public int SlotOf(Transform prop)
        {
            EnsureStandings();
            return VialSlot.IndexOf(standings, prop);
        }
    }
}
