using System.Collections.Generic;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Makes this process's solvent bottles match the ones the host says exist.
    /// <para>
    /// <see cref="VialReconciler"/> with a different prop on the end of it, and one deliberate
    /// difference: <b>this runs on the host too</b>. A vial's prop is created by the crate as the
    /// sample arrives, so a host has a spawn path of its own and reading its own snapshot back would
    /// be a second prop system a publish behind the first. A bottle has no arrival — there are two of
    /// them and they exist from the first frame — so there was no host-side spawner to duplicate, and
    /// giving both sides the same one means the shelf cannot look different depending on who is
    /// hosting.
    /// </para>
    /// <para>
    /// <b>Nothing is ever destroyed here.</b> A vial can be consumed and stops appearing in the list,
    /// which is how "gone" travels; a bottle is refilled rather than spent, so the set is fixed for
    /// the whole run. An empty snapshot therefore means "not told yet", never "they are gone", and
    /// treating it as the latter would delete both bottles on a client's first frame.
    /// </para>
    /// <para>
    /// It does not move a bottle in the local player's own hands, for the reason
    /// <see cref="VialReconciler"/> gives: those are driven by the callbacks in
    /// <see cref="LabCommands.Attempt"/>, and re-parenting from here would leave
    /// <see cref="PlayerInteractor.Carried"/> believing its hands were empty. The charge count is
    /// still refreshed, because that is a fact about the bottle rather than about where it is — and
    /// it is the fact that changes every time you flush something.
    /// </para>
    /// </summary>
    public sealed class BottleReconciler
    {
        private readonly LabRuntime runtime;
        private readonly List<BottlePlacement> snapshot = new();

        public BottleReconciler(LabRuntime runtime) => this.runtime = runtime;

        /// <summary>
        /// Read whichever source this process has and apply it. Called once a frame from
        /// <see cref="LabRuntime"/> — pull rather than push, for the reason <see cref="VialFeed"/>
        /// gives: one ordering to reason about instead of two.
        /// </summary>
        public void Tick()
        {
            if (runtime == null) return;

            var lab = runtime.Lab;
            if (lab != null)
            {
                Snapshot(lab.Solvent);
                Reconcile(snapshot);
                return;
            }

            var source = BottleFeed.Source;
            if (source == null || !source(snapshot)) return;

            Reconcile(snapshot);
        }

        /// <summary>Project this process's own store into the same records the wire carries.</summary>
        private void Snapshot(SolventStore store)
        {
            snapshot.Clear();
            if (store == null) return;

            var all = store.All;
            for (int i = 0; i < all.Count; i++)
            {
                var bottle = all[i];
                snapshot.Add(new BottlePlacement(bottle.Id, bottle.Charges, bottle.Capacity,
                                                 bottle.Location));
            }
        }

        /// <summary>
        /// Bring the props in line with <paramref name="bottles"/>. Public so the decision can be
        /// tested without a session or a lab: everything process-shaped is behind the seams above, and
        /// what is left is a function of a list and a scene.
        /// </summary>
        public void Reconcile(IReadOnlyList<BottlePlacement> bottles)
        {
            if (runtime == null || bottles == null) return;

            for (int i = 0; i < bottles.Count; i++) Place(bottles[i]);
        }

        private void Place(BottlePlacement bottle)
        {
            if (string.IsNullOrEmpty(bottle.Id)) return;

            var prop = runtime.BottlePropFor(bottle.Id);

            if (PropSockets.IsHeldLocally(bottle.Location))
            {
                // Not ours to move. The count still travels: this is the one thing about a bottle in
                // your own hands that changes while it is there.
                if (prop != null) prop.SetCharges(bottle.Charges);
                return;
            }

            var socket = PropSockets.For(bottle.Location, prop != null ? prop.transform : null,
                                         out bool reachable);

            if (prop == null)
            {
                // No socket yet means the wash station has not woken up. Wait rather than parking a
                // bottle at the origin — but say so, because "the scene has no wash station in it"
                // and "the bottles have not arrived yet" look identical from here and only one of
                // them fixes itself. Without a bottle there is no flushing at all, and §9 forbids
                // that failing quietly.
                if (socket == null) { WarnNoStation(); return; }

                if (runtime.SpawnBottle(bottle.Id, bottle.Capacity, bottle.Charges, socket,
                                        reachable) == null)
                {
                    WarnNoPrefab();
                }
                return;
            }

            if (socket != null && prop.transform.parent != socket) prop.AttachTo(socket, reachable);
            prop.SetCharges(bottle.Charges);
        }

        // Once each. This runs every frame, and a missing fixture is a standing condition rather than
        // an event — repeating it would bury everything else in the console.

        private bool warnedStation;
        private bool warnedPrefab;

        private void WarnNoStation()
        {
            if (warnedStation) return;
            warnedStation = true;

            Debug.LogWarning(
                $"[BottleReconciler] Nothing in this scene is registered as fixture " +
                $"'{SolventStore.StationId}', so the solvent bottles have nowhere to stand and " +
                "cannot be picked up. Add a WashStation component to the lab scene. Until then no " +
                "instrument in the lab can be flushed.");
        }

        private void WarnNoPrefab()
        {
            if (warnedPrefab) return;
            warnedPrefab = true;

            Debug.LogWarning(
                "[BottleReconciler] LabRuntime has no bottlePrefab assigned, so no solvent bottle " +
                "can be built and no instrument can be flushed. Assign the SolventBottle prefab on " +
                "the LabRuntime object.");
        }
    }
}
