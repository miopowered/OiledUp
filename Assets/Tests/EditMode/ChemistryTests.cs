using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The §5.6 suite. These exist to protect the design, not the code: each one guards a specific
    /// claim the game makes to the player. If one of these goes red, a fault archetype has stopped
    /// being diagnosable and the chemistry is lying — which §1.1 says we must never do.
    /// </summary>
    public sealed class ChemistryTests
    {
        private ContentSet content;
        private SampleGenerator generator;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            generator = new SampleGenerator(content.AllFaults);
        }

        [TearDown]
        public void TearDown()
        {
            if (content == null) return;
            foreach (var o in AllDefinitions()) Object.DestroyImmediate(o);
            content = null;
        }

        private IEnumerable<Object> AllDefinitions() =>
            content.Elements.Values.Cast<Object>()
                .Concat(content.Causes.Values)
                .Concat(content.Profiles.Values)
                .Concat(content.Faults.Values)
                .Concat(content.Machines.Values);

        // -----------------------------------------------------------------------------------------
        // 1. A healthy unit reads normal. Anything else punishes the player for nothing.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void HealthySample_ReadsNormalOnEveryTrackedElement()
        {
            var rng = new Rng(20260823);

            foreach (var profile in content.Profiles.Values)
            {
                for (int i = 0; i < 250; i++)
                {
                    var req = GenerationRequest.Default(profile, "TEST-01", 1);
                    req.ForceHealthy = true;
                    req.HoursSinceOilChange = rng.Range(0f, profile.DefaultOilChangeHours);

                    var sample = generator.Generate(req, ref rng);

                    foreach (var kv in sample.Truth.TrueValues)
                    {
                        Assert.AreEqual(
                            ReadingSeverity.Normal,
                            profile.Evaluate(kv.Key, kv.Value),
                            $"Healthy sample on '{profile.Id}' (iteration {i}, {req.HoursSinceOilChange:F0} h) " +
                            $"read {kv.Value:F4} for '{kv.Key}', which is not Normal.");
                    }
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // 2. Every fault, fully developed, is unambiguously critical somewhere.
        //    A fault that can top out at Caution is a fault the player can never correctly call.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void EveryFault_AtMaxSeverity_ProducesACriticalReading()
        {
            var rng = new Rng(4242);

            foreach (var fault in content.Faults.Values)
            {
                foreach (var profile in fault.ValidOn)
                {
                    for (int i = 0; i < 60; i++)
                    {
                        var req = GenerationRequest.Default(profile, "TEST-02", 1);
                        req.ForcedFault = fault;
                        req.ForcedSeverity01 = 1f;
                        req.CascadeChance = 0f;

                        var sample = generator.Generate(req, ref rng);

                        bool anyCritical = sample.Truth.TrueValues.Any(
                            kv => profile.Evaluate(kv.Key, kv.Value) == ReadingSeverity.Critical);

                        Assert.IsTrue(anyCritical,
                            $"Fault '{fault.Id}' at full severity on '{profile.Id}' produced nothing Critical " +
                            $"(iteration {i}). Readings: {Describe(sample.Truth, profile)}");
                    }
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // 3. Bearing overlay wear vs bushing wear. Both raise copper; only bearing wear raises lead.
        //    §4.3 calls this the novice trap, so the discriminator must be reliable, not vibes.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void BearingWear_AndBushingWear_AreSeparableByLeadCopperRatio()
        {
            const float discriminator = 0.4f;
            const int trials = 1000;

            var profile = content.Profile("diesel_engine_heavy");
            var bearing = content.Fault("bearing_overlay_wear");
            var bushing = content.Fault("bushing_wear");

            int correct = 0;
            var rng = new Rng(777);

            for (int i = 0; i < trials; i++)
            {
                bool expectBearing = (i % 2) == 0;
                var req = GenerationRequest.Default(profile, "TEST-03", 1);
                req.ForcedFault = expectBearing ? bearing : bushing;
                req.CascadeChance = 0f;

                var truth = generator.Generate(req, ref rng).Truth;

                float pb = truth.GetTrue("Pb");
                float cu = truth.GetTrue("Cu");
                bool calledBearing = cu > 0f && (pb / cu) > discriminator;

                if (calledBearing == expectBearing) correct++;
            }

            float accuracy = correct / (float)trials;
            Assert.Greater(accuracy, 0.95f,
                $"Pb/Cu ratio separated bearing from bushing wear only {accuracy:P1} of the time. " +
                "The §4.3 novice trap is unfair if the discriminator is not reliable.");
        }

        // -----------------------------------------------------------------------------------------
        // 4. Gear spalling: a clean ICP and a damning ferrography run on the SAME vial.
        //    This is the trap that teaches "a clean spectrometer is not a clean sample".
        // -----------------------------------------------------------------------------------------

        [Test]
        public void GearSpalling_LooksCleanOnIcp_AndCriticalOnFerrography()
        {
            var profile = content.Profile("gearbox_industrial");
            var spalling = content.Fault("gear_spalling");
            var rng = new Rng(31337);

            for (int i = 0; i < 100; i++)
            {
                var req = GenerationRequest.Default(profile, "TEST-04", 1);
                req.ForcedFault = spalling;
                req.ForcedSeverity01 = 1f;
                req.CascadeChance = 0f;

                var sample = generator.Generate(req, ref rng);

                var icp = FreshMachine("icp");
                var ferro = FreshMachine("ferrography");

                var icpResult = MeasurementPipeline.Run(sample.State, sample.Truth, icp, 1, ref rng);
                var ferroResult = MeasurementPipeline.Run(sample.State, sample.Truth, ferro, 1, ref rng);

                Assert.IsFalse(icpResult.Values.ContainsKey("FeLarge"),
                    "The ICP must not report large ferrous debris at all — it is blind to it.");

                bool icpCritical = icpResult.Values.Any(
                    kv => profile.Evaluate(kv.Key, kv.Value) == ReadingSeverity.Critical);
                Assert.IsFalse(icpCritical,
                    $"ICP flagged gear spalling as Critical on iteration {i}; the trap only works if " +
                    $"spectroscopy looks survivable. Readings: {Describe(icpResult)}");

                Assert.IsTrue(ferroResult.Values.TryGetValue("FeLarge", out float large),
                    "Ferrography must report large ferrous debris.");
                Assert.AreEqual(ReadingSeverity.Critical, profile.Evaluate("FeLarge", large),
                    $"Ferrography read FeLarge = {large:F1}, which is not Critical on '{profile.Id}'.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // 5. Contamination must actually bite. A skipped flush has to be able to manufacture a
        //    false positive on a genuinely healthy unit, or §5.2 is decoration.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void SkippedFlush_AfterCriticalSamples_ManufacturesAFalsePositive()
        {
            var profile = content.Profile("diesel_engine_heavy");
            var coolant = content.Fault("coolant_leak");
            var titrator = FreshMachine("karl_fischer");
            var rng = new Rng(90210);

            int runsUntilFalsePositive = -1;

            for (int run = 1; run <= 10; run++)
            {
                var dirty = GenerationRequest.Default(profile, $"DIRTY-{run}", 1);
                dirty.ForcedFault = coolant;
                dirty.ForcedSeverity01 = 1f;
                dirty.CascadeChance = 0f;
                var contaminated = generator.Generate(dirty, ref rng);
                MeasurementPipeline.Run(contaminated.State, contaminated.Truth, titrator, 1, ref rng);

                // A genuinely healthy unit, measured on the machine nobody flushed.
                var cleanReq = GenerationRequest.Default(profile, "CLEAN-01", 1);
                cleanReq.ForceHealthy = true;
                var clean = generator.Generate(cleanReq, ref rng);

                var probe = MeasurementPipeline.Run(clean.State, clean.Truth, titrator, 1, ref rng);

                Assert.AreEqual(ReadingSeverity.Normal, profile.Evaluate("Water", clean.Truth.GetTrue("Water")),
                    "Sanity: the probe sample must genuinely be healthy.");

                if (profile.Evaluate("Water", probe.Values["Water"]) == ReadingSeverity.Critical)
                {
                    runsUntilFalsePositive = run;
                    break;
                }
            }

            Assert.Greater(runsUntilFalsePositive, 0,
                "Ten unflushed critical samples never pushed a healthy sample to Critical. " +
                "Carryover is too weak for §5.2 to be a real mechanic.");

            // ...and the tell has to work: cleaning must make the same healthy sample read Normal again.
            titrator.Clean();

            var afterReq = GenerationRequest.Default(profile, "CLEAN-02", 1);
            afterReq.ForceHealthy = true;
            var after = generator.Generate(afterReq, ref rng);
            var afterResult = MeasurementPipeline.Run(after.State, after.Truth, titrator, 1, ref rng);

            Assert.AreEqual(ReadingSeverity.Normal, profile.Evaluate("Water", afterResult.Values["Water"]),
                "Cleaning the machine must restore a trustworthy reading, or the player is being " +
                "punished for something they could not fix.");
        }

        [Test]
        public void BlankRun_RevealsResidue_WithoutConsumingSample()
        {
            var profile = content.Profile("diesel_engine_heavy");
            var coolant = content.Fault("coolant_leak");
            var titrator = FreshMachine("karl_fischer");
            var rng = new Rng(5150);

            var dirtyReq = GenerationRequest.Default(profile, "DIRTY-01", 1);
            dirtyReq.ForcedFault = coolant;
            dirtyReq.ForcedSeverity01 = 1f;
            dirtyReq.CascadeChance = 0f;
            var dirty = generator.Generate(dirtyReq, ref rng);

            var blankBefore = MeasurementPipeline.RunBlank(titrator, 1, ref rng);
            Assert.AreEqual(0f, blankBefore.Values["Water"], 1e-4f, "A fresh machine must blank clean.");

            MeasurementPipeline.Run(dirty.State, dirty.Truth, titrator, 1, ref rng);

            var blankAfter = MeasurementPipeline.RunBlank(titrator, 1, ref rng);
            Assert.Greater(blankAfter.Values["Water"], 0f,
                "A blank must reveal residue, otherwise the player has no way to check before trusting a result.");
            Assert.AreEqual(0f, blankAfter.VolumeConsumedMl,
                "A blank consumes machine time and solvent, never sample volume.");
        }

        // -----------------------------------------------------------------------------------------
        // 6. Ground truth must be structurally unable to reach a client.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void SampleState_CannotReachGroundTruth()
        {
            var stateType = typeof(SampleState);
            var truthType = typeof(SampleGroundTruth);

            foreach (var f in stateType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.AreNotEqual(truthType, f.FieldType,
                    $"SampleState.{f.Name} exposes ground truth. Replicating SampleState would leak the answers.");
            }

            foreach (var p in stateType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.AreNotEqual(truthType, p.PropertyType,
                    $"SampleState.{p.Name} exposes ground truth.");
            }

            Assert.IsNull(stateType.GetField("TrueValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                "SampleState must not carry TrueValues; that is what SampleGroundTruth is for.");
        }

        [Test]
        public void NetworkLayer_NeverMentionsGroundTruth()
        {
            var netAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Residue.Net");

            if (netAssembly == null)
            {
                Assert.Pass("Residue.Net has no code yet; this guard activates with the first netcode type.");
                return;
            }

            var truthType = typeof(SampleGroundTruth);
            var offenders = new List<string>();

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in netAssembly.GetTypes())
            {
                foreach (var f in type.GetFields(all))
                {
                    if (Mentions(f.FieldType, truthType)) offenders.Add($"field {type.Name}.{f.Name}");
                }

                foreach (var m in type.GetMethods(all))
                {
                    if (Mentions(m.ReturnType, truthType)) offenders.Add($"return of {type.Name}.{m.Name}");
                    foreach (var p in m.GetParameters())
                    {
                        if (Mentions(p.ParameterType, truthType))
                            offenders.Add($"parameter '{p.Name}' of {type.Name}.{m.Name}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Residue.Net must never touch SampleGroundTruth. Offenders:\n  " + string.Join("\n  ", offenders));
        }

        private static bool Mentions(Type candidate, Type forbidden)
        {
            if (candidate == null) return false;
            if (candidate == forbidden) return true;
            if (candidate.IsArray) return Mentions(candidate.GetElementType(), forbidden);
            if (candidate.IsGenericType)
                return candidate.GetGenericArguments().Any(a => Mentions(a, forbidden));
            return false;
        }

        // -- helpers ------------------------------------------------------------------------------

        private MachineRuntimeState FreshMachine(string id) => new()
        {
            InstanceId = $"{id}-test",
            Def = content.Machine(id)
        };

        private static string Describe(SampleGroundTruth truth, EquipmentProfileDef profile) =>
            string.Join(", ", truth.TrueValues
                .Where(kv => profile.Evaluate(kv.Key, kv.Value) != ReadingSeverity.Normal)
                .Select(kv => $"{kv.Key}={kv.Value:F3}({profile.Evaluate(kv.Key, kv.Value)})"));

        private static string Describe(TestResult result) =>
            string.Join(", ", result.Values.Select(kv => $"{kv.Key}={kv.Value:F2}"));
    }
}
