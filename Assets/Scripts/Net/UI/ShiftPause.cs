using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Net.UI
{
    /// <summary>
    /// Taking the lab away from the local player while a screen is up, and giving all of it back
    /// afterwards (#44).
    /// <para>
    /// Split out of <see cref="MenuScreen"/> because it is the half that must be <i>exactly</i>
    /// symmetrical and is called from four places — resume, leave, the screen being disabled, and
    /// losing the host mid-pause. A <c>Time.timeScale</c> left at zero, or a
    /// <c>PlayerController</c> left disabled, outlives the menu that did it and presents as the game
    /// having hung; keeping both halves in one small type is what makes the pairing checkable at a
    /// glance instead of spread across a router.
    /// </para>
    /// <para>
    /// The sequence is <c>TerminalScreen.Open</c>/<c>Close</c>'s, through the same seam: cursor
    /// first via <see cref="PlayerController.SetCursorLocked"/>, then the controller and its
    /// interactor. Writing <c>Cursor.lockState</c> directly here would be one more screen with its
    /// own opinion about who owns the pointer, which is the bug this project keeps re-finding.
    /// </para>
    /// </summary>
    public sealed class ShiftPause
    {
        private PlayerController player;
        private PlayerInteractor interactor;

        /// <summary>The player is suspended right now.</summary>
        public bool Active { get; private set; }

        /// <summary>
        /// <c>Time.timeScale</c> was actually taken to zero — single player only. In co-op the day
        /// clock is the host's and carries on regardless, which is a sentence the pause menu has to
        /// put on screen rather than let the player discover.
        /// </summary>
        public bool ClockStopped { get; private set; }

        /// <param name="stopTheClock">
        /// True only when this process is the whole simulation. Freezing a client's timescale would
        /// stop its rendering of a shift that is still running everywhere else.
        /// </param>
        public void Begin(bool stopTheClock)
        {
            if (Active) return;

            Active = true;
            ClockStopped = stopTheClock;
            if (stopTheClock) Time.timeScale = 0f;

            player = LocalController();
            interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;

            PlayerController.SetCursorLocked(false);
            if (player != null) player.enabled = false;
            if (interactor != null) interactor.enabled = false;
        }

        /// <summary>
        /// Idempotent, and safe to call on a player that has since been destroyed — leaving a shift
        /// takes the avatar with it, and the timescale still has to come back.
        /// </summary>
        public void End()
        {
            if (!Active) return;

            Active = false;
            if (ClockStopped) Time.timeScale = 1f;
            ClockStopped = false;

            // Re-enabling the controller relocks the cursor through its own OnEnable when it owns it;
            // the caller relocks anyway on the menu-to-game transition, for the case where the avatar
            // is gone.
            if (interactor != null) interactor.enabled = true;
            if (player != null) player.enabled = true;

            interactor = null;
            player = null;
        }

        /// <summary>
        /// This player's own controller, among however many replicas of other players are standing in
        /// the room. <c>ManagesCursor</c> is the flag <c>PlayerAvatar</c> leaves on exactly one of
        /// them, so it is the only honest way to tell ours from theirs.
        /// </summary>
        public static PlayerController LocalController()
        {
            foreach (var controller in Object.FindObjectsByType<PlayerController>())
            {
                if (controller.ManagesCursor && controller.isActiveAndEnabled) return controller;
            }
            return null;
        }
    }
}
