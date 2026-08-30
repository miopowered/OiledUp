using System;
using UnityEngine;

namespace Residue.Net.Connect
{
    /// <summary>
    /// The black between two scenes, and the one place that decides <i>when</i> the load underneath it
    /// is allowed to start (#51).
    /// <para>
    /// <b>Why the load is queued rather than called.</b> A fade that is started and then immediately
    /// followed by a scene load is not a fade — the load lands one or two frames later and cuts
    /// through it. <see cref="Cover(string,Action)"/> hands the work over instead, and
    /// <see cref="Tick"/> runs it at the moment the screen is actually opaque. That is also what makes
    /// the whole thing safe to depend on: the clock lives here and is driven from
    /// <c>LabConnection.Update</c>, so the queued load runs whether or not any UI ever drew this. A
    /// fade owned by a screen would mean a missing screen is a game that never loads the lab.
    /// </para>
    /// <para>
    /// <b>The clock is passed in, and it must be an unscaled one.</b> The pause menu sets
    /// <c>Time.timeScale</c> to zero and LEAVE is pressed from behind it; a fade on
    /// <c>Time.deltaTime</c> would stop dead at whatever grey it had reached and never run the load it
    /// is holding. It is passed rather than read so the whole sequence is steppable off a frame loop —
    /// see <c>SceneTransitionTests</c>, and <c>LobbyRoom.Tick</c>, which is shaped this way for the
    /// same two reasons.
    /// </para>
    /// <para>
    /// Nothing here knows what a scene is. It is a number between 0 and 1, a line of text, and a
    /// callback — which is why it can be tested without an Editor, a NetworkManager or a build index.
    /// </para>
    /// </summary>
    public sealed class SceneFade
    {
        /// <summary>
        /// Out is quicker than in. Leaving is a decision the player has already made and does not want
        /// to sit through; arriving is the moment they are reading the room, so it is worth easing.
        /// </summary>
        public const float FadeOutSeconds = 0.22f;

        public const float FadeInSeconds = 0.34f;

        /// <summary>
        /// Longest frame the fade will believe. The first frame after a scene activates is far longer
        /// than a frame, and an unclamped delta spends the whole fade-in inside it — which is a cut
        /// with extra steps. Anything above this is treated as this.
        /// </summary>
        public const float MaxStepSeconds = 0.1f;

        private Action pending;
        private bool holding;
        private float opacity;

        /// <summary>0 for clear, 1 for fully covered. What a veil draws itself at.</summary>
        public float Opacity => opacity;

        /// <summary>Anything at all is drawn. Below this there is nothing on screen to see.</summary>
        public bool Covers => opacity > 0f;

        /// <summary>Nothing behind this is visible. The moment a queued load is allowed to run.</summary>
        public bool FullyCovered => opacity >= 1f;

        /// <summary>The step being waited on, in the player's words. Null when nothing is.</summary>
        public string Step { get; private set; }

        /// <summary>
        /// Cover the screen, and keep it covered until <see cref="Release"/>. Safe to call every
        /// frame — the step may change while it is held, which is how one unbroken cover carries a
        /// player from "Reserving a relay…" to "Waiting for the host…" without a flash in between.
        /// </summary>
        public void Cover(string step)
        {
            holding = true;
            if (!string.IsNullOrEmpty(step)) Step = step;
        }

        /// <summary>
        /// Cover the screen and run <paramref name="whenCovered"/> once it is opaque.
        /// <para>
        /// The work outlives <see cref="Release"/> on purpose: a queued scene load must happen even if
        /// whatever asked for the cover has since stopped wanting one, or leaving a lab would fade
        /// back into it. A second call replaces a load that has not run yet, which is the right answer
        /// for the case it happens in — the player left while a shift was still starting, and the
        /// menu is where they asked to be.
        /// </para>
        /// </summary>
        public void Cover(string step, Action whenCovered)
        {
            Cover(step);
            if (whenCovered != null) pending = whenCovered;
        }

        /// <summary>Stop holding. The screen fades back in, once any queued work has run.</summary>
        public void Release() => holding = false;

        /// <summary>
        /// Advance by <paramref name="seconds"/> of <b>real</b> time. See the type doc for why it is
        /// never <c>Time.deltaTime</c>.
        /// </summary>
        public void Tick(float seconds)
        {
            if (seconds > MaxStepSeconds) seconds = MaxStepSeconds;
            if (seconds < 0f) seconds = 0f;

            bool wantCovered = holding || pending != null;

            opacity = Mathf.Clamp01(opacity + (wantCovered
                ? seconds / FadeOutSeconds
                : -seconds / FadeInSeconds));

            if (wantCovered && opacity >= 1f && pending != null)
            {
                // Cleared before the call, not after: the work is very often another Cover — the
                // return to the menu queued from inside a load that could not start — and clearing
                // afterwards would throw that second one away.
                var work = pending;
                pending = null;
                work();
            }

            // Only once there is nothing left to see. Clearing the line the moment the hold ends
            // would blank the text over a screen that is still half black.
            if (!wantCovered && opacity <= 0f) Step = null;
        }
    }
}
