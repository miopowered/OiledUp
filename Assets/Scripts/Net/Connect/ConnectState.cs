namespace Residue.Net.Connect
{
    /// <summary>
    /// Where the connect flow has got to.
    /// <para>
    /// The states are split finer than the UI strictly needs because each one names a different
    /// thing to unwind if it fails. <see cref="Allocating"/> may already hold a Relay allocation;
    /// <see cref="Resolving"/> may already have joined a Lobby; <see cref="Connecting"/> has a
    /// transport running. Collapsing them into one "connecting" state is how a half-created session
    /// gets abandoned instead of cleaned up — see <c>LabConnection</c>, where every failure path is
    /// written against exactly one of these.
    /// </para>
    /// </summary>
    public enum ConnectState
    {
        /// <summary>Nothing started. The connect screen is up and taking input.</summary>
        Idle,

        /// <summary>Waiting on <c>ServiceBootstrap</c>. Nothing to unwind.</summary>
        Preparing,

        /// <summary>Host: Relay allocation, then Lobby creation.</summary>
        Allocating,

        /// <summary>Client: Lobby lookup by join code, then Relay join.</summary>
        Resolving,

        /// <summary>Transport is configured and NGO's handshake is in flight.</summary>
        Connecting,

        /// <summary>Host is up. The join code is readable and the lobby is being kept alive.</summary>
        Hosting,

        /// <summary>Client is connected and reading replicated views.</summary>
        Joined,

        /// <summary>Playing alone with no netcode of any kind. The default this game shipped with.</summary>
        SinglePlayer,

        /// <summary>Something failed, everything it held has been released, and the error is displayed.</summary>
        Failed
    }
}
