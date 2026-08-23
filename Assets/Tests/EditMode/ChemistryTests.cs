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
    /// claim the game makes to the player. If one goes red, a fault archetype has stopped being
    /// diagnosable and the chemistry is lying — which §1.1 says we must never do.
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
        // 1. A serviceable tank reads normal. Anything else condemns good oil for nothing.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void HealthySample_ReadsNormalOnEveryTrackedElement()
        {
            var rng = new Rng(20260823);

            foreach (var profile in content.Profiles.Values)
            {
                for (int i = 0; i < 250; i++)
                {
                    var req = GenerationRequest.Default(profile, "WERK-1 QUENCH 1", 1);
                    req.ForceHealthy = true;
                    req.HoursSinceOilChange = rng.Range(0f, profile.DefaultOilChangeHours);

                    var sample = generator.Generate(req, ref rng);

                    foreach (var kv in sample.Truth.TrueValues)
                    {
                        Assert.AreEqual(
                            ReadingSeverity.Normal,
                            profile.Evaluate(kv.Key, kv.Value),
                            $"Serviceable oil on '{profile.Id}' (iteration {i}, " +
                            $"{req.HoursSinceOilChange:F0} h) read {kv.Value:F4} for '{kv.Key}', not Normal.");
                    }
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // 2. Every fault, fully developed, is unambiguously critical somewhere.
        //    A fault that tops out at Caution is one the player can never correctly call.
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
                            $"Fault '{fault.Id}' at full severity on '{profile.Id}' produced nothing " +
                            $"Critical (iteration {i}). Readings: {Describe(sample.Truth, profile)}");
                    }
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // 3. Water ingress vs hydraulic carryover. Both drop the flash point; only one adds water.
        //    §4.3 calls this the novice trap, so the discriminator must be reliable, not vibes.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void WaterIngress_AndHydraulicCarryover_AreSeparableByWaterContent()
        {
            const float discriminator = 800f; // ppm
            const int trials = 1000;

            var profile = content.Profile("quench_oil_cold");
            var water = content.Fault("water_ingress");
            var hydraulic = content.Fault("hydraulic_carryover");

            int correct = 0;
            var rng = new Rng(777);

            for (int i = 0; i < trials; i++)
            {
                bool expectWater = (i % 2) == 0;
                var req = GenerationRequest.Default(profile, "TEST-03", 1);
                req.ForcedFault = expectWater ? water : hydraulic;
                req.CascadeChance = 0f;

                var truth = generator.Generate(req, ref rng).Truth;

                bool calledWater = truth.GetTrue("Water") > discriminator;
                if (calledWater == expectWater) correct++;
            }

            float accuracy = correct / (float)trials;
            Assert.Greater(accuracy, 0.95f,
                $"Water content separated the two only {accuracy:P1} of the time. Both faults drop " +
                "the flash point, so if water does not discriminate the pair is unfair.");
        }

        // -----------------------------------------------------------------------------------------
        // 4. Additive exhaustion: a clean conventional panel and a damning cooling curve on the SAME
        //    oil. This is the trap that teaches "a clean panel is not a clean oil".
        // -----------------------------------------------------------------------------------------

        [Test]
        public void AdditiveExhaustion_ReadsCleanOnTheConventionalPanel_AndCriticalOnTheCoolingCurve()
        {
            var profile = content.Profile("quench_oil_accelerated");
            var exhaustion = content.Fault("additive_exhaustion");
            var rng = new Rng(31337);

            string[] conventional = { "karl_fischer", "viscometer", "flash_point", "tan_titrator", "centrifuge", "elemental" };

            for (int i = 0; i < 60; i++)
            {
                var req = GenerationRequest.Default(profile, "TEST-04", 1);
                req.ForcedFault = exhaustion;
                req.ForcedSeverity01 = 1f;
                req.CascadeChance = 0f;

                var sample = generator.Generate(req, ref rng);
                sample.State.VolumeMl = 500f; // enough for the whole panel in one pass

                foreach (string id in conventional)
                {
                    var machine = FreshMachine(id);
                    var result = MeasurementPipeline.Run(sample.State, sample.Truth, machine, 1, ref rng);
                    Assert.IsNotNull(result, $"'{id}' refused the sample.");

                    foreach (var kv in result.Values)
                    {
                        Assert.AreNotEqual(ReadingSeverity.Critical, profile.Evaluate(kv.Key, kv.Value),
                            $"'{id}' flagged {kv.Key}={kv.Value:F3} as Critical on iteration {i}. " +
                            "The trap only works if the conventional panel looks survivable.");
                    }

                    // The manual's CANNOT DETECT page is the fair warning; the data must match it.
                    foreach (string hidden in new[] { "CRmax", "TCRmax", "CR300", "T400" })
                        Assert.IsFalse(result.Values.ContainsKey(hidden),
                            $"'{id}' reported {hidden}. Only the cooling curve tester sees quench performance.");
                }

                var curve = FreshMachine("cooling_curve");
                var curveResult = MeasurementPipeline.Run(sample.State, sample.Truth, curve, 1, ref rng);

                Assert.IsTrue(curveResult.Values.TryGetValue("CR300", out float cr300),
                    "The cooling curve tester must report the rate at 300 C.");
                Assert.AreEqual(ReadingSeverity.Critical, profile.Evaluate("CR300", cr300),
                    $"Cooling rate at 300 C read {cr300:F2}, which is not Critical on '{profile.Id}'. " +
                    "That figure is what decides whether the customer's parts come out hard.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // 5. Contamination must bite. A skipped flush has to be able to manufacture a false positive
        //    on genuinely serviceable oil, or §5.2 is decoration.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void SkippedFlush_AfterCriticalSamples_ManufacturesAFalsePositive()
        {
            var profile = content.Profile("quench_oil_cold");
            var water = content.Fault("water_ingress");
            var titrator = FreshMachine("karl_fischer");
            var rng = new Rng(90210);

            int runsUntilFalsePositive = -1;

            for (int run = 1; run <= 10; run++)
            {
                var dirty = GenerationRequest.Default(profile, $"WET-{run}", 1);
                dirty.ForcedFault = water;
                dirty.ForcedSeverity01 = 1f;
                dirty.CascadeChance = 0f;
                var contaminated = generator.Generate(dirty, ref rng);
                MeasurementPipeline.Run(contaminated.State, contaminated.Truth, titrator, 1, ref rng);

                var cleanReq = GenerationRequest.Default(profile, "GOOD-01", 1);
                cleanReq.ForceHealthy = true;
                var clean = generator.Generate(cleanReq, ref rng);

                var probe = MeasurementPipeline.Run(clean.State, clean.Truth, titrator, 1, ref rng);

                Assert.AreEqual(ReadingSeverity.Normal, profile.Evaluate("Water", clean.Truth.GetTrue("Water")),
                    "Sanity: the probe sample must genuinely be serviceable.");

                if (profile.Evaluate("Water", probe.Values["Water"]) == ReadingSeverity.Critical)
                {
                    runsUntilFalsePositive = run;
                    break;
                }
            }

            Assert.Greater(runsUntilFalsePositive, 0,
                "Ten unflushed wet samples never pushed serviceable oil to Critical. Carryover is too " +
                "weak for §5.2 to be a real mechanic.");

            titrator.Clean();

            var afterReq = GenerationRequest.Default(profile, "GOOD-02", 1);
            afterReq.ForceHealthy = true;
            var after = generator.Generate(afterReq, ref rng);
            var afterResult = MeasurementPipeline.Run(after.State, after.Truth, titrator, 1, ref rng);

            Assert.AreEqual(ReadingSeverity.Normal, profile.Evaluate("Water", afterResult.Values["Water"]),
                "Flushing must restore a trustworthy reading, or the player is being punished for " +
                "something they could not fix.");
        }

        [Test]
        public void BlankRun_RevealsResidue_WithoutConsumingSample()
        {
            var profile = content.Profile("quench_oil_cold");
            var water = content.Fault("water_ingress");
            var titrator = FreshMachine("karl_fischer");
            var rng = new Rng(5150);

            var dirtyReq = GenerationRequest.Default(profile, "WET-01", 1);
            dirtyReq.ForcedFault = water;
            dirtyReq.ForcedSeverity01 = 1f;
            dirtyReq.CascadeChance = 0f;
            var dirty = generator.Generate(dirtyReq, ref rng);

            var blankBefore = MeasurementPipeline.RunBlank(titrator, 1, ref rng);
            Assert.AreEqual(0f, blankBefore.Values["Water"], 1e-4f, "A fresh instrument must blank clean.");

            MeasurementPipeline.Run(dirty.State, dirty.Truth, titrator, 1, ref rng);

            var blankAfter = MeasurementPipeline.RunBlank(titrator, 1, ref rng);
            Assert.Greater(blankAfter.Values["Water"], 0f,
                "A blank must reveal residue, otherwise the player has no way to check before " +
                "trusting a borderline result.");
            Assert.AreEqual(0f, blankAfter.VolumeConsumedMl,
                "A blank consumes instrument time and solvent, never sample volume.");
        }

        // -----------------------------------------------------------------------------------------
        // 6. The root-cause trap: the fault is upstream, not in the tank.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void CleanerCarryover_RootCauseIsTheWasherLine_NotTheTank()
        {
            var cleaner = content.Fault("cleaner_carryover");
            Assert.IsNotNull(cleaner, "Cleaner carryover must exist; it is the §4.3 root-cause trap.");
            Assert.IsNotNull(cleaner.RootCause);
            Assert.AreEqual("washer_line_carryover", cleaner.RootCause.Id,
                "Replacing the charge does not fix cleaner carryover. The washer does.");
        }

        [Test]
        public void EveryFault_KnowsWhatHappensWhenItIsMissed()
        {
            foreach (var fault in content.Faults.Values)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(fault.MissedConsequence),
                    $"Fault '{fault.Id}' has no missed-consequence text, so the incident report " +
                    "cannot tell the player what their verdict actually cost.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // 7. Ground truth must be structurally unable to reach a client.
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
                Assert.AreNotEqual(truthType, p.PropertyType, $"SampleState.{p.Name} exposes ground truth.");
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
            if (candidate.IsGenericType) return candidate.GetGenericArguments().Any(a => Mentions(a, forbidden));
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
    }
}
