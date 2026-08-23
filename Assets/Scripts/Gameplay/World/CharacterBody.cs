using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A segmented figure animated by transforms rather than skinning.
    /// <para>
    /// No rig, no skinned mesh, no animation clips. For untextured hard-normal geometry a skinned
    /// character buys nothing — the silhouette is boxes either way — and a procedural cycle is
    /// something an agent can tune by changing a number instead of re-exporting an FBX. It also
    /// keeps the whole character inside the §2.5 "generated in C#" pipeline.
    /// </para>
    /// Built now rather than at M4 because this is what other players will see, and retrofitting a
    /// body onto a controller that never had one is where the awkward camera-versus-shoulders
    /// problems come from. It is hidden from its owner's eye camera by layer.
    /// </summary>
    public sealed class CharacterBody : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInteractor interactor;

        [Header("Segments")]
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform upperArmL, lowerArmL;
        [SerializeField] private Transform upperArmR, lowerArmR;
        [SerializeField] private Transform upperLegL, lowerLegL;
        [SerializeField] private Transform upperLegR, lowerLegR;

        [Header("Walk cycle")]
        [SerializeField] private float strideFrequency = 1.9f;
        [SerializeField] private float legSwing = 34f;
        [SerializeField] private float kneeBend = 42f;
        [SerializeField] private float armSwing = 22f;
        [SerializeField] private float elbowBend = 18f;

        [Header("Pose")]
        [SerializeField] private float pelvisHeight = 0.92f;
        [SerializeField] private float crouchPelvisDrop = 0.34f;
        [SerializeField] private float bobAmount = 0.022f;

        [Tooltip("How far the arms come forward when carrying something.")]
        [SerializeField] private float carryArmPitch = -62f;

        [SerializeField] private float carryElbowBend = 55f;

        private float phase;
        private float carryBlend;
        private float crouchBlend;

        private void LateUpdate()
        {
            if (player == null) return;

            float speed = player.SpeedFraction;
            bool moving = player.IsGrounded && speed > 0.05f;

            // Same relationship the head bob uses, so the camera rises on the same footfall the legs
            // produce. Two systems drifting out of phase reads as broken before anyone can say why.
            if (moving) phase += Time.deltaTime * strideFrequency * Mathf.PI * 2f * Mathf.Max(0.6f, speed);
            else phase = Mathf.MoveTowards(phase % (Mathf.PI * 2f), 0f, Time.deltaTime * 6f);

            bool carrying = interactor != null && interactor.Carried != null;
            carryBlend = Mathf.MoveTowards(carryBlend, carrying ? 1f : 0f, Time.deltaTime * 5f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, player.IsCrouching ? 1f : 0f, Time.deltaTime * 6f);

            PoseRoot(moving ? speed : 0f);
            PoseLegs(speed);
            PoseArms(speed);
        }

        private void PoseRoot(float amount)
        {
            if (pelvis != null)
            {
                float y = pelvisHeight - crouchPelvisDrop * crouchBlend
                          + Mathf.Sin(phase * 2f) * bobAmount * amount;
                pelvis.localPosition = new Vector3(0f, y, 0f);

                // Lean into the walk a little; more when crouched, which reads as bracing. Kept
                // small because the legs hang off the pelvis, so every degree here also swings them.
                float lean = 2f * amount + 8f * crouchBlend;
                pelvis.localRotation = Quaternion.Euler(lean, 0f, 0f);
            }

            if (torso != null)
                torso.localRotation = Quaternion.Euler(0f, Mathf.Sin(phase) * 3f * amount, 0f);

            // The head tracks pitch so an observer can tell what a teammate is looking at — which is
            // the whole point of §1.1.5 co-op by information asymmetry.
            if (neck != null && player.EyeCamera != null)
            {
                float pitch = player.EyeCamera.transform.parent != null
                    ? player.EyeCamera.transform.parent.localEulerAngles.x
                    : 0f;
                if (pitch > 180f) pitch -= 360f;
                neck.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -60f, 60f), 0f, 0f);
            }
        }

        private void PoseLegs(float amount)
        {
            float swing = Mathf.Sin(phase) * legSwing * amount;

            SetLeg(upperLegL, lowerLegL, swing);
            SetLeg(upperLegR, lowerLegR, -swing);

            if (crouchBlend <= 0.001f) return;

            // Crouching folds both legs regardless of the cycle.
            if (upperLegL != null) upperLegL.localRotation *= Quaternion.Euler(-38f * crouchBlend, 0f, 0f);
            if (upperLegR != null) upperLegR.localRotation *= Quaternion.Euler(-38f * crouchBlend, 0f, 0f);
            if (lowerLegL != null) lowerLegL.localRotation *= Quaternion.Euler(64f * crouchBlend, 0f, 0f);
            if (lowerLegR != null) lowerLegR.localRotation *= Quaternion.Euler(64f * crouchBlend, 0f, 0f);
        }

        private void SetLeg(Transform upper, Transform lower, float swing)
        {
            if (upper != null) upper.localRotation = Quaternion.Euler(swing, 0f, 0f);

            // A knee only bends one way. Taking the negative half of the cycle gives the trailing
            // leg its lift without the leading leg hyperextending backwards.
            if (lower != null)
                lower.localRotation = Quaternion.Euler(Mathf.Max(0f, -swing) * (kneeBend / Mathf.Max(1f, legSwing)), 0f, 0f);
        }

        private void PoseArms(float amount)
        {
            float swing = -Mathf.Sin(phase) * armSwing * amount;

            SetArm(upperArmL, lowerArmL, swing);
            SetArm(upperArmR, lowerArmR, -swing);
        }

        private void SetArm(Transform upper, Transform lower, float swing)
        {
            if (upper != null)
            {
                // Carrying overrides the swing rather than adding to it: you do not swing an arm
                // that is holding a vial.
                float pitch = Mathf.Lerp(swing, carryArmPitch, carryBlend);
                upper.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (lower != null)
            {
                float bend = Mathf.Lerp(Mathf.Max(0f, swing) * (elbowBend / Mathf.Max(1f, armSwing)),
                    carryElbowBend, carryBlend);
                lower.localRotation = Quaternion.Euler(bend, 0f, 0f);
            }
        }
    }
}
