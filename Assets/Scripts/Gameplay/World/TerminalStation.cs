using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical terminal you walk up to. Logging a vial and filing a verdict both happen here
    /// rather than from anywhere in the room, so the walk back to the desk is a real cost — that
    /// distance is part of the §5.5 layout problem later.
    /// </summary>
    public sealed class TerminalStation : Interactable
    {
        [SerializeField] private TerminalScreen screen;

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried != null) return "Put the vial down first";

            var lab = LabRuntime.Instance?.Lab;
            if (lab == null) return "Terminal";

            int open = 0;
            foreach (var s in lab.Samples.All)
            {
                if (!s.FiledVerdict.HasValue) open++;
            }
            return open > 0 ? $"Open terminal ({open} open)" : "Open terminal";
        }

        public override bool CanInteract(PlayerInteractor player) =>
            screen != null && player.Carried == null;

        public override void Interact(PlayerInteractor player)
        {
            if (screen == null) return;
            screen.Open();
        }
    }
}
