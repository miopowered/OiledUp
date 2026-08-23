using System.Threading.Tasks;

namespace Residue.Net.Session
{
    /// <summary>
    /// Where a player's <i>stable</i> id comes from.
    /// <para>
    /// §M4 keys rejoin on an identity that outlives a connection. NGO's <c>clientId</c> cannot do
    /// that job: it is allocated per connection and freely reused, so the second player to occupy
    /// client 2 would inherit the first one's hands. Everything about a session therefore hangs off
    /// this string, and this interface exists so the source of it is swappable.
    /// </para>
    /// Two implementations ship together on purpose. <see cref="UgsPlayerIdentity"/> is the shipping
    /// one; <see cref="LocalPlayerIdentity"/> is what actually runs today, because there is no cloud
    /// project linked yet and co-op has to be buildable and testable before there is one.
    /// </summary>
    public interface IPlayerIdentity
    {
        /// <summary>
        /// The resolved id, or null before <see cref="ResolveAsync"/> has completed. Never trust a
        /// null here — a session keyed on an empty string is a session shared by everyone who failed
        /// to sign in.
        /// </summary>
        string StableId { get; }

        /// <summary>True once <see cref="StableId"/> is populated and usable.</summary>
        bool IsReady { get; }

        /// <summary>
        /// A short label for the roster. Cosmetic only — never key a session on it, because two
        /// players may perfectly well both be called "Dave".
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Obtain the id, signing in if that is what this source requires. Returns null on failure
        /// rather than throwing, so a caller can fall back to another implementation instead of
        /// killing the connect flow.
        /// <para>
        /// Idempotent: calling it again once resolved returns the same id without a round trip.
        /// </para>
        /// </summary>
        Task<string> ResolveAsync();
    }
}
