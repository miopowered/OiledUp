using Residue.Data;

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

        /// <summary>
        /// A session exists — hosting one, or connected to someone else's.
        /// <para>
        /// <b>Not the same question as "is the player in the game".</b> It used to be, because hosting
        /// loaded the lab in the same breath; since there is a lobby it is possible to have a whole
        /// session and no lab, and anything deciding whether to hide the menu or lock the cursor wants
        /// <c>LabConnection.InLobby</c> or <c>LabConnection.ShiftStarted</c> instead. Deliberately not
        /// a new <see cref="ConnectState"/>: the states are split by <i>what would have to be
        /// unwound</i> (see the type doc), and a lobby and a running shift unwind identically. A state
        /// that meant something else would be the first one that did.
        /// </para>
        /// </summary>
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
            ConnectState.Idle => MenuStrings.ConnectIdle,
            ConnectState.Preparing => MenuStrings.ConnectPreparing,
            ConnectState.Allocating => MenuStrings.ConnectAllocating,
            ConnectState.Resolving => MenuStrings.ConnectResolving,
            ConnectState.Connecting => MenuStrings.ConnectConnecting,
            ConnectState.Hosting => MenuStrings.ConnectHosting,
            ConnectState.Joined => MenuStrings.ConnectJoined,
            ConnectState.SinglePlayer => MenuStrings.ConnectSinglePlayer,
            ConnectState.Failed => MenuStrings.ConnectIdle,
            _ => MenuStrings.ConnectIdle
        };
    }
}
