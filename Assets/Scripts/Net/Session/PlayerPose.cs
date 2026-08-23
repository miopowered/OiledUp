using System;
using UnityEngine;

namespace Residue.Net.Session
{
    /// <summary>
    /// Where a player was standing and looking. §3.1 gives clients authority over exactly this and
    /// nothing else, so it is also the only piece of a session the client itself was the source of —
    /// which is why the host has to remember it rather than ask for it back.
    /// <para>
    /// Kept as position plus two angles rather than a full rotation because that is what the
    /// controller actually owns: yaw lives on the body, pitch on the head pivot, and a quaternion
    /// would round-trip into a head that can roll.
    /// </para>
    /// Restoring the pose matters more than it looks. §5.5 is a layout game — the walk from the rack
    /// to the fume hood is a real cost — so respawning someone at the door quietly charges them that
    /// walk a second time for a dropped packet.
    /// </summary>
    [Serializable]
    public struct PlayerPose : IEquatable<PlayerPose>
    {
        public Vector3 Position;

        /// <summary>Body rotation about Y, degrees.</summary>
        public float Yaw;

        /// <summary>Head pitch, degrees, sign and range matching <c>PlayerController</c>.</summary>
        public float Pitch;

        /// <summary>
        /// False for a session that has never had a pose written to it — a player mid-first-join.
        /// The caller uses this to choose between "put them back" and "put them at a spawn point".
        /// </summary>
        public bool HasValue;

        public static PlayerPose At(Vector3 position, float yaw, float pitch) => new()
        {
            Position = position,
            Yaw = yaw,
            Pitch = pitch,
            HasValue = true
        };

        public static readonly PlayerPose Unknown = default;

        public bool Equals(PlayerPose other) =>
            HasValue == other.HasValue &&
            Position == other.Position &&
            Mathf.Approximately(Yaw, other.Yaw) &&
            Mathf.Approximately(Pitch, other.Pitch);

        public override bool Equals(object obj) => obj is PlayerPose o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasValue ? 1 : 0;
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ Yaw.GetHashCode();
                hash = (hash * 397) ^ Pitch.GetHashCode();
                return hash;
            }
        }

        public override string ToString() =>
            HasValue ? $"{Position} yaw {Yaw:F0}° pitch {Pitch:F0}°" : "pose unknown";
    }
}
