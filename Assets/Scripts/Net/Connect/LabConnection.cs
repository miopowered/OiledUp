using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Residue.Gameplay.Simulation;
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
// Aliased rather than imported plainly: this class now has a Lobby property of its own (the
// in-game room, LobbyRoom) and a bare `Lobby` would be a member and a type with one name.
using UgsLobby = Unity.Services.Lobbies.Models.Lobby;

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
    /// to false <i>before</i> <c>StartClient</c>, because the host may push the lab scene the instant
    /// the connection is approved — it does exactly that for anyone joining a shift already in
    /// progress — and <c>LabRuntime.Awake</c> reads that static as it loads. Setting it afterwards
    /// means every client builds its own <c>LabState</c> — and its own ground truth — which is hard
    /// rule 2 broken without a byte crossing the wire.
    /// </para>
    /// <para>
    /// <b>A session is not a shift.</b> <see cref="HostAsync"/> stops once the host is up; everyone
    /// gathers in <see cref="Lobby"/>, and the lab scene is not loaded until <see cref="StartShift"/>.
    /// So <see cref="IsLive"/> — "a session exists" — is no longer the same question as "is the
    /// player in the game", which is what <see cref="InLobby"/> and <see cref="ShiftStarted"/> are
    /// for. <see cref="ShiftStarted"/> is derived from the scene actually changing rather than from
    /// any message, because the scene is the thing that is true: it reads the same for a host, for a
    /// client NGO pulled across, for a client that joined a shift already running, and for single
    /// player, which gets it without a line of netcode.
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

        [Tooltip("Scene loaded when the shift starts. Must be in Build Settings.")]
        [SerializeField] private string labSceneName = "Lab";

        [Tooltip("Menu scene. Leaving a session returns here. Must be in Build Settings.")]
        [SerializeField] private string bootSceneName = "Boot";

        [Tooltip("Shown in the Lobby dashboard. Players never see it; they see the join code.")]
        [SerializeField] private string lobbyName = "Oiled Up";

        [Tooltip("Relay region, or blank for the service's own choice. Blank is right for co-op.")]
        [SerializeField] private string relayRegion = "";

        private readonly LobbyHeartbeat heartbeat = new();
        private readonly VoiceChat voice = new();
        private readonly LobbyRoom lobbyRoom = new();

        private UgsLobby lobby;
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

        /// <summary>
        /// A session exists. Note that this is <b>not</b> "the player is in the game" any more — a
        /// lobby is a live session with no lab in it. See <see cref="InLobby"/>.
        /// </summary>
        public bool IsLive => ConnectStates.IsLive(State);

        /// <summary>Proximity voice state and its local mute/deafen controls.</summary>
        public VoiceChat Voice => voice;

        /// <summary>
        /// Who is here and who is ready, before the shift starts. Opened by a successful host or join
        /// and closed by teardown; never null, so a screen can bind to it once and read
        /// <see cref="LobbyRoom.IsOpen"/> rather than null-checking on every frame.
        /// </summary>
        public LobbyRoom Lobby => lobbyRoom;

        /// <summary>
        /// The lab scene is loaded. Derived from the scene transition itself rather than from a
        /// message — see the type doc.
        /// </summary>
        public bool ShiftStarted { get; private set; }

        /// <summary>
        /// The player is gathering, not playing: hide the world, show the lobby, and leave the cursor
        /// alone. This is the question <see cref="IsLive"/> used to answer.
        /// <para>
        /// The <see cref="LobbyRoom.IsOpen"/> clause is what keeps a lobby from flashing at somebody
        /// who joined a shift already in progress: their room stays shut because the host, having
        /// sealed it, never sends them a roster. Without it there is a window between "approved" and
        /// "the host's scene load arrived" in which a running game would show a lobby screen.
        /// </para>
        /// </summary>
        public bool InLobby => IsLive && !ShiftStarted && lobbyRoom.IsOpen;

        /// <summary>The scene a shift runs in. Exposed so a screen can name it in a diagnostic.</summary>
        public string LabSceneName => labSceneName;

        /// <summary>The menu scene <see cref="LeaveAsync"/> returns to.</summary>
        public string BootSceneName => bootSceneName;

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

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            lobbyRoom.Starting += StartShift;

            // The lab may already be the active scene if this component was dropped into it directly,
            // and a Boot-scene start is the normal case. Either way the answer is the current scene.
            ShiftStarted = SceneManager.GetActiveScene().name == labSceneName;
        }

        private void Update()
        {
            heartbeat.Tick(Time.realtimeSinceStartupAsDouble);
            voice.Tick(Time.realtimeSinceStartup);

            // Unscaled, and deliberately: a pause menu that sets Time.timeScale to zero must not stop
            // a countdown. A countdown that stops is a hang with a number on it.
            lobbyRoom.Tick(Time.realtimeSinceStartup);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            Application.wantsToQuit -= OnWantsToQuit;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            lobbyRoom.Starting -= StartShift;
            lobbyRoom.Close();
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

        /// <summary>
        /// The one place <see cref="ShiftStarted"/> is decided.
        /// <para>
        /// Every way into the lab ends here — the host's own <see cref="StartShift"/>, NGO dragging a
        /// client across, a client synchronised into a shift that was already running, and single
        /// player, which has no netcode to ask. Every way out of it does too, so leaving mid-shift and
        /// hosting again works without a flag anybody has to remember to clear.
        /// </para>
        /// </summary>
        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            bool started = to.IsValid() && to.name == labSceneName;
            if (started == ShiftStarted) return;

            ShiftStarted = started;

            // The lobby is over the moment the lab is up — on the host and on every client alike.
            // Sealing rather than closing: LabNetwork has not spawned yet and, when it does, it reads
            // the room's stable-id map to seat everybody who was already standing in it.
            if (started) lobbyRoom.Seal();

            Changed?.Invoke();
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

            // A NEW SHIFT must never inherit a CONTINUE the player changed their mind about.
            RunSaveSlot.ForgetContinueRequest();
            StartLabScene();
        }

        /// <summary>
        /// Pick the saved run back up (#49). The same path as <see cref="StartSinglePlayer"/> with a
        /// latch set, because the component that rebuilds the run wakes on the other side of the
        /// scene load and there is nothing to hand it an argument — the same shape, and the same
        /// reason, as <see cref="LabRuntime.SimulatesLocally"/>.
        /// <para>
        /// Single player only, and that is not an oversight. A save is the host's run; offering
        /// CONTINUE as a way to open a lobby would mean everyone who joined walked into day 14 of
        /// somebody else's contract, halfway through consequences they never filed.
        /// </para>
        /// </summary>
        public void ContinueSinglePlayer()
        {
            if (!ConnectStates.AcceptsCommands(State)) return;

            RunSaveSlot.RequestContinue();
            StartLabScene();
        }

        private void StartLabScene()
        {
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

            UgsLobby created;
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

            // A co-op shift is a fresh contract. CONTINUE is single player only — see
            // ContinueSinglePlayer — and a latch left set would drop four players into day 14 of the
            // host's solo run.
            RunSaveSlot.ForgetContinueRequest();

            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Identity.StableId);
            Hook(manager);

            Set(ConnectState.Connecting, "Starting the host…");

            if (!manager.StartHost())
            {
                await TearDownAsync();
                Fail("Netcode refused to start the host. See the console for the transport error.");
                return;
            }

            // Before the next await, and that is not an accident. NGO auto-approves the host's own
            // local client inside StartHost — there was no callback installed yet — and then queues
            // every remote connection request for its next update. Opening the room here means our
            // approval callback is in place before that update runs, so nobody can slip in
            // unapproved, and the host's own seat is recorded by hand for exactly the reason
            // LabNetwork.SeatTheHost explains.
            lobbyRoom.OpenAsHost(Identity.DisplayName, Identity.StableId);

            _ = voice.JoinAsync(created.Id, Identity.DisplayName);

            JoinCodeText = created.LobbyCode;
            Set(ConnectState.Hosting, $"Hosting — join code {JoinCode.ForReading(JoinCodeText)}");

            // And that is where hosting stops. The lab is not loaded until StartShift; everyone
            // gathers in the lobby first, which is the whole point of there being one.
        }

        /// <summary>
        /// Load the lab for everybody. Host only, and normally reached through
        /// <see cref="LobbyRoom.Starting"/> rather than called directly.
        /// <para>
        /// Through NGO's scene manager, not Unity's. Scene-placed NetworkObjects — <c>LabNetwork</c>
        /// among them — only spawn for a scene the netcode layer loaded, and a client joining later is
        /// handed this same load as part of its connection.
        /// </para>
        /// </summary>
        public void StartShift()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening) return;
            if (ShiftStarted) return;

            var progress = manager.SceneManager.LoadScene(labSceneName, LoadSceneMode.Single);
            if (progress != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[LabConnection] Could not load '{labSceneName}' over the network " +
                               $"({progress}). Is it in Build Settings?", this);
                return;
            }

            Set(ConnectState.Hosting, string.IsNullOrEmpty(JoinCodeText)
                ? "Starting the shift…"
                : $"Starting the shift — join code {JoinCode.ForReading(JoinCodeText)}");
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

            UgsLobby found;
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
        /// Give everything back and return to <see cref="ConnectState.Idle"/>, in the menu.
        /// Idempotent.
        /// <para>
        /// The scene load is the half that is easy to forget. Leaving mid-shift without it drops the
        /// player back to <see cref="ConnectState.Idle"/> while they are still standing in a lab whose
        /// host is gone — no simulation, no menu to press anything on, and the failure text this class
        /// writes so carefully is on a screen that only exists in the Boot scene. It also has to work
        /// from single player, which is not <see cref="IsLive"/> and would otherwise be skipped: play,
        /// leave, play again has to work in one process, and it does because
        /// <see cref="TearDownAsync"/> has already put <see cref="LabRuntime.SimulatesLocally"/> back
        /// to true by the time Boot loads.
        /// </para>
        /// </summary>
        public async Task LeaveAsync()
        {
            await TearDownAsync();
            Error = null;
            Set(ConnectState.Idle);
            ReturnToBoot();
        }

        /// <summary>
        /// Back to the menu, unless that is already where we are. Unity's scene manager rather than
        /// NGO's — by the time this runs there is no session left to load a scene for.
        /// </summary>
        private void ReturnToBoot()
        {
            if (string.IsNullOrWhiteSpace(bootSceneName)) return;
            if (SceneManager.GetActiveScene().name == bootSceneName) return;

            SceneManager.LoadScene(bootSceneName);
        }

        private async Task TearDownAsync()
        {
            heartbeat.Release();

            // Before the shutdown, so the room's message handlers and its approval callback come off a
            // NetworkManager that is still there to take them off.
            lobbyRoom.Close();

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
        private static async Task CloseLobbyAsync(UgsLobby target, bool owned)
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

            // Not IsOpen yet — that waits for the host's first roster. A host that has already
            // started the shift never sends one, which is what stops a lobby flashing on the way into
            // a running game (see InLobby).
            lobbyRoom.OpenAsClient(Identity.DisplayName);

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

        /// <summary>
        /// Losing the host, as opposed to choosing to leave. Same unwind, and the same return to the
        /// menu for the same reason: <see cref="Fail"/> writes a sentence for the player to read, and
        /// the only screen that shows it lives in the Boot scene. Being dropped mid-shift and left
        /// standing in a dead lab with no explanation is the worse half of the bug
        /// <see cref="ReturnToBoot"/> exists to fix, because nobody chose it.
        /// </summary>
        private async Task DroppedAsync(string reason)
        {
            await TearDownAsync();
            Fail(reason);
            ReturnToBoot();
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

        private static string ReadRelayCode(UgsLobby source)
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
