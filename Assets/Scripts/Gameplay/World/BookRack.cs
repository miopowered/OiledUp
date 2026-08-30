using System;
using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The case the reference manuals live in, and the one place a manual can be put back.
    /// <para>
    /// A book was the one carryable with nowhere to go. Racks take vials, instruments take vials,
    /// the desk takes paper — a manual you picked up to check a threshold occupied one of three
    /// slots until the run ended. §6.1 assumes looking something up costs the walk and the shift
    /// time, which is a cost worth paying; carrying the book forever afterwards is not, and it is not
    /// a cost the player chose.
    /// </para>
    /// <para>
    /// <b>The pigeonholes are the affordance.</b> A player holding a manual should be able to tell
    /// where it goes without being told, and an empty cell in a case of full ones says "one is out"
    /// in a way a flat shelf cannot. Each manual remembers the cell it came from and goes back to it,
    /// so the case reads the same at the end of a shift as at the start.
    /// </para>
    /// <para>
    /// <b>Deliberately not an <see cref="IVialSlots"/>.</b> Registering slots is what lets a
    /// replicated <c>SampleLocation</c> resolve to a hole in a fixture, and a sample filed into a
    /// bookcase is a sample in a place the player has no reason to look. Manuals are unreplicated
    /// scene props, so the rack needs none of it; registering position-only keeps the reach check
    /// working and keeps oil out of the paperwork.
    /// </para>
    /// </summary>
    public sealed class BookRack : Interactable
    {
        /// <summary>
        /// What a <c>PutDown</c> aimed at this case names, and what the host locates when it checks
        /// that the player asking is actually standing at it.
        /// </summary>
        public const string FixtureId = "bookrack";

        /// <summary>
        /// Enough cells for every manual in the lab at once — three general references plus one per
        /// instrument — so returning one is never refused for want of somewhere to put it.
        /// </summary>
        public const int SlotCount = 8;

        private const int Columns = 2;
        private const float ColumnSpacing = 0.43f;
        private const float RowSpacing = 0.20f;

        /// <summary>
        /// Manuals lie tipped back rather than flat, so what faces the room is a cover instead of a
        /// 28 mm edge. The interaction ray is 6 px wide at the crosshair (§2.6) and a book you cannot
        /// reliably aim at is a book you cannot take. 28° is as far back as a 240 mm manual tips
        /// inside a 180 mm cell.
        /// </summary>
        public static readonly Vector3 SlotTilt = new(28f, 0f, 0f);

        [SerializeField] private string rackId = FixtureId;
        [SerializeField] private Transform slotRoot;

        private readonly List<Transform> slots = new();

        /// <summary>
        /// Which cell each manual belongs in, by <see cref="ReferenceBook.InventoryId"/>. Learned from
        /// where the case was authored rather than assigned, so the layout lives in the scene the
        /// player sees and not in a table that could disagree with it.
        /// </summary>
        private readonly Dictionary<string, int> homes = new();

        public string RackId => rackId;

        /// <summary>
        /// Where the cells sit relative to <c>slotRoot</c>. Public and static because
        /// <c>LabSceneBuilder</c> authors the same cells into the saved scene — one formula, so the
        /// holes in the mesh and the holes the component believes in cannot drift apart.
        /// </summary>
        public static Vector3 SlotOffset(int index) => new(
            (index % Columns - (Columns - 1) * 0.5f) * ColumnSpacing,
            index / Columns * RowSpacing,
            0f);

        /// <summary>
        /// Adopt the authored cells, then build any that are missing.
        /// <para>
        /// Adoption rather than creation is what lets the scene builder put a manual in the cell it
        /// will live in. A component that built its own cells on the first frame of play would leave
        /// the saved scene showing eight books stacked at the rack's origin, which reads as a broken
        /// scene to anyone opening it.
        /// </para>
        /// </summary>
        private void EnsureSlots()
        {
            if (slots.Count >= SlotCount) return;

            var parent = slotRoot != null ? slotRoot : transform;

            for (int i = 0; i < parent.childCount && slots.Count < SlotCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("Slot_", StringComparison.Ordinal)) slots.Add(child);
            }

            while (slots.Count < SlotCount)
            {
                int i = slots.Count;
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = SlotOffset(i);
                go.transform.localRotation = Quaternion.Euler(SlotTilt);
                slots.Add(go.transform);
            }
        }

        // Position only, no slots — see the type doc. Announced so the host can tell whether the
        // player asking to shelve something is standing here rather than across the room.
        private void OnEnable()
        {
            EnsureSlots();
            LabRuntime.RegisterFixture(rackId, transform);
        }

        private void OnDisable() => LabRuntime.ForgetFixture(rackId, transform);

        /// <summary>
        /// File anything the scene left leaning on the case, and remember where everything started.
        /// <para>
        /// Runs identically on a host and on a client because it reads the scene rather than the lab:
        /// manuals are unreplicated props, and two players must still agree about which shelf the
        /// threshold tables live on when one of them says so out loud.
        /// </para>
        /// </summary>
        private void Start()
        {
            EnsureSlots();

            var loose = GetComponentsInChildren<ReferenceBook>(includeInactive: true);
            foreach (var book in loose)
            {
                int slot = SlotOf(book.transform);
                if (slot < 0) slot = FreeSlot();
                if (slot < 0) continue;

                TryShelve(book, slot);
            }
        }

        // -- Cells -------------------------------------------------------------------------------------

        public Transform Slot(int index)
        {
            EnsureSlots();
            if (slots.Count == 0) return transform;
            return slots[Mathf.Clamp(index, 0, slots.Count - 1)];
        }

        /// <summary>The first empty cell, or -1 when the case is full.</summary>
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

        /// <summary>How many manuals are in the case, counted off the cells themselves.</summary>
        public int Shelved => SlotCount - FreeSlots;

        /// <summary>
        /// The cell this manual came out of if it is still free, otherwise the first empty one. -1
        /// when the case is full.
        /// </summary>
        public int HomeSlotFor(ReferenceBook book)
        {
            EnsureSlots();

            if (book != null && homes.TryGetValue(book.InventoryId, out int home) &&
                home >= 0 && home < slots.Count && VialSlot.Occupant(slots[home]) == null)
            {
                return home;
            }

            return FreeSlot();
        }

        /// <summary>
        /// Park a manual in a cell. Purely local, exactly as <see cref="SampleRack.TryPlace"/> is: the
        /// host has already agreed that the hand is empty, and this only moves the prop.
        /// </summary>
        public bool TryShelve(Carryable item, int slot)
        {
            if (item == null) return false;
            EnsureSlots();

            if (slot < 0 || slot >= slots.Count)
            {
                slot = FreeSlot();
            }
            else
            {
                // Unity's ==, not a null pattern: a destroyed prop is a live C# reference and would
                // read as an occupant that never moves out of the way.
                var occupant = VialSlot.Occupant(slots[slot]);
                if (occupant != null && occupant != item) slot = FreeSlot();
            }

            if (slot < 0) return false;

            item.AttachTo(slots[slot], interactable: true);
            if (item is ReferenceBook book) homes[book.InventoryId] = slot;
            return true;
        }

        // -- Interaction -------------------------------------------------------------------------------

        public override string Prompt(PlayerInteractor player)
        {
            var carried = player != null ? player.Carried : null;

            // Unity's ==, not a null pattern, for the reason TryShelve gives.
            if (carried == null)
            {
                // Three whole sentences rather than one with a count and an "s" spliced into it: a
                // translator cannot inflect a noun they were handed in two pieces (#55).
                int shelved = Shelved;
                if (shelved == 0) return PromptStrings.BookRackEmpty.Text;

                return shelved == 1
                    ? PromptStrings.BookRackOneManual.Text
                    : PromptStrings.BookRackManuals.Format(("count", shelved));
            }

            if (!(carried is ReferenceBook book)) return PromptStrings.BookRackManualsOnly.Text;

            return FreeSlots > 0
                ? PromptStrings.BookRackShelve.Format(("item", book.DisplayName))
                : PromptStrings.BookRackFull.Text;
        }

        public override bool CanInteract(PlayerInteractor player) =>
            player != null && player.Carried is ReferenceBook && FreeSlots > 0;

        /// <summary>
        /// Put the manual back. A request rather than a local move, for the same reason picking one up
        /// is: the host tracks whose hands are full, and a slot freed only on the client that freed it
        /// is a player the host goes on refusing a vial to.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            if (player == null || !(player.Carried is ReferenceBook book)) return;

            int slot = HomeSlotFor(book);
            if (slot < 0) return;

            LabCommands.Attempt(player, LabCommand.PutDown(rackId, slot), _ =>
            {
                // Whatever the host just emptied out of the selected hand, rather than the book this
                // started with — the two differ only if the player changed slots mid-flight, and in
                // that case the host acted on the new selection.
                var placed = player.ReleaseCarried();
                if (placed == null) return;

                if (!TryShelve(placed, slot))
                {
                    player.TryCarry(placed);
                    return;
                }

                player.Say(PromptStrings.BookRackShelved.Format(("item", placed.DisplayName)), 2f);
            });
        }
    }
}
