using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The one thing the tutorial's arrow is hanging over, and how far above it to hang.
    ///
    /// <para>
    /// <b>A transform and a clearance, and deliberately nothing else.</b> Everything a marker is
    /// allowed to know about its target is "where is it" and "how tall is it" — see
    /// <see cref="TutorialTargets"/> for why. A field naming a sample, a reading or a severity would
    /// be the seam through which hard rule 1 leaks: the arrow could then single a bottle out for a
    /// reason drawn from its chemistry, and a player would learn to follow the arrow instead of
    /// learning the lab.
    /// </para>
    ///
    /// <para>
    /// The anchor is a live <see cref="Transform"/> rather than a captured position because half of
    /// what gets marked moves — a carton is carried to a bench, a vial is racked, a slip is picked
    /// out of a tray. A position sampled once would leave the arrow hanging over where the thing used
    /// to be, which is worse than no arrow at all.
    /// </para>
    /// </summary>
    public readonly struct TutorialTarget
    {
        /// <summary>Nothing to point at. What every reader gets outside a tutorial run.</summary>
        public static readonly TutorialTarget None = default;

        /// <summary>The object the arrow hangs over, or null when there is nothing to mark.</summary>
        public readonly Transform Anchor;

        /// <summary>Metres between the top of the anchor's own geometry and the arrow's tip.</summary>
        public readonly float Clearance;

        public TutorialTarget(Transform anchor, float clearance)
        {
            Anchor = anchor;
            Clearance = clearance;
        }

        /// <summary>
        /// Unity's <c>==</c> rather than a null check, because a destroyed transform is still a live
        /// C# reference — a carton retired at the end of the day would otherwise read as a target
        /// right up until something tried to read its position.
        /// </summary>
        public bool Exists => Anchor != null;
    }
}
