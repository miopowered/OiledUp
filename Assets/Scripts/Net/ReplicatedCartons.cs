using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net.Views;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Fills in <see cref="CartonFeed"/> — the netcode half of putting the delivery in a client's bay
    /// (#80).
    /// <para>
    /// The same shape as <see cref="ReplicatedSlips"/>, and it translates for the same reason:
    /// <c>CartonView</c> is a wire record and <see cref="CartonPlacement"/> is the world layer's
    /// vocabulary for the same thing, and <c>Residue.Gameplay</c> cannot see this assembly (CLAUDE.md's
    /// assembly diagram), so the projection happens here and everything downstream is the code a host
    /// runs too.
    /// </para>
    /// <para>
    /// <b>Installed at startup rather than on spawn.</b> The feed is a pull, so there is no spawn hook
    /// to forget and no despawn hook to leave a stale reader behind: the answer is recomputed from live
    /// state every time it is asked for, and "the session went away" needs no notification.
    /// </para>
    /// </summary>
    internal static class ReplicatedCartons
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => CartonFeed.Source = Read;

        /// <summary>
        /// Every carton this client has been told about, or false when this process is not being told.
        /// <para>
        /// A server returns false deliberately. It builds its boxes from its own <c>DeliveryBay</c> as
        /// the truck sets them down, and reading its own snapshot back to place its own props would be
        /// a second prop system a publish behind the first — the same argument
        /// <see cref="LabView.Current"/> makes for a host reading its own lab.
        /// </para>
        /// </summary>
        private static bool Read(List<CartonPlacement> into)
        {
            var network = LabNetwork.Instance;
            if (network == null || !network.IsSpawned || network.IsServer) return false;

            var list = network.Cartons;
            if (list == null) return false;

            var catalog = Catalog;

            into.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                string id = row.Id.ToString();
                var customer = catalog != null ? catalog.Customer(row.CustomerId.ToString()) : null;

                into.Add(new CartonPlacement(
                    id,
                    row.JobNumber.ToString(),
                    customer != null ? customer.DisplayName : "an unnamed sender",
                    row.Stage,
                    row.IsSealed,
                    row.VialsRemaining,
                    row.Location,
                    row.NoteLocation,
                    NoteFor(network, row, customer, catalog)));
            }
            return true;
        }

        /// <summary>
        /// Every delivery note this client has been told about, for the desk (<see cref="LabRecords"/>).
        /// <para>
        /// The same objects the paper props are typed from, deliberately. The terminal's reconcile
        /// panel offers the lines an ambiguous vial might answer, and the note in the box is what the
        /// player is holding while they decide — a desk drawing from a second rebuild could renumber
        /// the page, and a row number is what a registration is filed under.
        /// </para>
        /// </summary>
        internal static void ReadNotes(LabNetwork network, List<DeliveryNote> into)
        {
            if (into == null) return;
            into.Clear();

            var list = network != null ? network.Cartons : null;
            if (list == null) return;

            var catalog = Catalog;

            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                var customer = catalog != null ? catalog.Customer(row.CustomerId.ToString()) : null;

                var note = NoteFor(network, row, customer, catalog);
                if (note != null) into.Add(note);
            }
        }

        // -- Rebuilding the paperwork ------------------------------------------------------------------
        //
        // Notes are cached because a note is written once and never rewritten — DeliveryNoteProp is
        // explicit that a page which re-typeset itself as the box emptied would answer #32 for the
        // player. Caching is therefore not only cheaper than rebuilding six pages a frame; it is the
        // behaviour the design asks for. Carton ids carry the day they were booked on, so an id is
        // unique for the whole run and a cached page can never be handed to a different delivery.

        private static readonly Dictionary<string, DeliveryNote> cached = new();
        private static int cachedLines = -1;

        /// <summary>
        /// The page that came in this box, rebuilt from the published lines.
        /// <para>
        /// <b>Every line comes back unanswered.</b> <c>DeliveryNote.Line.Sample</c> is host bookkeeping
        /// that is never printed and never travels (see <c>NoteLineView</c>), so the rebuilt page
        /// carries <see cref="SampleId.None"/> throughout. That is the correct shape rather than a
        /// lossy one: what the customer typed is exactly what the paper says, and which vial answers
        /// which claim is the player's job.
        /// </para>
        /// </summary>
        private static DeliveryNote NoteFor(LabNetwork network, in CartonView carton,
                                            CustomerDef customer, ContentCatalog catalog)
        {
            var lines = network.NoteLines;
            if (lines == null) return null;

            Refresh(lines, catalog);

            return cached.TryGetValue(carton.Id.ToString(), out var note)
                ? note
                : Build(lines, carton, customer, catalog);
        }

        /// <summary>
        /// Drop the cache when the published lines change shape. A note never changes, but the set of
        /// notes does — a box is flattened, tomorrow's delivery is booked — and the row count is the
        /// cheapest thing that moves when it does.
        /// </summary>
        private static void Refresh(NetworkList<NoteLineView> lines, ContentCatalog catalog)
        {
            if (lines.Count == cachedLines && cachedCatalog == catalog) return;

            cached.Clear();
            cachedLines = lines.Count;
            cachedCatalog = catalog;
        }

        private static ContentCatalog cachedCatalog;

        private static DeliveryNote Build(NetworkList<NoteLineView> lines, in CartonView carton,
                                          CustomerDef customer, ContentCatalog catalog)
        {
            string id = carton.Id.ToString();
            var note = new DeliveryNote(customer, carton.JobNumber.ToString(), carton.Day);

            // Row order is the printed order, and the host publishes a carton's lines contiguously in
            // index order. Filtered rather than indexed so a snapshot that arrives interleaved still
            // types the page out in the order the dispatcher booked it.
            var packed = ViewText.Fixed64(id);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!line.CartonId.Equals(packed)) continue;

                string tag = line.TankTag.ToString();
                note.Add(string.IsNullOrEmpty(tag) ? null : tag,
                         catalog != null ? catalog.Profile(line.ProfileId.ToString()) : null,
                         SampleId.None);
            }

            cached[id] = note;
            return note;
        }

        private static ContentCatalog Catalog =>
            LabRuntime.Instance != null ? LabRuntime.Instance.Catalog : null;
    }
}
