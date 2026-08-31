using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The coat on its hanger in the locker, and the only type in the game that knows a coat can be
    /// worn at all.
    ///
    /// <para>
    /// <b>It is a beat and never a gate, and that is a rule rather than a scoping decision.</b>
    /// <c>CLAUDE.md</c> records booking-in being removed (#73) because the loop "stopped dead at a
    /// keyboard: nothing could be prepped or run until it had been typed into the terminal". A coat
    /// you must fetch before you may touch a sample is the same shape with a different fixture — a
    /// walk to the far end of the corridor between the truck arriving and any analysis starting. So
    /// nothing anywhere asks whether the coat is on: not an instrument, not the terminal, not a
    /// verdict, not <c>SampleState</c>. <c>LabFixtureTests.NothingInTheLabAsksWhetherTheCoatIsOn</c>
    /// holds that shut by reading the source tree, because the failure mode is a single innocent
    /// <c>if</c> written by somebody who never read this paragraph.
    /// </para>
    ///
    /// <para>
    /// <b>Wearing is local and cosmetic.</b> It is not on the wire, it is not in <c>SampleState</c>,
    /// and it has nothing to do with the chemistry. The garment is a scene object that moves onto
    /// <see cref="CharacterBody.Torso"/>, so it inherits the walk cycle for free and lands on the
    /// player-body layer — which means the wearer sees it in third person (F4) and, once M4 puts other
    /// people in the room, on nobody's copy but their own. That is the correct amount of machinery for
    /// something that changes no number in the game.
    /// </para>
    ///
    /// <para>
    /// The component lives on the hanger rather than on the coat, because the hanger is the thing that
    /// stays put. A coat carrying its own <see cref="Interactable"/> would be aimed at while it hung
    /// there and then become unreachable the moment it moved onto the wearer's chest, where the
    /// interaction ray discards it as part of the player (<see cref="PlayerInteractor.IsSelf"/>) and
    /// the eye camera culls it besides. Taking it off would have no target. The hanger has one either
    /// way.
    /// </para>
    /// </summary>
    public sealed class LabCoat : Interactable
    {
        [Tooltip("The garment. Moves onto the wearer's torso and back; the hanger this component " +
                 "sits on never moves.")]
        [SerializeField] private Transform garment;

        [Tooltip("Where the coat sits in the torso pivot's own space. The torso mesh spans 0 to 0.46 " +
                 "above that pivot and the coat mesh hangs from its own shoulder line, so this is the " +
                 "shoulder height plus a hair of clearance.")]
        [SerializeField] private Vector3 wornOffset = new(0f, 0.46f, 0.006f);

        private Transform hanger;
        private Vector3 hangerPosition;
        private Quaternion hangerRotation;
        private int hangerLayer;

        /// <summary>True while the garment is on somebody rather than on its hanger.</summary>
        public bool IsWorn { get; private set; }

        // -- Interaction ------------------------------------------------------------------------------

        public override string Prompt(PlayerInteractor player)
        {
            if (garment == null) return null;
            return IsWorn ? PromptStrings.CoatHang.Text : PromptStrings.CoatWear.Text;
        }

        public override bool CanInteract(PlayerInteractor player) => garment != null;

        public override void Interact(PlayerInteractor player)
        {
            if (IsWorn)
            {
                if (Hang() && player != null) player.Say(PromptStrings.CoatHung.Text, 2.5f);
                return;
            }

            // Found from the player rather than wired to one: with four people in the room there is
            // no such thing as "the" body, and the coat has to land on whoever reached for it.
            var body = player != null
                ? player.GetComponentInChildren<CharacterBody>(includeInactive: true)
                : null;

            if (Wear(body) && player != null) player.Say(PromptStrings.CoatWorn.Text, 2.5f);
        }

        // -- Wearing ----------------------------------------------------------------------------------

        /// <summary>
        /// Put the coat on <paramref name="body"/>.
        /// <para>
        /// The hanger pose is captured here rather than in an <c>Awake</c>, the way
        /// <see cref="ItemInspectionView.Open"/> captures the pose it will restore: what counts is
        /// where the garment was when it left, not where the scene builder first put it.
        /// </para>
        /// </summary>
        /// <returns>False if there is nothing to wear or nobody with a torso to wear it.</returns>
        public bool Wear(CharacterBody body)
        {
            if (IsWorn || garment == null) return false;

            var torso = body != null ? body.Torso : null;
            if (torso == null) return false;

            hanger = garment.parent;
            hangerPosition = garment.localPosition;
            hangerRotation = garment.localRotation;
            hangerLayer = garment.gameObject.layer;

            garment.SetParent(torso, worldPositionStays: false);
            garment.localPosition = wornOffset;
            garment.localRotation = Quaternion.identity;

            // The layer the rest of the body is on, so the owner's eye camera culls it exactly as it
            // culls the shoulders underneath (ThirdPersonView). Without this the wearer spends the
            // shift looking at the inside of a coat.
            SetLayer(garment, ThirdPersonView.PlayerBodyLayer);

            IsWorn = true;
            return true;
        }

        /// <summary>Put it back exactly where it came from, layer included.</summary>
        public bool Hang()
        {
            if (!IsWorn || garment == null) return false;

            garment.SetParent(hanger, worldPositionStays: false);
            garment.localPosition = hangerPosition;
            garment.localRotation = hangerRotation;
            SetLayer(garment, hangerLayer);

            IsWorn = false;
            return true;
        }

        /// <summary>
        /// A coat left on a body that is about to stop existing would be destroyed with it, so the
        /// scene would come back one coat short. Guarded on the hanger still being there, because a
        /// scene unloading takes both.
        /// </summary>
        private void OnDisable()
        {
            if (IsWorn && hanger != null) Hang();
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }
    }
}
