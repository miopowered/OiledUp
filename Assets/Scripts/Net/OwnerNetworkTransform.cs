using Unity.Netcode.Components;

namespace Residue.Net
{
    /// <summary>
    /// A <see cref="NetworkTransform"/> the owning client drives, rather than the server.
    /// <para>
    /// §3.1 draws the authority line in exactly one place: the host owns the lab, and a client owns
    /// its own transform and look direction. Server-authoritative movement would put a round trip
    /// between pressing W and moving, which is the one thing that would make the grounded, weighty
    /// controller built at M2 feel broken instead of heavy.
    /// </para>
    /// The trade is that a client can lie about where it is standing. That is deliberately
    /// acceptable here: position buys nothing on its own, because every action that matters is a
    /// server call validated against lab state, and the lab does not care where you claim to be.
    /// A cheat that lets you stand inside a wall does not let you read a sample's ground truth.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
