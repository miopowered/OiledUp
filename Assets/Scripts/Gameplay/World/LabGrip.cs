using System;
using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>What kind of thing is in a player's hands. One pair of hands, so exactly one of these.</summary>
    public enum GripKind
    {
        Empty,
        Vial,
        Slip,
        Book,

        /// <summary>
        /// A solvent bottle (§5.2). Its own kind rather than a flavour of vial, because the two
        /// refuse each other everywhere: an instrument will not run solvent, and a flush will not
        /// accept a sample.
        /// </summary>
        Bottle,

        /// <summary>
        /// A delivery carton (#30). Its own kind because it is the one carried thing that is itself a
        /// container: a rack will not take one, an instrument certainly will not, and the host has to
        /// know a box is in your hands to refuse opening it there.
        /// </summary>
        Carton,

        /// <summary>
        /// A delivery note (#31). Paper, like a slip, but named by the carton it came out of rather
        /// than by a ticket — the numbers on a slip are the host's, and a note carries only what the
        /// customer claimed.
        /// </summary>
        Note
    }

    /// <summary>
    /// What the <i>host</i> believes a player is holding.
    /// <para>
    /// This exists because §3.1 makes every interaction a request the host validates, and half the
    /// requests are about the thing in your hands: load this, agitate this, file this. A client that
    /// asks to load a vial it is not carrying has to be refused by the server rather than assumed
    /// impossible, and the server can only do that if it keeps its own record of whose hands hold
    /// what. Local props cannot serve — <see cref="Carryable"/> is a scene object on one machine
    /// (§3.2), and the host has no way to look inside a client's process.
    /// </para>
    /// <para>
    /// It is deliberately not <c>Residue.Net.Session.HeldItem</c>, which says the same thing on the
    /// other side of the assembly wall. <c>Residue.Gameplay</c> cannot reference <c>Residue.Net</c>
    /// — that direction is what keeps ground truth off the wire — so the validation layer needs its
    /// own vocabulary and the netcode layer translates. The two differ in one useful way: a grip
    /// names a slip by its <see cref="Residue.Gameplay.Simulation.ResultSlips"/> ticket, which is
    /// what filing needs and what a save file has no business remembering.
    /// </para>
    /// </summary>
    public readonly struct LabGrip : IEquatable<LabGrip>
    {
        public readonly GripKind Kind;

        /// <summary>The sample in the vial, or the sample the slip reports on. Unset otherwise.</summary>
        public readonly SampleId Sample;

        /// <summary>The slip's ticket. Zero unless <see cref="Kind"/> is <see cref="GripKind.Slip"/>.</summary>
        public readonly int Ticket;

        /// <summary>
        /// Which one, for the things a sample id cannot name — a
        /// <see cref="Residue.Gameplay.Simulation.SolventBottleState.Id"/>, today. Null otherwise.
        /// <para>
        /// It matters that this is here rather than derived at the far end: there are two bottles in
        /// the lab and a flush spends a charge out of a particular one, so "a bottle" is not a
        /// sufficient answer to what the player is holding.
        /// </para>
        /// </summary>
        public readonly string ItemId;

        private LabGrip(GripKind kind, SampleId sample, int ticket, string itemId = null)
        {
            Kind = kind;
            Sample = sample;
            Ticket = ticket;
            ItemId = string.IsNullOrEmpty(itemId) ? null : itemId;
        }

        public static readonly LabGrip Empty = default;

        public static LabGrip OnVial(SampleId sample) => new(GripKind.Vial, sample, 0);

        public static LabGrip OnSlip(SampleId sample, int ticket) => new(GripKind.Slip, sample, ticket);

        public static LabGrip OnBottle(string bottleId) =>
            new(GripKind.Bottle, SampleId.None, 0, bottleId);

        public static LabGrip OnCarton(string cartonId) =>
            new(GripKind.Carton, SampleId.None, 0, cartonId);

        /// <summary>
        /// The note out of this carton. Named by the box rather than by the job number, because the
        /// box is what the host can look the paper up in — one carton, one note.
        /// </summary>
        public static LabGrip OnNote(string cartonId) =>
            new(GripKind.Note, SampleId.None, 0, cartonId);

        public static readonly LabGrip OnBook = new(GripKind.Book, SampleId.None, 0);

        public static LabGrip OnBookItem(string bookId) =>
            new(GripKind.Book, SampleId.None, 0, bookId);

        public bool IsEmpty => Kind == GripKind.Empty;

        public bool Equals(LabGrip other) =>
            Kind == other.Kind && Sample == other.Sample && Ticket == other.Ticket &&
            string.Equals(ItemId, other.ItemId, System.StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LabGrip o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ Sample.Value;
                hash = (hash * 397) ^ Ticket;
                hash = (hash * 397) ^ (ItemId != null ? ItemId.GetHashCode() : 0);
                return hash;
            }
        }

        public static bool operator ==(LabGrip a, LabGrip b) => a.Equals(b);
        public static bool operator !=(LabGrip a, LabGrip b) => !a.Equals(b);

        public override string ToString() => Kind switch
        {
            GripKind.Vial => $"vial {Sample}",
            GripKind.Slip => $"slip #{Ticket} ({Sample})",
            GripKind.Bottle => $"solvent bottle {ItemId ?? "?"}",
            GripKind.Carton => $"carton {ItemId ?? "?"}",
            GripKind.Note => $"the note from carton {ItemId ?? "?"}",
            GripKind.Book => "a manual",
            _ => "empty-handed"
        };
    }
}
