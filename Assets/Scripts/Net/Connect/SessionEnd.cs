namespace Residue.Net.Connect
{
    /// <summary>
    /// How a session that had already started came to an end (#52).
    /// <para>
    /// Four cases, because they call for four different sentences and only one of them is worth
    /// offering a rejoin for. See <see cref="SessionEnd"/> for what separates them.
    /// </para>
    /// </summary>
    public enum SessionEndKind
    {
        /// <summary>The host said it was going. Nothing is left to rejoin.</summary>
        HostClosed,

        /// <summary>Never got in at all — refused at approval, or the lab was full.</summary>
        Refused,

        /// <summary>Was in, and the host put this client out with a reason of its own.</summary>
        Kicked,

        /// <summary>Was in, and the wire went quiet. The only case with a seat still waiting.</summary>
        Dropped
    }

    /// <summary>
    /// The end of a live session, as the client experienced it: which of the four things happened,
    /// the sentence to show, and whether a rejoin is an honest thing to offer (#52).
    /// <para>
    /// <b>What separates the four.</b> A client has exactly two facts at the moment NGO calls
    /// <c>OnClientDisconnectCallback</c>: whether this connection had ever finished connecting, and
    /// whether anybody said anything on the way out
    /// (<c>NetworkManager.DisconnectReason</c>). That is enough, and it is all that is honest:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>The reason names a shutdown</b> — <see cref="HostClosedNote"/>, which
    /// <c>LabConnection</c> sends to everyone before it takes the host down, or NGO's own
    /// "shutting down" text if the host went out through <c>NetworkManager.Shutdown</c> alone.
    /// <see cref="SessionEndKind.HostClosed"/>. Checked <i>first</i>, before "were we ever
    /// connected", so a host that quits during someone's handshake reads as a host that quit
    /// rather than as a refusal.
    /// </description></item>
    /// <item><description>
    /// <b>Never connected</b> — the connection was turned away at approval:
    /// <see cref="SessionEndKind.Refused"/>. The reason is the host's own refusal text and is shown
    /// verbatim, because <c>SessionRegistry</c> writes it for a player to read.
    /// </description></item>
    /// <item><description>
    /// <b>Connected, then a reason</b> — somebody on the other end made a decision about this
    /// client specifically: <see cref="SessionEndKind.Kicked"/>.
    /// </description></item>
    /// <item><description>
    /// <b>Connected, then silence</b> — <see cref="SessionEndKind.Dropped"/>.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Why "connection lost" and "this client dropped" are one case and not two.</b> #52 lists
    /// them separately and from the outside they are different events, but from inside a socket that
    /// has stopped answering they are the same one: NGO raises a single callback with no reason, and
    /// the transport cannot tell a client whether the silence started at its end or at the host's.
    /// Splitting them would mean guessing, printing the guess as a fact, and having the player plan
    /// around it — so they share <see cref="SessionEndKind.Dropped"/> and a sentence that is true of
    /// both. The fourth case the client really can see is <see cref="SessionEndKind.Refused"/>,
    /// which is the "rejected" half of #52's "kicked/rejected" and needs its own wording: nothing
    /// was ever started, so there is nothing to be sorry about losing.
    /// </para>
    /// <para>
    /// <b>Only <see cref="SessionEndKind.Dropped"/> offers a rejoin</b>, and that is the whole point
    /// of splitting the cases. <c>SessionRegistry</c> holds an absent player's seat, so a client that
    /// fell off a host which is still running really can walk back into its own pose and its own
    /// hands. The other three cannot: a closed lobby does not answer its join code, and a host that
    /// refused or removed this client will do it again. Showing a retry that cannot work is worse
    /// than showing none, because it spends the player's last idea about what to do.
    /// </para>
    /// </summary>
    public readonly struct SessionEnd
    {
        /// <summary>
        /// What the host sends every client before it shuts down, so the other end can tell a
        /// deliberate close from a dead wire. A whole sentence rather than a code, because it is
        /// also the fallback the client shows if anything ever routes it straight through.
        /// </summary>
        public const string HostClosedNote = "The host closed the lab.";

        private SessionEnd(SessionEndKind kind, string headline, string detail)
        {
            Kind = kind;
            Headline = headline;
            Detail = detail;
        }

        public SessionEndKind Kind { get; }

        /// <summary>Three or four words naming what happened. Written to be read at heading size.</summary>
        public string Headline { get; }

        /// <summary>The sentence underneath it, including what the player can do next.</summary>
        public string Detail { get; }

        /// <summary>
        /// A rejoin would be a real offer. True for exactly one kind — see the type doc.
        /// </summary>
        public bool OffersRejoin => Kind == SessionEndKind.Dropped;

        /// <param name="wasConnected">
        /// This connection had reached <c>OnClientConnectedCallback</c> at least once. False means
        /// the handshake never completed, which is the only way to recognise a refusal — an approval
        /// refusal and a mid-shift kick arrive through the same callback with the same shape.
        /// </param>
        /// <param name="reason">
        /// <c>NetworkManager.DisconnectReason</c>, which is empty unless the other end sent one.
        /// </param>
        public static SessionEnd Classify(bool wasConnected, string reason)
        {
            string said = reason?.Trim();
            bool spoken = !string.IsNullOrEmpty(said);

            // First, and deliberately ahead of the wasConnected test: a host that quits while
            // somebody is still shaking hands has closed the lab, not refused them, and telling
            // that player they were turned away would send them off to re-check a join code.
            if (spoken && NamesAShutdown(said))
            {
                return new SessionEnd(SessionEndKind.HostClosed,
                    "THE HOST CLOSED THE LAB",
                    "The shift ended when your host left. Their lobby has been deleted and its join " +
                    "code will not answer any more, so there is nothing left to rejoin.");
            }

            if (!wasConnected)
            {
                return new SessionEnd(SessionEndKind.Refused,
                    "THE LAB TURNED YOU AWAY",
                    spoken
                        ? said + " Nothing was started, so nothing was lost."
                        : "The host refused the connection without saying why. Nothing was started.");
            }

            if (spoken)
            {
                return new SessionEnd(SessionEndKind.Kicked,
                    "THE HOST DISCONNECTED YOU",
                    said + " Rejoining would only be refused again; ask your host for a fresh code.");
            }

            return new SessionEnd(SessionEndKind.Dropped,
                "THE CONNECTION DROPPED",
                "Nothing more came back from the host. Your seat is held for you, so rejoining puts " +
                "you back where you were standing, with whatever you were holding.");
        }

        /// <summary>
        /// Our own note, or NGO's. <c>NetworkManager</c> writes "Disconnected due to host shutting
        /// down." from <c>ProcessServerShutdown</c> whenever a host goes out through
        /// <c>Shutdown()</c> without passing this class's note first — a transport failure on the
        /// host, or any future exit that forgets to say goodbye. Matching both means the message a
        /// player reads never depends on which of the two paths the host took.
        /// </summary>
        private static bool NamesAShutdown(string reason) =>
            reason.IndexOf(HostClosedNote, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason.IndexOf("shutting down", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
