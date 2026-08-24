using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical solvent bottle. Carries a bottle id and a charge count and nothing else — the
    /// truth lives in <see cref="SolventStore"/> on the host (§3.1), exactly as a vial's does.
    /// <para>
    /// <b>Why solvent is a thing you carry.</b> §5.2 keeps the flush at the instrument, because that
    /// is where the carryover is. §5.5 wants the wash station to be a place in the room rather than a
    /// line item. A bottle is what joins the two: the walk between them is a real cost, and while it
    /// is in your hands you are not carrying a vial — §2.6's one pair of hands, spent on housekeeping
    /// instead of on analysis.
    /// </para>
    /// <para>
    /// Deliberately not a <c>NetworkObject</c>. There are two of these and NGO could carry them, but
    /// they would then need networked parents to sit in a rack that is plain scene geometry, and the
    /// project already has a working answer for "a local prop whose position lives on the host" —
    /// <see cref="BottleReconciler"/>, which is <see cref="VialReconciler"/> with a different prop
    /// on the end of it.
    /// </para>
    /// </summary>
    public sealed class SolventBottle : Carryable
    {
        [SerializeField] private Renderer fluidRenderer;
        [SerializeField] private Transform fluidTransform;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;

        /// <summary>Which bottle this is. Matches <see cref="SolventBottleState.Id"/>.</summary>
        public string BottleId { get; private set; }

        /// <summary>Flushes left in it, as this process was last told.</summary>
        public int Charges { get; private set; }

        public int Capacity { get; private set; } = SolventStore.BottleCapacity;

        public bool IsEmpty => Charges <= 0;

        public bool IsFull => Charges >= Capacity;

        /// <summary>
        /// The charge count is part of the name on purpose. It is the one number that decides whether
        /// walking to an instrument is worth it, and a player should never have to open a screen —
        /// or guess — to find out how many flushes they are carrying.
        /// </summary>
        public override string DisplayName => $"Solvent bottle ({Charges}/{Capacity})";

        public void Bind(string bottleId, int capacity)
        {
            BottleId = bottleId;
            Capacity = Mathf.Max(1, capacity);
            name = $"Solvent_{bottleId}";
        }

        /// <summary>
        /// Show how much is left. The column drops a step per flush rather than draining smoothly:
        /// charges are discrete and a continuous bar would invite the player to read half a charge
        /// into it.
        /// </summary>
        public void SetCharges(int charges)
        {
            Charges = Mathf.Clamp(charges, 0, Capacity);

            float fraction = Capacity > 0 ? (float)Charges / Capacity : 0f;

            if (fluidTransform != null)
            {
                var scale = fluidTransform.localScale;
                scale.y = Mathf.Max(0.02f, fraction);
                fluidTransform.localScale = scale;
            }

            if (fluidRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            fluidRenderer.GetPropertyBlock(block);

            // Palette solvent family (row 9): a pale, cold, faintly green-blue wash, darkening as it
            // empties. Never a signal colour — hard rule 4 reserves red, amber and green for verdicts,
            // and a bottle that went red when it ran low would teach the player to misread a result.
            float v = Mathf.Lerp(0.14f, 0.62f, fraction);
            block.SetColor(BaseColor, new Color(v * 0.72f, v, v * 0.94f, 1f));
            fluidRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// What the primary button reports while it is in your hands. A carried item cannot be looked
        /// at, so without this the only way to check the count is to put the bottle down again.
        /// </summary>
        public override void UseInHand(PlayerInteractor player)
        {
            if (player == null) return;

            player.Say(IsEmpty
                ? "Solvent bottle: empty. Refill it at the wash station."
                : $"Solvent bottle: {Charges} flush{(Charges == 1 ? "" : "es")} left.");
        }

        public override string UseHint => "check the bottle";
    }
}
