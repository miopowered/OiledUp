using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Whose hands are whose.
    /// <para>
    /// A bottle somebody else is carrying has to appear in <i>their</i> hands, and the world layer has
    /// no way to find them: a client id is a netcode fact, and <c>Residue.Gameplay</c> cannot see
    /// <c>Residue.Net</c>. So the netcode layer answers this instead, and the reconciler asks.
    /// </para>
    /// Null in single player, where the only hands in the room are the ones
    /// <see cref="PlayerInteractor"/> already holds.
    /// </summary>
    public interface IPlayerHands
    {
        /// <summary>
        /// This process's own player. The reconciler needs it to know which pair of hands it must
        /// <i>not</i> touch — see <see cref="VialReconciler"/>.
        /// </summary>
        ulong LocalClientId { get; }

        /// <summary>
        /// Where a bottle sits while that player carries one, or null if their avatar has not spawned
        /// here yet. Null is a real answer during the window between somebody joining and their body
        /// arriving, and the caller leaves the prop alone until it stops being null.
        /// </summary>
        Transform CarrySocket(ulong clientId);
    }
}
