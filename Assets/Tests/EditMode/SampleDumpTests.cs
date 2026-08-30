using NUnit.Framework;
using Residue.Editor.Chemistry;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the one promise the debug dump makes that nothing else can catch: a seed names a
    /// sample, forever.
    /// <para>
    /// If the report ever drifts run to run, a balance discussion built on a pasted dump is quoting
    /// a sample nobody else can reproduce — and the tool that was supposed to make the chemistry
    /// inspectable is the thing lying. That is hard rule 1 territory, so it is asserted rather than
    /// assumed. It is testable at all only because <see cref="SampleDump"/> returns a string and
    /// <c>SampleDumpWindow</c> merely displays it.
    /// </para>
    /// </summary>
    public sealed class SampleDumpTests
    {
        /// <summary>
        /// The acceptance criterion, literally: "same seed produces identical output across runs".
        /// Byte-identical, not merely equivalent — a locale-sensitive number format or a dictionary
        /// enumeration order would show up here and nowhere else.
        /// </summary>
        [Test]
        public void SameRequest_ProducesByteIdenticalReportTwice()
        {
            var request = SampleDumpRequest.Default();

            string first = SampleDump.Build(request);
            string second = SampleDump.Build(request);

            Assert.AreEqual(first, second,
                "Two dumps of the same seed differed. Something in the report depends on state that " +
                "is not the seed — check number formatting and any dictionary being enumerated.");
        }

        /// <summary>
        /// Without this, a report that ignored the seed entirely would pass the test above. The
        /// determinism claim only means something if the seed is doing work.
        /// </summary>
        [Test]
        public void DifferentSeed_ProducesADifferentReport()
        {
            var request = SampleDumpRequest.Default();
            string first = SampleDump.Build(request);

            request.Seed = unchecked(request.Seed + 1);
            string second = SampleDump.Build(request);

            Assert.AreNotEqual(first, second,
                "Changing the seed changed nothing, so the report is not actually generated from it.");
        }

        /// <summary>
        /// The §4.3 keystone. Quench additive exhaustion moves nothing but cooling-curve quantities,
        /// so every conventional instrument reads a clean panel — and the whole point of this tool is
        /// that you can see that without running the suite. Asserted against ground truth rather than
        /// against a measured verdict, so instrument noise cannot make it flaky.
        /// </summary>
        [Test]
        public void AdditiveExhaustion_IsReportedAsVisibleOnlyToTheCoolingCurveTester()
        {
            var request = SampleDumpRequest.Default();
            request.ProfileId = "quench_oil_accelerated";
            request.FaultId = "additive_exhaustion";
            request.ForceSeverity = true;
            request.Severity01 = 1f;
            request.CascadeChance = 0f;

            string report = SampleDump.Build(request);

            StringAssert.Contains("additive_exhaustion", report);
            StringAssert.Contains($"{SampleDump.VisibleOnlyToLabel}", report);
            StringAssert.Contains("cooling_curve", report);
            StringAssert.DoesNotContain(SampleDump.InvisibleLabel, report,
                "Something abnormal is reported by no instrument at all. That is not a trap, it is a " +
                "fault the player could never have found — see hard rule 3.");
        }

        /// <summary>A bad id must say so and name the alternatives, not throw or return an empty pane.</summary>
        [Test]
        public void UnknownProfileId_ExplainsItselfAndListsTheRealOnes()
        {
            var request = SampleDumpRequest.Default();
            request.ProfileId = "no_such_profile";

            string report = SampleDump.Build(request);

            StringAssert.Contains("no_such_profile", report);
            StringAssert.Contains("quench_oil_accelerated", report);
        }
    }
}
