using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Makes this process's delivery cartons match the boxes the host says exist (#80).
    ///
    /// <para>
    /// <see cref="SlipReconciler"/> with a bigger prop on the end of it: one pass over the published
    /// list per frame, spawning what appeared, re-parenting what moved, and destroying what stopped
    /// appearing. §3.2 keeps a carton a local prop for the reason it keeps a vial one, so only the
    /// record travels and every client builds the bay for itself out of it.
    /// </para>
    ///
    /// <para>
    /// <b>Two props per row, because a box and its paperwork move independently.</b> The note starts
    /// inside the carton and comes out, gets carried to a bench and left there, and none of that
    /// touches the box. Its socket is the one place this does not simply defer to
    /// <see cref="PropSockets"/>: a note that is <i>in</i> its box is at <c>InCrate(cartonId, -1)</c>,
    /// and resolving that through <see cref="IVialSlots"/> would hand back a <i>bottle</i> hole — the
    /// paper has a socket of its own on the prop and does not compete for one (see
    /// <see cref="Carton.InsideSlot"/>).
    /// </para>
    ///
    /// <para>
    /// <b>The bottles inside are not this type's business.</b> Each one's own <c>VialView</c> already
    /// names this carton as its container, so <see cref="VialReconciler"/> places them the moment
    /// <see cref="CartonProp"/> registers its slots — which it only does once the seal has gone. That
    /// is what keeps a sealed box opaque on a client without anything here having to know it: no
    /// slots, no socket, no bottle.
    /// </para>
    ///
    /// Host-side this does not run at all: <see cref="LabRuntime"/> ticks it only where there is no
    /// <c>LabState</c>, and a host builds its boxes from its own bay through
    /// <see cref="DeliveryBayStation"/> — one prop system with two callers rather than two prop
    /// systems, exactly as for vials and slips.
    /// </summary>
    public sealed class CartonReconciler
    {
        private readonly LabRuntime runtime;

        private readonly List<CartonPlacement> snapshot = new();
        private readonly HashSet<string> present = new(StringComparer.Ordinal);
        private readonly List<string> departed = new();

        public CartonReconciler(LabRuntime runtime) => this.runtime = runtime;

        /// <summary>
        /// Read the feed and apply it, or do nothing at all if this process is not being told about
        /// boxes. Called once a frame from <see cref="LabRuntime"/>.
        /// </summary>
        public void Tick()
        {
            var source = CartonFeed.Source;
            if (source == null) return;
            if (!source(snapshot)) return;

            Reconcile(snapshot);
        }

        /// <summary>
        /// Bring the props in line with <paramref name="cartons"/>. Public so the decision can be
        /// tested without a session: everything netcode-shaped is behind <see cref="CartonFeed"/>, and
        /// what is left is a function of a list and a scene.
        /// </summary>
        public void Reconcile(IReadOnlyList<CartonPlacement> cartons)
        {
            if (runtime == null || cartons == null) return;

            // Published before anything is placed. CartonProp and CartonLid read their box out of the
            // feed every frame they draw a prompt, and a prompt built from last frame's rows would
            // describe a box somebody has already carried off.
            CartonFeed.Publish(cartons);

            present.Clear();
            for (int i = 0; i < cartons.Count; i++)
            {
                var carton = cartons[i];
                if (string.IsNullOrEmpty(carton.Id)) continue;

                // Belt and braces: the host already drops these, and a box that was both listed and
                // flattened would be kept standing in the room by having been mentioned.
                if (carton.Stage == CartonStage.Discarded) continue;

                present.Add(carton.Id);
                Place(carton);
            }

            // Collected before retiring any, because retiring mutates the dictionary being read.
            departed.Clear();
            foreach (var pair in runtime.CartonProps)
            {
                if (!present.Contains(pair.Key)) departed.Add(pair.Key);
            }

            // Takes the note with it — see LabRuntime.RetireCarton. A flattened box's paperwork is
            // gone by definition: the bay refuses to flatten one whose note is still inside or still
            // in somebody's hands.
            for (int i = 0; i < departed.Count; i++) runtime.RetireCarton(departed[i]);
        }

        private void Place(in CartonPlacement carton)
        {
            var prop = runtime.CartonPropFor(carton.Id);

            // Still on the lorry. Nothing is built for it, deliberately: #30's whole point is that the
            // load has to be set down and carried in, and a box that existed in the room before the
            // courier put it down would be a box somebody could reach.
            if (carton.Stage == CartonStage.OnTheRoad) return;

            if (PropSockets.IsHeldLocally(carton.Location))
            {
                // Put it in your arms and leave it there — and note that this does not return without
                // touching the transform, which is the bug VialReconciler had to be fixed for. Between
                // the host accepting your press and its next publish the location still names the bay,
                // so the branch below dutifully parents the box back onto its standing place; when the
                // publish lands and says you are carrying it, an early return here would decline to
                // undo that.
                //
                // Safe in a way that re-parenting away from the hands would not be: it does not touch
                // PlayerInteractor.Carried, and the host only reports Held(you) for a request it
                // accepted — the same request that set Carried.
                if (prop == null) return;

                var hands = VialFeed.Hands?.CarrySocket(carton.Location.HolderClientId);
                if (hands != null && prop.transform.parent != hands) prop.AttachTo(hands, interactable: false);
            }
            else
            {
                var socket = PropSockets.For(carton.Location, prop != null ? prop.transform : null,
                                             out bool reachable);

                if (prop == null)
                {
                    // No socket yet means the bay has not woken up, or the location is one with no
                    // place in the room. Either way, wait rather than parking a box at the origin.
                    if (socket == null) return;

                    prop = runtime.SpawnCarton(carton.Id, carton.JobNumber, carton.SenderName, socket,
                                               reachable);
                    if (prop == null) { WarnNoPrefab(); return; }
                }
                else if (socket != null && prop.transform.parent != socket)
                {
                    prop.AttachTo(socket, reachable);
                }
            }

            PlaceNote(carton, prop);
        }

        /// <summary>
        /// Put the paperwork where the host says it is.
        /// <para>
        /// The prop itself is built by <see cref="CartonProp"/> when the seal goes, on this side
        /// exactly as on the host's — there is one place a note comes into existence, which is the
        /// moment the box is opened (#31). This only moves it, and re-prints it if the lines arrived
        /// after the box did.
        /// </para>
        /// </summary>
        private void PlaceNote(in CartonPlacement carton, CartonProp box)
        {
            var note = runtime.NotePropFor(carton.Id);
            if (note == null) return;

            // The page is written once and never rewritten (see DeliveryNoteProp), so this is a
            // late-arrival fix rather than a refresh: a client that built the prop from a half-arrived
            // snapshot would otherwise be holding permanently blank paper — and the note is the whole
            // of #32's evidence.
            if (!note.IsPrinted && carton.Note != null)
            {
                note.Bind(carton.Id, carton.JobNumber, carton.SenderName,
                          DeliveryNoteProp.Printed(carton.Note));
            }

            if (PropSockets.IsHeldLocally(carton.NoteLocation))
            {
                var hands = VialFeed.Hands?.CarrySocket(carton.NoteLocation.HolderClientId);
                if (hands != null && note.transform.parent != hands) note.AttachTo(hands, interactable: false);
                return;
            }

            var socket = NoteSocket(carton, box, note, out bool reachable);
            if (socket != null && note.transform.parent != socket) note.AttachTo(socket, reachable);
        }

        /// <summary>
        /// Where the paper belongs. Its own case for "still in the box", and
        /// <see cref="PropSockets"/> for everywhere else — see the type doc for why the two cannot be
        /// one lookup.
        /// </summary>
        private Transform NoteSocket(in CartonPlacement carton, CartonProp box, DeliveryNoteProp note,
                                     out bool reachable)
        {
            if (carton.NoteLocation.Kind == SampleLocationKind.InCrate &&
                string.Equals(carton.NoteLocation.ContainerId, carton.Id, StringComparison.Ordinal))
            {
                reachable = true;
                return box != null ? box.NoteSocket : null;
            }

            return PropSockets.For(carton.NoteLocation, note.transform, out reachable);
        }

        private bool warnedPrefab;

        /// <summary>
        /// Once, because this runs every frame and a missing prefab is a standing condition rather
        /// than an event. Said at all because §9 forbids failing quietly: a socket was found and the
        /// box still did not appear, which from the player's side is a delivery that arrives and
        /// cannot be unloaded — and therefore a shift with no samples in it.
        /// </summary>
        private void WarnNoPrefab()
        {
            if (warnedPrefab) return;
            warnedPrefab = true;

            Debug.LogWarning(
                "[CartonReconciler] LabRuntime has no cartonPrefab assigned, so no delivery carton " +
                "can be built on this client and the day's samples can never be carried in. Assign " +
                "the Carton prefab on the LabRuntime object.");
        }
    }
}
