using System.Collections.Generic;
using NUnit.Framework;
using Residue.Gameplay.Settings;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The audio layer's two promises (#46): the room tone actually gets built, and every sound
    /// answers to the slider it belongs under.
    /// <para>
    /// Both are here because silence is the failure this project is least equipped to notice. There
    /// are no screenshots of audio, the EditMode suite is the only thing that runs without a human at
    /// the Editor, and the lab shipped mute for long enough that the README said so as a feature
    /// gap — while <c>LabAmbience</c> had been sitting in the scene the whole time, warning once
    /// every half minute that its clip was null.
    /// </para>
    /// </summary>
    public sealed class AudioTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            GameSettings.AmbienceVolume = 1f;
            GameSettings.EffectsVolume = 1f;
        }

        private AudioSource NewSource()
        {
            var go = new GameObject("TestSource");
            spawned.Add(go);
            return go.AddComponent<AudioSource>();
        }

        /// <summary>
        /// Promise: the room tone exists at all.
        /// <para>
        /// This is the test that was missing. The lab was silent and the only evidence was a runtime
        /// warning about a null clip, which reads like a lifetime bug and sent the investigation
        /// looking for something destroying the clip — when nothing had built it. Asserting the build
        /// directly says which of those two it is, without an Editor.
        /// </para>
        /// </summary>
        [Test]
        public void TheRoomTone_IsActuallyBuilt()
        {
            var go = new GameObject("Ambience");
            spawned.Add(go);
            var ambience = go.AddComponent<LabAmbience>();

            ambience.BuildClips();

            foreach (var field in typeof(LabAmbience).GetFields(
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType != typeof(AudioClip)) continue;

                var clip = (AudioClip)field.GetValue(ambience);
                Assert.IsNotNull(clip,
                    $"LabAmbience.{field.Name} did not get built, so the lab plays nothing and every " +
                    "source holding it warns once per tick for the rest of the session.");
                Assert.Greater(clip.samples, 0, $"LabAmbience.{field.Name} is an empty clip.");
            }
        }

        /// <summary>
        /// Promise: the ambience slider reaches the room tone.
        /// <para>
        /// Half volume means half, and it is applied on registration rather than only on the next
        /// change — a source created while the slider was already down must not start at full and
        /// duck a moment later.
        /// </para>
        /// </summary>
        [Test]
        public void ASourceRegistered_TakesItsCategorySliderImmediately()
        {
            GameSettings.AmbienceVolume = 0.5f;

            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Ambience, 0.2f);

            Assert.AreEqual(0.1f, source.volume, 1e-4f,
                "A source joining a bus whose slider is already down has to arrive at that volume.");
        }

        /// <summary>
        /// Promise: moving a slider moves what is already playing.
        /// <para>
        /// And moving it back restores the authored volume exactly. The bus keeps the authored value
        /// rather than reading it back off the source for this reason: a bus that re-read would
        /// compound every change, so the room would get quieter each time the slider was touched and
        /// no setting could recover it.
        /// </para>
        /// </summary>
        [Test]
        public void MovingASlider_MovesWhatIsAlreadyPlaying_AndBackAgain()
        {
            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Ambience, 0.4f);

            GameSettings.AmbienceVolume = 0.25f;
            Assert.AreEqual(0.1f, source.volume, 1e-4f, "The slider has to reach a live source.");

            GameSettings.AmbienceVolume = 0.5f;
            GameSettings.AmbienceVolume = 1f;
            Assert.AreEqual(0.4f, source.volume, 1e-4f,
                "Back at full, a source must be at its authored volume — not compounded down by the " +
                "trip through the other values.");
        }

        /// <summary>
        /// Promise: the two sliders are actually separate.
        /// <para>
        /// A settings screen with four sliders that all move the same gain is worse than one slider,
        /// because it tells the player something untrue about what they control.
        /// </para>
        /// </summary>
        [Test]
        public void TheCategories_DoNotBleedIntoEachOther()
        {
            var ambience = NewSource();
            var effects = NewSource();
            AudioBus.Register(ambience, AudioCategory.Ambience, 1f);
            AudioBus.Register(effects, AudioCategory.Effects, 1f);

            GameSettings.AmbienceVolume = 0f;

            Assert.AreEqual(0f, ambience.volume, 1e-4f, "Ambience should have followed its own slider.");
            Assert.AreEqual(1f, effects.volume, 1e-4f, "Effects should not have moved.");
        }

        /// <summary>
        /// Promise: a destroyed source does not keep the bus alive or throw on the next slider move.
        /// <para>
        /// Props are created and destroyed constantly — a slip prints, a carton is discarded — so
        /// this is the ordinary case rather than an edge one, and a caller that forgets to unregister
        /// must not be able to break the settings screen for everything else.
        /// </para>
        /// </summary>
        [Test]
        public void ADestroyedSource_IsDroppedRatherThanThrowing()
        {
            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Effects, 1f);
            int before = AudioBus.Count;

            Object.DestroyImmediate(source.gameObject);

            Assert.DoesNotThrow(() => AudioBus.Apply(),
                "A slider move after a prop was destroyed must not throw.");
            Assert.Less(AudioBus.Count, before, "The dead source should have been dropped.");
        }

        /// <summary>
        /// Promise: registering the same source twice re-routes it rather than stacking it.
        /// <para>
        /// A prop that is re-bound — which pooled printouts are, every frame — would otherwise grow
        /// the list without bound and pay for every past registration on every slider move.
        /// </para>
        /// </summary>
        [Test]
        public void RegisteringTwice_ReplacesRatherThanStacks()
        {
            var source = NewSource();

            AudioBus.Register(source, AudioCategory.Ambience, 1f);
            int after = AudioBus.Count;
            AudioBus.Register(source, AudioCategory.Effects, 0.5f);

            Assert.AreEqual(after, AudioBus.Count, "A re-registration must not add a second entry.");

            GameSettings.AmbienceVolume = 0f;
            Assert.AreEqual(0.5f, source.volume, 1e-4f,
                "The source should be on Effects now, so the Ambience slider must not touch it.");
        }
    }
}
