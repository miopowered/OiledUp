using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The tap on the wash station's drum. Hold it with a bottle in your hands and it tops the bottle
    /// up, one solvent unit per flush it buys.
    /// <para>
    /// A hold rather than a tap because §9 requires prep to be hand-operated tasks with a real time
    /// cost, and because a fill that happened instantly would make the wash station a place you touch
    /// rather than a place you go. It is deliberately much shorter than the 20 s flush: the cost of
    /// solvent is the walk and the hands, not this.
    /// </para>
    /// <para>
    /// <b>It works from a joined client.</b> Everything it reads is either the bottle in the player's
    /// own hands — a local prop whose charge count the host publishes — or
    /// <see cref="ILabView.SolventUnits"/>, which travels. Nothing here touches a
    /// <c>LabState</c>, so a host and a client run identical code, and the host decides.
    /// </para>
    /// Separate from <see cref="WashStation"/> because that one is a tap (stow a bottle) and this one
    /// is a hold, and <see cref="Interactable.HoldSeconds"/> belongs to the thing you are looking at.
    /// Two colliders on one fixture, the same split <see cref="MachineActionButton"/> has from
    /// <see cref="MachineStation"/>.
    /// </summary>
    public sealed class SolventValve : Interactable
    {
        [Tooltip("Seconds of holding to top a bottle up. Deliberately far shorter than a flush — the " +
                 "cost of solvent is the trip, not this.")]
        [SerializeField] private float fillSeconds = 4f;

        /// <summary>
        /// Even tuned down, filling stays a held action. At a tap the wash station stops being
        /// somewhere you go and becomes a button that happens to be over there.
        /// </summary>
        private const float MinimumFillSeconds = 1f;

        public override float HoldSeconds => Mathf.Max(MinimumFillSeconds, fillSeconds);

        /// <summary>Whole flushes the drum can still cover, as this process last heard.</summary>
        private static int DrumCharges
        {
            get
            {
                var lab = LabView.Current;
                return lab == null ? 0 : (int)(lab.SolventUnits / SolventStore.UnitsPerCharge);
            }
        }

        // -- Sound ------------------------------------------------------------------------------------

        /// <summary>
        /// The fill was #46's fourth complaint: four seconds of holding a key with nothing but a
        /// progress ring to say anything was happening. The solvent runs while the key is down and
        /// stops when it is not, so the sound is the progress bar.
        /// <para>
        /// Local to whoever is holding, like <see cref="MachineStation"/>'s agitation: hold state is
        /// not replicated, and the person who needs to hear it is the one pressing the key. See
        /// <see cref="MachineStation.Prompt"/> for why <paramref name="player"/> is captured in a
        /// prompt rather than pushed in.
        /// </para>
        /// </summary>
        private void Update()
        {
            bool pouring = watcher != null && (Interactable)watcher.Target == this &&
                           watcher.HoldProgress > 0f;

            if (!pouring)
            {
                if (pour != null && pour.isPlaying) pour.Stop();
                return;
            }

            if (pour == null)
            {
                pour = gameObject.AddComponent<AudioSource>();
                pour.playOnAwake = false;
                pour.loop = true;
                pour.clip = LabSoundBank.SolventPour;
                pour.spatialBlend = 1f;
                pour.rolloffMode = AudioRolloffMode.Linear;
                pour.minDistance = 1.5f;
                pour.maxDistance = 14f;
                pour.dopplerLevel = 0f;
                AudioBus.Register(pour, AudioCategory.Effects, 0.32f);
            }

            if (!pour.isPlaying && pour.clip != null) pour.Play();
        }

        private void OnDestroy() => AudioBus.Unregister(pour);

        private AudioSource pour;
        private PlayerInteractor watcher;

        public override string Prompt(PlayerInteractor player)
        {
            watcher = player;

            // No view yet — a client between the scene loading and the first publish. Say nothing
            // rather than quote a drum reading of zero it has simply not been told about.
            if (LabView.Current == null) return null;

            var bottle = player.Carried as SolventBottle;

            if (bottle == null)
            {
                return player.Carried != null
                    ? "Solvent tap — you need a bottle, not that"
                    : "Solvent tap — fetch a bottle from the cradle";
            }

            if (bottle.IsFull) return $"Bottle is full ({bottle.Capacity} flushes)";

            int drum = DrumCharges;
            if (drum < 1) return "Solvent drum is empty — order more at the terminal";

            int taking = Mathf.Min(bottle.Capacity - bottle.Charges, drum);
            return $"Hold to fill ({HoldSeconds:F0}s, +{taking} flush{(taking == 1 ? "" : "es")}, " +
                   $"{drum} left in the drum)";
        }

        /// <summary>
        /// Advisory, like every prompt: <see cref="SolventStore.TryFill"/> makes the same checks when
        /// the request lands, against the host's own record of what is in this player's hands.
        /// </summary>
        public override bool CanInteract(PlayerInteractor player)
        {
            var bottle = player.Carried as SolventBottle;
            if (bottle == null || bottle.IsFull) return false;

            // Filling is housekeeping, not analysis, so the shift clock does not gate it — the same
            // rule the flush and the recalibration get.
            return DrumCharges >= 1;
        }

        public override void Interact(PlayerInteractor player)
        {
            if (!(player.Carried is SolventBottle)) return;

            // The toast does not quote a number. The host decides how much the drum could cover, and
            // the count on the bottle in the player's hands is refreshed by BottleReconciler from the
            // publish that follows — quoting the figure this side guessed would be a second answer
            // that is wrong whenever somebody else filled first.
            LabCommands.Attempt(player, LabCommand.FillBottle(WashStation.FixtureId),
                _ => player.Say("Solvent bottle topped up."));
        }
    }
}
