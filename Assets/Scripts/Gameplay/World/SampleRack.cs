using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A slotted rack you can set vials into. This is <see cref="Chemistry.SampleLocationKind.OnSurface"/>
    /// made physical (§3.2).
    /// <para>
    /// Without somewhere to put a vial down, one pair of hands and four instruments deadlocks the
    /// moment every machine is busy: you cannot free a machine, and you cannot reach the terminal,
    /// because both need empty hands. Racks are what turn "one pair of hands" from a softlock into
    /// the logistics problem §5.5 is actually about.
    /// </para>
    /// Taking a vial back out needs no code here — <see cref="VialProp"/> is itself an
    /// <see cref="Interactable"/>, so the player targets the specific vial they want.
    /// </summary>
    public sealed class SampleRack : Interactable, IVialSlots
    {
        /// <summary>
        /// The rack a vial goes back to when nobody chose where to put it — a dropped player's, in
        /// particular (see <c>PlayerSession</c>). Named rather than found, so the host and the world
        /// agree on the destination without a scene lookup.
        /// </summary>
        public const string DefaultRackId = "rack";

        [SerializeField] private string rackId = DefaultRackId;
        [SerializeField] private Transform slotRoot;
        [SerializeField] private int slotCount = 8;
        [SerializeField] private int columns = 4;
        [SerializeField] private float spacing = 0.12f;

        private readonly List<Transform> slots = new();

        public string RackId => rackId;

        /// <summary>
        /// Build the holes, once, on whichever call gets here first.
        /// <para>
        /// Lazy rather than in <c>Awake</c> because <see cref="LabRuntime.SlotsFor"/> now hands this
        /// rack to <see cref="VialReconciler"/> the moment it registers, and "the slots are built by a
        /// lifecycle method that has already run" is a promise this component would rather not depend
        /// on — Unity does not run <c>Awake</c> in edit mode at all, which is where the reconciler is
        /// tested.
        /// </para>
        /// </summary>
        private void EnsureSlots()
        {
            if (slots.Count > 0 || slotCount <= 0) return;

            for (int i = 0; i < slotCount; i++)
            {
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(slotRoot != null ? slotRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * spacing,
                    0f,
                    (i / columns) * spacing);
                slots.Add(go.transform);
            }
        }

        // Announced so the host can tell whether a player is actually standing at this rack when they
        // ask to put something down in it, and with its slots so a client can resolve the rack#N a
        // put-down was recorded against. See LabRuntime.RegisterFixture.
        private void OnEnable()
        {
            EnsureSlots();
            LabRuntime.RegisterFixture(rackId, transform, this);
        }

        private void OnDisable() => LabRuntime.ForgetFixture(rackId, transform);

        // -- IVialSlots -------------------------------------------------------------------------------
        //
        // A rack has a fixed number of holes, so Slot() clamps rather than growing. What lives in each
        // is read off the transform — see VialSlot for why there is no list of occupants any more.

        public Transform Slot(int index)
        {
            EnsureSlots();
            if (slots.Count == 0) return transform;
            return slots[Mathf.Clamp(index, 0, slots.Count - 1)];
        }

        public int FreeSlot()
        {
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                if (VialSlot.Occupant(slots[i]) == null) return i;
            }
            return -1;
        }

        public int SlotOf(Transform prop)
        {
            EnsureSlots();
            return VialSlot.IndexOf(slots, prop);
        }

        // -- Shelf space ------------------------------------------------------------------------------

        /// <summary>
        /// How many holes are empty. Counts anything carryable, not just vials: a results slip you
        /// have not walked to the desk yet needs somewhere to live too, and it competes for the same
        /// shelf space.
        /// </summary>
        public int FreeSlots
        {
            get
            {
                EnsureSlots();
                int n = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (VialSlot.Occupant(slots[i]) == null) n++;
                }
                return n;
            }
        }

        /// <summary>The first slot with nothing in it, or -1 when the rack is full.</summary>
        public int NextFreeSlot() => FreeSlot();

        /// <summary>
        /// Park an item in a slot. Purely local: where a vial <i>is</i> belongs to the host and is
        /// written by <see cref="LabCommandExecutor"/> when it accepts the put-down. This only moves
        /// the prop, which is also what makes it the right thing to call when the host has already
        /// decided — a dropped player's vial going back on the rack (§M4) takes the same route.
        /// </summary>
        public bool TryPlace(Carryable item, int slot)
        {
            if (item == null) return false;
            EnsureSlots();

            if (slot < 0 || slot >= slots.Count)
            {
                slot = FreeSlot();
            }
            else
            {
                // Already in that hole is not a reason to move it. On a client the reconciler can put
                // the prop where the host said before this callback runs, and treating the item as
                // its own obstruction would bounce it into the next slot along.
                var occupant = VialSlot.Occupant(slots[slot]);
                if (occupant != null && occupant != item) slot = FreeSlot();
            }

            if (slot < 0) return false;

            item.AttachTo(slots[slot], interactable: true);
            return true;
        }

        /// <inheritdoc cref="TryPlace(Carryable,int)"/>
        public bool TryPlace(Carryable item) => TryPlace(item, -1);

        public override string Prompt(PlayerInteractor player)
        {
            int free = FreeSlots;

            if (player.Carried == null)
            {
                return free == slots.Count
                    ? "Rack — empty"
                    : $"Rack — {slots.Count - free} sample{(slots.Count - free == 1 ? "" : "s")}. " +
                      "Look at one to take it.";
            }

            return free > 0 ? $"Set down in rack ({free} free)" : "Rack full";
        }

        public override bool CanInteract(PlayerInteractor player) =>
            player.Carried != null && FreeSlots > 0;

        public override void Interact(PlayerInteractor player)
        {
            if (player.Carried == null) return;

            var item = player.Carried;

            // The slot is chosen here because the rack is the thing that knows which of its holes are
            // full, and it is sent along so the host records the same shelf the player is looking at.
            int slot = FreeSlot();
            if (slot < 0) return;

            LabCommands.Attempt(player, LabCommand.PutDown(rackId, slot), _ =>
            {
                if (!TryPlace(item, slot)) return;
                player.ReleaseCarried();
                player.Say($"{item.DisplayName} set down.", 2f);
            });
        }
    }
}
