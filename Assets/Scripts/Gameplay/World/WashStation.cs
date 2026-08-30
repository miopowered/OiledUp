using System.Collections.Generic;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The wash station: the solvent drum, and the cradles the bottles live in when nobody is
    /// carrying one.
    /// <para>
    /// §5.5 names a single wash station as a deliberate shared bottleneck, and #14 asked whether
    /// flushing should move here from the instruments. It should not, and this is the half that
    /// should: an instrument's carryover is in its own sample path, so the flush happens where the
    /// residue is — but the <i>solvent</i> lives here, and getting it to the instrument is a walk with
    /// a bottle in your hands. That walk is the layout cost the criterion was asking for, and it is
    /// paid in the one currency §2.6 makes scarce, which is hands rather than seconds.
    /// </para>
    /// <para>
    /// This component is the shelf. Filling is <see cref="SolventValve"/>, on the same fixture, for
    /// the reason <see cref="MachineActionButton"/> is separate from <see cref="MachineStation"/>: one
    /// is a tap and one is a hold, and <see cref="Interactable.HoldSeconds"/> is a property of the
    /// thing you are looking at rather than of what you happen to be holding.
    /// </para>
    /// Taking a bottle back out needs no code here — <see cref="SolventBottle"/> is itself an
    /// <see cref="Interactable"/>, so the player targets the bottle they want, exactly as they do with
    /// a vial in a rack.
    /// </summary>
    public sealed class WashStation : Interactable, IVialSlots
    {
        /// <summary>
        /// Announced under <see cref="SolventStore.StationId"/> so the host can tell whether a player
        /// asking to fill is standing here, and so a client can resolve the container id in a bottle's
        /// location record back to a transform in its own room.
        /// </summary>
        public const string FixtureId = SolventStore.StationId;

        [SerializeField] private Transform cradleRoot;

        [Tooltip("Spacing between cradles, in metres.")]
        [SerializeField] private float spacing = 0.22f;

        private readonly List<Transform> cradles = new();

        /// <summary>
        /// One cradle per bottle the store creates, so the two can never disagree. The count is a
        /// balance decision (<see cref="SolventStore.BottleCount"/>) and a scene that hard-coded its
        /// own would silently strand a bottle the day it changed.
        /// </summary>
        private void EnsureCradles()
        {
            if (cradles.Count > 0) return;

            for (int i = 0; i < SolventStore.BottleCount; i++)
            {
                var go = new GameObject($"Cradle_{i:D2}");
                go.transform.SetParent(cradleRoot != null ? cradleRoot : transform, false);
                go.transform.localPosition = new Vector3(
                    (i - (SolventStore.BottleCount - 1) * 0.5f) * spacing, 0f, 0f);
                cradles.Add(go.transform);
            }
        }

        private void OnEnable()
        {
            EnsureCradles();
            LabRuntime.RegisterFixture(FixtureId, transform, this);
        }

        private void OnDisable() => LabRuntime.ForgetFixture(FixtureId, transform);

        // -- IVialSlots -------------------------------------------------------------------------------

        public Transform Slot(int index)
        {
            EnsureCradles();
            if (cradles.Count == 0) return transform;
            return cradles[Mathf.Clamp(index, 0, cradles.Count - 1)];
        }

        public int FreeSlot()
        {
            EnsureCradles();
            for (int i = 0; i < cradles.Count; i++)
            {
                if (VialSlot.Occupant(cradles[i]) == null) return i;
            }
            return -1;
        }

        public int SlotOf(Transform prop)
        {
            EnsureCradles();
            return VialSlot.IndexOf(cradles, prop);
        }

        // -- Interaction ------------------------------------------------------------------------------

        /// <summary>
        /// How the drum reads, or null on a process that has not been told yet — a client between the
        /// scene loading and the first publish. Null rather than "0 flushes", because a client
        /// reporting an empty drum it has never heard about would send the player to the terminal to
        /// buy solvent they already own.
        /// </summary>
        private static string DrumReading
        {
            get
            {
                var lab = LabView.Current;
                if (lab == null) return null;

                int drum = (int)(lab.SolventUnits / SolventStore.UnitsPerCharge);
                return drum == 1
                    ? PromptStrings.WashDrumOne.Text
                    : PromptStrings.WashDrum.Format(("count", drum));
            }
        }

        /// <summary>
        /// Three sentences, each with its own "we have not been told yet" twin, and the drum reading
        /// arrives as one argument rather than as a suffix somebody appends (#55). The old shape
        /// built <c>" (drum holds 4 flushes)"</c> and glued it onto whichever prompt applied, which
        /// handed a translator a bracket with no sentence around it.
        /// </summary>
        public override string Prompt(PlayerInteractor player)
        {
            string drum = DrumReading;

            if (player.Carried is SolventBottle bottle)
            {
                if (FreeSlot() < 0) return PromptStrings.WashNoCradle.Text;

                return drum == null
                    ? PromptStrings.WashSetDownUnknown.Format(("item", bottle.DisplayName))
                    : PromptStrings.WashSetDown.Format(("item", bottle.DisplayName), ("drum", drum));
            }

            if (player.Carried != null)
            {
                return drum == null
                    ? PromptStrings.WashSolventOnlyUnknown.Text
                    : PromptStrings.WashSolventOnly.Format(("drum", drum));
            }

            return drum == null
                ? PromptStrings.WashIdleUnknown.Text
                : PromptStrings.WashIdle.Format(("drum", drum));
        }

        /// <summary>
        /// Only solvent bottles. The host's <see cref="LabCommandExecutor"/> treats every slotted
        /// fixture the same and would happily record a vial here, so the refusal is stated where the
        /// player can read it rather than left to be discovered.
        /// </summary>
        public override bool CanInteract(PlayerInteractor player) =>
            player.Carried is SolventBottle && FreeSlot() >= 0;

        public override void Interact(PlayerInteractor player)
        {
            if (!(player.Carried is SolventBottle bottle)) return;

            int slot = FreeSlot();
            if (slot < 0) return;

            LabCommands.Attempt(player, LabCommand.PutDown(FixtureId, slot), _ =>
            {
                if (!TryPlace(bottle, slot)) return;
                player.ReleaseCarried();
                player.Say(PromptStrings.WashStowed.Text, 2f);
            });
        }

        /// <summary>
        /// Park a bottle in a cradle. Purely local: where it <i>is</i> belongs to the host, and this
        /// only runs once the host has agreed.
        /// </summary>
        private bool TryPlace(Carryable item, int slot)
        {
            if (item == null) return false;
            EnsureCradles();

            if (slot < 0 || slot >= cradles.Count)
            {
                slot = FreeSlot();
            }
            else
            {
                // Already in that cradle is not a reason to move it: on a client the reconciler may
                // have placed the prop before this callback runs.
                var occupant = VialSlot.Occupant(cradles[slot]);
                if (occupant != null && occupant != item) slot = FreeSlot();
            }

            if (slot < 0) return false;

            item.AttachTo(cradles[slot], interactable: true);
            return true;
        }
    }
}
