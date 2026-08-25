using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The patch of bench or floor somebody set something down on, made into a fixture.
    /// <para>
    /// <b>The position is the id.</b> A <c>SampleLocation</c> names its container with a string and a
    /// slot number and carries nothing else across the wire, so a place that exists only because a
    /// player aimed at it has nowhere to put its coordinates except into that string — hence
    /// <c>drop@1.4,0.9,-2.05</c>. That is what keeps a dropped vial reachable on every machine in the
    /// session instead of only on the one that dropped it: every process rebuilds the same transform
    /// from the same id, with nothing extra to replicate and no ordering to agree on first. A named
    /// spot that a client could not resolve would be a sample nobody could pick up, which is the
    /// failure <c>LabNetwork.OnItemReleased</c> exists to prevent.
    /// </para>
    /// <para>
    /// Materialised on demand and reaped once it is empty again, so a shift's worth of drops does not
    /// leave a shift's worth of empty transforms standing in the room.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DropSpot : MonoBehaviour, IVialSlots
    {
        /// <summary>Marks a container id as a drop rather than a placed fixture.</summary>
        public const string Prefix = "drop@";

        /// <summary>
        /// How far apart two things sharing one spot sit. Only reached when two items are recorded
        /// against the same centimetre, which <see cref="ItemDrop"/> already refuses to create.
        /// </summary>
        public const float SlotSpacing = 0.13f;

        /// <summary>
        /// Seconds an empty spot is kept before it is destroyed. Long enough to cover the round trip
        /// between asking the host and parenting the prop, which is the one window in which a spot is
        /// legitimately empty and still wanted.
        /// </summary>
        private const float ReapAfterSeconds = 6f;

        private readonly List<Transform> slots = new();

        private string surfaceId;
        private float emptySince = -1f;

        /// <summary>The container id this spot answers to. Null until <see cref="Bind"/>.</summary>
        public string SurfaceId => surfaceId;

        // -- Ids ---------------------------------------------------------------------------------------

        /// <summary>
        /// The id for a world position, quantised to the centimetre.
        /// <para>
        /// Quantised because the id is compared as a string on four machines: two players aiming at
        /// the same shelf must produce two different spots, but one player's own drop must produce the
        /// same id everywhere it is read. Centimetres are finer than anything the player can aim to
        /// and still leave the id far short of the 61 characters a <c>FixedString64Bytes</c> carries.
        /// </para>
        /// </summary>
        public static string IdFor(Vector3 position) =>
            Prefix +
            position.x.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            position.y.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            position.z.ToString("0.##", CultureInfo.InvariantCulture);

        public static bool IsDropId(string containerId) =>
            !string.IsNullOrEmpty(containerId) &&
            containerId.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Read the position back out of an id. False for anything that is not one.</summary>
        public static bool TryPosition(string containerId, out Vector3 position)
        {
            position = default;
            if (!IsDropId(containerId)) return false;

            var parts = containerId.Substring(Prefix.Length).Split(',');
            if (parts.Length != 3) return false;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }

            // A corrupt id must not park a prop at infinity, where it is both invisible and impossible
            // to walk to. Refusing leaves the prop where it is, which is the reconciler's safe answer.
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
            {
                return false;
            }

            position = new Vector3(x, y, z);
            return true;
        }

        /// <summary>
        /// The spot for this id, building it if this process has not seen it before. Null for an id
        /// that is not a drop, and for one that will not parse — both of which mean "not mine", so a
        /// caller can chain this after <see cref="LabRuntime.SlotsFor"/> without checking first.
        /// </summary>
        public static DropSpot Resolve(string containerId)
        {
            if (!IsDropId(containerId)) return null;
            if (LabRuntime.SlotsFor(containerId) is DropSpot known) return known;
            if (!TryPosition(containerId, out var position)) return null;

            var go = new GameObject($"Drop_{containerId}");
            go.transform.position = position;

            var spot = go.AddComponent<DropSpot>();
            spot.Bind(containerId);
            return spot;
        }

        /// <summary>
        /// Give the spot its id and announce it. Separate from <c>OnEnable</c> because
        /// <c>AddComponent</c> runs the lifecycle before <see cref="Resolve"/> can hand the id over,
        /// so the first registration cannot come from there.
        /// </summary>
        public void Bind(string containerId)
        {
            if (!IsDropId(containerId)) return;
            surfaceId = containerId;
            name = $"Drop_{containerId}";
            LabRuntime.RegisterFixture(surfaceId, transform, this);
        }

        private void OnEnable()
        {
            if (surfaceId != null) LabRuntime.RegisterFixture(surfaceId, transform, this);
        }

        private void OnDisable()
        {
            if (surfaceId != null) LabRuntime.ForgetFixture(surfaceId, transform);
        }

        // -- IVialSlots --------------------------------------------------------------------------------

        /// <summary>
        /// Grows on demand, like the delivery crate rather than like a rack: the floor has no fixed
        /// number of holes in it, and refusing a second item at one spot would strand it in a hand.
        /// </summary>
        public Transform Slot(int index)
        {
            if (index < 0) index = 0;

            while (slots.Count <= index)
            {
                int i = slots.Count;
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Offset(i);
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

        /// <summary>
        /// Where the n-th thing at one spot sits, as a phyllotactic spiral around the aimed point.
        /// <para>
        /// A pure function of the index, so two clients that number the same pile differently still
        /// spread it over the same set of positions. Slot 0 is the aimed point exactly, which is the
        /// only one that normally exists.
        /// </para>
        /// </summary>
        private static Vector3 Offset(int index)
        {
            if (index <= 0) return Vector3.zero;

            const float goldenAngle = 2.399963f;   // radians
            float radius = SlotSpacing * Mathf.Sqrt(index);
            return new Vector3(Mathf.Cos(goldenAngle * index) * radius, 0f,
                               Mathf.Sin(goldenAngle * index) * radius);
        }

        // -- Placing -----------------------------------------------------------------------------------

        /// <summary>
        /// Park an item here. Purely local, exactly as <see cref="SampleRack.TryPlace"/> is: where the
        /// host thinks the thing is was written when it accepted the put-down, and this only moves the
        /// prop.
        /// </summary>
        public bool TryPlace(Carryable item, int slot)
        {
            if (item == null) return false;

            // Already here is not a reason to move it: on a client the reconciler can park the prop
            // before the accepted callback runs, and treating the item as its own obstruction would
            // bounce it into the next slot along every frame.
            int current = SlotOf(item.transform);

            if (slot < 0 || slot > slots.Count)
            {
                slot = current >= 0 ? current : FreeSlot();
            }
            else if (slot < slots.Count)
            {
                // Unity's ==, not a null pattern: a destroyed prop is a live C# reference and a
                // pattern match would read it as an occupant that will never move out of the way.
                var occupant = VialSlot.Occupant(slots[slot]);
                if (occupant != null && occupant != item) slot = FreeSlot();
            }

            item.AttachTo(Slot(slot), interactable: true);
            emptySince = -1f;
            return true;
        }

        /// <inheritdoc cref="TryPlace(Carryable,int)"/>
        public bool TryPlace(Carryable item) => TryPlace(item, -1);

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (VialSlot.Occupant(slots[i]) != null) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Tidy up after itself. A spot is nothing but a place a prop hangs off, so once nothing hangs
        /// off it there is nothing to keep — and if the host still says something is here, the next
        /// reconcile pass builds it again from the id, which is the whole point of encoding the
        /// position there.
        /// </summary>
        private void Update()
        {
            if (surfaceId == null || !IsEmpty)
            {
                emptySince = -1f;
                return;
            }

            if (emptySince < 0f) emptySince = Time.time;
            else if (Time.time - emptySince >= ReapAfterSeconds) Destroy(gameObject);
        }
    }
}
