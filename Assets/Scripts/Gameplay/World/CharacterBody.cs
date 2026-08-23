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

        // -- Remote driving ----------------------------------------------------------------------

        private bool remote;
        private bool remoteCrouching;
        private bool remoteCarrying;
        private Vector3 lastRemotePosition;
        private float remoteSpeed;

        /// <summary>
        /// Animate from replicated movement instead of from the controller.
        /// <para>
        /// On someone else's copy of a player the controller is switched off — it would fight the
        /// networked transform for the same fields — so everything the walk cycle reads is frozen at
        /// whatever it held on spawn. Left alone, remote players slide around the lab in a T-pose,
        /// which is the single most obvious way a co-op game looks broken.
        /// </para>
        /// Speed is recovered from how far the transform actually moved, which needs nothing sent
        /// over the wire: the position is replicated anyway, and its derivative is free.
        /// </summary>
        public void SetRemote(bool value)
        {
            remote = value;
            lastRemotePosition = transform.position;
            remoteSpeed = 0f;
        }

        /// <summary>
        /// Push the two things a replica cannot infer from position alone. Crouching and carrying
        /// change the pose without moving anybody, so they have to be told rather than derived.
        /// </summary>
        public void SetRemoteState(bool crouching, bool carrying)
        {
            remoteCrouching = crouching;
            remoteCarrying = carrying;
        }

        private void TrackRemoteMotion()
        {
            Vector3 now = transform.position;
            Vector3 delta = now - lastRemotePosition;
            delta.y = 0f;
            lastRemotePosition = now;

            float measured = Time.deltaTime > 0.0001f ? delta.magnitude / Time.deltaTime : 0f;

            // Smoothed, because a replicated transform arrives in steps at the send rate and the raw
            // derivative is a square wave. The legs would strobe rather than walk.
            remoteSpeed = Mathf.Lerp(remoteSpeed, measured, Time.deltaTime * 10f);
        }

        private void LateUpdate()
        {
            if (player == null && !remote) return;

            float speed;
            bool moving;

            if (remote)
            {
                TrackRemoteMotion();

                // Against sprint speed, matching PlayerController.SpeedFraction so one cycle looks
                // the same whether you are watching yourself or someone else.
                speed = Mathf.Clamp01(remoteSpeed / 4.6f);
                moving = speed > 0.05f;
            }
            else
            {
                speed = player.SpeedFraction;
                moving = player.IsGrounded && speed > 0.05f;
            }

            // Same relationship the head bob uses, so the camera rises on the same footfall the legs
            // produce. Two systems drifting out of phase reads as broken before anyone can say why.
            if (moving) phase += Time.deltaTime * strideFrequency * Mathf.PI * 2f * Mathf.Max(0.6f, speed);
            else phase = Mathf.MoveTowards(phase % (Mathf.PI * 2f), 0f, Time.deltaTime * 6f);

            bool carrying = remote
                ? remoteCarrying
                : interactor != null && interactor.Carried != null;

            bool crouching = remote ? remoteCrouching : player.IsCrouching;

            carryBlend = Mathf.MoveTowards(carryBlend, carrying ? 1f : 0f, Time.deltaTime * 5f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouching ? 1f : 0f, Time.deltaTime * 6f);

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
            //
            // Read off the head pivot, which the networked transform drives on a replica, so this
            // works for a teammate without the pitch being sent as its own value.
            if (neck != null && player != null && player.EyeCamera != null)
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
