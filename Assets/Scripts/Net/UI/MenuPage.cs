namespace Residue.Net.UI
{
    /// <summary>
    /// The one thing <see cref="MenuScreen"/> is showing.
    /// <para>
    /// Deliberately not a parallel copy of <c>ConnectState</c>. Most of what is on screen is
    /// <i>derived</i> from the connection — a live lobby is <see cref="Lobby"/> whether the player
    /// asked for it or not — and the only pages the player navigates to freely are the ones that do
    /// not contradict a session in flight. Keeping a second state machine in step with the first is
    /// how a menu ends up showing the title screen over a running handshake, so this enum records a
    /// <i>request</i> and <see cref="MenuScreen"/> overrules it from <c>LabConnection</c> on every
    /// refresh.
    /// </para>
    /// </summary>
    public enum MenuPage
    {
        /// <summary>Nothing. The player is in the lab and the screen is out of their way.</summary>
        None,

        /// <summary>The front door: single player, co-op, settings, quit.</summary>
        Title,

        /// <summary>Host or type a join code, and everything that can go wrong doing so.</summary>
        CoOp,

        /// <summary>Gathering: the join code, who is here, who is ready, and the countdown.</summary>
        Lobby,

        /// <summary>The shared <c>SettingsPanel</c>, reached from <see cref="Title"/> or <see cref="Pause"/>.</summary>
        Settings,

        /// <summary>Over a shift in progress. Only reachable once the lab is loaded.</summary>
        Pause
    }
}
