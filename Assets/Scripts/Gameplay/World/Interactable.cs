using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Anything the player can look at and act on. Interaction is a raycast from camera centre at
    /// 2.5 m (§2.6); this is the receiving end.
    /// <para>
    /// <see cref="Prompt"/> is asked every frame while targeted, so a station can explain why it is
    /// refusing — "needs 5 ml, 3 ml left" rather than a dead object that ignores you. §9 is explicit
    /// that the player must never be punished for something they could not have checked, and that
    /// starts with the machine saying what is wrong.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("Seconds the player must hold the key. 0 is a tap.")]
        [SerializeField] protected float holdSeconds;

        /// <summary>Virtual so a station can scale it — see <see cref="MachineActionButton"/>.</summary>
        public virtual float HoldSeconds => holdSeconds;

        /// <summary>Text shown next to the crosshair. Null or empty hides the prompt entirely.</summary>
        public abstract string Prompt(PlayerInteractor player);

        /// <summary>False greys the prompt out and blocks the interaction, but still shows the reason.</summary>
        public virtual bool CanInteract(PlayerInteractor player) => true;

        public abstract void Interact(PlayerInteractor player);

        /// <summary>
        /// Highlight while targeted. Flat-shaded geometry does not suit outline shaders (§2.6), so
        /// this is a subtle emissive pulse instead.
        /// </summary>
        public virtual void SetTargeted(bool targeted)
        {
            if (highlight == null) return;
            highlight.Active = targeted;
        }

        [SerializeField] private EmissivePulse highlight;

        protected virtual void Reset() => highlight = GetComponentInChildren<EmissivePulse>();
    }
}
