using Residue.Data;
using Residue.Gameplay.Simulation;
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
            // The blank flag rather than the reading it came off: a prompt is asked every frame, and
            // on a client the numbers are a lookup that may not have resolved yet. Neither is a good
            // reason to caption a blank slip as a sample one.
            if (player.Carried is PrintoutProp printout)
                return printout.IsBlank
                    ? PromptStrings.TerminalFileBlank.Format(("machine", printout.MachineName))
                    : PromptStrings.TerminalFileResults.Format(("tag", printout.RecordTag));

            // You do not type up a report one-handed with a sample in the other. Racks exist for
            // exactly this, so name them rather than just refusing.
            if (player.Carried != null) return PromptStrings.TerminalRackFirst.Text;

            // A player with no view of their own and no shared one to borrow. Say so rather than
            // going dead: §9 forbids an object that refuses without explaining itself, and a silent
            // terminal reads as a broken interaction rather than as a missing screen.
            if (ScreenFor(player) == null) return PromptStrings.TerminalNoDisplay.Text;

            var lab = LabView.Current;
            if (lab == null) return PromptStrings.TerminalName.Text;

            int open = lab.OpenSampleCount;
            return open > 0
                ? PromptStrings.TerminalOpenWithCount.Format(("count", open))
                : PromptStrings.TerminalOpen.Text;
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
                player.Say(PromptStrings.TerminalNoDisplayToast.Text);
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
            // The ticket, not the reading. This used to refuse a slip whose Result was null, which was
            // a fair test of "blank paper" while only the host held slips — and became a trap the
            // moment a client did, because a client's slip holds no TestResult at all until somebody
            // reads it. A slip with no ticket is the only one that names nothing; everything else is
            // the host's to accept or refuse.
            if (printout.Ticket == ResultSlips.NoTicket)
            {
                player.Say(PromptStrings.TerminalSlipBlank.Text);
                return;
            }

            LabCommands.Attempt(player, LabCommand.FileSlip(printout.Ticket), result =>
            {
                player.ReleaseCarried();

                // Through the runtime, so the prop table lets go of it at the same moment the object
                // does. On a client the reconciler would destroy it a publish later anyway, once the
                // host dropped the row; doing it here keeps the hand-over instant on both sides.
                var lab = LabRuntime.Instance;
                if (lab != null) lab.RetireSlip(printout.Ticket);
                else Destroy(printout.gameObject);

                // A solvent blank or a certified standard belongs to the instrument rather than to a
                // sample, so it has no record to join and the host simply took the paper.
                //
                // The three-way phrasing is the client's doing. SampleFor reads a LabState, so it is
                // null for every slip a joined player files — and the old two-way version therefore
                // told them they had filed a blank whatever they were holding. The paper knows which
                // it is; the record tag is the part only a host can add.
                var sample = lab != null ? lab.SampleFor(result.Sample) : null;

                player.Say(printout.IsBlank
                    ? PromptStrings.TerminalBlankFiled.Format(("machine", printout.MachineName))
                    : sample != null
                        ? PromptStrings.TerminalResultsFiledTagged.Format(
                            ("tag", sample.RecordTag), ("machine", printout.MachineName))
                        : PromptStrings.TerminalResultsFiled.Format(
                            ("machine", printout.MachineName)));
            });
        }
    }
}
