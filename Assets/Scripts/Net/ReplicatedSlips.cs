using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Gameplay.World;
using Residue.Net.Views;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Fills in <see cref="SlipFeed"/> — the netcode half of putting results slips in a client's room.
    /// <para>
    /// The same shape as <see cref="ReplicatedVials"/>, and it translates for the same reason:
    /// <c>SlipView</c> is a wire record and <see cref="SlipPlacement"/> is the world layer's
    /// vocabulary for the same thing, and <c>Residue.Gameplay</c> cannot see this assembly
    /// (CLAUDE.md's assembly diagram), so the projection happens here and everything downstream is
    /// the code a host runs too.
    /// </para>
    /// <para>
    /// <b>Installed at startup rather than on spawn.</b> The feed is a pull, so there is no spawn hook
    /// to forget and no despawn hook to leave a stale reader behind: the answer is recomputed from
    /// live state every time it is asked for, and "the session went away" needs no notification.
    /// </para>
    /// </summary>
    internal static class ReplicatedSlips
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SlipFeed.Source = Read;
            SlipFeed.Numbers = Numbers;
        }

        /// <summary>
        /// Every slip this client has been told about, or false when this process is not being told.
        /// <para>
        /// A server returns false deliberately. It prints its own paper as its own runs finish, and
        /// reading its own snapshot back to place its own props would be a second prop system a
        /// publish behind the first — the same argument <see cref="LabView.Current"/> makes for a host
        /// reading its own lab.
        /// </para>
        /// </summary>
        private static bool Read(List<SlipPlacement> into)
        {
            var network = LabNetwork.Instance;
            if (network == null || !network.IsSpawned || network.IsServer) return false;

            var list = network.Slips;
            if (list == null) return false;

            into.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var slip = list[i];
                into.Add(new SlipPlacement(slip.Ticket, slip.ResultKey, slip.SampleId, slip.IsBlank,
                                           slip.MachineName.ToString(), slip.RecordTag.ToString(),
                                           slip.Location));
            }
            return true;
        }

        /// <summary>
        /// The numbers a slip names, rebuilt from the two lists that already carry every published
        /// reading.
        /// <para>
        /// <b>It rebuilds, it does not compute.</b> Every value here arrived measured; §3.1 keeps
        /// <c>MeasurementPipeline</c> host-only and nothing below multiplies, adds noise or consults a
        /// threshold. This is the same read <see cref="ReplicatedRecords"/> does for the terminal,
        /// deliberately against the same rows rather than against a copy of them on the slip — one
        /// wire path to a run's figures means the paper in a player's hand and the panel at the desk
        /// cannot disagree.
        /// </para>
        /// Called only when somebody glances at a slip, so allocating the result is the right trade:
        /// the alternative is holding a rebuilt run per slip and refreshing it four times a second to
        /// serve a keypress nobody may press.
        /// </summary>
        private static bool Numbers(int resultKey, out TestResult result)
        {
            result = null;
            if (resultKey == 0) return false;

            var network = LabNetwork.Instance;
            if (network == null || !network.IsSpawned) return false;

            var results = network.Results;
            var readings = network.Readings;
            if (results == null) return false;

            bool found = false;
            for (int i = 0; i < results.Count; i++)
            {
                var row = results[i];
                if (row.Key != resultKey) continue;

                result = new TestResult
                {
                    MachineId = row.MachineDefId.ToString(),
                    DayRun = row.DayRun,
                    VolumeConsumedMl = row.VolumeConsumedMl,
                    Cost = row.Cost,
                    IsBlank = row.IsBlank,
                    IsReference = row.IsReference,
                    Suspect = row.Suspect
                };
                found = true;
                break;
            }

            // A slip whose run has not arrived reads as "not yet" rather than as an empty result. The
            // two lists are written in one pass, so this is the frame-boundary case and it resolves
            // itself on the next publish — with nothing shown under the wrong heading meanwhile.
            if (!found || readings == null) return found;

            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                if (reading.ResultKey != resultKey) continue;

                result.Values[reading.ElementId.ToString()] = reading.Value;
            }

            return true;
        }
    }
}
