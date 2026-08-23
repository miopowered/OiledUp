using System.Collections.Generic;
using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A slotted rack you can set vials into. This is <see cref="SampleLocationKind.OnSurface"/>
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
    public sealed class SampleRack : Interactable
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

        // Holds anything carryable, not just vials: a results slip you have not walked to the desk
        // yet needs somewhere to live too, and it competes for the same shelf space.
        private readonly List<Carryable> occupants = new();

        public string RackId => rackId;

        private void Awake()
        {
            for (int i = 0; i < slotCount; i++)
            {
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(slotRoot != null ? slotRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * spacing,
                    0f,
                    (i / columns) * spacing);
                slots.Add(go.transform);
                occupants.Add(null);
            }
        }

        // Announced so the host can tell whether a player is actually standing at this rack when they
        // ask to put something down in it. See LabRuntime.RegisterFixture.
        private void OnEnable() => LabRuntime.RegisterFixture(rackId, transform);

        private void OnDisable() => LabRuntime.ForgetFixture(rackId, transform);

        /// <summary>
        /// Drop entries whose vial has been taken elsewhere. The player removes a vial by targeting
        /// it directly, so the rack never gets told — cheaper to notice than to wire a callback
        /// through every possible destination.
        /// </summary>
        private void Compact()
        {
            for (int i = 0; i < occupants.Count; i++)
            {
                var v = occupants[i];
                if (v == null) continue;
                if (v.transform.parent != slots[i]) occupants[i] = null;
            }
        }

        public int FreeSlots
        {
            get
            {
                Compact();
                int n = 0;
                foreach (var o in occupants)
                {
                    if (o == null) n++;
                }
                return n;
            }
        }

        /// <summary>The first slot with nothing in it, or -1 when the rack is full.</summary>
        public int NextFreeSlot()
        {
            Compact();
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == null) return i;
            }
            return -1;
        }

        /// <summary>
        /// Park an item in a slot. Purely local: where a vial <i>is</i> belongs to the host and is
        /// written by <see cref="LabCommandExecutor"/> when it accepts the put-down. This only moves
        /// the prop, which is also what makes it the right thing to call when the host has already
        /// decided — a dropped player's vial going back on the rack (§M4) takes the same route.
        /// </summary>
        public bool TryPlace(Carryable item, int slot)
        {
            if (item == null) return false;
            Compact();

            if (slot < 0 || slot >= occupants.Count || occupants[slot] != null) slot = NextFreeSlot();
            if (slot < 0) return false;

            occupants[slot] = item;
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
                // A joined client's rack holds nothing because vials are local props and none were
                // spawned here (§3.2 — see ILabView.HasVialProps), not because the host's rack is
                // bare. Setting something down still works: the manual is a scene prop and exists in
                // every process, which is why only the empty-handed branch has to explain itself.
                if (LabView.VialsMissingHere) return $"Rack — {LabView.VialsAreHostOnly}";

                return free == slotCount
                    ? "Rack — empty"
                    : $"Rack — {slotCount - free} sample{(slotCount - free == 1 ? "" : "s")}. Look at one to take it.";
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
            int slot = NextFreeSlot();
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
