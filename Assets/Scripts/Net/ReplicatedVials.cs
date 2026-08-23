using System.Collections.Generic;
using Residue.Gameplay.World;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Fills in <see cref="VialFeed"/> — the netcode half of putting bottles in a client's room.
    /// <para>
    /// All it does is translate. <c>VialView</c> is a wire record and <see cref="VialPlacement"/> is
    /// the world layer's vocabulary for the same thing; <c>Residue.Gameplay</c> cannot see this
    /// assembly and must not (CLAUDE.md's assembly diagram), so the projection happens here and
    /// everything downstream is the code a host runs too.
    /// </para>
    /// <para>
    /// <b>Installed at startup rather than on spawn.</b> The feed is a pull — <c>VialReconciler</c>
    /// asks once a frame and gets <c>false</c> whenever there is no session, no
    /// <see cref="LabNetwork"/>, or this process is the one doing the simulating. Wiring it that way
    /// means there is no spawn hook to forget and no despawn hook to leave a stale reader behind: the
    /// answer is recomputed from live state every time it is asked for, so "the session went away"
    /// needs no notification.
    /// </para>
    /// </summary>
    internal static class ReplicatedVials
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            VialFeed.Source = Read;
            VialFeed.Hands = new SpawnedPlayerHands();
        }

        /// <summary>
        /// Every bottle this client has been told about, or false when this process is not being told.
        /// <para>
        /// A server returns false deliberately. It publishes this list, and reading its own snapshot
        /// back to place its own props would be a second prop system a publish behind the first — the
        /// same argument <see cref="LabView.Current"/> makes for a host reading its own lab.
        /// </para>
        /// </summary>
        private static bool Read(List<VialPlacement> into)
        {
            var network = LabNetwork.Instance;
            if (network == null || !network.IsSpawned || network.IsServer) return false;

            var list = network.Vials;
            if (list == null) return false;

            into.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var vial = list[i];
                into.Add(new VialPlacement(vial.SampleId, vial.Label.ToString(), vial.VolumeMl,
                                           vial.Location));
            }
            return true;
        }

        /// <summary>
        /// Client id to a pair of hands, via the spawned player objects.
        /// <para>
        /// Not <c>NetworkSpawnManager.GetPlayerNetworkObject</c>, which throws on a client asked about
        /// anyone but itself — and every id this is asked about is somebody else, because the local
        /// player's own hands are the one thing the reconciler will not touch.
        /// </para>
        /// Cached, because a bottle in somebody's hands is looked up every frame it stays there and
        /// the spawn table is not indexed by owner.
        /// </summary>
        private sealed class SpawnedPlayerHands : IPlayerHands
        {
            private readonly Dictionary<ulong, Transform> sockets = new();

            public ulong LocalClientId
            {
                get
                {
                    var manager = NetworkManager.Singleton;
                    return manager != null ? manager.LocalClientId : 0UL;
                }
            }

            public Transform CarrySocket(ulong clientId)
            {
                // Unity's ==, so a body that has despawned since it was cached is re-resolved rather
                // than handed back as a live-looking reference to a destroyed transform.
                if (sockets.TryGetValue(clientId, out var cached) && cached != null) return cached;

                var socket = Find(clientId);
                if (socket != null) sockets[clientId] = socket;
                return socket;
            }

            private static Transform Find(ulong clientId)
            {
                var manager = NetworkManager.Singleton;
                var spawned = manager != null ? manager.SpawnManager : null;
                if (spawned == null) return null;

                foreach (var obj in spawned.SpawnedObjectsList)
                {
                    if (obj == null || !obj.IsPlayerObject || obj.OwnerClientId != clientId) continue;

                    var avatar = obj.GetComponent<PlayerAvatar>();
                    return avatar != null ? avatar.CarrySocket : null;
                }
                return null;
            }
        }
    }
}
