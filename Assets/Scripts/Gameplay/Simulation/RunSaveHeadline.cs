using System;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The few facts about a save that the main menu needs to offer CONTINUE (#49): which day, which
    /// contract, how the books stood, and when it was written.
    /// <para>
    /// It exists so the menu never has to hold a <see cref="RunSnapshot"/>. A snapshot carries ground
    /// truth, and <c>Residue.Net.UI</c> is a client-facing assembly — one screen holding a whole run
    /// would put the answers in the same process as the player looking at them. Everything here is
    /// already on the pause screen of the run it came from.
    /// </para>
    /// </summary>
    public readonly struct RunSaveHeadline
    {
        /// <summary>Format the save was written in. Compare against <see cref="RunSnapshot.SchemaVersion"/>.</summary>
        public readonly int Schema;

        public readonly int Day;
        public readonly string ContractName;
        public readonly float Money;

        /// <summary><c>DateTime.UtcNow.Ticks</c> at the moment of writing.</summary>
        public readonly long SavedUtcTicks;

        public RunSaveHeadline(int schema, int day, string contractName, float money, long savedUtcTicks)
        {
            Schema = schema;
            Day = day;
            ContractName = contractName;
            Money = money;
            SavedUtcTicks = savedUtcTicks;
        }

        /// <summary>True when this build can actually load it. False still gets a button, and a reason.</summary>
        public bool IsLoadable => Schema == RunSnapshot.SchemaVersion;

        public DateTime SavedLocal => SavedUtcTicks <= 0
            ? DateTime.MinValue
            : new DateTime(SavedUtcTicks, DateTimeKind.Utc).ToLocalTime();

        /// <summary>One line for a button: "Shakedown — day 14".</summary>
        public string Describe() =>
            string.IsNullOrEmpty(ContractName) ? $"day {Day}" : $"{ContractName} — day {Day}";
    }
}
