using System;

namespace Residue.Editor.Chemistry
{
    /// <summary>
    /// Everything <see cref="SampleDump"/> needs to produce a report, as plain data.
    /// <para>
    /// Definitions are named by id rather than held as references on purpose. The window survives a
    /// domain reload with its inputs intact, the whole request prints into the report as text a
    /// balance discussion can be pasted into, and the EditMode suite can pin a run without touching
    /// the AssetDatabase.
    /// </para>
    /// <para>
    /// <see cref="SerializableAttribute"/> so the window's inputs survive a domain reload. A
    /// recompile that silently reset the seed would make "the same seed twice" a thing you cannot
    /// check by hand.
    /// </para>
    /// </summary>
    [Serializable]
    public struct SampleDumpRequest
    {
        /// <summary>Seeds <c>Residue.Chemistry.Rng</c>. The same value must reproduce the report exactly.</summary>
        public int Seed;

        /// <summary><c>EquipmentProfileDef.Id</c>. Decides every threshold the sample is scored against.</summary>
        public string ProfileId;

        public string EquipmentTag;
        public int Day;
        public float HoursSinceOilChange;

        /// <summary><c>FaultDef.Id</c> to force. Null or empty rolls from the pool.</summary>
        public string FaultId;

        /// <summary>Pin progression instead of rolling it from the fault's severity band.</summary>
        public bool ForceSeverity;

        /// <summary>0..1 progression, used only when <see cref="ForceSeverity"/> is set.</summary>
        public float Severity01;

        public bool ForceHealthy;

        /// <summary>Land the sample in the Caution band — §6.3's ambiguity budget.</summary>
        public bool ForceBorderline;

        public float HealthyChance;
        public float CascadeChance;

        /// <summary>
        /// Opens on the keystone trap. Quench additive exhaustion moves nothing but cooling-curve
        /// quantities, so six of the seven instruments report a clean panel — which is the one thing
        /// this tool exists to make visible, and it should be visible before anyone has typed
        /// anything.
        /// </summary>
        public static SampleDumpRequest Default() => new()
        {
            Seed = 20260830,
            ProfileId = "quench_oil_accelerated",
            EquipmentTag = "WERK-1 QUENCH 1",
            Day = 1,
            HoursSinceOilChange = 2500f,
            FaultId = "additive_exhaustion",
            ForceSeverity = true,
            Severity01 = 0.75f,
            ForceHealthy = false,
            ForceBorderline = false,
            HealthyChance = 0.35f,
            CascadeChance = 0f
        };
    }
}
