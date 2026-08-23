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

        [Tooltip("Only a fallback. The interacting player's own terminal view always wins.")]
        [SerializeField] private TerminalScreen screen;

        private void OnEnable() => LabRuntime.RegisterFixture(FixtureId, transform);

        private void OnDisable() => LabRuntime.ForgetFixture(FixtureId, transform);

        /// <summary>
        /// Which terminal view this press should raise.
        /// <para>
        /// There is one desk and up to four players, so the screen cannot belong to the fixture —
        /// it belongs to whoever walked up to it, and each player carries their own. The serialized
        /// field survives only as a fallback for a scene that still keeps a single shared view at the
        /// root; the player wins whenever they have one, so a scene which has both cannot
        /// accidentally show player A's samples to player B.
        /// </para>
        /// <para>
        /// Two players opening this at once is fine and needs no arbitration: each is looking at
        /// their own panel over the same replicated view of the lab, and neither can do anything to
        /// the record except ask the host for it (§3.1).
        /// </para>
        /// <para>
        /// Public because that precedence is otherwise only observable through a live
        /// <c>UIDocument</c>, which no edit-mode test has.
        /// </para>
        /// </summary>
        public TerminalScreen ScreenFor(PlayerInteractor player)
        {
            var mine = player != null ? player.Terminal : null;
            return mine != null ? mine : screen;
        }

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried is PrintoutProp printout)
                return printout.Result != null && printout.Result.IsBlank
                    ? $"File blank slip ({printout.MachineName})"
                    : $"File results — {printout.EquipmentTag}";

            // You do not type up a report one-handed with a sample in the other. Racks exist for
            // exactly this, so name them rather than just refusing.
            if (player.Carried != null) return "Rack the vial before filing";

            // A player with no view of their own and no shared one to borrow. Say so rather than
            // going dead: §9 forbids an object that refuses without explaining itself, and a silent
            // terminal reads as a broken interaction rather than as a missing screen.
            if (ScreenFor(player) == null) return "Terminal — no display for you";

            var lab = LabView.Current;
            if (lab == null) return "Terminal";

            int open = lab.OpenSampleCount;
            return open > 0 ? $"Open terminal ({open} open)" : "Open terminal";
        }

        /// <summary>
        /// Filing a slip is deliberately not gated on a screen. It is a hand-over at the desk, not
        /// something you read — and refusing it because this player has no display would strand the
        /// slip in their hands with the day clock running.
        /// </summary>
        public override bool CanInteract(PlayerInteractor player)
        {
            if (player.Carried is PrintoutProp) return true;
            if (player.Carried != null) return false;
            return ScreenFor(player) != null;
        }

        public override void Interact(PlayerInteractor player)
        {
            if (player.Carried is PrintoutProp printout)
            {
                FileResults(player, printout);
                return;
            }

            var target = ScreenFor(player);
            if (target == null)
            {
                player.Say("This terminal has no display for you.");
                return;
            }

            target.Open();
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
