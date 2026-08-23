using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Builds the labels printed on arriving vials, e.g. "RIG-7 COMPRESSOR B".
    /// <para>
    /// Tags matter more than flavour: the player types one into the terminal to log a vial, and
    /// mis-logging is a real failure mode (§5.1). So tags need to be similar enough to each other
    /// that transcribing one carelessly is plausible.
    /// </para>
    /// </summary>
    public static class EquipmentTags
    {
        private static readonly string[] Sites =
        {
            "RIG-7", "RIG-12", "HAUL-04", "HAUL-09", "PLANT-3", "PLANT-8", "PUMP-21", "YARD-2"
        };

        private static readonly string[] EngineUnits =
        {
            "ENGINE A", "ENGINE B", "GENSET 1", "GENSET 2", "COMPRESSOR B", "PRIME MOVER"
        };

        private static readonly string[] GearboxUnits =
        {
            "GEARBOX A", "GEARBOX C", "FINAL DRIVE L", "FINAL DRIVE R", "SWING DRIVE", "MILL DRIVE"
        };

        private static readonly string[] HydraulicUnits =
        {
            "HYD MAIN", "HYD AUX", "BOOM CIRCUIT", "TRACK MOTOR", "PRESS RAM"
        };

        public static string For(string profileId, ref Rng rng)
        {
            string site = Sites[rng.Range(0, Sites.Length)];
            var units = profileId switch
            {
                "gearbox_industrial" => GearboxUnits,
                "hydraulic_system" => HydraulicUnits,
                _ => EngineUnits
            };
            return $"{site} {units[rng.Range(0, units.Length)]}";
        }

        /// <summary>Field notes are often vague, sometimes wrong, sometimes absent (§4.4).</summary>
        private static readonly string[] Notes =
        {
            "Operator reports intermittent noise under load.",
            "Routine draw, nothing reported.",
            "Ran hot last week, cooled off since.",
            "Sample drawn after shutdown, may be settled.",
            "Third draw this quarter. Previous two clean.",
            "Filter changed at last service.",
            "Unit topped up with whatever was on the truck.",
            "Tech notes 'sounds fine to me'.",
            null,
            null
        };

        public static string Note(ref Rng rng) => Notes[rng.Range(0, Notes.Length)];
    }
}
