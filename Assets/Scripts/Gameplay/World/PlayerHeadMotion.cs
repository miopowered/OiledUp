using Residue.Gameplay.Settings;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Head bob, landing dip and sprint FOV. Everything that makes movement feel like a body rather
    /// than a floating camera.
    /// <para>
    /// Operates on a rig transform beneath the head pivot, never on the head itself. The controller
    /// owns eye height and pitch; if this wrote to the same transform the two would overwrite each
    /// other every frame and the result would look like jitter rather than like a bug.
    /// </para>
    /// Kept subtle on purpose. §2.4 already rules out motion blur and depth of field for fighting
    /// the crisp flat-shaded read, and an aggressive bob does the same thing to a room where the
    /// player spends their time reading small numbers off instrument displays.
    /// </summary>
    public sealed class PlayerHeadMotion : MonoBehaviour
    {
        [SerializeField] private PlayerController player;

        [Tooltip("Transform beneath the head pivot that carries bob and dip. The cameras hang off it.")]
        [SerializeField] private Transform rig;

        [SerializeField] private Camera eyeCamera;

        [Header("Bob")]
        [Tooltip("Full step cycles per second at walking pace.")]
        [SerializeField] private float bobFrequency = 1.9f;

        [SerializeField] private float bobVertical = 0.032f;
        [SerializeField] private float bobHorizontal = 0.022f;

        [Tooltip("Degrees of roll at the extremes of the step. Small; this reads subconsciously.")]
        [SerializeField] private float bobRoll = 0.5f;

        [Header("Landing")]
        [Tooltip("Metres of dip per m/s of impact speed, capped by maxLandingDip.")]
        [SerializeField] private float landingDipPerSpeed = 0.012f;

        [SerializeField] private float maxLandingDip = 0.11f;
        [SerializeField] private float landingRecovery = 7f;

        [Header("Field of view")]
        [Tooltip("Authored default only. The live base is GameSettings.FieldOfView, which this seeds " +
                 "on a profile that has never set one.")]
        [SerializeField] private float baseFov = 70f;

        [SerializeField] private float sprintFovBonus = 6f;
        [SerializeField] private float fovLerp = 6f;

        private float bobPhase;
        private float dip;

        private void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            rig = transform;
        }

        private void Awake()
        {
            GameSettings.Load();
            GameSettings.SeedDefaultFieldOfView(baseFov);
        }

        private void LateUpdate()
        {
            if (player == null || rig == null) return;

            ApplyBob();
            ApplyLanding();
            ApplyFov();
        }

        private void ApplyBob()
        {
            // Head bob is the usual motion-sickness trigger, so it is a switch rather than a taste
            // (#43). Off is expressed as zero amplitude through the same filter below, not as an
            // early return: bailing out would freeze the rig wherever the last step left it and
            // leave the camera permanently off-centre, which looks like the toggle broke something.
            bool bob = GameSettings.HeadBob;

            float speed = bob ? player.SpeedFraction : 0f;

            // Advance the phase by speed so the cycle slows with you rather than running at a fixed
            // rate and sliding out of step with the legs.
            if (bob && player.IsGrounded && speed > 0.05f)
                bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f * Mathf.Max(0.6f, speed);
            else
                bobPhase = Mathf.MoveTowards(bobPhase % (Mathf.PI * 2f), 0f, Time.deltaTime * 6f);

            float amount = bob && player.IsGrounded ? speed : 0f;

            // Vertical runs at double frequency: one dip per footfall, two per stride.
            float y = Mathf.Sin(bobPhase * 2f) * bobVertical * amount;
            float x = Mathf.Sin(bobPhase) * bobHorizontal * amount;
            float roll = Mathf.Sin(bobPhase) * bobRoll * amount;

            currentBob = Vector3.Lerp(currentBob, new Vector3(x, y, 0f), Time.deltaTime * 12f);
            currentRoll = Mathf.Lerp(currentRoll, roll, Time.deltaTime * 12f);

            rig.localPosition = currentBob + Vector3.down * dip;
            rig.localRotation = Quaternion.Euler(0f, 0f, currentRoll);
        }

        private Vector3 currentBob;
        private float currentRoll;

        private void ApplyLanding()
        {
            float impact = player.ConsumeLandingImpact();

            // The controller holds a constant -2 m/s downward bias to stay welded to the floor, so
            // anything at or below that is just walking over a threshold, not a landing.
            if (impact > 2.5f)
                dip = Mathf.Min(dip + impact * landingDipPerSpeed, maxLandingDip);

            dip = Mathf.Lerp(dip, 0f, Time.deltaTime * landingRecovery);
        }

        private void ApplyFov()
        {
            if (eyeCamera == null) return;

            // GameSettings owns the base; the sprint kick modulates it rather than replacing it, so
            // a player who chose 90 gets 96 while sprinting instead of being yanked back to 70.
            float target = GameSettings.FieldOfView + (player.IsSprinting ? sprintFovBonus : 0f);
            eyeCamera.fieldOfView = Mathf.Lerp(eyeCamera.fieldOfView, target, Time.deltaTime * fovLerp);
        }
    }
}
