using Residue.Net.Session;

namespace Residue.Net.Connect
{
    /// <summary>
    /// What <see cref="ServiceBootstrap"/> found when it went looking for Unity Gaming Services.
    /// <para>
    /// Deliberately not a bool. "Signed in" and "not signed in" are both <i>successful</i> outcomes
    /// as far as the game is concerned — single player needs an identity and no services at all —
    /// so the caller has to be handed something usable in either case rather than a failure it is
    /// tempted to treat as fatal. <see cref="Identity"/> is therefore never null on a resolved
    /// status; only <see cref="Online"/> tells you whether host and join are available.
    /// </para>
    /// </summary>
    public readonly struct ServiceStatus
    {
        /// <summary>
        /// The player's stable id source. UGS Authentication when <see cref="Online"/>, the
        /// persisted local GUID otherwise. Never null once the bootstrap has resolved.
        /// </summary>
        public IPlayerIdentity Identity { get; }

        /// <summary>True when UGS initialised and anonymous sign-in succeeded.</summary>
        public bool Online { get; }

        /// <summary>
        /// One player-facing sentence about why we are where we are. Written to be shown on the
        /// connect screen verbatim, so it never names a package, an exception type or a callback.
        /// </summary>
        public string Detail { get; }

        /// <summary>True when there is an id to key a session on, online or not.</summary>
        public bool HasIdentity => Identity != null && Identity.IsReady;

        private ServiceStatus(IPlayerIdentity identity, bool online, string detail)
        {
            Identity = identity;
            Online = online;
            Detail = detail;
        }

        public static ServiceStatus Ready(IPlayerIdentity identity) =>
            new(identity, true, "Signed in.");

        public static ServiceStatus Offline(IPlayerIdentity identity, string detail) =>
            new(identity, false, detail);
    }
}
