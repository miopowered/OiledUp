namespace Residue.Net.Connect
{
    /// <summary>
    /// The rules about <see cref="ConnectState"/>, kept out of the MonoBehaviour that uses them.
    /// <para>
    /// Split out because these are the only part of the connect flow that can be tested without a
    /// live Unity Gaming Services project — everything else needs a Relay allocation to exercise.
    /// A button that stays live during an in-flight allocation is how you get two Relay allocations
    /// and one lobby, so <see cref="AcceptsCommands"/> is worth pinning in a test even though it
    /// looks trivial.
    /// </para>
    /// </summary>
    public static class ConnectStates
    {
        /// <summary>An operation is in flight and holding resources it would have to unwind.</summary>
        public static bool IsBusy(ConnectState state) =>
            state == ConnectState.Preparing ||
            state == ConnectState.Allocating ||
            state == ConnectState.Resolving ||
            state == ConnectState.Connecting;

        /// <summary>A session exists — hosting one, or connected to someone else's.</summary>
        public static bool IsLive(ConnectState state) =>
            state == ConnectState.Hosting || state == ConnectState.Joined;

        /// <summary>
        /// True when HOST / JOIN / SINGLE PLAYER may be pressed. Note that
        /// <see cref="ConnectState.Failed"/> accepts commands: a failure is a prompt to try again,
        /// not a dead end, and it has already released whatever it was holding.
        /// </summary>
        public static bool AcceptsCommands(ConnectState state) =>
            !IsBusy(state) && !IsLive(state) && state != ConnectState.SinglePlayer;

        /// <summary>
        /// The default line shown under the buttons. Every state has one; a blank status reads as a
        /// frozen screen, which is exactly the impression a connect flow must never give.
        /// </summary>
        public static string Label(ConnectState state) => state switch
        {
            ConnectState.Idle => "Not connected.",
            ConnectState.Preparing => "Signing in…",
            ConnectState.Allocating => "Opening a session…",
            ConnectState.Resolving => "Looking up that join code…",
            ConnectState.Connecting => "Connecting…",
            ConnectState.Hosting => "Hosting.",
            ConnectState.Joined => "Connected.",
            ConnectState.SinglePlayer => "Single player.",
            ConnectState.Failed => "Not connected.",
            _ => "Not connected."
        };
    }
}
