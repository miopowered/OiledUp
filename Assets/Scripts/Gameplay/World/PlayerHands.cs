using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// First-person hands: the part of the body the owner actually sees.
    /// <para>
    /// Three poses, chosen by what is in the interactor — empty, holding a vial, holding something
    /// two-handed. There is no blend tree and no clips; each pose is a target transform and the
    /// hands spring towards it. With flat-shaded blocky geometry that is indistinguishable from an
    /// authored animation, and it means the pose is always correct for the carry state rather than
    /// correct once the clip finishes.
    /// </para>
    /// Hands exist mainly so the carried vial has something holding it. A vial floating at chest
    /// height in front of the camera is the single thing that most makes a first-person game read as
    /// unfinished, and §1.1.4 wants the player to feel like they are handling a real sample.
    /// </summary>
    public sealed class PlayerHands : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInteractor interactor;

        [Tooltip("Root the hands hang off. Should be the camera rig, so bob carries through.")]
        [SerializeField] private Transform leftHand;

        [SerializeField] private Transform rightHand;

        [Header("Poses (local to the camera rig)")]
        [SerializeField] private Vector3 leftIdlePosition = new(-0.26f, -0.34f, 0.36f);
        [SerializeField] private Vector3 leftIdleRotation = new(18f, 22f, -8f);
        [SerializeField] private Vector3 rightIdlePosition = new(0.26f, -0.34f, 0.36f);
        [SerializeField] private Vector3 rightIdleRotation = new(18f, -22f, 8f);

        [Tooltip("The right hand rises to the carry socket; the left stays down and out of the way.")]
        [SerializeField] private Vector3 rightVialPosition = new(0.22f, -0.20f, 0.40f);

        [SerializeField] private Vector3 rightVialRotation = new(-8f, -12f, 6f);
        [SerializeField] private Vector3 leftVialPosition = new(-0.28f, -0.40f, 0.28f);
        [SerializeField] private Vector3 leftVialRotation = new(24f, 26f, -10f);

        [Tooltip("Both hands come up and in for anything bigger than a vial — a printout, a manual, a carton.")]
        [SerializeField] private Vector3 leftBothPosition = new(-0.20f, -0.24f, 0.44f);

        [SerializeField] private Vector3 leftBothRotation = new(-4f, 18f, -14f);
        [SerializeField] private Vector3 rightBothPosition = new(0.20f, -0.24f, 0.44f);
        [SerializeField] private Vector3 rightBothRotation = new(-4f, -18f, 14f);

        [Header("Feel")]
        [SerializeField] private float poseSpeed = 11f;

        [Tooltip("How far the hands lag behind a fast turn, in metres per degree per second.")]
        [SerializeField] private float swayPerDegree = 0.0016f;

        [SerializeField] private float maxSway = 0.05f;
        [SerializeField] private float swayRecovery = 7f;

        [Tooltip("Extra swing on the idle hands while walking, so they are not welded to the view.")]
        [SerializeField] private float walkSwing = 0.026f;

        private Vector3 sway;
        private float lastYaw;
        private float phase;

        private void Awake()
        {
            if (player != null) lastYaw = player.transform.eulerAngles.y;

            // Snap to the idle pose rather than lerping to it from wherever the builder left them,
            // which is the rig origin — i.e. inside the near clip plane for the first few frames.
            Snap(leftHand, leftIdlePosition, leftIdleRotation);
            Snap(rightHand, rightIdlePosition, rightIdleRotation);
        }

        private static void Snap(Transform hand, Vector3 position, Vector3 rotation)
        {
            if (hand == null) return;

            hand.localPosition = position;
            hand.localRotation = Quaternion.Euler(rotation);
        }

        private void LateUpdate()
        {
            if (player == null) return;

            UpdateSway();
            UpdatePhase();

            bool carrying = interactor != null && interactor.Carried != null;
            bool vial = carrying && interactor.CarriedVial != null;

            // Idle hands swing with the stride; carried hands do not, because the item they hold has
            // to stay put relative to the carry socket or it visibly detaches.
            float swing = carrying ? 0f : Mathf.Sin(phase) * walkSwing * player.SpeedFraction;

            if (carrying)
            {
                Drive(leftHand, vial ? leftVialPosition : leftBothPosition,
                    vial ? leftVialRotation : leftBothRotation, 0f);
                Drive(rightHand, vial ? rightVialPosition : rightBothPosition,
                    vial ? rightVialRotation : rightBothRotation, 0f);
            }
            else
            {
                Drive(leftHand, leftIdlePosition, leftIdleRotation, swing);
                Drive(rightHand, rightIdlePosition, rightIdleRotation, -swing);
            }
        }

        private void UpdatePhase()
        {
            if (player.IsGrounded && player.SpeedFraction > 0.05f)
                phase += Time.deltaTime * 1.9f * Mathf.PI * 2f * Mathf.Max(0.6f, player.SpeedFraction);
            else
                phase = Mathf.MoveTowards(phase % (Mathf.PI * 2f), 0f, Time.deltaTime * 6f);
        }

        private void UpdateSway()
        {
            float yaw = player.transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(lastYaw, yaw);
            lastYaw = yaw;

            // Turning left leaves the hands trailing to the right. Clamped, or a fast flick throws
            // them off screen.
            sway.x = Mathf.Clamp(sway.x - delta * swayPerDegree, -maxSway, maxSway);
            sway = Vector3.Lerp(sway, Vector3.zero, Time.deltaTime * swayRecovery);
        }

        private void Drive(Transform hand, Vector3 position, Vector3 rotation, float swing)
        {
            if (hand == null) return;

            Vector3 target = position + sway + new Vector3(0f, swing, 0f);

            hand.localPosition = Vector3.Lerp(hand.localPosition, target, Time.deltaTime * poseSpeed);
            hand.localRotation = Quaternion.Slerp(hand.localRotation, Quaternion.Euler(rotation),
                Time.deltaTime * poseSpeed);
        }
    }
}
