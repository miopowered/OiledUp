using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical vial. Carries a <see cref="SampleId"/> and nothing else — all state lives
    /// server-side in the registry (§3.2).
    /// <para>
    /// Deliberately not a NetworkObject. A busy shift has 200+ of these and spawning a networked
    /// object per vial would drown the connection; at M4 these become pooled local props that each
    /// client re-parents when the server broadcasts a location change.
    /// </para>
    /// </summary>
    public sealed class VialProp : Carryable
    {
        [SerializeField] private Renderer fluidRenderer;
        [SerializeField] private Transform fluidTransform;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;

        public SampleId SampleId { get; private set; }
        public string Label { get; private set; } = "UNLABELLED";

        public override string DisplayName => Label;

        public void Bind(SampleId id, string label)
        {
            SampleId = id;
            Label = string.IsNullOrEmpty(label) ? "UNLABELLED" : label;
            name = $"Vial_{id}";
        }

        /// <summary>
        /// Show how much oil is left. Both the colour and the fluid column shrink, so a nearly
        /// spent sample reads as spent from across the room — the volume economy is a decision the
        /// player makes constantly and should not require opening a screen to see.
        /// </summary>
        public void SetFillFraction(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);

            if (fluidTransform != null)
            {
                var scale = fluidTransform.localScale;
                scale.y = Mathf.Max(0.02f, fraction);
                fluidTransform.localScale = scale;
            }

            if (fluidRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            fluidRenderer.GetPropertyBlock(block);

            // Palette oxide family, darker as it empties. Never a signal colour.
            float v = Mathf.Lerp(0.10f, 0.42f, fraction);
            block.SetColor(BaseColor, new Color(v * 1.25f, v * 0.85f, v * 0.30f, 1f));
            fluidRenderer.SetPropertyBlock(block);
        }
    }
}
