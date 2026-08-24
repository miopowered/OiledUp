using System.Collections.Generic;
using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Makes this process's vial props match the bottles the host says exist.
    /// <para>
    /// §3.2 is explicit that a vial is a local prop and never a <c>NetworkObject</c> — a busy shift has
    /// 200+ of them and one networked object per bottle would drown the connection. So only the record
    /// travels, and every client builds the room for itself out of it. That is what this does: one
    /// pass over the published list per frame, spawning what appeared, re-parenting what moved, and
    /// destroying what stopped appearing.
    /// </para>
    /// <para>
    /// <b>Consumed bottles are dropped from the list rather than tombstoned</b> (see
    /// <c>LabNetwork.Sync</c>), so "gone" is an absence and not a flag. Reconciling by set difference
    /// rather than by event is what makes that safe: a client that missed a message, joined halfway
    /// through the day, or reconnected converges on the next publish regardless, because nothing here
    /// depends on having seen the previous one.
    /// </para>
    /// <para>
    /// <b>It never touches the local player's hands.</b> Those are driven by the callbacks in
    /// <see cref="LabCommands.Attempt"/>, which are the only thing that also writes
    /// <see cref="PlayerInteractor.Carried"/>. Re-parenting a carried prop from here would leave the
    /// interactor believing its hands were empty while a bottle hung in them — it would offer to pick
    /// up a second one, and the host would refuse a request the player was invited to make, which is
    /// exactly what hard rule 3 forbids. The fill level is still refreshed, because that is a fact
    /// about the bottle rather than about where it is.
    /// </para>
    /// Host-side this does not run at all: <see cref="LabRuntime"/> spawns props from its own
    /// <c>LabState</c> through the same <see cref="LabRuntime.SpawnVial(SampleId,string,float,Transform,bool)"/>
    /// it always did, so there is one prop system with two callers rather than two prop systems.
    /// </summary>
    public sealed class VialReconciler
    {
        private readonly LabRuntime runtime;

        private readonly List<VialPlacement> snapshot = new();
        private readonly HashSet<SampleId> present = new();
        private readonly List<SampleId> departed = new();

        public VialReconciler(LabRuntime runtime) => this.runtime = runtime;

        /// <summary>
        /// Read the feed and apply it, or do nothing at all if this process is not being told about
        /// bottles. Called once a frame from <see cref="LabRuntime"/>.
        /// </summary>
        public void Tick()
        {
            var source = VialFeed.Source;
            if (source == null) return;
            if (!source(snapshot)) return;

            Reconcile(snapshot);
        }

        /// <summary>
        /// Bring the props in line with <paramref name="vials"/>. Public so the decision can be tested
        /// without a session: everything netcode-shaped is behind <see cref="VialFeed"/>, and what is
        /// left is a function of a list and a scene.
        /// </summary>
        public void Reconcile(IReadOnlyList<VialPlacement> vials)
        {
            if (runtime == null || vials == null) return;

            present.Clear();
            for (int i = 0; i < vials.Count; i++)
            {
                var vial = vials[i];
                if (!vial.Sample.IsValid) continue;

                // Belt and braces: the host already drops these, and a bottle that is both listed and
                // spent would otherwise be kept alive by having been mentioned.
                if (vial.Location.Kind == SampleLocationKind.Consumed) continue;

                present.Add(vial.Sample);
                Place(vial);
            }

            // Collected before retiring any, because retiring mutates the dictionary being read.
            departed.Clear();
            foreach (var pair in runtime.Props)
            {
                if (!present.Contains(pair.Key)) departed.Add(pair.Key);
            }

            for (int i = 0; i < departed.Count; i++) runtime.RetireVial(departed[i]);
        }

        private void Place(VialPlacement vial)
        {
            var prop = runtime.PropFor(vial.Sample);

            if (PropSockets.IsHeldLocally(vial.Location))
            {
                // Not ours to move — see the type doc. The volume still travels: a vial that has just
                // come out of an instrument is lighter than it went in, and the player is holding the
                // one thing that shows it.
                if (prop != null) prop.SetFillFraction(vial.VolumeMl / VialProp.FullMl);
                return;
            }

            var socket = PropSockets.For(vial.Location, prop != null ? prop.transform : null,
                                         out bool reachable);

            if (prop == null)
            {
                // No socket yet means the fixture has not woken up, or the location is one with no
                // place in the room. Either way, wait rather than parking a bottle at the origin.
                if (socket == null) return;

                runtime.SpawnVial(vial.Sample, vial.Label, vial.VolumeMl, socket, reachable);
                return;
            }

            if (socket != null && prop.transform.parent != socket) prop.AttachTo(socket, reachable);
            prop.SetFillFraction(vial.VolumeMl / VialProp.FullMl);
        }

    }
}
