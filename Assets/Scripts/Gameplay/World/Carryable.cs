using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Anything the player can hold. You have one pair of hands, so every carryable competes with
    /// every other — a vial in your hand is a printout you are not carrying to the desk.
    /// <para>
    /// That competition is the point. §5.5 is about the cost of moving things around a room, and it
    /// only exists if carrying capacity is genuinely scarce.
    /// </para>
    /// </summary>
    public abstract class Carryable : Interactable
    {
        /// <summary>Shown in prompts and toasts.</summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Park this item in a socket.
        /// <para>
        /// <paramref name="interactable"/> controls whether the player can target it directly. It
        /// must be false while carried (it would sit between the camera and everything else) and
        /// while inside a machine, where the station mediates access.
        /// </para>
        /// </summary>
        public void AttachTo(Transform socket, bool interactable = true)
        {
            transform.SetParent(socket, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = interactable;

            if (TryGetComponent<Rigidbody>(out var body))
            {
                body.isKinematic = true;
                body.detectCollisions = interactable;
            }
        }

        public override string Prompt(PlayerInteractor player) =>
            player.Carried == null ? $"Take {DisplayName}" : "Hands full";

        public override bool CanInteract(PlayerInteractor player) => player.Carried == null;

        public override void Interact(PlayerInteractor player) => player.TryCarry(this);
    }
}
