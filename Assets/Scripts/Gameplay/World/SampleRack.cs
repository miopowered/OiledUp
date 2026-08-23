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
        [SerializeField] private string rackId = "rack";
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

        public bool TryPlace(Carryable item)
        {
            if (item == null) return false;
            Compact();

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != null) continue;

                occupants[i] = item;
                item.AttachTo(slots[i], interactable: true);

                if (item is VialProp vial)
                {
                    var sample = LabRuntime.Instance?.SampleFor(vial.SampleId);
                    if (sample != null)
                        SampleLifecycle.TryMove(sample, SampleLocation.OnSurface(rackId, i), out _);
                }
                return true;
            }
            return false;
        }

        public override string Prompt(PlayerInteractor player)
        {
            int free = FreeSlots;

            if (player.Carried == null)
                return free == slotCount
                    ? "Rack — empty"
                    : $"Rack — {slotCount - free} sample{(slotCount - free == 1 ? "" : "s")}. Look at one to take it.";

            return free > 0 ? $"Set down in rack ({free} free)" : "Rack full";
        }

        public override bool CanInteract(PlayerInteractor player) =>
            player.Carried != null && FreeSlots > 0;

        public override void Interact(PlayerInteractor player)
        {
            if (player.Carried == null) return;

            var item = player.Carried;
            if (!TryPlace(item)) return;

            player.ReleaseCarried();
            player.Say($"{item.DisplayName} set down.", 2f);
        }
    }
}
