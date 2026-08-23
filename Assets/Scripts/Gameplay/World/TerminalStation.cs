using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical terminal you walk up to. Filing a results slip and filing a verdict both happen
    /// here rather than from anywhere in the room, so the walk back to the desk is a real cost —
    /// that distance is part of the §5.5 layout problem later.
    /// </summary>
    public sealed class TerminalStation : Interactable
    {
        /// <summary>
        /// What the host locates when it checks that a player filing paperwork is actually at the
        /// desk. A constant rather than a serialized field because there is one terminal and every
        /// terminal command is aimed at it — nothing has to be wired for the check to work, which
        /// matters because the scene is built by a generator this workstream does not own.
        /// </summary>
        public const string FixtureId = "terminal";

        [SerializeField] private TerminalScreen screen;

        private void OnEnable() => LabRuntime.RegisterFixture(FixtureId, transform);

        private void OnDisable() => LabRuntime.ForgetFixture(FixtureId, transform);

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried is PrintoutProp printout)
                return printout.Result != null && printout.Result.IsBlank
                    ? $"File blank slip ({printout.MachineName})"
                    : $"File results — {printout.EquipmentTag}";

            // You do not type up a report one-handed with a sample in the other. Racks exist for
            // exactly this, so name them rather than just refusing.
            if (player.Carried != null) return "Rack the vial before filing";

            var lab = LabRuntime.Instance?.Lab;
            if (lab == null) return "Terminal";

            int open = 0;
            foreach (var s in lab.Samples.All)
            {
                if (!s.FiledVerdict.HasValue) open++;
            }
            return open > 0 ? $"Open terminal ({open} open)" : "Open terminal";
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            if (screen == null) return false;
            return player.Carried == null || player.Carried is PrintoutProp;
        }

        public override void Interact(PlayerInteractor player)
        {
            if (screen == null) return;

            if (player.Carried is PrintoutProp printout)
            {
                FileResults(player, printout);
                return;
            }

            screen.Open();
        }

        /// <summary>
        /// Transcribe a slip into the sample's record. This is the only path by which a measured
        /// value ever reaches the terminal — instruments do not file their own work.
        /// <para>
        /// <b>The request names the slip, never its numbers.</b> §3.1 forbids a client computing a
        /// test result; a client permitted to <i>post</i> one instead would be the same hole with an
        /// extra step, and the archive it wrote into is what every verdict is later scored against.
        /// So the ticket goes over and the host files the values it produced itself — see
        /// <see cref="Residue.Gameplay.Simulation.ResultSlips"/>.
        /// </para>
        /// The lifecycle still decides whether the slip may join the record; a sample that already
        /// has a verdict on it is history, and quietly appending to a closed record would make the
        /// §5.3 "which verdicts are suspect" list wrong.
        /// </summary>
        private void FileResults(PlayerInteractor player, PrintoutProp printout)
        {
            if (printout.Result == null)
            {
                player.Say("That slip is blank.");
                return;
            }

            LabCommands.Attempt(player, LabCommand.FileSlip(printout.Ticket), result =>
            {
                player.ReleaseCarried();
                Destroy(printout.gameObject);

                // A solvent blank or a certified standard belongs to the instrument rather than to a
                // sample, so it has no record to join and the host simply took the paper.
                var sample = LabRuntime.Instance != null
                    ? LabRuntime.Instance.SampleFor(result.Sample)
                    : null;

                player.Say(sample != null
                    ? $"{sample.RecordTag}: {printout.MachineName} results filed."
                    : $"{printout.MachineName} blank slip filed.");
            });
        }
    }
}
