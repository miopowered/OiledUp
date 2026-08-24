using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Makes this process's results slips match the paper the host says exists.
    /// <para>
    /// <see cref="VialReconciler"/> with a different prop on the end of it, keyed by
    /// <c>ResultSlips</c> ticket instead of by sample: a blank and a certified standard belong to no
    /// sample, and two runs of the same oil print two slips that have to be told apart. §3.2 keeps a
    /// slip a local prop for the reason it keeps a vial one, so only the record travels and every
    /// client builds the tray for itself out of it — one pass over the published list per frame,
    /// spawning what appeared, re-parenting what moved, destroying what stopped appearing.
    /// </para>
    /// <para>
    /// <b>A slip is consumed by filing, which is what makes the destroy case load-bearing here rather
    /// than tidy-up.</b> The host discards the ticket the moment the numbers join a record; the row
    /// stops appearing on the next publish and every process destroys its paper. Reconciling by set
    /// difference rather than by event is what makes that safe for somebody who joined halfway
    /// through the day or missed a message: nothing below depends on having seen the previous list.
    /// </para>
    /// <para>
    /// <b>Two players reaching for the same tray is the case this has to get right.</b> The host
    /// arbitrates through <c>LabCommands</c> — <c>ResultSlips.TryClaim</c> refuses a slip somebody
    /// else already has — so the loser's <c>Take</c> callback never runs and their
    /// <see cref="PlayerInteractor.Carried"/> stays empty. What they see instead is this: the location
    /// now reads <c>Held(winner)</c>, and the prop is parented into that player's hands with its
    /// colliders off. The paper is never in two pairs of hands, because there is only ever one prop
    /// per ticket and one record saying where it is.
    /// </para>
    /// Host-side this does not run at all: <see cref="LabRuntime"/> prints slips from its own
    /// <c>LabState</c> through <see cref="LabRuntime.SpawnPrintout"/>, so there is one prop system
    /// with two callers rather than two prop systems.
    /// </summary>
    public sealed class SlipReconciler
    {
        private readonly LabRuntime runtime;

        private readonly List<SlipPlacement> snapshot = new();
        private readonly HashSet<int> present = new();
        private readonly List<int> departed = new();

        public SlipReconciler(LabRuntime runtime) => this.runtime = runtime;

        /// <summary>
        /// Read the feed and apply it, or do nothing at all if this process is not being told about
        /// paper. Called once a frame from <see cref="LabRuntime"/>.
        /// </summary>
        public void Tick()
        {
            var source = SlipFeed.Source;
            if (source == null) return;
            if (!source(snapshot)) return;

            Reconcile(snapshot);
        }

        /// <summary>
        /// Bring the props in line with <paramref name="slips"/>. Public so the decision can be tested
        /// without a session: everything netcode-shaped is behind <see cref="SlipFeed"/>, and what is
        /// left is a function of a list and a scene.
        /// </summary>
        public void Reconcile(IReadOnlyList<SlipPlacement> slips)
        {
            if (runtime == null || slips == null) return;

            present.Clear();
            for (int i = 0; i < slips.Count; i++)
            {
                var slip = slips[i];
                if (slip.Ticket == 0) continue;

                present.Add(slip.Ticket);
                Place(slip);
            }

            // Collected before retiring any, because retiring mutates the dictionary being read.
            departed.Clear();
            foreach (var pair in runtime.SlipProps)
            {
                if (!present.Contains(pair.Key)) departed.Add(pair.Key);
            }

            for (int i = 0; i < departed.Count; i++) runtime.RetireSlip(departed[i]);
        }

        private void Place(SlipPlacement slip)
        {
            var prop = runtime.SlipPropFor(slip.Ticket);

            // Rebound every pass rather than only on spawn, and before the branch on where it is,
            // because the key is the one thing about a slip that can arrive late. A run's ResultView
            // row and the slip that names it are published together, but a client that built the prop
            // from a half-arrived snapshot would otherwise hold paper that could never find its own
            // numbers — including paper already in its own hands, which is the case that matters.
            if (prop != null)
            {
                prop.Bind(slip.Ticket, slip.ResultKey, slip.Sample, slip.IsBlank, slip.MachineName,
                          slip.RecordTag);
            }

            if (PropSockets.IsHeldLocally(slip.Location))
            {
                // Put it in your hand and leave it there — and note that this does not return without
                // touching the transform, which is the bug VialReconciler had to be fixed for. Between
                // the host accepting your press and its next publish the location still names the
                // tray, so the branch below dutifully parents the paper back into the instrument; when
                // the publish lands and says you are holding it, an early return here would decline to
                // undo that, and the slip would sit in the tray for ever while the interactor was
                // certain it was in your hand.
                //
                // Re-parenting to the carry socket is safe in a way that re-parenting away from it
                // would not be: it does not touch PlayerInteractor.Carried, and the host only reports
                // Held(you) for a request it accepted, which is the same request that set Carried.
                if (prop == null) return;

                var hands = VialFeed.Hands?.CarrySocket(slip.Location.HolderClientId);
                if (hands != null && prop.transform.parent != hands) prop.AttachTo(hands, interactable: false);
                return;
            }

            var socket = PropSockets.ForSlip(slip.Location, prop != null ? prop.transform : null,
                                             out bool reachable);

            if (prop == null)
            {
                // No socket yet means the instrument has not woken up, or the location is one with no
                // place in the room. Either way, wait rather than parking paper at the origin.
                if (socket == null) return;

                if (runtime.SpawnSlip(slip.Ticket, slip.ResultKey, slip.Sample, slip.IsBlank,
                                      slip.MachineName, slip.RecordTag, socket, reachable) == null)
                {
                    WarnNoPrefab();
                }
                return;
            }

            if (socket != null && prop.transform.parent != socket) prop.AttachTo(socket, reachable);
        }

        private bool warnedPrefab;

        /// <summary>
        /// Once, because this runs every frame and a missing prefab is a standing condition rather
        /// than an event. Said at all because §9 forbids failing quietly: a socket was found and the
        /// paper still did not appear, which from the player's side is an instrument that runs and
        /// prints nothing — and no way to file a result for the rest of the shift.
        /// </summary>
        private void WarnNoPrefab()
        {
            if (warnedPrefab) return;
            warnedPrefab = true;

            Debug.LogWarning(
                "[SlipReconciler] LabRuntime has no printoutPrefab assigned, so no results slip can " +
                "be built on this client and nothing can be filed at the terminal. Assign the " +
                "Printout prefab on the LabRuntime object.");
        }
    }
}
