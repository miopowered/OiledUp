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

        /// <summary>One printed slip: where it came from, and what it says.</summary>
        public readonly struct Slip
        {
            public readonly int Ticket;

            /// <summary>The sample it reports on, or <see cref="SampleId.None"/> for a blank or a standard.</summary>
            public readonly SampleId Sample;

            /// <summary><see cref="MachineInstance.InstanceId"/> of the tray it landed in.</summary>
            public readonly string MachineInstanceId;

            public readonly TestResult Result;

            public Slip(int ticket, SampleId sample, string machineInstanceId, TestResult result)
            {
                Ticket = ticket;
                Sample = sample;
                MachineInstanceId = machineInstanceId;
                Result = result;
            }
        }

        private sealed class Entry
        {
            public SampleId Sample;
            public string MachineInstanceId;
            public TestResult Result;
            public ulong HeldBy = NoHolder;
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
                Result = result
            };
            return ticket;
        }

        public bool TryGet(int ticket, out Slip slip)
        {
            if (open.TryGetValue(ticket, out var entry))
            {
                slip = new Slip(ticket, entry.Sample, entry.MachineInstanceId, entry.Result);
                return true;
            }

            slip = default;
            return false;
        }

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

            entry.HeldBy = clientId;
            return true;
        }

        /// <summary>True when this player is the one carrying that slip.</summary>
        public bool IsHeldBy(int ticket, ulong clientId) =>
            open.TryGetValue(ticket, out var entry) && entry.HeldBy == clientId;

        /// <summary>Put it down again — on a rack, or back where it was when its holder dropped out.</summary>
        public void Release(int ticket)
        {
            if (open.TryGetValue(ticket, out var entry)) entry.HeldBy = NoHolder;
        }

        /// <summary>Release everything one connection was carrying. For a disconnect.</summary>
        public void ReleaseAllHeldBy(ulong clientId)
        {
            foreach (var entry in open.Values)
            {
                if (entry.HeldBy == clientId) entry.HeldBy = NoHolder;
            }
        }

        /// <summary>
        /// The paper is gone: filed into a record, or replaced in the tray by a newer run. Either
        /// way the ticket stops naming anything, so a stale prop cannot file a second time.
        /// </summary>
        public void Discard(int ticket) => open.Remove(ticket);
    }
}
