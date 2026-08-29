using System.Collections.Generic;
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
    /// <b>Host-side.</b> It reads <see cref="LabState.Deliveries"/> directly, and a client has no lab
    /// by construction (<see cref="LabRuntime.SimulatesLocally"/>), so on a joined client this
    /// component registers the bay's position and then does nothing. Cartons are not on the wire yet —
    /// they need a view alongside <c>VialView</c> before a second player can see one, which is a
    /// separate change to <c>Residue.Net</c>. Everything below is written so that when it lands, the
    /// carton props can be placed by a reconciler and this file does not move.
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
            ShowTruck(lab.Deliveries.TruckAtBay);
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
        /// box up, which is a command result rather than a delivery event.
        /// </summary>
        private void Update()
        {
            if (lab == null) return;
            ShowTruck(lab.Deliveries.TruckAtBay);
        }

        // -- Announcements ------------------------------------------------------------------------------

        private void OnDeliveryDue(DeliveryBay bay) =>
            Announce($"Delivery due at the bay in about {Mathf.RoundToInt(bay.SecondsUntilArrival)}s.");

        private void OnDeliveryArrived(IReadOnlyList<Carton> arrived)
        {
            BuildMissingProps();

            if (arrived == null || arrived.Count == 0) return;

            string first = arrived[0].JobNumber;
            string rest = arrived.Count == 1 ? string.Empty : $" and {arrived.Count - 1} more";

            Announce($"Delivery at the bay — carton {first}{rest}. It needs carrying in.");
        }

        /// <summary>
        /// #30's "before the bay blocks", said out loud. The truck does not leave and nothing is lost;
        /// what is lost is the shift time spent unloading two deliveries at once later. Hard rule 3
        /// does not permit that cost to arrive as a surprise, so it is stated the moment it applies and
        /// the truck sitting outside with boxes on it is the standing reminder.
        /// </summary>
        private void OnDeliveryHeld(DeliveryBay bay) =>
            Announce($"Bay full — {bay.OnTheRoadCount} carton(s) still on the truck. " +
                     "Carry one in and the rest come off.");

        /// <summary>
        /// Cardboard does not survive the night (#31). <c>LabState.EndDay</c> has already flattened
        /// whatever was spent; this is the half that removes the objects.
        /// </summary>
        private void OnDayEnded(IReadOnlyList<ConsequenceReport> reports) => RetireDiscarded();

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
