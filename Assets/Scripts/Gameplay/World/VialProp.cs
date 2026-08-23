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
    public sealed class VialProp : Interactable
    {
        [SerializeField] private Renderer fluidRenderer;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;

        public SampleId SampleId { get; private set; }
        public string Label { get; private set; } = "UNLABELLED";

        public void Bind(SampleId id, string label)
        {
            SampleId = id;
            Label = string.IsNullOrEmpty(label) ? "UNLABELLED" : label;
            name = $"Vial_{id}";
        }

        /// <summary>Tint the fluid so a used-up vial reads differently from a fresh one at a glance.</summary>
        public void SetFillFraction(float fraction)
        {
            if (fluidRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            fluidRenderer.GetPropertyBlock(block);

            // Darker as it empties. Palette oxide family, not an arbitrary colour.
            float v = Mathf.Lerp(0.10f, 0.42f, Mathf.Clamp01(fraction));
            block.SetColor(BaseColor, new Color(v * 1.25f, v * 0.85f, v * 0.30f, 1f));
            fluidRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Park this vial in a socket.
        /// <para>
        /// <paramref name="interactable"/> controls whether the player can target the vial directly.
        /// It must be false while carried (it would sit between the camera and everything else) and
        /// while loaded in an instrument — there the <see cref="MachineStation"/> mediates, and
        /// letting the player grab the vial straight out would leave the machine still believing it
        /// was loaded.
        /// </para>
        /// </summary>
        public void AttachTo(Transform socket, bool interactable = true)
        {
            transform.SetParent(socket, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = interactable;

            if (TryGetComponent<Rigidbody>(out var body))
            {
                body.isKinematic = true;
                body.detectCollisions = interactable;
            }
        }

        public override string Prompt(PlayerInteractor player) =>
            player.Carried == null ? $"Take {Label}" : "Hands full";

        public override bool CanInteract(PlayerInteractor player) => player.Carried == null;

        public override void Interact(PlayerInteractor player) => player.TryCarry(this);
    }
}
