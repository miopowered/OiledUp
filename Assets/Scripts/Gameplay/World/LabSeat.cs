using Residue.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A chair you can actually sit in. The one fixture in the lab a player is <i>in</i> rather than
    /// stood at.
    ///
    /// <para>
    /// <b>What sitting takes away is walking, and nothing else.</b> Look, the interaction ray, the
    /// crosshair, the inventory and the terminal all keep working, because the whole point of this
    /// chair is the terminal 0.6 m in front of it. That is why <see cref="PlayerController.Sit"/>
    /// switches off the <see cref="CharacterController"/> instead of the controller component: the
    /// terminal screen and the pause menu both own <c>PlayerController.enabled</c> and both hand it
    /// back unconditionally, so a seat built on that flag would be released by closing the terminal it
    /// exists to serve. The store-and-restore shape is <see cref="ItemInspectionView"/>'s — capture on
    /// the way in, mirror it on the way out, and release on <c>OnDisable</c> so nothing can be left
    /// parked.
    /// </para>
    ///
    /// <para>
    /// <b>Standing up is movement input, not a key.</b> Interact is taken — it is what opens the
    /// terminal you sat down for — and Space already inspects whatever is in your hands. Movement is
    /// free while seated, reads as the obvious thing to try, and needs no key name in the prompt, so
    /// the sentence survives a rebind and a translation. It arms only once the stick or the keys have
    /// returned to neutral, or sitting down mid-stride would stand you straight back up.
    /// </para>
    ///
    /// <para>
    /// <b>Getting in and out is a short glide, not a cut.</b> Roughly a fifth of a second of eased
    /// camera travel, owned by <see cref="PlayerController"/> because it owns the transform and the eye
    /// height the glide writes to — this class hands over two endpoints and never looks again. The
    /// state changes on the frame <see cref="Seat"/> is called; only the view is late. Sitting down
    /// also turns you towards the desk, but as an offset paid over the glide rather than as a heading,
    /// so a player moving the mouse on the way down is never fought for control of it.
    /// </para>
    ///
    /// <para>
    /// Local, like <see cref="LockerDoor"/>: a seated player is a camera position, not lab state. The
    /// host is told nothing and asked for nothing.
    /// </para>
    /// </summary>
    public sealed class LabSeat : Interactable
    {
        [Tooltip("Where the seated player's feet are parked, and which way they face. Defaults to " +
                 "this object, whose pivot is the chair's base centre.")]
        [SerializeField] private Transform anchor;

        [Tooltip("Eye height above the parked feet. The pad is at 0.49 m and a seated technician's " +
                 "eyes are about 0.65 m above it.")]
        [SerializeField] private float seatedEyeHeight = 1.15f;

        [Tooltip("How far back along the seat's own -Z the player is put down again. Far enough to " +
                 "clear both the chair and the desk it is tucked under.")]
        [SerializeField] private float standBack = 0.62f;

        private PlayerController occupant;
        private InputAction move;
        private bool armed;

        /// <summary>True while somebody on this machine is sat here.</summary>
        public bool IsOccupied => occupant != null;

        private Transform Anchor => anchor != null ? anchor : transform;

        /// <summary>
        /// Where standing up puts you: straight back out of the chair, away from whatever it is pulled
        /// up to. Derived from the seat rather than authored as a world point, so moving the chair
        /// moves the spot with it.
        /// </summary>
        public Vector3 StandPosition()
        {
            var seat = Anchor;
            Vector3 back = seat.forward;
            back.y = 0f;

            // A seat facing straight up or down has no usable "back". Fall back to its own position
            // rather than dividing by nothing.
            if (back.sqrMagnitude < 0.0001f) return seat.position;

            return seat.position - back.normalized * standBack;
        }

        // -- Interaction ------------------------------------------------------------------------------

        public override string Prompt(PlayerInteractor player)
        {
            if (!IsOccupied) return PromptStrings.SeatSit.Text;

            // Looking down at the chair you are already in. "That chair is taken" is true and
            // useless; the way out is the thing worth saying.
            return player != null && player.GetComponent<PlayerController>() == occupant
                ? PromptStrings.SeatSeated.Text
                : PromptStrings.SeatTaken.Text;
        }

        public override bool CanInteract(PlayerInteractor player) => !IsOccupied;

        public override void Interact(PlayerInteractor player)
        {
            if (player == null) return;

            // The controller sits on the same object as the interactor — one player, one root — so
            // this needs nothing wired and works for a spawned prefab as well as the scene copy.
            if (!Seat(player.GetComponent<PlayerController>())) return;

            player.Say(PromptStrings.SeatSeated.Text, 4f);
        }

        // -- Sitting ----------------------------------------------------------------------------------

        /// <summary>
        /// Sit <paramref name="player"/> down. The half with no input and no interactor in it, so the
        /// seat can be driven headlessly.
        /// </summary>
        public bool Seat(PlayerController player)
        {
            if (IsOccupied || player == null) return false;

            var seat = Anchor;
            if (!player.Sit(seat.position, seat.eulerAngles.y, seatedEyeHeight)) return false;

            occupant = player;
            move = null;
            armed = false;
            return true;
        }

        /// <summary>
        /// Give the player back, on their feet and clear of the furniture. Idempotent, and safe on an
        /// occupant that has since been destroyed — leaving a session takes the avatar with it and the
        /// chair still has to let go.
        /// <para>
        /// The chair is free the instant this is called, before the player has finished travelling out
        /// of it. That is the right way round: the alternative is a chair that reports itself occupied
        /// by somebody who has already decided to leave, and every caller here — <c>OnDisable</c>
        /// included — needs letting go to be something that has definitely happened by the time it
        /// returns. <see cref="PlayerController.Stand"/> owns the rest, and finishes it on its own
        /// <c>OnDisable</c> if nothing else does.
        /// </para>
        /// </summary>
        public void Release()
        {
            var seated = occupant;
            occupant = null;
            move = null;
            armed = false;

            // Unity's operator, so a destroyed avatar reads as null here rather than being
            // dereferenced. Stand is itself a no-op on a player who is not sat down.
            if (seated != null) seated.Stand(StandPosition());
        }

        private void OnDisable() => Release();

        private void Update()
        {
            if (occupant == null)
            {
                // A destroyed avatar. Drop the reference rather than testing it again every frame.
                if (!ReferenceEquals(occupant, null)) Release();
                return;
            }

            // A screen or the pause menu has the player right now — both switch this component off and
            // both switch it back on. Standing somebody up from under a menu would teleport a person
            // who is reading, and the movement action they are not driving would decide when. It is the
            // same flag PlayerController.CanGlide reads, for the same reason: nothing about being in a
            // chair may change while the world is stopped.
            if (!occupant.enabled) return;

            // Something outside put them back in the world (a rejoin placement re-enables the motor,
            // and PlayerController clears the flag when it notices). Let go without teleporting them.
            if (!occupant.IsSeated)
            {
                occupant = null;
                move = null;
                armed = false;
                return;
            }

            if (WantsToStand()) Release();
        }

        /// <summary>
        /// Read straight off the Move action rather than through <see cref="PlayerController"/>, which
        /// is deliberately not consuming it while the motor is off. Level-triggered, so it also
        /// survives input queued from the Editor without an <c>InputSystem.Update</c> — see the note in
        /// <c>CLAUDE.md</c> about edge-triggered actions vanishing in a synthetic frame.
        /// </summary>
        private bool WantsToStand()
        {
            var action = MoveAction();
            if (action == null) return false;

            float amount = action.ReadValue<Vector2>().sqrMagnitude;

            if (!armed)
            {
                if (amount < 0.05f) armed = true;
                return false;
            }

            return amount > 0.25f;
        }

        private InputAction MoveAction()
        {
            if (move != null) return move;

            var asset = occupant != null ? occupant.InputAsset : null;
            var map = asset != null ? asset.FindActionMap("Player", throwIfNotFound: false) : null;
            move = map != null ? map.FindAction("Move", throwIfNotFound: false) : null;
            return move;
        }
    }
}
