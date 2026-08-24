using System.Collections.Generic;
using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The crate that arrives each morning. Holds a physical vial per sample and hands them out
    /// one at a time — you have one pair of hands, which is where queue pressure starts.
    /// <para>
    /// The bottles get into it two ways and the crate does not care which. On a process that
    /// simulates, <see cref="OnDayStarted"/> spawns one per delivered sample. On a client there is no
    /// <c>LabState</c> to read, and <see cref="VialReconciler"/> parents the same prefab into the same
    /// slots off the replicated record. Everything below counts what is physically in the slots, so
    /// both arrive at the same crate.
    /// </para>
    /// </summary>
    public sealed class IntakeCrate : Interactable, IVialSlots
    {
        /// <summary>
        /// What <c>SampleLocation.InCrate</c> names this crate, and what the host locates when it
        /// checks that a player is standing at it.
        /// </summary>
        public const string FixtureId = "intake";

        [SerializeField] private Transform slotRoot;
        [SerializeField] private int columns = 4;
        [SerializeField] private float slotSpacing = 0.11f;

        private readonly List<Transform> slots = new();

        // Registered with its slots, not just its position: a client resolving InCrate("intake", 3)
        // has the id and nothing else to go on. See LabRuntime.RegisterFixture.
        private void OnEnable() => LabRuntime.RegisterFixture(FixtureId, transform, this);

        private void OnDisable() => LabRuntime.ForgetFixture(FixtureId, transform);

        private void Start()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab == null) return;

            lab.Lab.DayStarted += OnDayStarted;
            if (lab.Lab.DayInProgress) OnDayStarted(lab.Lab.Day);
        }

        private void OnDestroy()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab != null) lab.Lab.DayStarted -= OnDayStarted;
        }

        /// <summary>
        /// Unpack the morning delivery. Host-side only — it reads <c>LabState</c> directly, and a
        /// client has none by construction (<see cref="LabRuntime.SimulatesLocally"/>).
        /// <para>
        /// Each bottle goes in the slot the sample's own location names, so the crate a client builds
        /// out of the replicated record is arranged the same way as the host's. Two players describing
        /// "the one at the front left" have to be talking about the same bottle.
        /// </para>
        /// </summary>
        private void OnDayStarted(int day)
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab == null) return;

            foreach (var sample in lab.Lab.Samples.All)
            {
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;
                if (lab.PropFor(sample.Id) != null) continue;

                int index = sample.Location.SlotIndex;
                if (index < 0) index = FreeSlot();

                lab.SpawnVial(sample, Slot(index));
            }
        }

        // -- IVialSlots -------------------------------------------------------------------------------

        /// <summary>
        /// Grows on demand. A crate does not have a fixed number of holes in it the way a rack does —
        /// how many bottles turn up is the contract's business, and §10 scales that per day.
        /// </summary>
        public Transform Slot(int index)
        {
            if (index < 0) index = 0;

            while (slots.Count <= index)
            {
                int i = slots.Count;
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(slotRoot != null ? slotRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * slotSpacing,
                    0f,
                    (i / columns) * slotSpacing);
                slots.Add(go.transform);
            }
            return slots[index];
        }

        public int FreeSlot()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (VialSlot.Occupant(slots[i]) == null) return i;
            }
            return slots.Count;   // never full: Slot() will build the next one
        }

        public int SlotOf(Transform prop) => VialSlot.IndexOf(slots, prop);

        // -- Interaction ------------------------------------------------------------------------------

        /// <summary>How many bottles are still sitting in the crate, counted off the slots themselves.</summary>
        public int Remaining
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (VialSlot.Occupant(slots[i]) is VialProp) n++;
                }
                return n;
            }
        }

        /// <summary>The next bottle out, or null if there is nothing left. The crate picks, not you.</summary>
        private VialProp Next()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (VialSlot.Occupant(slots[i]) is VialProp vial) return vial;
            }
            return null;
        }

        public override string Prompt(PlayerInteractor player)
        {
            if (!player.InventoryHasSpace) return "Inventory full";
            return Remaining > 0
                ? $"Take next sample ({Remaining} in crate)"
                : "Crate empty";
        }

        public override bool CanInteract(PlayerInteractor player) =>
            player.InventoryHasSpace && Remaining > 0;

        /// <summary>
        /// Hand out the next vial. The crate picks which one — one pair of hands, one bottle at a
        /// time — but taking it out is §5.1's unload step and belongs to the host, so this asks and
        /// only empties its own slot when the answer comes back. With four players reaching into the
        /// same crate, the host is also the only thing that can stop two of them leaving with the
        /// same sample.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            var vial = Next();
            if (vial == null) return;

            // Nothing is removed from a list here: the slot is empty once the prop is in a hand, and
            // the crate counts its slots. That is also what makes the crate right on a client, where
            // the bottles were put there by the reconciler and no list was ever built.
            LabCommands.Attempt(player, LabCommand.TakeVial(vial.SampleId), _ =>
            {
                player.TryCarry(vial);
                ReadLabelAloud(player, vial);
            });
        }

        /// <summary>
        /// Reads the paper label out loud, because the tag has to be transcribed at the terminal from
        /// memory or from a second look at the vial. That transcription is where §5.1's mis-logging
        /// comes from, so the tag is stated once, here.
        /// </summary>
        private static void ReadLabelAloud(PlayerInteractor player, VialProp vial)
        {
            var sample = LabRuntime.Instance != null ? LabRuntime.Instance.SampleFor(vial.SampleId) : null;

            if (sample == null)
            {
                // A client has the bottle and the label on it, and none of the paperwork behind it.
                // The label is the half §5.1 turns on — a mis-log is only catchable because the tag is
                // still on the glass — so a client that said nothing here would be a client that could
                // not check its own booking-in. The profile and the hours are the host's to know.
                player.Say($"{vial.SampleId} — {vial.Label}. Book it in at the terminal.", 5f);
                return;
            }

            player.Say($"{sample.Id} — {sample.EquipmentTag} — {sample.Profile.DisplayName}, " +
                       $"{sample.HoursSinceOilChange:F0} h on the oil." +
                       (string.IsNullOrEmpty(sample.FieldTechNote) ? "" : $" \"{sample.FieldTechNote}\"") +
                       " Book it in at the terminal.",
                5f);
        }
    }
}
