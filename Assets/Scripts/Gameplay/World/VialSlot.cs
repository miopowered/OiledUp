using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Reading a container's slots. Shared by everything that implements <see cref="IVialSlots"/>.
    /// <para>
    /// One rule, in one place: <b>a slot's occupant is whatever is parented to it</b>. The crate and
    /// the racks used to keep a parallel list of what they were holding, which was correct only while
    /// they were the only things that ever put anything down. They are not any more — a client's props
    /// are placed by <see cref="VialReconciler"/> off the replicated record — and a list that only one
    /// of the two writers updates is a shelf that reports itself empty while you are looking at a
    /// bottle in it.
    /// </para>
    /// Deriving it costs a handful of <c>childCount</c> reads on a container with under a dozen slots,
    /// asked only when a player is stood in front of it.
    /// </summary>
    public static class VialSlot
    {
        /// <summary>What is sitting in this slot, or null if it is free.</summary>
        public static Carryable Occupant(Transform slot)
        {
            if (slot == null) return null;

            for (int i = 0; i < slot.childCount; i++)
            {
                if (slot.GetChild(i).TryGetComponent<Carryable>(out var item)) return item;
            }
            return null;
        }

        /// <summary>Which of <paramref name="slots"/> holds <paramref name="prop"/>, or -1.</summary>
        public static int IndexOf(List<Transform> slots, Transform prop)
        {
            if (slots == null || prop == null) return -1;

            var parent = prop.parent;
            if (parent == null) return -1;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == parent) return i;
            }
            return -1;
        }
    }
}
