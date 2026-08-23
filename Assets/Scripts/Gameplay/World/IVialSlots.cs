using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A fixture with numbered places a prop can sit in.
    /// <para>
    /// <see cref="LabRuntime.RegisterFixture(string,Transform,IVialSlots)"/> already knew where a
    /// fixture <i>is</i>, which is all the host needs to decide whether a player is standing at it.
    /// It is not enough to put a bottle down. The crate and the racks build their own slot transforms
    /// privately, so a <c>SampleLocation</c> naming <c>rack#3</c> had nothing in the scene to resolve
    /// to and only the container itself could ever place anything. This is the missing half.
    /// </para>
    /// <para>
    /// <b>Occupancy is read off the slot transforms, never tracked beside them.</b> A slot's child
    /// <i>is</i> its occupant. That is what lets a prop parented by the host's own crate code and one
    /// parented by <see cref="VialReconciler"/> be counted by the same rule — a second ledger would be
    /// a second thing to fall out of step with the room, and it would be wrong on exactly the side
    /// that has no <c>LabState</c> to correct it from.
    /// </para>
    /// </summary>
    public interface IVialSlots
    {
        /// <summary>
        /// The transform a prop sits on for slot <paramref name="index"/>. A container that grows
        /// (the delivery crate) creates the slot on demand; a fixed one clamps into range. Never null.
        /// </summary>
        Transform Slot(int index);

        /// <summary>
        /// The first slot with nothing parked in it, or -1 when the container is full and cannot grow.
        /// </summary>
        int FreeSlot();

        /// <summary>
        /// Which slot <paramref name="prop"/> is parked in, or -1 if it is not in this container.
        /// <para>
        /// Exists so a caller can leave a prop where it already is. The host republishes four times a
        /// second and several of its locations name a container but no particular slot (a dropped
        /// player's vial goes back to <c>rack#-1</c>); without this, every one of those would pick
        /// "first free" again and the shelf would shuffle continuously.
        /// </para>
        /// </summary>
        int SlotOf(Transform prop);
    }
}
