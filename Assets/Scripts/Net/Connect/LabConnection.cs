using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Residue.Gameplay.World;
using Residue.Net.Session;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lobby = Unity.Services.Lobbies.Models.Lobby;

namespace Residue.Net.Connect
{
    /// <summary>
    /// The one object that decides whether this process is a host, a client, or alone — and the one
    /// that gives back everything it took when it stops being any of them.
    /// <para>
    /// <b>Three entry points, and only one of them touches the network.</b>
    /// <see cref="StartSinglePlayer"/> sets <see cref="LabRuntime.SimulatesLocally"/> true and loads
    /// the lab: no services, no <c>NetworkManager</c>, no lobby, byte-for-byte the way this game has
    /// been played since M0. That path must keep working with the wi-fi off and it is the one this
    /// class protects hardest.
    /// </para>
    /// <para>
    /// <b>The join code the player reads out is the Lobby's invite code, not Relay's.</b> Relay's
    /// join code is longer and is carried inside the lobby as data, so the human-facing string stays
    /// six characters. It also means the lobby is the thing being looked up, which is what gives us
    /// somewhere to hang a heartbeat and a player list later.
    /// </para>
    /// <para>
    /// <b>Ordering that is load-bearing:</b> a client sets <see cref="LabRuntime.SimulatesLocally"/>
    /// to false <i>before</i> <c>StartClient</c>, because the host pushes the lab scene the instant
    /// the connection is approved and <c>LabRuntime.Awake</c> reads that static as it loads. Setting
    /// it afterwards means every client builds its own <c>LabState</c> — and its own ground truth —
    /// which is hard rule 2 broken without a byte crossing the wire.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LabConnection : MonoBehaviour
    {
        /// <summary>Where the Relay join code rides inside the lobby.</summary>
        public const string RelayJoinCodeKey = "relayJoinCode";

        /// <summary>How long a quit will wait for the lobby to close before giving up on it.</summary>
        private const float QuitGraceSeconds = 3f;

        public static LabConnection Instance { get; private set; }

        [Tooltip("Scene loaded once a session exists. Must be in Build Settings.")]
        [SerializeField] private string labSceneName = "Lab";

        [Tooltip("Shown in the Lobby dashboard. Players never see it; they see the join code.")]
        [SerializeField] private string lobbyName = "Oiled Up";

        [Tooltip("Relay region, or blank for the service's own choice. Blank is right for co-op.")]
        [SerializeField] private string relayRegion = "";

        private readonly LobbyHeartbeat heartbeat = new();
        private readonly VoiceChat voice = new();

        private Lobby lobby;
        private bool ownsLobby;
        private bool quitting;

        public ConnectState State { get; private set; } = ConnectState.Idle;

        /// <summary>The six characters the host reads aloud, or null when not hosting.</summary>
        public string JoinCodeText { get; private set; }

        /// <summary>Neutral progress line. Always populated; a blank status reads as a hang.</summary>
        public string Status { get; private set; } = ConnectStates.Label(ConnectState.Idle);

        /// <summary>Player-facing failure, or null. Written to be displayed verbatim.</summary>
        public string Error { get; private set; }

        /// <summary>Resolved once <see cref="ConnectState.Preparing"/> has passed.</summary>
        public IPlayerIdentity Identity { get; private set; }

        /// <summary>Raised on the main thread whenever any of the above changes.</summary>
        public event Action Changed;

        public bool IsBusy => ConnectStates.IsBusy(State);
        public bool IsLive => ConnectStates.IsLive(State);

        /// <summary>Proximity voice state and its local mute/deafen controls.</summary>
        public VoiceChat Voice => voice;

        private static UnityTransport Transport =>
            NetworkManager.Singleton != null
                ? NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport
                : null;

        // -- Lifecycle ---------------------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Two of these would race for the NetworkManager and each unwind the other's lobby.
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.wantsToQuit += OnWantsToQuit;
        }

        private void Update()
        {
            heartbeat.Tick(Time.realtimeSinceStartupAsDouble);
            voice.Tick(Time.realtimeSinceStartup);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            Application.wantsToQuit -= OnWantsToQuit;
            Instance = null;

            // Play-mode exit in the Editor lands here rather than in the quit handler. Fire and
            // forget: there is no frame left to await in, and an abandoned lobby that outlives the
            // Editor session by thirty seconds beats a hung domain reload.
            heartbeat.Release();
            _ = voice.LeaveAsync();
            var closing = lobby;
            bool owned = ownsLobby;
            lobby = null;

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                Unhook(manager);
                if (manager.IsListening || manager.IsClient || manager.IsServer) manager.Shutdown();
            }

            if (closing != null) _ = CloseLobbyAsync(closing, owned);
        }

        /// <summary>
        /// Hold the quit until the lobby is actually closed, capped at
        /// <see cref="QuitGraceSeconds"/>.
        /// <para>
        /// A lobby that is never deleted keeps answering its join code for its full timeout, so the
        /// next person to try it joins a Relay allocation with nobody on the other end and sits on
        /// "Connecting…" until NGO gives up. Three seconds of a slightly slow quit is the cheaper
        /// failure by a wide margin.
        /// </para>
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (quitting) return true;

            bool nothingToRelease = lobby == null &&
                                    (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
            if (nothingToRelease) return true;

            quitting = true;
            _ = QuitAsync();
            return false;
        }

        private async Task QuitAsync()
        {
            var work = TearDownAsync();
            await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(QuitGraceSeconds)));
            Application.Quit();
        }

        // -- Single player -----------------------------------------------------------------------------

        /// <summary>
        /// Start the game with no networking at all.
        /// <para>
        /// No <c>NetworkManager</c>, no sign-in, no lobby, no Relay. The lab scene is loaded through
        /// Unity's own scene manager exactly as it was before co-op existed, so a broken service, an
        /// expired allocation or an unlinked cloud project cannot reach this path to break it.
        /// </para>
        /// </summary>
        public void StartSinglePlayer()
        {
            if (!ConnectStates.AcceptsCommands(State)) return;

            Error = null;
            LabRuntime.SimulatesLocally = true;
            Set(ConnectState.SinglePlayer);
            SceneManager.LoadScene(labSceneName);
        }

        // -- Host --------------------------------------------------------------------------------------

        /// <summary>
        /// Reserve a Relay allocation, publish it as a Lobby, and start the host.
        /// <para>
        /// <b>What each failure unwinds.</b> The Relay allocation comes first and has no delete
        /// call in the SDK — an allocation nobody ever binds to is dropped by the service on its
        /// own, so a Lobby failure after a successful allocation abandons it deliberately and says
        /// so. A transport or <c>StartHost</c> failure after the lobby exists deletes the lobby,
        /// because that one really would linger and answer its join code.
        /// </para>
        /// </summary>
        public async Task HostAsync()
        {
            if (!ConnectStates.AcceptsCommands(State)) return;
            if (!await PrepareAsync()) return;

            Set(ConnectState.Allocating, "Reserving a relay…");

            Allocation allocation;
            string relayJoinCode;
            try
            {
                allocation = await RelayService.Instance.CreateAllocationAsync(
                    SessionRegistry.DefaultCapacity - 1,
                    string.IsNullOrWhiteSpace(relayRegion) ? null : relayRegion);

                relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            }
            catch (Exception e)
            {
                Fail("Could not reserve a relay. Check your connection and try again.", e);
                return;
            }

            Set(ConnectState.Allocating, "Opening the lobby…");

            Lobby created;
            try
            {
                created = await LobbyService.Instance.CreateLobbyAsync(
                    lobbyName,
                    SessionRegistry.DefaultCapacity,
                    new CreateLobbyOptions
                    {
                        IsPrivate = true,
                        Data = new Dictionary<string, DataObject>
                        {
                            // Member-visible, not public: the relay code is only useful to someone
                            // who already has the invite code, and a private lobby never shows up
                            // in a query anyway.
                            [RelayJoinCodeKey] = new DataObject(
                                DataObject.VisibilityOptions.Member, relayJoinCode)
                        }
                        // Player is left null on purpose. The service fills it in with the caller's
                        // authenticated id; passing our own would reject the request outright on the
                        // offline path, where the stable id is a local GUID and not a UGS PlayerId.
                    });
            }
            catch (Exception e)
            {
                // The allocation above is abandoned here. There is no API to release one, and Relay
                // reaps an allocation that is never bound to, so this leaks nothing but a few
                // seconds of a slot we never used.
                Fail("Reserved a relay but could not open a lobby. Nothing was started; try again.", e);
                return;
            }

            if (!ConfigureTransport(allocation.ServerEndpoints, allocation.AllocationIdBytes,
                                    allocation.Key, allocation.ConnectionData, null, out string why))
            {
                await CloseLobbyAsync(created, true);
                Fail(why);
                return;
            }

            lobby = created;
            ownsLobby = true;
            heartbeat.Bind(created.Id, Time.realtimeSinceStartupAsDouble);

            var manager = NetworkManager.Singleton;
            LabRuntime.SimulatesLocally = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Identity.StableId);
            Hook(manager);

            Set(ConnectState.Connecting, "Starting the host…");

            if (!manager.StartHost())
            {
                await TearDownAsync();
                Fail("Netcode refused to start the host. See the console for the transport error.");
                return;
            }

            _ = voice.JoinAsync(created.Id, Identity.DisplayName);

            JoinCodeText = created.LobbyCode;
            Set(ConnectState.Hosting, $"Hosting — join code {JoinCode.ForReading(JoinCodeText)}");

            // Through NGO's scene manager, not Unity's. Scene-placed NetworkObjects — LabNetwork
            // among them — only spawn for a scene the netcode layer loaded, and clients are handed
            // this same load as part of their connection.
            var progress = manager.SceneManager.LoadScene(labSceneName, LoadSceneMode.Single);
            if (progress != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[LabConnection] Could not load '{labSceneName}' over the network " +
                               $"({progress}). Is it in Build Settings?", this);
            }
        }

        // -- Join --------------------------------------------------------------------------------------

        /// <summary>
        /// Look the lobby up by its invite code, join the Relay allocation it names, and connect.
        /// <para>
        /// <b>What each failure unwinds.</b> A malformed code fails before any network call. A
        /// lobby lookup failure holds nothing. Everything after it does hold something — we are a
        /// member of that lobby the moment the join returns — so a missing relay code, a Relay
        /// failure, a transport failure and a refused connection all leave the lobby explicitly.
        /// Leaving a lobby you are in but not connected to is what makes the host's seat count wrong
        /// for the next person.
        /// </para>
        /// </summary>
        public async Task JoinAsync(string rawCode)
        {
            if (!ConnectStates.AcceptsCommands(State)) return;

            string code = JoinCode.Normalise(rawCode);
            if (!JoinCode.IsWellFormed(code))
            {
                Fail(string.IsNullOrEmpty(code)
                    ? "Type the join code your host read out."
                    : $"“{code}” is not a join code — they are {JoinCode.Length} letters and digits.");
                return;
            }

            if (!await PrepareAsync()) return;

            Set(ConnectState.Resolving, "Looking up that join code…");

            Lobby found;
            try
            {
                found = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
            }
            catch (LobbyServiceException e)
            {
                Fail(Explain(e, code), e);
                return;
            }
            catch (Exception e)
            {
                Fail("Could not reach the lobby service. Check your connection and try again.", e);
                return;
            }

            string relayJoinCode = ReadRelayCode(found);
            if (relayJoinCode == null)
            {
                await CloseLobbyAsync(found, false);
                Fail("That lobby is not running a game. Ask your host for a fresh code.");
                return;
            }

            Set(ConnectState.Resolving, "Joining the relay…");

            JoinAllocation join;
            try
            {
                join = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            }
            catch (Exception e)
            {
                await CloseLobbyAsync(found, false);
                Fail("That game's relay is gone. The host has probably closed it.", e);
                return;
            }

            if (!ConfigureTransport(join.ServerEndpoints, join.AllocationIdBytes, join.Key,
                                    join.ConnectionData, join.HostConnectionData, out string why))
            {
                await CloseLobbyAsync(found, false);
                Fail(why);
                return;
            }

            lobby = found;
            ownsLobby = false;

            // Hard rule 2, and it has to be here. The host pushes the lab scene as soon as we are
            // approved, and LabRuntime.Awake reads this static while that scene loads.
            LabRuntime.SimulatesLocally = false;

            var manager = NetworkManager.Singleton;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Identity.StableId);
            Hook(manager);

            Set(ConnectState.Connecting, "Connecting…");

            if (!manager.StartClient())
            {
                await TearDownAsync();
                Fail("Netcode refused to start the client. See the console for the transport error.");
            }
        }

        // -- Leaving -----------------------------------------------------------------------------------

        /// <summary>
        /// Give everything back and return to <see cref="ConnectState.Idle"/>. Idempotent.
        /// </summary>
        public async Task LeaveAsync()
        {
            await TearDownAsync();
            Error = null;
            Set(ConnectState.Idle);
        }

        private async Task TearDownAsync()
        {
            heartbeat.Release();
            await voice.LeaveAsync();

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                Unhook(manager);
                if (manager.IsListening || manager.IsClient || manager.IsServer) manager.Shutdown();
                manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            }

            var closing = lobby;
            bool owned = ownsLobby;
            lobby = null;
            ownsLobby = false;
            JoinCodeText = null;

            // Back to the single-player default. A process that has been a client once must not stay
            // one: the next thing the player does is likely to be SINGLE PLAYER, and a false here
            // gives them an empty lab with no error explaining it.
            LabRuntime.SimulatesLocally = true;

            if (closing != null) await CloseLobbyAsync(closing, owned);
        }

        /// <summary>
        /// Delete the lobby if we own it, otherwise remove ourselves from it.
        /// <para>
        /// Never throws. This runs on quit and on every failure path, and a teardown that can fail
        /// is a teardown that leaves half the session behind.
        /// </para>
        /// </summary>
        private static async Task CloseLobbyAsync(Lobby target, bool owned)
        {
            if (target == null || string.IsNullOrEmpty(target.Id)) return;

            try
            {
                if (owned)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(target.Id);
                    return;
                }

                string playerId = AuthenticationService.Instance != null &&
                                  AuthenticationService.Instance.IsSignedIn
                    ? AuthenticationService.Instance.PlayerId
                    : null;

                if (playerId != null)
                    await LobbyService.Instance.RemovePlayerAsync(target.Id, playerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LabConnection] Could not close lobby '{target.Id}': {e.Message}. " +
                                 "It will expire on its own within a minute.");
            }
        }

        // -- Netcode callbacks -------------------------------------------------------------------------

        private bool hooked;

        private void Hook(NetworkManager manager)
        {
            if (hooked) return;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            hooked = true;
        }

        private void Unhook(NetworkManager manager)
        {
            if (!hooked) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            hooked = false;
        }

        private void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || clientId != manager.LocalClientId) return;
            if (manager.IsServer) return;   // the host's own connect; already Hosting

            _ = voice.JoinAsync(lobby.Id, Identity.DisplayName);
            Set(ConnectState.Joined);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.IsServer) return;      // a remote client leaving us
            if (clientId != manager.LocalClientId) return;

            string reason = manager.DisconnectReason;
            _ = DroppedAsync(string.IsNullOrEmpty(reason)
                ? "Disconnected from the host."
                : $"Disconnected: {reason}");
        }

        private async Task DroppedAsync(string reason)
        {
            await TearDownAsync();
            Fail(reason);
        }

        // -- Plumbing ----------------------------------------------------------------------------------

        private async Task<bool> PrepareAsync()
        {
            Error = null;

            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Fail("No NetworkManager in the scene. Co-op cannot start; single player still can.");
                return false;
            }

            if (Transport == null)
            {
                Fail("The NetworkManager has no UnityTransport. Co-op cannot start.");
                return false;
            }

            if (!manager.NetworkConfig.ConnectionApproval)
            {
                // Not fatal here, but LabNetwork reads the stable id out of the approval payload and
                // will never see one without this. Rejoin silently stops working.
                Debug.LogWarning("[LabConnection] NetworkConfig.ConnectionApproval is off. The stable " +
                                 "player id will not reach LabNetwork, so rejoin will not recognise " +
                                 "anyone.", this);
            }

            Set(ConnectState.Preparing);

            var status = await ServiceBootstrap.EnsureAsync();
            Identity = status.Identity;

            if (!status.Online)
            {
                Fail($"{status.Detail} Single player still works.");
                return false;
            }

            if (Identity == null || !Identity.IsReady)
            {
                Fail("Could not establish a player identity. Single player still works.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Point the transport at Relay.
        /// <para>
        /// The primitive overload is used rather than the <c>RelayServerData</c> one so this
        /// assembly does not have to reference the transport package directly — everything needed
        /// is already on the allocation.
        /// </para>
        /// </summary>
        private static bool ConfigureTransport(List<RelayServerEndpoint> endpoints,
                                               byte[] allocationId, byte[] key,
                                               byte[] connectionData, byte[] hostConnectionData,
                                               out string error)
        {
            var endpoint = SelectEndpoint(endpoints);
            if (endpoint == null)
            {
                error = "The relay offered no endpoint this build can use.";
                return false;
            }

            var transport = Transport;
            if (transport == null)
            {
                error = "The NetworkManager has no UnityTransport.";
                return false;
            }

            transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port, allocationId, key,
                                         connectionData, hostConnectionData, endpoint.Secure);
            error = null;
            return true;
        }

        /// <summary>
        /// Prefer DTLS, fall back to plain UDP.
        /// <para>
        /// Both ends choose independently from their own allocation, so the preference order has to
        /// be identical on both or a host on DTLS waits for a client on UDP. WebSockets are skipped:
        /// they need the transport configured for them, which a desktop build is not.
        /// </para>
        /// </summary>
        private static RelayServerEndpoint SelectEndpoint(List<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0) return null;

            RelayServerEndpoint udp = null;
            foreach (var candidate in endpoints)
            {
                if (candidate == null) continue;
                if (candidate.ConnectionType == RelayServerEndpoint.ConnectionTypeDtls) return candidate;
                if (candidate.ConnectionType == RelayServerEndpoint.ConnectionTypeUdp) udp ??= candidate;
            }
            return udp;
        }

        private static string ReadRelayCode(Lobby source)
        {
            if (source?.Data == null) return null;
            if (!source.Data.TryGetValue(RelayJoinCodeKey, out var entry)) return null;
            return string.IsNullOrWhiteSpace(entry?.Value) ? null : entry.Value;
        }

        /// <summary>
        /// Turn a lobby error into something a player can act on. Anything unrecognised keeps the
        /// service's own wording rather than being flattened into "something went wrong".
        /// </summary>
        private static string Explain(LobbyServiceException e, string code) => e.Reason switch
        {
            LobbyExceptionReason.LobbyNotFound =>
                $"No game is using the code {JoinCode.ForReading(code)}. Check it and try again.",
            LobbyExceptionReason.InvalidJoinCode =>
                $"{JoinCode.ForReading(code)} is not a valid join code.",
            LobbyExceptionReason.LobbyFull =>
                $"That game is full — {SessionRegistry.DefaultCapacity} players is the limit.",
            _ => $"Could not join: {e.Message}"
        };

        private void Set(ConnectState state, string status = null)
        {
            State = state;
            Status = status ?? ConnectStates.Label(state);
            Changed?.Invoke();
        }

        private void Fail(string message, Exception cause = null)
        {
            Error = message;
            State = ConnectState.Failed;
            Status = ConnectStates.Label(ConnectState.Failed);

            if (cause != null)
                Debug.LogWarning($"[LabConnection] {message} ({cause.GetType().Name}: {cause.Message})", this);
            else
                Debug.LogWarning($"[LabConnection] {message}", this);

            Changed?.Invoke();
        }
    }
}
