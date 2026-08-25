using System;
using System.Collections.Generic;
using System.Text;
using Residue.Net.Session;
using Residue.Net.Views;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net.Connect
{
    /// <summary>
    /// The room everybody stands in before the shift starts: who is here, who is ready, and the
    /// countdown the host starts when they are.
    ///
    /// <para>
    /// <b>Why named messages and not a <c>NetworkVariable</c>.</b> A <c>NetworkVariable</c> has to
    /// live on a spawned <c>NetworkObject</c>, and during the lobby there is nothing to hang one on:
    /// the lab scene — where every scene-placed <c>NetworkObject</c> in this game lives — is
    /// deliberately <i>not</i> loaded yet, and that is the entire point of having a lobby. The two
    /// ways out are a new spawnable prefab registered in the <c>NetworkManager</c>'s prefab list, or
    /// <c>CustomMessagingManager</c>. The prefab needs Editor work — a GUID, a <c>.meta</c>, an entry
    /// in a serialized list — which is exactly the class of change that cannot be reviewed in a diff
    /// and cannot be made without the GUI. Named messages need nothing but a string, work the moment
    /// the transport is up, and cost one 289-byte packet a second. The roster is four rows; this is
    /// not the place to spend a prefab.
    /// </para>
    ///
    /// <para>
    /// <b>A plain C# class</b>, for the same reason <c>SessionRegistry</c> and <c>LabState</c> are
    /// ones: the ready/countdown sequence has to be steppable without a frame loop, and it has no
    /// business owning a <c>GameObject</c> that would then need placing in a scene. It is owned and
    /// ticked by <see cref="LabConnection"/>.
    /// </para>
    ///
    /// <para>
    /// <b>The host is the authority and the only writer.</b> Clients send two things — a name and a
    /// ready flag — and read one: the whole roster. Nothing a client sends is trusted beyond those
    /// two values, and the sender is taken from NGO's own header rather than from the payload, so a
    /// client cannot ready somebody else up or claim to be the host. Every handler treats its buffer
    /// as hostile: lengths are range-checked before they are used and the whole body is wrapped, so a
    /// malformed packet is dropped rather than thrown out of NGO's message pump (which would take the
    /// rest of that batch with it).
    /// </para>
    ///
    /// <para>
    /// <b>It also does connection approval, and it has to.</b> <c>LabNetwork</c> installs
    /// <c>NetworkManager.ConnectionApprovalCallback</c> in its <c>OnNetworkSpawn</c>, which cannot run
    /// until the lab scene is loaded — which is now <i>after</i> the lobby. NGO's
    /// <c>ApproveConnection</c> returns early when the callback is null while
    /// <c>NetworkConfig.ConnectionApproval</c> is on, so the connection is neither approved nor
    /// denied: the client sits pending until its own approval timeout drops it, with no reason to
    /// show. So this class installs one for the duration, enforcing
    /// <see cref="SessionRegistry.DefaultCapacity"/> and refusing an empty stable id in words a player
    /// can act on. It also records <c>clientId -&gt; stableId</c>, which is the only place that
    /// mapping exists before <c>LabNetwork</c> is alive — see <see cref="StableIdOf"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Decisions worth knowing about.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <i>The host may start with people not ready.</i> <see cref="ReadyCount"/> and
    /// <see cref="EveryoneReady"/> exist so the button can say "START (2/3 READY)" rather than being
    /// greyed out. A lobby four people cannot leave because the fifth walked away from their keyboard
    /// is a worse failure than starting a shift somebody was not looking at.
    /// </description></item>
    /// <item><description>
    /// <i>Somebody joining cancels the countdown.</i> They arrived with three seconds left, never saw
    /// the roster, and never got to ready up; dropping them straight into a shift is the thing the
    /// lobby exists to stop. The host restarts it, which costs five seconds.
    /// </description></item>
    /// <item><description>
    /// <i>Somebody leaving does not.</i> The opposite rule would let one flaky connection hold four
    /// players at the door indefinitely, and the people still standing there have already said they
    /// want to play. Readying up during the countdown does not cancel it either.
    /// </description></item>
    /// <item><description>
    /// <i>A client that arrives after the shift has started never sees a lobby.</i> The host stops
    /// broadcasting when it seals (<see cref="Seal"/>) and ignores a late hello, so
    /// <see cref="IsOpen"/> on that client stays false and <c>LabConnection.InLobby</c> with it.
    /// Nothing flashes on the way into a running shift.
    /// </description></item>
    /// </list>
    /// </summary>
    public sealed class LobbyRoom
    {
        /// <summary>
        /// Long enough to put a drink down, short enough that nobody starts wondering whether the
        /// button worked. It is also the window in which a cancel is still useful.
        /// </summary>
        public const float CountdownSeconds = 5f;

        /// <summary>Host to everyone: the whole roster and the countdown.</summary>
        public const string LobbyMessage = "oiledup.lobby";

        /// <summary>Client to host, once: this is what to call me.</summary>
        public const string HelloMessage = "oiledup.hello";

        /// <summary>Client to host: ready, or not.</summary>
        public const string ReadyMessage = "oiledup.ready";

        /// <summary>
        /// Idle re-send rate. A client that missed a change — approved a frame late, or a handler that
        /// dropped a malformed batch — is wrong for at most a second rather than until the next time
        /// somebody clicks something.
        /// </summary>
        private const float IdleBroadcastSeconds = 1f;

        /// <summary>
        /// While counting down, because the number on screen is a promise about when the lab loads.
        /// A client extrapolates between these, so this only has to be often enough to stop the two
        /// clocks visibly parting.
        /// </summary>
        private const float CountdownBroadcastSeconds = 0.25f;

        /// <summary>
        /// How long a client waits for the host to echo a ready it asked for before asking again.
        /// Named messages are reliable, so this only fires when the request raced the handshake.
        /// </summary>
        private const float ReadyConfirmSeconds = 2f;

        /// <summary>Countdown field when nobody is counting. Negative is unreachable otherwise.</summary>
        private const float NotCountingDown = -1f;

        /// <summary>
        /// Roster labels are read, not stored, so this is a layout budget rather than a wire one.
        /// A name long enough to push the ready column off the panel is a name that has been used as
        /// a weapon.
        /// </summary>
        private const int MaxNameCharacters = 24;

        /// <summary>A stable id is a GUID or a UGS PlayerId. Anything much longer is not one.</summary>
        private const int MaxStableIdBytes = 256;

        /// <summary>61 bytes, from the type itself rather than from a number written down twice.</summary>
        private static readonly int NameCapacity = new FixedString64Bytes().Capacity;

        // -- Host-side roster --------------------------------------------------------------------------

        /// <summary>What the host knows about one connection. Never leaves the host.</summary>
        private sealed class Occupant
        {
            public string Name = "Player";
            public bool Ready;
        }

        private readonly Dictionary<ulong, Occupant> occupants = new();

        /// <summary>
        /// <c>clientId -&gt; stableId</c>, taken from the connection payload at approval. Host-only,
        /// and it deliberately outlives <see cref="IsOpen"/>: <c>LabNetwork.OnNetworkSpawn</c> reads it
        /// to seat everybody who was already in the lobby, and that runs after the shift has started.
        /// </summary>
        private readonly Dictionary<ulong, string> stableIds = new();

        private readonly List<ulong> sendTo = new();

        // -- Shared ------------------------------------------------------------------------------------

        private readonly List<LobbySeat> seats = new();
        private readonly List<LobbySeat> rebuilt = new();

        private NetworkManager manager;
        private Action<NetworkManager.ConnectionApprovalRequest,
                       NetworkManager.ConnectionApprovalResponse> approval;

        private bool registered;
        private bool hooked;
        private string localName = "Player";

        private float now;
        private float nextBroadcast;
        private float countdownEndsAt = NotCountingDown;
        private bool counting;
        private bool startingFired;
        private bool sealedOff;

        private bool localReady;
        private bool readyPending;
        private float readyAskedAt;

        // -- The API a screen reads --------------------------------------------------------------------

        /// <summary>
        /// True while the lobby is gathering. On the host that is from the moment the host starts
        /// until <see cref="Seal"/> or <see cref="Close"/>; on a client it is false until the first
        /// roster arrives, which is what keeps a shift already in progress from flashing a lobby.
        /// </summary>
        public bool IsOpen { get; private set; }

        public bool IsHost { get; private set; }

        /// <summary>
        /// Everyone in the room, host first and then in connection order. A fresh snapshot after every
        /// <see cref="Changed"/>; hold it for a frame, not for a session.
        /// </summary>
        public IReadOnlyList<LobbySeat> Seats => seats;

        public int Capacity => SessionRegistry.DefaultCapacity;

        /// <summary>
        /// What this player has asked for, which on a client is not quite the same thing as what the
        /// host has agreed to. The button follows the click immediately and the roster row follows the
        /// host, so a request that is somehow lost shows up as a row that disagrees with the button
        /// rather than as a click that did nothing — and <see cref="Tick"/> asks again.
        /// </summary>
        public bool LocalReady => IsHost ? HostSeatReady() : localReady;

        /// <summary>
        /// True when the room is not empty and nobody in it is still deciding. Advisory — see the type
        /// doc on why the host is not blocked by it.
        /// </summary>
        public bool EveryoneReady
        {
            get
            {
                if (seats.Count == 0) return false;
                for (int i = 0; i < seats.Count; i++)
                {
                    if (!seats[i].Ready) return false;
                }
                return true;
            }
        }

        public int ReadyCount
        {
            get
            {
                int ready = 0;
                for (int i = 0; i < seats.Count; i++)
                {
                    if (seats[i].Ready) ready++;
                }
                return ready;
            }
        }

        public bool IsCountingDown => counting;

        /// <summary>
        /// Seconds left, as of the last <see cref="Tick"/>. Zero when nobody is counting. Derived from
        /// a deadline rather than accumulated, so a dropped frame costs nothing and a client that
        /// re-syncs mid-count does not jump.
        /// </summary>
        public float CountdownRemaining =>
            counting ? Mathf.Max(0f, countdownEndsAt - now) : 0f;

        /// <summary>Raised on the main thread whenever anything above moves.</summary>
        public event Action Changed;

        /// <summary>
        /// The countdown reached zero. The host acts on this by loading the lab; a client gets it too,
        /// because the screen wants to stop saying "1" a moment before the scene arrives.
        /// </summary>
        public event Action Starting;

        // -- The API a screen calls --------------------------------------------------------------------

        public void ToggleReady() => SetReady(!LocalReady);

        public void SetReady(bool ready)
        {
            if (!IsOpen) return;

            if (IsHost)
            {
                if (!occupants.TryGetValue(HostClientId, out var mine)) return;
                if (mine.Ready == ready) return;

                mine.Ready = ready;
                PublishRoster();
                return;
            }

            localReady = ready;
            readyPending = true;
            readyAskedAt = now;
            SendReady(ready);
            Raise();
        }

        /// <summary>
        /// Start the five seconds. Host only — a client calling it is ignored rather than told off,
        /// because the same screen runs on both and a shared code path that quietly does nothing on
        /// the side that has no authority is easier to keep correct than one that throws.
        /// </summary>
        public void StartCountdown()
        {
            if (!IsOpen || !IsHost || counting) return;

            counting = true;
            startingFired = false;
            countdownEndsAt = now + CountdownSeconds;
            PublishRoster();
        }

        /// <summary>Host only, and idempotent. Same reasoning as <see cref="StartCountdown"/>.</summary>
        public void CancelCountdown()
        {
            if (!IsHost || !counting) return;

            counting = false;
            countdownEndsAt = NotCountingDown;
            if (IsOpen) PublishRoster();
            else Raise();
        }

        /// <summary>
        /// The identity a connection presented at approval, or null if this process never saw it.
        /// <para>
        /// Host-only by construction: a stable id is what a seat in the lab is keyed on, and no client
        /// has any business knowing another player's. On a client this always returns null, which is
        /// the honest answer rather than an omission.
        /// </para>
        /// </summary>
        public string StableIdOf(ulong clientId) =>
            stableIds.TryGetValue(clientId, out string stableId) ? stableId : null;

        // -- The surface LabConnection drives ----------------------------------------------------------
        //
        // Public rather than internal only so the ready/countdown sequence can be stepped in an
        // edit-mode test with no NetworkManager at all — see ConnectTests. Nothing on a screen should
        // be calling any of these.

        /// <summary>
        /// Open as the authority. Call immediately after <c>StartHost</c> returns true and before the
        /// next await, so the approval callback is installed before NGO's update pumps the first
        /// queued connection request.
        /// <para>
        /// The host seats itself by hand from the id the connect flow put in
        /// <c>NetworkConfig.ConnectionData</c>, for exactly the reason <c>LabNetwork.SeatTheHost</c>
        /// gives at length: NGO approves the host's own local client during <c>StartHost</c>, which is
        /// before this runs, so <see cref="Approve"/> never sees them.
        /// </para>
        /// </summary>
        public void OpenAsHost(string displayName, string stableId)
        {
            Close();

            manager = NetworkManager.Singleton;
            IsHost = true;
            IsOpen = true;
            sealedOff = false;
            localName = Clean(displayName);
            localReady = false;
            readyPending = false;
            counting = false;
            countdownEndsAt = NotCountingDown;
            startingFired = false;
            nextBroadcast = 0f;

            ulong hostId = HostClientId;
            occupants[hostId] = new Occupant { Name = localName };
            if (!string.IsNullOrWhiteSpace(stableId)) stableIds[hostId] = stableId.Trim();

            if (manager != null)
            {
                approval = Approve;
                manager.ConnectionApprovalCallback = approval;

                manager.OnClientConnectedCallback += OnClientArrived;
                manager.OnClientDisconnectCallback += OnClientLeft;
                hooked = true;

                Register(HelloMessage, OnHello);
                Register(ReadyMessage, OnReady);
                registered = true;
            }

            // With no NetworkManager at all — the edit-mode path — everything above is skipped and the
            // roster is a party of one. The ready/countdown sequence still runs, which is the part
            // worth pinning in a test.

            RebuildFromOccupants();
            Raise();
        }

        /// <summary>
        /// Open as a listener. Call from <c>OnClientConnectedCallback</c> for the local client, once
        /// the transport can actually carry the hello.
        /// <para>
        /// <see cref="IsOpen"/> stays false until the host answers. If the host has already started the
        /// shift it never will, and this room stays shut for the rest of the session — which is what
        /// stops a late joiner seeing a lobby they are not in.
        /// </para>
        /// </summary>
        public void OpenAsClient(string displayName)
        {
            Close();

            manager = NetworkManager.Singleton;
            IsHost = false;
            IsOpen = false;
            sealedOff = false;
            localName = Clean(displayName);
            localReady = false;
            readyPending = false;
            counting = false;
            countdownEndsAt = NotCountingDown;
            startingFired = false;

            if (manager == null) return;

            Register(LobbyMessage, OnRoster);
            registered = true;

            SendHello();
        }

        /// <summary>
        /// The shift has started. Stops the broadcast and shuts the room, but deliberately keeps the
        /// approval callback and the stable-id map: <c>LabNetwork</c> has not spawned yet and, when it
        /// does, it reads that map to seat everybody who was already standing here. Its own approval
        /// callback replaces ours as it spawns, and <see cref="Close"/> is careful not to undo that.
        /// </summary>
        public void Seal()
        {
            bool wasGathering = IsOpen || counting;

            // One-way. Without the latch, a roster arriving after the lab was up — from a buggy host,
            // or a packet that overtook the scene load — would put a lobby screen back over a running
            // shift, which is the one thing InLobby exists to prevent.
            sealedOff = true;
            IsOpen = false;
            counting = false;
            countdownEndsAt = NotCountingDown;

            if (wasGathering) Raise();
        }

        /// <summary>
        /// Give everything back. Idempotent, never throws, and safe to call on a manager that is
        /// already shutting down — it runs on every teardown path there is.
        /// </summary>
        public void Close()
        {
            if (manager != null)
            {
                if (registered && manager.CustomMessagingManager != null)
                {
                    try
                    {
                        manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyMessage);
                        manager.CustomMessagingManager.UnregisterNamedMessageHandler(HelloMessage);
                        manager.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[LobbyRoom] Could not unregister a message handler: {e.Message}");
                    }
                }

                if (hooked)
                {
                    manager.OnClientConnectedCallback -= OnClientArrived;
                    manager.OnClientDisconnectCallback -= OnClientLeft;
                }

                // Only if it is still ours. By the time a session is torn down, LabNetwork has usually
                // replaced it with its own, and clearing that would leave the lab approving nobody.
                if (approval != null &&
                    ReferenceEquals(manager.ConnectionApprovalCallback, approval))
                {
                    manager.ConnectionApprovalCallback = null;
                }
            }

            registered = false;
            hooked = false;
            sealedOff = false;
            approval = null;
            manager = null;

            IsOpen = false;
            IsHost = false;
            counting = false;
            countdownEndsAt = NotCountingDown;
            startingFired = false;
            localReady = false;
            readyPending = false;

            occupants.Clear();
            stableIds.Clear();
            seats.Clear();
        }

        /// <summary>
        /// Drive from <c>Update</c> with an <b>unscaled</b> clock. A pause menu that sets
        /// <c>Time.timeScale</c> to zero must not stop a countdown; a countdown that stops is a hang
        /// with a number on it.
        /// <para>
        /// The clock is passed in rather than read so the whole sequence is steppable off a frame
        /// loop. <see cref="CountdownRemaining"/> is only as fresh as the last call.
        /// </para>
        /// </summary>
        public void Tick(float nowSeconds)
        {
            now = nowSeconds;
            if (!IsOpen) return;

            if (counting)
            {
                if (now < countdownEndsAt)
                {
                    // Every frame while counting: the number on screen is the whole point of the state.
                    Raise();
                }
                else
                {
                    counting = false;
                    countdownEndsAt = NotCountingDown;

                    // Tell the clients the count is over before the scene load starts pulling them
                    // across, so nothing is left showing "1".
                    if (IsHost) Broadcast();
                    Raise();

                    if (!startingFired)
                    {
                        startingFired = true;
                        Starting?.Invoke();
                        return;
                    }
                }
            }

            if (IsHost)
            {
                if (now >= nextBroadcast) Broadcast();
                return;
            }

            if (readyPending && now - readyAskedAt >= ReadyConfirmSeconds)
            {
                // Reliable delivery means this should not happen. It can if the request raced the
                // approval, and a ready the host never heard is a player who cannot start the game.
                readyAskedAt = now;
                SendReady(localReady);
            }
        }

        // -- Connection approval -----------------------------------------------------------------------

        /// <summary>
        /// Admit a connection to the lobby, or refuse it in words the player will actually see —
        /// NGO delivers <c>response.Reason</c> to the rejected client as its disconnect reason, and
        /// <c>LabConnection.OnClientDisconnected</c> prints it verbatim.
        /// <para>
        /// The stable id is not verified, for the reason <c>LabNetwork.Approve</c> gives: it is a
        /// convenience, not a credential, and there is no ground truth on this side of the wire for a
        /// forged one to reach. It is refused when <i>empty</i>, though, because every client that
        /// failed to sign in would otherwise share one seat and one session.
        /// </para>
        /// </summary>
        private void Approve(NetworkManager.ConnectionApprovalRequest request,
                             NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;

            var payload = request.Payload;
            if (payload == null || payload.Length == 0)
            {
                response.Reason = "Your game did not send a player id, so the lab cannot give you a seat.";
                return;
            }

            if (payload.Length > MaxStableIdBytes)
            {
                response.Reason = "Your game sent a player id the lab could not read.";
                return;
            }

            string stableId;
            try { stableId = Encoding.UTF8.GetString(payload).Trim(); }
            catch { stableId = null; }

            if (string.IsNullOrWhiteSpace(stableId))
            {
                response.Reason = "Your game did not send a player id, so the lab cannot give you a seat.";
                return;
            }

            // Same identity, second connection — a reconnect, or a duplicate sign-in. The newest wins
            // and the seat is not counted twice, exactly as SessionRegistry.Join decides it.
            bool displaced = TryFindByStableId(stableId, request.ClientNetworkId, out ulong stale);
            if (displaced) Forget(stale);

            if (stableIds.Count >= Capacity)
            {
                response.Reason = $"That lab is full — {Capacity} players is the limit.";
                return;
            }

            stableIds[request.ClientNetworkId] = stableId;
            if (!occupants.ContainsKey(request.ClientNetworkId))
                occupants[request.ClientNetworkId] = new Occupant();

            response.Approved = true;

            // As before the lobby existed. The body is created in whatever scene is current — the Boot
            // menu — and PlayerAvatar keeps it frozen until LabNetwork's PlaceRpc arrives, which is
            // what stops it falling for the length of the lobby.
            response.CreatePlayerObject = true;

            if (displaced && manager != null)
            {
                Debug.Log($"[LobbyRoom] {stableId} reconnected; dropping stale client {stale}.");
                manager.DisconnectClient(stale);
            }

            PublishRoster();
        }

        private bool TryFindByStableId(string stableId, ulong except, out ulong clientId)
        {
            foreach (var pair in stableIds)
            {
                if (pair.Key == except) continue;
                if (!string.Equals(pair.Value, stableId, StringComparison.Ordinal)) continue;

                clientId = pair.Key;
                return true;
            }

            clientId = 0;
            return false;
        }

        private void Forget(ulong clientId)
        {
            stableIds.Remove(clientId);
            occupants.Remove(clientId);
        }

        // -- Connection callbacks ----------------------------------------------------------------------

        private void OnClientArrived(ulong clientId)
        {
            if (!IsHost || !IsOpen || manager == null) return;
            if (clientId == manager.LocalClientId) return;

            if (!occupants.ContainsKey(clientId)) occupants[clientId] = new Occupant();

            // Somebody walked in mid-count. They have not seen the roster and have had no chance to
            // ready up, so the countdown goes back in the box — see the type doc.
            if (counting)
            {
                counting = false;
                countdownEndsAt = NotCountingDown;
            }

            PublishRoster();
        }

        private void OnClientLeft(ulong clientId)
        {
            if (!IsHost) return;

            // Deliberately does not cancel the countdown. One flaky connection must not be able to
            // hold everybody else at the door.
            if (!occupants.Remove(clientId) && !stableIds.ContainsKey(clientId)) return;

            stableIds.Remove(clientId);
            if (IsOpen) PublishRoster();
        }

        // -- Messages in -------------------------------------------------------------------------------

        /// <summary>
        /// Client to host: this is what to call me. Arrives once, immediately after the connection is
        /// approved, and is the only thing that turns a bare client id into a roster row with a name
        /// on it. Answering with a full roster is what gets the joiner a lobby inside one round trip
        /// rather than at the next heartbeat.
        /// </summary>
        private void OnHello(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !IsOpen) return;

            try
            {
                if (!TryReadName(reader, out string name)) return;

                if (!occupants.TryGetValue(sender, out var occupant))
                {
                    occupant = new Occupant();
                    occupants[sender] = occupant;
                }

                occupant.Name = name;
                PublishRoster();
            }
            catch (Exception e)
            {
                Drop(HelloMessage, sender, e);
            }
        }

        private void OnReady(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || !IsOpen) return;

            try
            {
                if (!reader.TryBeginRead(sizeof(byte))) return;
                reader.ReadValueSafe(out byte flag);

                if (!occupants.TryGetValue(sender, out var occupant)) return;

                bool ready = flag != 0;
                if (occupant.Ready == ready)
                {
                    // Still worth answering: the client is asking because it thinks we disagree.
                    Broadcast();
                    return;
                }

                occupant.Ready = ready;
                PublishRoster();
            }
            catch (Exception e)
            {
                Drop(ReadyMessage, sender, e);
            }
        }

        /// <summary>
        /// Host to everyone: the whole roster, every time. A snapshot rather than a delta for the same
        /// reason <c>LabNetwork.PublishAll</c> gives — four rows is cheaper than an invalidation rule
        /// that can be got wrong, and a client that missed one packet is corrected by the next.
        /// </summary>
        private void OnRoster(ulong sender, FastBufferReader reader)
        {
            if (IsHost || sealedOff) return;   // authority, or a room the shift has already replaced
            if (manager != null && sender != NetworkManager.ServerClientId) return;

            try
            {
                if (!reader.TryBeginRead(sizeof(byte))) return;
                reader.ReadValueSafe(out byte count);
                if (count > Capacity) return;                    // hostile or a version mismatch

                rebuilt.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (!reader.TryBeginRead(sizeof(ulong) + sizeof(byte))) return;
                    reader.ReadValueSafe(out ulong clientId);
                    reader.ReadValueSafe(out byte flags);

                    if (!TryReadName(reader, out string name)) return;

                    rebuilt.Add(new LobbySeat(clientId, name,
                                              (flags & 1) != 0, (flags & 2) != 0));
                }

                if (!reader.TryBeginRead(sizeof(float))) return;
                reader.ReadValueSafe(out float remaining);

                Adopt(remaining);
            }
            catch (Exception e)
            {
                Drop(LobbyMessage, sender, e);
                rebuilt.Clear();
            }
        }

        /// <summary>
        /// Take the host's word for the roster and the clock. The one thing not adopted wholesale is
        /// our own ready flag while a request of ours is still unanswered — see
        /// <see cref="LocalReady"/>.
        /// </summary>
        private void Adopt(float remaining)
        {
            bool changed = !IsOpen || !SameSeats(rebuilt);
            if (changed)
            {
                seats.Clear();
                seats.AddRange(rebuilt);
            }
            rebuilt.Clear();

            IsOpen = true;

            bool wasCounting = counting;
            if (float.IsNaN(remaining) || remaining < 0f)
            {
                counting = false;
                countdownEndsAt = NotCountingDown;
            }
            else
            {
                counting = true;
                countdownEndsAt = now + Mathf.Min(remaining, CountdownSeconds);

                // Armed on the rising edge only. Clearing it whenever the host says "not counting"
                // would clear it on the very message the host sends as the count hits zero, and
                // Starting would then fire a second time on the next count.
                if (!wasCounting) startingFired = false;
            }
            if (wasCounting != counting) changed = true;

            ulong mine = manager != null ? manager.LocalClientId : 0;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i].ClientId != mine) continue;

                if (!readyPending)
                {
                    if (localReady != seats[i].Ready) changed = true;
                    localReady = seats[i].Ready;
                }
                else if (seats[i].Ready == localReady)
                {
                    readyPending = false;
                }
                break;
            }

            if (changed) Raise();
        }

        // -- Messages out ------------------------------------------------------------------------------

        /// <summary>Rebuild, send, and tell the screen — the three things every host-side change wants.</summary>
        private void PublishRoster()
        {
            RebuildFromOccupants();
            Broadcast();
            Raise();
        }

        private void Broadcast()
        {
            nextBroadcast = now + (counting ? CountdownBroadcastSeconds : IdleBroadcastSeconds);

            // Nothing goes out once the room has been sealed. A roster arriving at a client that
            // joined a shift already in progress is exactly what would flash a lobby at them.
            if (!IsOpen || !IsHost) return;
            if (manager == null || manager.CustomMessagingManager == null || !manager.IsServer) return;

            // Everyone but us. The host is the author of this message; NGO would loop a send-to-all
            // straight back into our own handler, which would be the authority reading its own echo.
            sendTo.Clear();
            var connected = manager.ConnectedClientsIds;
            for (int i = 0; i < connected.Count; i++)
            {
                if (connected[i] != manager.LocalClientId) sendTo.Add(connected[i]);
            }
            if (sendTo.Count == 0) return;

            try
            {
                int size = sizeof(byte) +
                           seats.Count * (sizeof(ulong) + sizeof(byte) + sizeof(byte) + NameCapacity) +
                           sizeof(float);

                using var writer = new FastBufferWriter(size, Allocator.Temp);

                writer.WriteValueSafe((byte)Mathf.Min(seats.Count, Capacity));
                for (int i = 0; i < seats.Count && i < Capacity; i++)
                {
                    var seat = seats[i];
                    writer.WriteValueSafe(seat.ClientId);
                    writer.WriteValueSafe((byte)((seat.Ready ? 1 : 0) | (seat.IsHost ? 2 : 0)));
                    WriteName(writer, seat.Name);
                }

                writer.WriteValueSafe(counting
                    ? Mathf.Max(0f, countdownEndsAt - now)
                    : NotCountingDown);

                manager.CustomMessagingManager.SendNamedMessage(LobbyMessage, sendTo, writer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyRoom] Could not send the roster: {e.Message}");
            }
        }

        private void SendHello()
        {
            if (manager == null || manager.CustomMessagingManager == null) return;

            try
            {
                using var writer = new FastBufferWriter(sizeof(byte) + NameCapacity, Allocator.Temp);
                WriteName(writer, localName);
                manager.CustomMessagingManager.SendNamedMessage(
                    HelloMessage, NetworkManager.ServerClientId, writer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyRoom] Could not send hello: {e.Message}");
            }
        }

        private void SendReady(bool ready)
        {
            if (manager == null || manager.CustomMessagingManager == null) return;

            try
            {
                using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
                writer.WriteValueSafe((byte)(ready ? 1 : 0));
                manager.CustomMessagingManager.SendNamedMessage(
                    ReadyMessage, NetworkManager.ServerClientId, writer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyRoom] Could not send ready: {e.Message}");
            }
        }

        // -- Wire text ---------------------------------------------------------------------------------
        //
        // A name travels as its FixedString64Bytes payload: one length byte, then that many UTF-8
        // bytes. NGO's own FixedString reader is not used, because it takes the declared length on
        // trust and then writes that many bytes into a 61-byte struct — fine for a host talking to
        // itself, not fine for a number an attacker chose. The length here is a byte, and it is
        // range-checked against the type's own capacity before anything is written.

        private static void WriteName(FastBufferWriter writer, string name)
        {
            var packed = ViewText.Fixed64(name);       // truncates rather than throwing; see ViewText

            int length = Mathf.Clamp(packed.Length, 0, NameCapacity);
            writer.WriteValueSafe((byte)length);
            for (int i = 0; i < length; i++) writer.WriteValueSafe(packed[i]);
        }

        private static bool TryReadName(FastBufferReader reader, out string name)
        {
            name = null;

            if (!reader.TryBeginRead(sizeof(byte))) return false;
            reader.ReadValueSafe(out byte length);
            if (length > NameCapacity) return false;
            if (!reader.TryBeginRead(length)) return false;

            var packed = new FixedString64Bytes { Length = length };
            for (int i = 0; i < length; i++)
            {
                reader.ReadValueSafe(out byte value);
                packed[i] = value;
            }

            name = Clean(packed.ToString());
            return true;
        }

        /// <summary>
        /// A roster label is drawn, so a control character or a line break in it is a layout somebody
        /// else has to look at. Trimmed, stripped and capped; never rejected, because a name is
        /// cosmetic and refusing a connection over one would be absurd.
        /// </summary>
        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Player";

            var builder = new StringBuilder(Mathf.Min(raw.Length, MaxNameCharacters));
            foreach (char c in raw)
            {
                if (char.IsControl(c)) continue;
                builder.Append(c);
                if (builder.Length >= MaxNameCharacters) break;
            }

            string clean = builder.ToString().Trim();
            return clean.Length == 0 ? "Player" : clean;
        }

        // -- Roster plumbing ---------------------------------------------------------------------------

        /// <summary>
        /// Host first, then in client-id order. A stable order matters more than it looks: a roster
        /// that reshuffles every second is one nobody can click a name in.
        /// </summary>
        private bool RebuildFromOccupants()
        {
            ulong hostId = HostClientId;

            rebuilt.Clear();
            if (occupants.TryGetValue(hostId, out var host))
                rebuilt.Add(new LobbySeat(hostId, host.Name, host.Ready, true));

            sortable.Clear();
            foreach (ulong clientId in occupants.Keys)
            {
                if (clientId != hostId) sortable.Add(clientId);
            }
            sortable.Sort();

            for (int i = 0; i < sortable.Count; i++)
            {
                var occupant = occupants[sortable[i]];
                rebuilt.Add(new LobbySeat(sortable[i], occupant.Name, occupant.Ready, false));
            }

            bool changed = !SameSeats(rebuilt);
            if (changed)
            {
                seats.Clear();
                seats.AddRange(rebuilt);
            }
            rebuilt.Clear();
            return changed;
        }

        private readonly List<ulong> sortable = new();

        private bool SameSeats(List<LobbySeat> candidate)
        {
            if (candidate.Count != seats.Count) return false;
            for (int i = 0; i < candidate.Count; i++)
            {
                if (!seats[i].Matches(candidate[i])) return false;
            }
            return true;
        }

        private bool HostSeatReady() =>
            occupants.TryGetValue(HostClientId, out var mine) && mine.Ready;

        /// <summary>
        /// The connection the local player is on. NGO gives the host id 0, which is also the right
        /// answer when there is no NetworkManager at all — see <see cref="OpenAsHost"/>.
        /// </summary>
        private ulong HostClientId => manager != null ? manager.LocalClientId : 0UL;

        private void Register(string name, CustomMessagingManager.HandleNamedMessageDelegate handler)
        {
            if (manager == null || manager.CustomMessagingManager == null) return;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(name, handler);
        }

        private void Raise() => Changed?.Invoke();

        /// <summary>
        /// A bad packet is dropped and named, never rethrown. An exception escaping a named-message
        /// handler comes out inside NGO's message pump and takes the rest of that batch with it, so
        /// one hostile client could stall everybody else's traffic.
        /// </summary>
        private static void Drop(string message, ulong sender, Exception cause) =>
            Debug.LogWarning($"[LobbyRoom] Dropped a malformed '{message}' from {sender} " +
                             $"({cause.GetType().Name}: {cause.Message}).");
    }
}
