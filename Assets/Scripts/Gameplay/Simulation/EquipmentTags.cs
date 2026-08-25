using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Builds the labels on arriving vials, e.g. "WERK-2 QUENCH 4".
    /// <para>
    /// Tags identify a customer's plant and the specific tank the oil was drawn from, and they are
    /// what the lab files a sample under (#73). They are deliberately similar to each other: a rack
    /// of vials from one plant should take a moment to tell apart, because §5.4's re-draws and §6.1's
    /// unit history are only interesting if the player has to look rather than glance.
    /// </para>
    /// </summary>
    public static class EquipmentTags
    {
        private static readonly string[] Plants =
        {
            "WERK-1", "WERK-2", "WERK-4", "HALLE-3", "HALLE-6", "LINE-7", "LINE-9", "BAU-2"
        };

        private static readonly string[] QuenchTanks =
        {
            "QUENCH 1", "QUENCH 2", "QUENCH 4", "BATH A", "BATH B", "BATH C",
            "SEALED QUENCH 1", "PRESS QUENCH", "AGITATED TANK 3"
        };

        private static readonly string[] HotTanks =
        {
            "MARTEMPER 1", "MARTEMPER 2", "HOT BATH A", "HOT BATH B", "ISOTHERM 1", "SALT-ADJ TANK"
        };

        private static readonly string[] VacuumTanks =
        {
            "VAC FURNACE 1", "VAC FURNACE 2", "VAC QUENCH A", "VAC CHAMBER 3"
        };

        private static readonly string[] ProtectionTanks =
        {
            "DIP TANK 1", "DIP TANK 2", "PRESERVE LINE", "RUSTPROOF BATH"
        };

        public static string For(string profileId, ref Rng rng)
        {
            string plant = Plants[rng.Range(0, Plants.Length)];
            var tanks = profileId switch
            {
                "quench_oil_martempering" => HotTanks,
                "quench_oil_vacuum" => VacuumTanks,
                "corrosion_protection_oil" => ProtectionTanks,
                _ => QuenchTanks
            };
            return $"{plant} {tanks[rng.Range(0, tanks.Length)]}";
        }

        /// <summary>
        /// Notes from the customer's process engineer. Often vague, sometimes wrong, sometimes
        /// absent (§4.4) — the note is a hint, never evidence.
        /// </summary>
        private static readonly string[] Notes =
        {
            "Operator reports parts coming out soft on the night shift.",
            "Routine quarterly draw, nothing reported.",
            "Bath ran above setpoint for two shifts last week.",
            "Drawn cold before start-up, may not be representative.",
            "Third draw this quarter. Previous two passed.",
            "Filters changed at last service.",
            "Tank topped up last month, drum was already open.",
            "Some staining seen on finished parts.",
            "Agitator was down for a day. Back in service now.",
            "Process engineer says it 'looks fine to me'.",
            null,
            null
        };

        public static string Note(ref Rng rng) => Notes[rng.Range(0, Notes.Length)];
    }
}
