using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The taped lid. Holding Interact on it is how a carton gets opened (#31).
    /// <para>
    /// Its own component on its own collider, rather than a second branch inside
    /// <see cref="CartonProp"/>, for the reason <see cref="MachineActionButton"/> exists: picking a box
    /// up is a tap and cutting it open is a hold, and <see cref="Interactable.HoldSeconds"/> belongs to
    /// the thing being looked at. One component cannot offer both, and folding them together would
    /// mean either no tap-to-carry or no hold-to-open.
    /// </para>
    /// <para>
    /// The seconds are the same currency as a flush: §9 wants preparation to be a hand-operated task
    /// with a real cost rather than a menu click, and this one is paid standing at the box with the
    /// day clock running.
    /// </para>
    /// </summary>
    public sealed class CartonLid : Interactable
    {
        [Tooltip("The box this lid belongs to. Falls back to a search up the hierarchy.")]
        [SerializeField] private CartonProp carton;

        [Tooltip("Seconds of holding Interact to cut a carton open. The flush's cost, in cardboard.")]
        [SerializeField] private float openHoldSeconds = 2.5f;

        private CartonProp Box
        {
            get
            {
                if (carton == null) carton = GetComponentInParent<CartonProp>();
                return carton;
            }
        }

        /// <summary>
        /// A lid with no collider of its own is a lid the interaction ray never reaches — the box's
        /// root collider answers first and the player gets "take carton" everywhere they aim, with no
        /// way to open anything. §9 forbids failing quietly, and this one would present as a carton
        /// that simply cannot be opened.
        /// </summary>
        private void Start()
        {
            if (GetComponent<Collider>() == null)
            {
                Debug.LogError(
                    "[CartonLid] This lid has no Collider, so it cannot be aimed at and the carton " +
                    "can never be opened. Give the Lid child its own BoxCollider, and shrink the " +
                    "carton root's collider to the body so the two do not overlap.", this);
            }
        }

        private Carton State
        {
            get
            {
                var box = Box;
                return box != null ? box.Carton : null;
            }
        }

        public override float HoldSeconds
        {
            get
            {
                var state = State;
                return state != null && state.IsSealed ? Mathf.Max(0f, openHoldSeconds) : 0f;
            }
        }

        public override string Prompt(PlayerInteractor player)
        {
            var state = State;
            if (state == null || !state.IsSealed) return null;

            // Said out loud rather than left as a dead object: you cannot get both hands into a box you
            // are carrying, and the walk from the bay to a bench is the point of #30.
            if (state.Location.Kind == SampleLocationKind.Held)
                return "Set the carton down before opening it";

            return $"Hold to open carton {state.JobNumber}";
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var state = State;
            return state != null && state.IsSealed &&
                   state.Location.Kind != SampleLocationKind.Held;
        }

        /// <summary>
        /// Ask to open it. The lid only swings and the vials only appear once the host has agreed —
        /// <see cref="CartonProp"/> notices the seal has gone and reveals the contents from there, so
        /// the same code runs for the player who opened it and for anyone else looking at the box.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            var box = Box;
            if (box == null) return;

            LabCommands.Attempt(player, LabCommand.OpenCarton(box.CartonId), _ =>
            {
                var state = box.Carton;
                int count = state != null && state.Note != null ? state.Note.Count : 0;

                player.Say(state != null
                    ? $"Carton {state.JobNumber} open — {count} vial(s) and a delivery note."
                    : "Carton open.", 4f);
            });
        }
    }
}
