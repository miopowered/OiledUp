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
        [SerializeField] private TerminalScreen screen;

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
        /// </summary>
        private void FileResults(PlayerInteractor player, PrintoutProp printout)
        {
            var lab = LabRuntime.Instance;
            var sample = lab?.SampleFor(printout.SampleId);

            if (printout.Result == null)
            {
                player.Say("That slip is blank.");
                return;
            }

            // A solvent blank belongs to the instrument, not to a sample. It is already readable in
            // the terminal's INSTRUMENTS panel, so filing one just discards the paper.
            if (printout.Result.IsBlank || sample == null)
            {
                player.ReleaseCarried();
                Destroy(printout.gameObject);
                player.Say($"{printout.MachineName} blank slip filed.");
                return;
            }

            // The lifecycle decides whether this slip may join the record — a sample that already has
            // a verdict on it is history, and quietly appending to a closed record would make the
            // §5.3 "which verdicts are suspect" list wrong.
            if (!Chemistry.SampleLifecycle.TryFileResult(sample, printout.Result, out string refusal))
            {
                player.Say(refusal);
                return;
            }

            player.ReleaseCarried();
            Destroy(printout.gameObject);

            player.Say($"{sample.RecordTag}: {printout.MachineName} results filed.");
        }
    }
}
