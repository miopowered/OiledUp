using Residue.Gameplay.World;
using Residue.Net.Session;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// A connected player, as the host's validator sees them.
    /// <para>
    /// This is the adapter that lets <see cref="LabCommandExecutor"/> stay ignorant of netcode. It
    /// answers the two questions the executor asks of whoever is requesting something — what are you
    /// holding, where are you standing — out of that player's <see cref="PlayerSession"/>, which is
    /// the host's own record rather than anything the client asserted at the time of the request.
    /// </para>
    /// <para>
    /// <b>Why the hands are kept here and mirrored into the session rather than the other way round.</b>
    /// <see cref="HeldItem"/> is a save-and-wire descriptor: it names a sample and a machine, and it
    /// deliberately holds nothing transient. A slip's <c>ResultSlips</c> ticket is exactly that —
    /// meaningless across a restart, and gone the moment the paper is filed — so it lives in this
    /// object, which dies with the connection, in step with the grip it describes. The session gets
    /// the durable half, which is all the disconnect path needs: <c>PlayerSession.Unbind</c> only has
    /// to know that a vial was in that hand and which sample it was.
    /// </para>
    /// <para>
    /// One actor per connection, discarded on disconnect. A rejoining player comes back empty-handed
    /// by design (<c>PlayerSession.ReleasedOnDisconnect</c> is a note, not a claim), so there is
    /// nothing here worth carrying across the gap.
    /// </para>
    /// </summary>
    public sealed class SessionActor : ILabActor
    {
        private readonly PlayerSession session;
        private LabGrip grip = LabGrip.Empty;

        public SessionActor(PlayerSession session)
        {
            this.session = session;
        }

        public PlayerSession Session => session;

        public ulong ClientId => session.ClientId;

        public string DisplayName => session.DisplayName;

        /// <summary>
        /// False until this player has replicated a transform. Reach then goes unchecked rather than
        /// refusing everything — a client's first seconds must not be silently dead, and §3.1 gives
        /// the client authority over its own position anyway, so the check was never a guarantee.
        /// </summary>
        public bool HasPosition => session.Pose.HasValue;

        public Vector3 Position => session.Pose.Position;

        public LabGrip Grip => grip;

        public void SetGrip(LabGrip next)
        {
            grip = next;
            session.Held = Describe(next);
        }

        /// <summary>
        /// The durable half of the grip, for the session. A slip's machine is not recorded because
        /// this actor does not know it and nothing on the disconnect path needs it — paper is
        /// released by ticket in <see cref="LabNetwork"/>, not by looking it up here.
        /// <para>
        /// <b>A solvent bottle deliberately reduces to <see cref="HeldItem.None"/>.</b> This
        /// descriptor is save-file material (§M5) and a bottle id is not durable across a run the way
        /// a <c>SampleId</c> is; more to the point, a rejoining player comes back empty-handed by
        /// design, so nothing would be restored from it. The disconnect case that <i>does</i> matter —
        /// a bottle stranded in the hands of a connection that no longer exists — is handled where the
        /// store lives, on <see cref="LabNetwork"/>'s disconnect path, rather than by widening a type
        /// two milestones' worth of code already reads.
        /// </para>
        /// </summary>
        private static HeldItem Describe(LabGrip grip) => grip.Kind switch
        {
            GripKind.Vial => HeldItem.Vial(grip.Sample),
            GripKind.Slip => HeldItem.Printout(grip.Sample, null),
            GripKind.Book => HeldItem.ReferenceBook(default),
            _ => HeldItem.None
        };

        public override string ToString() => $"{DisplayName} [{ClientId}] holding {grip}";
    }
}
