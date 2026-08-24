using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Turns the host's record of where something is into the transform a local prop should hang off.
    /// <para>
    /// Extracted out of <see cref="VialReconciler"/> when solvent bottles became physical objects with
    /// exactly the same problem: §3.2 keeps both of them local props, both are placed from a
    /// replicated <see cref="SampleLocation"/>, and both have to end up in the same hole of the same
    /// rack on every machine in the session. Two copies of this would be two shelves that agreed until
    /// the day one of them was fixed.
    /// </para>
    /// Null is a real answer everywhere below and means <b>leave the prop where it is</b> — for a
    /// location with nothing physical behind it, for a fixture this scene has not registered, and for
    /// hands that have not spawned yet.
    /// </summary>
    public static class PropSockets
    {
        /// <summary>True when the host says this is in the hands of the player at this keyboard.</summary>
        public static bool IsHeldLocally(SampleLocation location)
        {
            if (location.Kind != SampleLocationKind.Held) return false;

            var hands = VialFeed.Hands;
            return hands != null && hands.LocalClientId == location.HolderClientId;
        }

        /// <summary>
        /// Where a prop in this location belongs, and whether the player may target it there.
        /// <para>
        /// <paramref name="existing"/> is the prop's current transform, or null if it has not been
        /// built yet. It is consulted only for a container that named no particular slot: the host
        /// republishes four times a second, and picking "first free" again each time would shuffle the
        /// shelf under the player's hand.
        /// </para>
        /// </summary>
        public static Transform For(SampleLocation location, Transform existing, out bool reachable)
        {
            reachable = true;

            switch (location.Kind)
            {
                case SampleLocationKind.Held:
                    // Somebody else's hands. Colliders off: you cannot take an object out of them, and
                    // a live collider riding a moving player is something the interaction ray would
                    // trip over on the way to whatever you were actually aiming at.
                    reachable = false;
                    return VialFeed.Hands?.CarrySocket(location.HolderClientId);

                case SampleLocationKind.InCrate:
                case SampleLocationKind.InFridge:
                case SampleLocationKind.OnSurface:
                case SampleLocationKind.InMachine:
                {
                    // Inside an instrument the station mediates access (§5.4): the vial comes back out
                    // by pressing the machine, not by grabbing through its door.
                    reachable = location.Kind != SampleLocationKind.InMachine;

                    var slots = LabRuntime.SlotsFor(location.ContainerId);
                    if (slots == null) return null;

                    int index = location.SlotIndex;
                    if (index < 0)
                    {
                        // A container with no slot named — a dropped player's vial goes back to the
                        // rack that way. Keep the slot it is already in.
                        int current = existing != null ? slots.SlotOf(existing) : -1;
                        index = current >= 0 ? current : slots.FreeSlot();
                    }

                    return index >= 0 ? slots.Slot(index) : null;
                }

                default:
                    // Archived, consumed, and whatever a later version adds. Filing a verdict does not
                    // move the bottle on the host either — it stays on the shelf it was left on — so
                    // the honest thing here is to stop having an opinion about it.
                    return null;
            }
        }
    }
}
