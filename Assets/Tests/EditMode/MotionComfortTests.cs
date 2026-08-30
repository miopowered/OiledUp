using NUnit.Framework;
using Residue.Gameplay.Settings;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The motion comfort settings (#54). These are accessibility controls rather than taste, which
    /// is what makes them worth pinning: the failure mode is not an ugly camera, it is a player who
    /// stops after five minutes with a headache and cannot fix it from the menu.
    /// <para>
    /// The issue's own wording sets the bar — "off has to mean off, not reduced" — so most of what is
    /// below is about zero meaning exactly zero, and about a value the player chose surviving.
    /// </para>
    /// </summary>
    public sealed class MotionComfortTests
    {
        [SetUp]
        public void SetUp() => GameSettings.Load();

        [TearDown]
        public void TearDown()
        {
            GameSettings.HeadBobScale = 1f;
            GameSettings.CameraShakeScale = 1f;
        }

        /// <summary>
        /// Promise: zero is off, and the setting is a range rather than a switch.
        /// <para>
        /// #54 asks for a scale specifically because the players who need this are spread across a
        /// range — someone who can play with a third of the motion should not have to choose between
        /// queasy and floating.
        /// </para>
        /// </summary>
        [Test]
        public void TheComfortScales_GoAllTheWayToZero_AndHoldAMiddle()
        {
            GameSettings.HeadBobScale = 0f;
            GameSettings.CameraShakeScale = 0f;

            Assert.AreEqual(0f, GameSettings.HeadBobScale, "Off has to mean off, not reduced.");
            Assert.AreEqual(0f, GameSettings.CameraShakeScale, "Off has to mean off, not reduced.");

            GameSettings.HeadBobScale = 0.35f;
            Assert.AreEqual(0.35f, GameSettings.HeadBobScale, 1e-4f,
                "A scale that only had ends would be the switch #54 asked to replace.");
        }

        /// <summary>Promise: a slider cannot be driven outside the range the screen offers.</summary>
        [Test]
        public void TheComfortScales_AreClamped()
        {
            GameSettings.HeadBobScale = 4f;
            Assert.AreEqual(1f, GameSettings.HeadBobScale, 1e-4f);

            GameSettings.CameraShakeScale = -2f;
            Assert.AreEqual(0f, GameSettings.CameraShakeScale, 1e-4f);
        }

        /// <summary>
        /// Promise: the two are independent.
        /// <para>
        /// They are separate complaints. Head bob is your own footsteps; camera shake is the view
        /// moving when you did not ask it to. A player can be fine with one and not the other, and a
        /// screen offering two sliders that moved together would be lying about what it controls.
        /// </para>
        /// </summary>
        [Test]
        public void HeadBob_AndCameraShake_AreSeparate()
        {
            GameSettings.HeadBobScale = 0f;
            GameSettings.CameraShakeScale = 1f;

            Assert.AreEqual(0f, GameSettings.HeadBobScale);
            Assert.AreEqual(1f, GameSettings.CameraShakeScale,
                "Turning the bob off must not touch the landing dip.");
        }

        /// <summary>
        /// Promise: an existing player who had turned head bob off keeps it off.
        /// <para>
        /// #54 replaced the old boolean with a scale, and the old key is still on disk for anyone who
        /// played before that. Silently restoring the bob on update is the one migration failure that
        /// actually hurts here — it hands a motion-sickness trigger back to the person who went
        /// looking for the switch in the first place, at the moment they least expect it.
        /// </para>
        /// </summary>
        [Test]
        public void APlayerWhoHadTurnedBobOff_DoesNotGetItBackOnUpdate()
        {
            string oldKey = GameSettings.LegacyHeadBobKey;
            string newKey = GameSettings.HeadBobScaleKey;

            bool hadScale = PlayerPrefs.HasKey(newKey);
            float savedScale = PlayerPrefs.GetFloat(newKey, 1f);
            int savedBool = PlayerPrefs.GetInt(oldKey, 1);

            try
            {
                // A profile from before the scale existed: the switch is off and nothing else.
                PlayerPrefs.DeleteKey(newKey);
                PlayerPrefs.SetInt(oldKey, 0);

                Assert.AreEqual(0f, GameSettings.ReadHeadBobScale(), 1e-4f,
                    "The old off switch has to survive as a zero scale.");

                PlayerPrefs.SetInt(oldKey, 1);
                Assert.AreEqual(1f, GameSettings.ReadHeadBobScale(), 1e-4f,
                    "And a profile that had the bob on has to come back on.");

                // Once a scale exists it wins outright, or every launch would be dragged back to
                // whatever the abandoned switch happened to say.
                PlayerPrefs.SetFloat(newKey, 0.4f);
                Assert.AreEqual(0.4f, GameSettings.ReadHeadBobScale(), 1e-4f,
                    "A stored scale has to outrank the legacy switch.");
            }
            finally
            {
                if (hadScale) PlayerPrefs.SetFloat(newKey, savedScale);
                else PlayerPrefs.DeleteKey(newKey);
                PlayerPrefs.SetInt(oldKey, savedBool);
            }
        }
    }
}
