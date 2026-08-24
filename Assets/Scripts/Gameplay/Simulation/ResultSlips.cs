using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The host's outstanding paperwork: one ticket per results slip an instrument has printed and
    /// nobody has filed yet.
    /// <para>
    /// <b>This is what stops a client filing numbers it made up.</b> §3.1 says never let a client
    /// compute a test result, and that is worth very little if a client may instead <i>post</i> one.
    /// A slip is a local prop (§3.2), so on a client the values on it are just bytes in that
    /// player's process; if "file this slip" carried those values to the host, a modified client
    /// could write anything it liked into the archive that every verdict is later scored against — and
    /// hard rule 1 would be broken from the outside. A ticket is an opaque handle instead: the client
    /// says <i>which</i> slip, and the host files the numbers it produced itself.
    /// </para>
    /// <para>
    /// It also closes something single player quietly got away with. A slip used to be identified by
    /// the <see cref="MachineInstance.LastResult"/> still sitting on the instrument, which moves on
    /// the moment that instrument runs again — so a slip carried away, followed by another run, filed
    /// the wrong numbers. A ticket keeps the paper and its values together for exactly as long as the
    /// paper exists.
    /// </para>
    /// <para>
    /// <b>It is also where a slip physically is.</b> §3.2 keeps the paper a local prop, so the prop
    /// on any one machine proves nothing about the lab; the tray, the rack hole or the pair of hands
    /// it is in is a fact the host owns, and this is the only record of it. That is what lets a slip
    /// reach a client's room at all — see <c>Residue.Net.Views.SlipView</c>.
    /// </para>
    /// Ground truth never touches this: a <see cref="TestResult"/> is measured values only, already
    /// polluted by residue, noise and drift, which is the whole reason it is the thing that gets
    /// filed.
    /// </summary>
    public sealed class ResultSlips
    {
        /// <summary>Not a slip. Tickets start at 1 so a default-constructed handle names nothing.</summary>
        public const int NoTicket = 0;

        /// <summary>Nobody is carrying it. <c>ulong.MaxValue</c> matches <c>PlayerSession.NoClientId</c>.</summary>
        private const ulong NoHolder = ulong.MaxValue;

        /// <summary>One printed slip: where it came from, what it says, and where it is now.</summary>
        public readonly struct Slip
        {
            public readonly int Ticket;

            /// <summary>The sample it reports on, or <see cref="SampleId.None"/> for a blank or a standard.</summary>
            public readonly SampleId Sample;

            /// <summary><see cref="MachineInstance.InstanceId"/> of the tray it landed in.</summary>
            public readonly string MachineInstanceId;

            public readonly TestResult Result;

            /// <summary>Where the paper physically is — see <see cref="ResultSlips"/>.</summary>
            public readonly SampleLocation Location;

            public Slip(int ticket, SampleId sample, string machineInstanceId, TestResult result,
                        SampleLocation location)
            {
                Ticket = ticket;
                Sample = sample;
                MachineInstanceId = machineInstanceId;
                Result = result;
                Location = location;
            }
        }

        private sealed class Entry
        {
            public SampleId Sample;
            public string MachineInstanceId;
            public TestResult Result;

            /// <summary>
            /// Where the paper is. Replaces the plain holder id this used to keep: with slips
            /// replicating, "not held by anyone" stopped being enough — a client has to be told which
            /// tray or which rack hole to draw it in, and a second field beside the holder would be
            /// two records of one fact.
            /// </summary>
            public SampleLocation Location;

            public ulong HeldBy =>
                Location.Kind == SampleLocationKind.Held ? Location.HolderClientId : NoHolder;
        }

        private readonly Dictionary<int, Entry> open = new();
        private int nextTicket = 1;

        /// <summary>Slips that exist somewhere in the lab — in a tray, in a rack, or in a hand.</summary>
        public int Count => open.Count;

        /// <summary>
        /// Print a slip and hand back its ticket. Called by the host when an instrument finishes;
        /// the local prop carries the ticket so the player can later name it.
        /// </summary>
        public int Issue(SampleId sample, string machineInstanceId, TestResult result)
        {
            if (result == null) return NoTicket;

            int ticket = nextTicket++;
            open[ticket] = new Entry
            {
                Sample = sample,
                MachineInstanceId = machineInstanceId,
                Result = result,
                Location = InTray(machineInstanceId)
            };
            return ticket;
        }

        public bool TryGet(int ticket, out Slip slip)
        {
            if (open.TryGetValue(ticket, out var entry))
            {
                slip = new Slip(ticket, entry.Sample, entry.MachineInstanceId, entry.Result,
                                entry.Location);
                return true;
            }

            slip = default;
            return false;
        }

        /// <summary>
        /// Every slip that exists, for whoever has to draw them — the publisher, in practice.
        /// <para>
        /// Fills a caller's list rather than returning one: this is read four times a second on the
        /// host, and an iterator per publish is a garbage collection nobody asked for. Ordered by
        /// ticket so a positional list on the wire does not churn when the dictionary reuses a slot
        /// freed by a filed slip.
        /// </para>
        /// </summary>
        public void CollectInto(List<Slip> into)
        {
            if (into == null) return;
            into.Clear();

            foreach (var pair in open)
            {
                var entry = pair.Value;
                into.Add(new Slip(pair.Key, entry.Sample, entry.MachineInstanceId, entry.Result,
                                  entry.Location));
            }

            into.Sort((a, b) => a.Ticket.CompareTo(b.Ticket));
        }

        /// <summary>
        /// The tray a slip was printed into. <see cref="SampleLocationKind.InMachine"/> is shared with
        /// a vial loaded in the sample path, and the two are told apart by what is standing in the
        /// location rather than by the kind — a slip resolves to the instrument's output tray and is
        /// takeable there, where a vial resolves to its holder and is not (§5.4).
        /// </summary>
        private static SampleLocation InTray(string machineInstanceId) =>
            SampleLocation.InMachine(machineInstanceId, 0);

        /// <summary>
        /// Take a slip into a player's hands. Refuses one somebody else is already carrying — with
        /// four players in the room, two people reaching for the same tray at once is not an edge
        /// case, and the second one has to be told rather than handed a duplicate.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryClaim(int ticket, ulong clientId, out string refusal)
        {
            refusal = null;

            if (!open.TryGetValue(ticket, out var entry))
            {
                refusal = "That slip has already been filed.";
                return false;
            }

            if (entry.HeldBy != NoHolder && entry.HeldBy != clientId)
            {
                refusal = "Someone else has already picked that slip up.";
                return false;
            }

            entry.Location = SampleLocation.Held(clientId);
            return true;
        }

        /// <summary>True when this player is the one carrying that slip.</summary>
        public bool IsHeldBy(int ticket, ulong clientId) =>
            open.TryGetValue(ticket, out var entry) && entry.HeldBy == clientId;

        /// <summary>
        /// Put it down somewhere in particular — a rack hole the player chose. The location is
        /// recorded rather than merely cleared because it is what every other process draws the paper
        /// from; a slip that only knew it was <i>not</i> in anyone's hands would snap back to the tray
        /// on every screen but the one that put it down.
        /// </summary>
        public void Release(int ticket, SampleLocation where)
        {
            if (open.TryGetValue(ticket, out var entry)) entry.Location = where;
        }

        /// <summary>
        /// Release everything one connection was carrying. For a disconnect.
        /// <para>
        /// Not a courtesy, for the reason <c>PlayerSession</c> gives about vials: a slip left marked
        /// held by a connection that no longer exists is a result nobody can ever file. Going back to
        /// the tray also puts the paper somewhere that still exists — a dropped player's carry socket
        /// is destroyed with their avatar, taking the prop parented to it.
        /// </para>
        /// </summary>
        public void ReleaseAllHeldBy(ulong clientId)
        {
            foreach (var pair in open)
            {
                var entry = pair.Value;
                if (entry.HeldBy == clientId) entry.Location = InTray(entry.MachineInstanceId);
            }
        }

        /// <summary>
        /// The paper is gone: filed into a record, or replaced in the tray by a newer run. Either
        /// way the ticket stops naming anything, so a stale prop cannot file a second time.
        /// </summary>
        public void Discard(int ticket) => open.Remove(ticket);
    }
}
