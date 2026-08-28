using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The §2.2 redundant-encoding rule, and the one-caption rule that goes with it.
    /// <para>
    /// Hard rule 4 reserves red, amber and green for verdict state, which concentrates the game's
    /// most important information onto the axis red-green colourblindness removes (#41). The answer
    /// is that hue is never alone: every severity ships a glyph and a word beside its colour, and the
    /// colours themselves are separated in brightness so a greyscale reading still ranks them. Those
    /// are visual claims, so they are <i>computed</i> here rather than asserted — this file is the
    /// greyscale screenshot.
    /// </para>
    /// <para>
    /// Nothing here needs a panel, a scene or a running Editor. It is arithmetic over static colour
    /// values plus reflection over a component's signature, so it survives a headless run.
    /// </para>
    /// </summary>
    public sealed class SignalEncodingTests
    {
        private static readonly ReadingSeverity[] Severities =
        {
            ReadingSeverity.Normal, ReadingSeverity.Caution, ReadingSeverity.Critical
        };

        private static readonly Verdict[] Verdicts =
        {
            Verdict.Normal, Verdict.Monitor, Verdict.Critical
        };

        // -------------------------------------------------------------------------------------------
        // Brightness. The honest test of "readable with hue removed".
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: desaturate the results table and the three states are still ranked.
        /// <para>
        /// This is the one that used to fail. Amber sat at 0.722 and green at 0.642 — 0.08 apart,
        /// which in greyscale is the same grey. A deuteranope had CAUTION and NORMAL rendered
        /// identically in <i>both</i> channels the table offered, and the word beside them was doing
        /// all the work on its own.
        /// </para>
        /// <para>
        /// If this fails, re-pick the colour. Do not lower
        /// <see cref="SignalPalette.MinimumLuminanceSeparation"/> — the number is the requirement,
        /// not the assertion.
        /// </para>
        /// </summary>
        [Test]
        public void SignalColours_AreSeparatedInBrightness_NotOnlyInHue()
        {
            var swatches = new (string Name, Color Colour)[]
            {
                ("CRITICAL", SignalPalette.Critical),
                ("CAUTION", SignalPalette.Caution),
                ("NORMAL", SignalPalette.Normal)
            };

            for (int i = 0; i < swatches.Length; i++)
            {
                for (int j = i + 1; j < swatches.Length; j++)
                {
                    float a = SignalPalette.Luminance(swatches[i].Colour);
                    float b = SignalPalette.Luminance(swatches[j].Colour);

                    Assert.GreaterOrEqual(Mathf.Abs(a - b), SignalPalette.MinimumLuminanceSeparation,
                        $"{swatches[i].Name} ({a:F3}) and {swatches[j].Name} ({b:F3}) are " +
                        $"{Mathf.Abs(a - b):F3} apart in luminance. In a greyscale reading of the " +
                        "results table they are the same swatch, so a player who cannot separate " +
                        "them by hue has nothing left. Re-pick the colour.");
                }
            }
        }

        /// <summary>
        /// The verdict colours must be verdict colours. A signal hue borrowed for chrome spends the
        /// instant read that hard rule 4 exists to buy, and it does it silently.
        /// <para>
        /// Judged on hue and saturation rather than on RGB distance: "is this amber" is a question
        /// about where the colour sits on the wheel, and a near-grey is not any hue at all however
        /// close its channels happen to land. A light passes by being desaturated <i>or</i> by
        /// sitting clear of all three signal hues.
        /// </para>
        /// </summary>
        [Test]
        public void AMachinesStatusLight_NeverBorrowsASignalColour()
        {
            const float greyBelow = 0.25f;
            const float clearOfHue = 0.10f;

            var lights = new (string Name, Color Colour)[]
            {
                ("running", MachineStation.RunningLight),
                ("result waiting", MachineStation.ResultLight),
                ("idle", MachineStation.IdleLight)
            };

            var signals = new (string Name, Color Colour)[]
            {
                ("CRITICAL", SignalPalette.Critical),
                ("CAUTION", SignalPalette.Caution),
                ("NORMAL", SignalPalette.Normal)
            };

            foreach (var light in lights)
            {
                Color.RGBToHSV(light.Colour, out float hue, out float saturation, out _);
                if (saturation < greyBelow) continue; // a neutral is not borrowing anybody's hue

                foreach (var signal in signals)
                {
                    Color.RGBToHSV(signal.Colour, out float signalHue, out _, out _);

                    float apart = Mathf.Abs(hue - signalHue);
                    apart = Mathf.Min(apart, 1f - apart); // the wheel wraps

                    Assert.Greater(apart, clearOfHue,
                        $"The '{light.Name}' light sits {apart:F3} of a turn from {signal.Name} at " +
                        $"saturation {saturation:F2}. A machine that glows amber for 'busy' teaches " +
                        "the player to stop reading amber as 'caution' on a result (§2.2).");
                }
            }
        }

        /// <summary>
        /// Promise: the light is legible with hue removed, because the three states differ in
        /// brightness before the emission multiplier and the pulse are applied on top.
        /// </summary>
        [Test]
        public void AMachinesStatusLight_SeparatesItsStatesInBrightness()
        {
            float running = SignalPalette.Luminance(MachineStation.RunningLight);
            float result = SignalPalette.Luminance(MachineStation.ResultLight);
            float idle = SignalPalette.Luminance(MachineStation.IdleLight);

            Assert.GreaterOrEqual(Mathf.Abs(running - idle), SignalPalette.MinimumLuminanceSeparation,
                $"running {running:F3} against idle {idle:F3}");
            Assert.GreaterOrEqual(Mathf.Abs(result - running), SignalPalette.MinimumLuminanceSeparation,
                $"result waiting {result:F3} against running {running:F3}");
            Assert.GreaterOrEqual(Mathf.Abs(result - idle), SignalPalette.MinimumLuminanceSeparation,
                $"result waiting {result:F3} against idle {idle:F3}");
        }

        // -------------------------------------------------------------------------------------------
        // The second and third channels.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every severity is nameable and markable, and no two share either. A duplicated glyph is a
        /// channel that looks present and carries nothing.
        /// </summary>
        [Test]
        public void EverySeverity_HasItsOwnGlyphAndItsOwnWord()
        {
            AssertDistinct("severity glyph", Severities, SignalPalette.Glyph);
            AssertDistinct("severity label", Severities, SignalPalette.Label);
            AssertDistinct("verdict glyph", Verdicts, SignalPalette.Glyph);
            AssertDistinct("verdict label", Verdicts, SignalPalette.Label);
        }

        /// <summary>
        /// <see cref="SignalPalette.Marked(ReadingSeverity)"/> is what a border or a light is paired
        /// with, so it has to be both channels and not just the prettier one.
        /// </summary>
        [Test]
        public void Marked_CarriesTheGlyphAndTheWord()
        {
            foreach (var severity in Severities)
            {
                string marked = SignalPalette.Marked(severity);
                StringAssert.Contains(SignalPalette.Glyph(severity), marked);
                StringAssert.Contains(SignalPalette.Label(severity), marked);
            }

            foreach (var verdict in Verdicts)
            {
                string marked = SignalPalette.Marked(verdict);
                StringAssert.Contains(SignalPalette.Glyph(verdict), marked);
                StringAssert.Contains(SignalPalette.Label(verdict), marked);
            }
        }

        /// <summary>
        /// A marker that renders on the terminal but not on an instrument is missing exactly where
        /// the player is standing when they read a fresh number. Every glyph character must have a
        /// bitmap in <see cref="PixelFont"/>, which is the only font a machine screen has.
        /// </summary>
        [Test]
        public void EveryGlyph_RastersOnAnInstrumentScreen()
        {
            var markers = new List<string> { SignalPalette.UnknownGlyph };
            foreach (var severity in Severities) markers.Add(SignalPalette.Glyph(severity));
            foreach (var verdict in Verdicts) markers.Add(SignalPalette.Glyph(verdict));

            string blank = PixelFont.Glyph(' ');

            foreach (string marker in markers)
            {
                Assert.IsFalse(string.IsNullOrEmpty(marker), "A severity with no marker has one channel.");

                foreach (char c in marker)
                {
                    Assert.AreNotEqual(blank, PixelFont.Glyph(c),
                        $"'{c}' has no bitmap in PixelFont, so it rasters as a blank on every " +
                        "instrument screen. Pick a marker from the font's character set.");
                }
            }
        }

        /// <summary>
        /// MONITOR is a call the player made; CAUTION is a number an instrument produced. Printing
        /// one where the other belongs tells the player they filed something they did not.
        /// </summary>
        [Test]
        public void AFiledMonitor_IsNotWordedAsACautionReading()
        {
            Assert.AreEqual("MONITOR", SignalPalette.Label(Verdict.Monitor));
            Assert.AreEqual("CAUTION", SignalPalette.Label(ReadingSeverity.Caution));
        }

        // -------------------------------------------------------------------------------------------
        // #56 — the two captions that could disagree.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: two players standing at the same instrument read the same caption.
        /// <para>
        /// <see cref="MachineDisplay"/> had two <c>Show</c> overloads — one captioning with the
        /// host's paper label, one with the sample id — and both were documented as correct. This
        /// pins the structural fix rather than the behaviour: there is one entry point, so there is
        /// nothing left to disagree. If a second overload comes back, the caption split comes back
        /// with it.
        /// </para>
        /// </summary>
        [Test]
        public void AnInstrumentScreen_HasOneWayToDrawAFinishedReading()
        {
            var shows = new List<MethodInfo>();
            foreach (var method in typeof(MachineDisplay).GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name == "Show") shows.Add(method);
            }

            Assert.AreEqual(1, shows.Count,
                "MachineDisplay.Show must not be overloaded (#56). Two overloads is how the host " +
                "and a client ended up captioning the same run differently; derive the caption in " +
                "one place instead.");

            var parameters = shows[0].GetParameters();
            Assert.AreEqual(typeof(SampleId), parameters[parameters.Length - 1].ParameterType,
                "The screen must be handed the sample's id and resolve the name itself. Passing a " +
                "SampleState is what let the host path reach for a field the client path could not.");
        }

        /// <summary>
        /// The caption is a pure function of the run and the name the lab files it under, and a run
        /// that belongs to the instrument rather than to a sample names itself — otherwise a panel of
        /// plausible numbers with no sample beside it reads as somebody else's sample.
        /// </summary>
        [Test]
        public void AStandardAndABlank_NameThemselvesRatherThanASample()
        {
            var blank = new TestResult { IsBlank = true };
            var standard = new TestResult { IsReference = true };
            var ordinary = new TestResult();

            Assert.AreEqual(RunCaption.Blank, RunCaption.For(blank, "WERK-1 QUENCH 1"));
            Assert.AreEqual(RunCaption.Standard, RunCaption.For(standard, "WERK-1 QUENCH 1"));
            Assert.AreEqual("WERK-1 QUENCH 1", RunCaption.For(ordinary, "WERK-1 QUENCH 1"));

            Assert.AreEqual(RunCaption.Unnamed, RunCaption.For(ordinary, null),
                "A run this process cannot name yet must say so, the same way on both sides, " +
                "rather than one of them inventing a name.");
        }

        /// <summary>
        /// The host used to caption with <see cref="SampleState.EquipmentTag"/> and everything else
        /// with <see cref="SampleState.RecordTag"/>. Since #73 removed booking-in those are the same
        /// string, which is what makes #56's option 1 nearly free — but only while it stays true, so
        /// pin it here where the caption is drawn rather than only in the lifecycle suite.
        /// </summary>
        [Test]
        public void TheInstrumentCaptionsARunTheWayTheLabFilesIt()
        {
            var sample = new SampleState
            {
                Id = new SampleId(4),
                EquipmentTag = "HALLE-3 MARTEMPER 2"
            };

            var result = new TestResult();

            Assert.AreEqual(RunCaption.For(result, sample.EquipmentTag),
                            RunCaption.For(result, sample.RecordTag),
                "The bottle's label and the name on the record are one string (#73). If they ever " +
                "part again, the instrument and the terminal start calling the same run two things.");
        }

        // -------------------------------------------------------------------------------------------

        private static void AssertDistinct<T>(string what, T[] values, Func<T, string> project)
        {
            var seen = new Dictionary<string, T>();

            foreach (var value in values)
            {
                string projected = project(value);

                Assert.IsFalse(string.IsNullOrWhiteSpace(projected),
                    $"{value} has no {what}, so it is carried by colour alone.");

                if (seen.TryGetValue(projected, out var clash))
                {
                    Assert.Fail($"{value} and {clash} share the {what} '{projected}'. A channel " +
                                "that cannot tell them apart is not a second channel.");
                }

                seen[projected] = value;
            }
        }
    }
}
