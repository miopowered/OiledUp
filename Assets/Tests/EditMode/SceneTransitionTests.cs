using NUnit.Framework;
using Residue.Gameplay.World;
using Residue.Net.Connect;
using Residue.Net.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The fade between the menu and the lab (#51).
    /// <para>
    /// <b>What is deliberately not here.</b> Whether the black actually lands before the camera does
    /// is a thing you look at, and whether NGO replicates its own load needs two processes — see
    /// docs/MULTIPLAYER.md. What is left is the part that would strand a player: a scene load queued
    /// behind a fade and never run, a cover that never lifts, or a loading screen that spends the
    /// signal set on chrome.
    /// </para>
    /// </summary>
    public sealed class SceneTransitionTests
    {
        /// <summary>Steps the fade in frames of <paramref name="seconds"/> until it settles.</summary>
        private static void Run(SceneFade fade, float total, float seconds = 1f / 60f)
        {
            for (float elapsed = 0f; elapsed < total; elapsed += seconds) fade.Tick(seconds);
        }

        [Test]
        public void Fade_StartsClearAndSaysNothing()
        {
            var fade = new SceneFade();

            Assert.AreEqual(0f, fade.Opacity);
            Assert.IsFalse(fade.Covers);
            Assert.IsNull(fade.Step);
        }

        /// <summary>
        /// A cover that never reaches full opacity is a scene load that never happens — the load is
        /// queued on exactly that moment. Checked with a margin, because the fade must be finished
        /// well inside its own advertised duration rather than one frame after it.
        /// </summary>
        [Test]
        public void Fade_ReachesFullBlackWithinItsOwnDuration()
        {
            var fade = new SceneFade();
            fade.Cover("Loading the lab…");

            Run(fade, SceneFade.FadeOutSeconds);

            Assert.IsTrue(fade.FullyCovered, "The fade-out did not finish inside FadeOutSeconds.");
            Assert.AreEqual("Loading the lab…", fade.Step);
        }

        /// <summary>
        /// The whole point of queuing rather than calling: the load must run when the screen is
        /// opaque, not when it was asked for. Running it early is the cut this issue is about.
        /// </summary>
        [Test]
        public void Fade_RunsTheLoadOnlyOnceTheScreenIsBlack_AndOnlyOnce()
        {
            var fade = new SceneFade();
            int loads = 0;
            float opacityWhenLoaded = -1f;

            fade.Cover("Loading the lab…", () =>
            {
                loads++;
                opacityWhenLoaded = fade.Opacity;
            });

            fade.Tick(1f / 60f);
            Assert.AreEqual(0, loads, "The load ran before the screen was covered.");

            Run(fade, SceneFade.FadeOutSeconds * 3f);

            Assert.AreEqual(1, loads, "A queued load ran more than once. That is two scene loads.");
            Assert.AreEqual(1f, opacityWhenLoaded, 0.0001f);
        }

        /// <summary>
        /// <b>The one that strands a player.</b> Whatever asked for the cover can stop wanting one
        /// while the screen is still going black — the player pressed LEAVE, or a host dropped — and
        /// a queued scene load abandoned there leaves them standing in a lab with no session and no
        /// menu, which is the failure <c>ReturnToBoot</c> exists to prevent.
        /// </summary>
        [Test]
        public void Fade_AQueuedLoadSurvivesTheHoldBeingReleased()
        {
            var fade = new SceneFade();
            int loads = 0;

            fade.Cover("Returning to the menu…", () => loads++);
            fade.Tick(1f / 60f);
            fade.Release();

            Run(fade, SceneFade.FadeOutSeconds * 2f);

            Assert.AreEqual(1, loads,
                "Releasing the hold threw away the scene load it was covering.");
        }

        /// <summary>
        /// A load queued from inside a load must not be dropped. That is not hypothetical: a netcode
        /// load that cannot start asks for the menu back, and it does so from the callback.
        /// </summary>
        [Test]
        public void Fade_ALoadQueuedFromInsideALoadStillRuns()
        {
            var fade = new SceneFade();
            bool second = false;

            fade.Cover("Loading the lab…",
                () => fade.Cover("Returning to the menu…", () => second = true));

            Run(fade, SceneFade.FadeOutSeconds * 3f);

            Assert.IsTrue(second, "The second load was cleared by the first one finishing.");
        }

        /// <summary>
        /// Releasing gives the screen back, and only then forgets the line. Blanking the text as the
        /// hold ends leaves a caption-less grey pane over a game that is half visible.
        /// </summary>
        [Test]
        public void Fade_RevealsAndKeepsItsLineUntilThereIsNothingLeftToSee()
        {
            var fade = new SceneFade();
            fade.Cover("Loading the lab…");
            Run(fade, SceneFade.FadeOutSeconds);

            fade.Release();
            fade.Tick(SceneFade.FadeInSeconds * 0.5f);

            Assert.Less(fade.Opacity, 1f);
            Assert.IsTrue(fade.Covers);
            Assert.AreEqual("Loading the lab…", fade.Step,
                "The line went before the black did.");

            Run(fade, SceneFade.FadeInSeconds);

            Assert.AreEqual(0f, fade.Opacity);
            Assert.IsFalse(fade.Covers);
            Assert.IsNull(fade.Step);
        }

        /// <summary>
        /// One unbroken cover has to carry a player from "Reserving a relay…" through "Connecting…"
        /// to "Waiting for the host…" — those are four different waits and one screen. Re-covering
        /// with a new line must not restart the fade.
        /// </summary>
        [Test]
        public void Fade_TheLineChangesWithoutTheBlackBlinking()
        {
            var fade = new SceneFade();
            fade.Cover("Reserving a relay…");
            Run(fade, SceneFade.FadeOutSeconds);

            fade.Cover("Waiting for the host…");
            fade.Tick(1f / 60f);

            Assert.AreEqual(1f, fade.Opacity, "The cover blinked between two steps.");
            Assert.AreEqual("Waiting for the host…", fade.Step);
        }

        /// <summary>
        /// The first frame after a scene activates is far longer than a frame. Unclamped, the whole
        /// fade-in is spent inside it and the reveal is a cut with extra steps.
        /// </summary>
        [Test]
        public void Fade_ASingleEnormousFrameDoesNotSkipTheFade()
        {
            var fade = new SceneFade();
            fade.Cover("Loading the lab…");
            Run(fade, SceneFade.FadeOutSeconds);
            fade.Release();

            fade.Tick(4f);

            Assert.Greater(fade.Opacity, 0f,
                "One long frame consumed the entire fade-in, which is the cut this replaces.");
        }

        [Test]
        public void Fade_TickingWithNothingHappeningIsSafe()
        {
            var fade = new SceneFade();

            Assert.DoesNotThrow(() =>
            {
                fade.Tick(0f);
                fade.Tick(-1f);
                fade.Tick(10f);
                fade.Release();
                fade.Tick(1f / 60f);
            });

            Assert.AreEqual(0f, fade.Opacity);
        }

        // -----------------------------------------------------------------------------------------
        // The veil. Hard rule 4, and the invisible-element-eats-every-click trap.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Hard rule 4: red, amber and green mean verdict state and nothing else. A loading screen is
        /// the single most likely place for that to die quietly, because every convention outside
        /// this project draws progress in green and trouble in red. Stated as a sweep over the whole
        /// signal set so a fourth signal colour added later has to answer here too.
        /// </summary>
        [Test]
        public void Veil_UsesNoSignalColour()
        {
            foreach (var signal in new[]
                     {
                         SignalPalette.Critical, SignalPalette.Caution, SignalPalette.Normal
                     })
            {
                Assert.AreNotEqual(signal, LoadingVeil.Face, "The fade colour is a signal colour.");
                Assert.AreNotEqual(signal, LoadingVeil.Pulse, "The sweep is drawn in a signal colour.");
                Assert.AreNotEqual(signal, LoadingVeil.Rest);
            }

            // And not merely a different constant with the same hue. The signal set is warm-red,
            // amber and green; everything here is either the near-black the menu already sits on or
            // a cool. A green channel that dominates both others is the shape being ruled out.
            foreach (var colour in new[] { LoadingVeil.Face, LoadingVeil.Pulse, LoadingVeil.Rest })
            {
                Assert.IsFalse(colour.g > colour.r + 0.2f && colour.g > colour.b + 0.2f,
                    $"{colour} reads as green, which means a verdict and nothing else.");
                Assert.IsFalse(colour.r > 0.6f && colour.g < 0.5f && colour.b < 0.4f,
                    $"{colour} reads as red, which means a verdict and nothing else.");
            }
        }

        /// <summary>
        /// A full-screen element that is invisible but still in the layout eats every click the
        /// player aimed at the menu underneath it. <c>display:None</c> takes it out of picking as
        /// well as out of the draw, which an opacity of zero does not.
        /// </summary>
        [Test]
        public void Veil_IsNotThereAtAllWhenThereIsNothingToShow()
        {
            var veil = new LoadingVeil();

            veil.Refresh(null, 0f);

            // The inline style rather than the resolved one: this element has never been attached to
            // a panel, so there is nothing to have resolved it.
            Assert.AreEqual(DisplayStyle.None, veil.Root.style.display.value,
                "An idle veil was left in the layout, where it swallows clicks.");
            Assert.AreEqual(PickingMode.Ignore, veil.Root.pickingMode);
        }

        /// <summary>
        /// Refreshing before anything has been wired up must not throw. This runs from
        /// <c>MenuScreen.Update</c>, which can reach a frame before a <c>LabConnection</c> has been
        /// found, and an exception in Update is a screen that never draws again.
        /// </summary>
        [Test]
        public void Veil_SurvivesHavingNoConnection()
        {
            var veil = new LoadingVeil();

            Assert.DoesNotThrow(() =>
            {
                veil.Refresh(null, 0f);
                veil.Refresh(null, 12.5f);
            });

            Assert.IsNotNull(veil.Root);
        }
    }
}
