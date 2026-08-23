using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Breathes a renderer's emission so a targeted object reads without an outline.
    /// <para>
    /// §2.6 rules out outline highlights because they fight flat shading — an outline on an
    /// untextured hard-normal mesh reads as a rendering bug rather than a highlight. Emission works
    /// because §2.4 sets bloom to a high threshold, so only emissive surfaces bloom at all.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EmissivePulse : MonoBehaviour
    {
        [SerializeField] private Renderer target;
        [SerializeField] private Color colour = new(0.55f, 0.70f, 0.85f);
        [SerializeField] private float minIntensity = 0.15f;
        [SerializeField] private float maxIntensity = 0.75f;
        [SerializeField] private float cyclesPerSecond = 1.4f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock block;
        private bool active;

        private void Awake()
        {
            if (target == null) target = GetComponentInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            Apply(0f);
        }

        /// <summary>
        /// Whether the pulse is running. Deliberately NOT called <c>enabled</c>: shadowing
        /// <see cref="Behaviour.enabled"/> would mean Unity's lifecycle and our code were reading
        /// two different flags.
        /// </summary>
        public bool Active
        {
            get => active;
            set
            {
                active = value;
                if (!value) Apply(0f);
            }
        }

        /// <summary>
        /// Point the pulse at a specific renderer. For highlights created at runtime, where the
        /// first renderer under the object is not necessarily the one that should glow.
        /// </summary>
        public void Retarget(Renderer renderer)
        {
            if (renderer == null) return;

            Apply(0f);   // let go of the old renderer, or it stays lit forever
            target = renderer;
            Apply(0f);
        }

        private void Update()
        {
            if (!active || target == null) return;

            // 0..1 triangle-ish wave. Sine keeps it from reading as a blink.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * cyclesPerSecond * Mathf.PI * 2f);
            Apply(Mathf.Lerp(minIntensity, maxIntensity, t));
        }

        private void Apply(float intensity)
        {
            if (target == null) return;

            // Lazily, because Retarget can be called from AddComponent before Awake has finished.
            block ??= new MaterialPropertyBlock();

            target.GetPropertyBlock(block);
            block.SetColor(EmissionColor, colour * intensity);
            target.SetPropertyBlock(block);
        }
    }
}
