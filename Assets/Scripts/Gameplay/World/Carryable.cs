using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Anything the player can store, select, hold and inspect through the same interaction contract.
    /// <para>
    /// That competition is the point. §5.5 is about the cost of moving things around a room, and it
    /// only exists if carrying capacity is genuinely scarce.
    /// </para>
    /// </summary>
    public abstract class Carryable : Interactable
    {
        /// <summary>Shown in prompts and toasts.</summary>
        public abstract string DisplayName { get; }

        /// <summary>Optional text shown beside the physical object during inspection.</summary>
        public virtual string InspectionText => DisplayName;

        /// <summary>Initial object orientation in the inspection view.</summary>
        public virtual Quaternion InspectionRotation => Quaternion.identity;

        /// <summary>Stable orientation used by the inventory's rendered 2D thumbnail.</summary>
        public virtual Quaternion InventoryIconRotation => Quaternion.identity;

        /// <summary>Allows a carryable to exclude obsolete/helper renderers from its slot thumbnail.</summary>
        public virtual bool IncludeInInventoryIcon(Renderer renderer) => renderer != null;

        /// <summary>Extra controls shown while this item is being inspected.</summary>
        public virtual string InspectionHelp => null;

        /// <summary>Lifecycle hooks for physical inspection presentation.</summary>
        public virtual void BeginInspection() { }
        public virtual void TickInspection() { }
        public virtual void EndInspection() { }

        /// <summary>Lets an item capture an inspection click before it becomes drag rotation.</summary>
        public virtual bool HandleInspectionPointer(Camera camera, Vector2 screenPosition) => false;

        /// <summary>World-space bounds used to fit the object into the inspection camera.</summary>
        public Bounds VisualBounds
        {
            get
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.2f);
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }
        }

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

        /// <summary>Only the selected inventory slot renders in the first-person hand.</summary>
        public virtual void SetHeldVisible(bool visible)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        public override string Prompt(PlayerInteractor player) =>
            player.InventoryHasSpace ? $"Take {DisplayName}" : "Inventory full";

        public override bool CanInteract(PlayerInteractor player) => player.InventoryHasSpace;

        /// <summary>
        /// Picking something up is a request, not a grab — see <see cref="PlayerInteractor.Take"/>.
        /// The prop stays where it is until the host agrees it is yours.
        /// </summary>
        public override void Interact(PlayerInteractor player) => player.Take(this);

        /// <summary>
        /// What the primary button does while this is in your hands: shake a vial, open a manual,
        /// glance at a slip.
        /// <para>
        /// Needed because a carried item cannot be targeted — its colliders are off and the
        /// interactor skips it — so the normal look-at-and-press route is unavailable by design.
        /// </para>
        /// </summary>
        public virtual void UseInHand(PlayerInteractor player) { }

        /// <summary>HUD hint for <see cref="UseInHand"/>, or null if the item does nothing in hand.</summary>
        public virtual string UseHint => null;
    }
}
