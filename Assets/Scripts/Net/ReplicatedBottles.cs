using System.Collections.Generic;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Fills in <see cref="BottleFeed"/> — the netcode half of putting solvent bottles in a client's
    /// room.
    /// <para>
    /// Translation and nothing else, exactly like <see cref="ReplicatedVials"/>:
    /// <c>SolventBottleView</c> is a wire record and <see cref="BottlePlacement"/> is the world
    /// layer's vocabulary for the same thing, and <c>Residue.Gameplay</c> cannot see this assembly.
    /// </para>
    /// <para>
    /// Installed at startup rather than on spawn, and pulled rather than pushed, for the reasons
    /// <see cref="ReplicatedVials"/> gives: the answer is recomputed from live state every time it is
    /// asked for, so there is no spawn hook to forget and no stale reader to leave behind.
    /// </para>
    /// </summary>
    internal static class ReplicatedBottles
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => BottleFeed.Source = Read;

        /// <summary>
        /// Every bottle this client has been told about, or false when this process is not the one
        /// being told. A server returns false and reads its own <c>SolventStore</c> instead — see
        /// <see cref="BottleReconciler"/>, which is the one place that choice is made.
        /// </summary>
        private static bool Read(List<BottlePlacement> into)
        {
            var network = LabNetwork.Instance;
            if (network == null || !network.IsSpawned || network.IsServer) return false;

            var list = network.SolventBottles;
            if (list == null) return false;

            into.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var bottle = list[i];
                into.Add(new BottlePlacement(bottle.Id.ToString(), bottle.Charges, bottle.Capacity,
                                             bottle.Location));
            }
            return true;
        }
    }
}
