using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The leaf of a locker door. Tap to swing it open, tap again to shut it.
    /// <para>
    /// Its own component on its own collider, for the reason <see cref="CartonLid"/> is separate from
    /// <see cref="CartonProp"/>: swinging the door and taking what is inside are two verbs, and the
    /// second one must only be reachable once the first has happened. A single collider spanning both
    /// would answer the ray first every time and the locker would be a cupboard that opens onto
    /// nothing.
    /// </para>
    /// <para>
    /// Purely local, and deliberately: a locker holds nothing the host tracks, so an open door is a
    /// piece of scenery rather than lab state. Nothing here asks <c>LabCommands</c> for permission,
    /// and in co-op two players can disagree about whether a door is open in the same way they can
    /// disagree about where they are standing.
    /// </para>
    /// <para>
    /// The hinge is the transform, not the mesh. <c>ProcMesh</c> authors geometry in final mesh-local
    /// coordinates and has no rotation, so the leaf is built offset in +X from this object's origin
    /// and this object sits on the hinge line.
    /// </para>
    /// </summary>
    public sealed class LockerDoor : Interactable
    {
        [Tooltip("Yaw of the leaf when open, in its own local space. Negative swings the free edge " +
                 "out into the room. Kept just under 90 degrees so an open door stays inside its own " +
                 "carcass footprint and cannot clip the neighbour in the bank.")]
        [SerializeField] private float openYaw = -88f;

        [Tooltip("Degrees per second. Fast enough to read as a tap, slow enough to see which way it went.")]
        [SerializeField] private float swingSpeed = 300f;

        public bool IsOpen { get; private set; }

        public override string Prompt(PlayerInteractor player) =>
            IsOpen ? PromptStrings.LockerClose.Text : PromptStrings.LockerOpen.Text;

        public override void Interact(PlayerInteractor player) => Toggle();

        /// <summary>
        /// Swing it the other way. Public so the swing can be driven without a player — there is no
        /// state behind it worth hiding, and a door is the one fixture a test or a tool has a reason
        /// to open.
        /// </summary>
        public void Toggle() => IsOpen = !IsOpen;

        /// <summary>
        /// <c>Time.deltaTime</c> rather than unscaled, on purpose. This is the room moving, and the
        /// pause menu takes the timescale to zero because the room is supposed to stop — a door that
        /// carried on swinging behind a pause screen would be the one thing in the lab still running.
        /// </summary>
        private void Update()
        {
            float target = IsOpen ? openYaw : 0f;
            float current = transform.localEulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(current, target)) < 0.01f) return;

            transform.localRotation = Quaternion.Euler(
                0f, Mathf.MoveTowardsAngle(current, target, swingSpeed * Time.deltaTime), 0f);
        }
    }
}
