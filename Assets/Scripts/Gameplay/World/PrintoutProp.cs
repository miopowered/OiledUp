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

        /// <summary>
        /// The host's handle for this slip (<see cref="Residue.Gameplay.Simulation.ResultSlips"/>).
        /// <para>
        /// Filing names the ticket and never the values. §3.1 forbids a client computing a test
        /// result, and a client that could post one instead would be the same hole with an extra
        /// step — so the numbers below are for reading at a glance, and the ticket is what the
        /// terminal actually sends.
        /// </para>
        /// </summary>
        public int Ticket { get; private set; }

        public SampleId SampleId { get; private set; }
        public TestResult Result { get; private set; }
        public string MachineName { get; private set; } = "instrument";
        public string EquipmentTag { get; private set; } = "UNKNOWN";

        public override string DisplayName => $"{MachineName} printout — {EquipmentTag}";

        public void Bind(int ticket, SampleId sampleId, TestResult result, string machineName,
                         string equipmentTag)
        {
            Ticket = ticket;
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

        public override string UseHint => "read slip";

        /// <summary>Glance at the slip without walking to the desk. Reading is not filing.</summary>
        public override void UseInHand(PlayerInteractor player)
        {
            if (Result == null) { player.Say("The slip is blank."); return; }

            var text = new System.Text.StringBuilder();
            text.Append(Result.IsBlank ? $"{MachineName} blank: " : $"{EquipmentTag} · {MachineName}: ");

            int shown = 0;
            foreach (var kv in Result.Values)
            {
                if (shown++ >= 6) { text.Append("…"); break; }
                text.Append($"{kv.Key} {kv.Value:0.###}   ");
            }

            if (shown == 0) text.Append("no values reported");
            player.Say(text.ToString(), 6f);
        }
    }
}
