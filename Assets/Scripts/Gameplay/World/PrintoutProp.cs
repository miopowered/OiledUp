using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A results slip produced by an instrument. Physical, carryable, and losable.
    /// <para>
    /// Instruments used to write straight into the sample's history, which meant the numbers
    /// teleported to the terminal and the room was decoration. A printout has to be walked to the
    /// desk, and it competes with vials for your one pair of hands. §9 lists "too much reading, not
    /// enough doing" as a live risk; this is the fix.
    /// </para>
    /// A slip you drop and forget is data you paid for and did not get. That is deliberate — but it
    /// is only fair because the machine's own display shows the same values (see
    /// <see cref="MachineDisplay"/>), so nothing is hidden that could not be read.
    /// </summary>
    public sealed class PrintoutProp : Carryable
    {
        [SerializeField] private MeshRenderer paper;

        public SampleId SampleId { get; private set; }
        public TestResult Result { get; private set; }
        public string MachineName { get; private set; } = "instrument";
        public string EquipmentTag { get; private set; } = "UNKNOWN";

        public override string DisplayName => $"{MachineName} printout — {EquipmentTag}";

        public void Bind(SampleId sampleId, TestResult result, string machineName, string equipmentTag)
        {
            SampleId = sampleId;
            Result = result;
            MachineName = string.IsNullOrEmpty(machineName) ? "instrument" : machineName;
            EquipmentTag = string.IsNullOrEmpty(equipmentTag) ? "UNKNOWN" : equipmentTag;
            name = $"Printout_{sampleId}_{MachineName}";
        }

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried != null) return "Hands full";
            return Result != null && Result.IsBlank
                ? $"Take blank slip — {MachineName}"
                : $"Take printout — {EquipmentTag}";
        }
    }
}
