using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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
    /// <para>
    /// <b>Ending is four things, not one</b> (#52). Choosing to leave is <see cref="LeaveAsync"/>
    /// and explains itself. Everything else lands in <see cref="Ended"/> as a
    /// <see cref="SessionEnd"/>, which names which of the four happened and decides whether
    /// <see cref="RejoinAsync"/> is an honest offer. It is written before the unwind rather than
    /// after it, because the unwind waits on the network that has just failed and until it lands the
    /// player is still walking around a lab that has stopped answering.
    /// </para>
    /// <para>
    /// <b>Nothing this class loads is a cut, and nothing it loads blocks a frame</b> (#51). Both of
    /// the plain loads are <c>LoadSceneAsync</c> and both are queued behind <see cref="Transition"/>,
    /// which is also what <see cref="HoldReason"/> keeps covered through the parts of the wait that
    /// are <i>not</i> a scene load at all — a Relay allocation, a lobby create, and a client sitting
    /// on a connection whose host has not sent it a lab yet. The netcode load in
    /// <see cref="StartShift"/> stays NGO's and is only <i>deferred</i> behind the fade: it is the
    /// one that replicates, and a plain Unity load in its place would spawn no scene-placed
    /// <c>NetworkObject</c> and hand a joining client nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LabConnection : MonoBehaviour
    {
        /// <summary>Where the Relay join code rides inside the lobby.</summary>
        public const string RelayJoinCodeKey = "relayJoinCode";

        /// <summary>How long a quit will wait for the lobby to close before giving up on it.</summary>
        private const float QuitGraceSeconds = 3f;

        /// <summary>
        /// How long a wait may run before the screen starts saying more than the step name. Long
        /// enough that a normal load never sees it, short enough to land before the player has
        /// decided the game is hung — which on a client waiting for a host is the point at which
        /// they quit and rejoin, and make the wait longer for everybody.
        /// </summary>
        private const float PatienceSeconds = 6f;

        /// <summary>
        /// The longest anything will be held behind a black screen.
        /// <para>
        /// A safety valve, and it is deliberately generous. Every wait here ends on something
        /// arriving — a scene, a host's first publish — and none of those has a timeout of its own,
        /// so a load that silently never completes would otherwise be a black screen with a line of
        /// text on it and no way out. Giving up shows the player a room they can at least walk out
        /// of, which is a bug report rather than a hang.
        /// </para>
        /// </summary>
        private const float MaxHoldSeconds = 30f;

        private const string GenericLoadStep = "Loading…";
        private const string WaitingForHostStep = "Waiting for the host to start the shift…";
        private const string WaitingForLabStep = "Waiting for the host to send the lab…";
        private const string OpeningLabStep = "Opening the lab…";
        private const string LoadingLabStep = "Loading the lab…";
        private const string ReturningStep = "Returning to the menu…";

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
        private readonly SceneFade fade = new();

        private UgsLobby lobby;
        private bool ownsLobby;
        private bool quitting;

        /// <summary>
        /// A scene that has been asked for and not arrived in, with the line to show while waiting.
        /// <para>
        /// This is what keeps the screen covered across the load itself, which no
        /// <see cref="ConnectState"/> describes: single player is <see cref="ConnectState.SinglePlayer"/>
        /// from the moment START is pressed, and the lab does not exist for another second after that.
        /// Cleared by <see cref="OnActiveSceneChanged"/> — by arriving, not by a callback that a
        /// failed load would never fire.
        /// </para>
        /// </summary>
        private string awaitingScene;

        private string awaitingStep;

        /// <summary>Guards <see cref="StartShift"/> across the frames its load spends queued.</summary>
        private bool shiftLoadQueued;

        /// <summary>
        /// How long <see cref="HoldReason"/> has wanted the screen covered, without a break. Reset by
        /// the wait ending rather than by the cover lifting, so <see cref="MaxHoldSeconds"/> latches
        /// instead of flickering the veil once it has given up.
        /// </summary>
        private float holdSeconds;

        /// <summary>
        /// <see cref="MaxHoldSeconds"/> has been passed and the cover has been lifted. Latched,
        /// because the reason for it is still true on the next frame and an unlatched cap would
        /// flicker the veil once a second forever.
        /// </summary>
        private bool holdGaveUp;

        /// <summary>
        /// The code this client actually joined with, kept for <see cref="RejoinAsync"/>. Cleared by
        /// teardown along with everything else, so a rejoin can never be offered against the last
        /// session but one.
        /// </summary>
        private string joinedWithCode;

        /// <summary>
        /// This connection reached <c>OnClientConnectedCallback</c>. The one fact that separates a
        /// refusal from a drop — see <see cref="SessionEnd.Classify"/>.
        /// </summary>
        private bool everConnected;

        public ConnectState State { get; private set; } = ConnectState.Idle;

        /// <summary>The six characters the host reads aloud, or null when not hosting.</summary>
        public string JoinCodeText { get; private set; }

        /// <summary>Neutral progress line. Always populated; a blank status reads as a hang.</summary>
        public string Status { get; private set; } = ConnectStates.Label(ConnectState.Idle);

        /// <summary>Player-facing failure, or null. Written to be displayed verbatim.</summary>
        public string Error { get; private set; }

        /// <summary>
        /// Set when a session that was already running ended without the player asking (#52), and
        /// null at every other moment — including after a plain <see cref="LeaveAsync"/>, which is
        /// the player's own decision and needs no explaining back to them.
        /// <para>
        /// Written <i>before</i> the unwind rather than after it, which is the whole reason it is a
        /// separate property from <see cref="Error"/>. <see cref="TearDownAsync"/> awaits a voice
        /// leave and a lobby delete — seconds, on the network that has just failed — and until this
        /// lands the player is still standing in the lab with a working set of hands. It is what
        /// <c>MenuScreen</c> watches to take those hands away.
        /// </para>
        /// </summary>
        public SessionEnd? Ended { get; private set; }

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

        /// <summary>
        /// The black over a scene change and the step being waited on (#51). Read by
        /// <c>LoadingVeil</c>, which draws it and decides nothing.
        /// <para>
        /// Driven from <see cref="Update"/> so it keeps running with no screen attached at all — see
        /// <see cref="SceneFade"/> for why the load is queued on this rather than called next to it.
        /// </para>
        /// </summary>
        public SceneFade Transition => fade;

        /// <summary>
        /// A second line for a wait that has gone on long enough to be mistaken for a hang, or null.
        /// <para>
        /// Only ever reassurance, never a step: the step is <see cref="SceneFade.Step"/> and is
        /// already on screen. This exists for the client waiting on a host, which is the longest wait
        /// the game has and the one where "looks hung" turns into a player quitting and rejoining —
        /// which drops their seat, restarts the handshake, and makes it longer still.
        /// </para>
        /// </summary>
        public string LoadingNote { get; private set; }

        /// <summary>
        /// The lab is not merely loaded but furnished: on a client, the host's first publish has
        /// landed and the racks have something in them.
        /// <para>
        /// <b>Not a second answer to "are we in the lab"</b> — that is <see cref="ShiftStarted"/>,
        /// and this is built on it. A client's scene finishes loading a beat before
        /// <c>LabNetwork</c> spawns and publishes, and revealing on the scene alone drops the player
        /// into a room with no instruments and no boxes, which is the exact reading behind the
        /// empty-lab bug this milestone has already chased once. The machine list is the tell: the
        /// lab always has instruments, so a non-empty one is the first publish having arrived.
        /// </para>
        /// <para>
        /// A host and single player have nothing to wait for — <c>LabRuntime</c> builds the state as
        /// the scene loads — which is what <see cref="LabRuntime.SimulatesLocally"/> is answering
        /// here rather than anything about the network.
        /// </para>
        /// </summary>
        public bool LabReady =>
            ShiftStarted &&
            (LabRuntime.SimulatesLocally ||
             (LabNetwork.Instance != null &&
              LabNetwork.Instance.Machines != null &&
              LabNetwork.Instance.Machines.Count > 0));

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

            // Same clock, same reason, and here it is worse than a stopped number: LEAVE is pressed
            // from behind a pause menu with the timescale at zero, and the fade holding that scene
            // load would never reach the point where it runs it.
            TickTransition(Time.unscaledDeltaTime);
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
                if (manager.IsServer && manager.IsListening) SayGoodbye(manager);
                RequestShutdown(manager);
            }

            if (closing != null) _ = CloseLobbyAsync(closing, owned);
        }

        /// <summary>
        /// Ask NGO to shut down, but only if it is not already doing so (#74).
        /// <para>
        /// <c>NetworkManager</c> tears itself down from <c>OnApplicationQuit</c> and again from
        /// <c>OnDestroy</c>, and a request from here on top of that is a third run through
        /// <c>ShutdownInternal</c>. That method disposes its <c>NetworkSceneManager</c> and nulls the
        /// field on the very next line, so the guard against disposing one twice only holds while the
        /// method runs to completion — any run that is interrupted leaves the field pointing at a
        /// scene manager whose <c>SceneEventDataStore</c> is already null, and the next teardown
        /// walks it. Fewer shutdowns is the only lever we have on that from outside the package.
        /// </para>
        /// </summary>
        private static void RequestShutdown(NetworkManager manager)
        {
            if (manager.ShutdownInProgress) return;
            if (!manager.IsListening && !manager.IsClient && !manager.IsServer) return;

            manager.Shutdown();
        }

        /// <summary>
        /// Tell everyone why, while there is still a wire to tell them on.
        /// <para>
        /// Server only, and before <see cref="RequestShutdown"/> rather than instead of it: without
        /// this a client sees an unexplained disconnect and cannot tell a host that quit from its own
        /// connection failing — which would leave it offering a rejoin against a lobby that is being
        /// deleted in the same breath. NGO sends the reason as a message and drops the connection on
        /// its next update, which the shutdown flag leaves room for.
        /// </para>
        /// </summary>
        private static void SayGoodbye(NetworkManager manager)
        {
            var connected = manager.ConnectedClientsIds;

            // Backwards, because disconnecting mutates the list this is reading.
            for (int i = connected.Count - 1; i >= 0; i--)
            {
                ulong clientId = connected[i];
                if (clientId == manager.LocalClientId) continue;

                manager.DisconnectClient(clientId, SessionEnd.HostClosedNote);
            }
        }

        /// <summary>
        /// Hold a real quit until the lobby is actually closed, capped at
        /// <see cref="QuitGraceSeconds"/>.
        /// <para>
        /// A lobby that is never deleted keeps answering its join code for its full timeout, so the
        /// next person to try it joins a Relay allocation with nobody on the other end and sits on
        /// "Connecting…" until NGO gives up. Three seconds of a slightly slow quit is the cheaper
        /// failure by a wide margin.
        /// </para>
        /// <para>
        /// In the Editor it holds nothing, and that is a fix rather than a shortcut — see the
        /// comment on the branch. Every exit path in the game still comes through here, which is the
        /// invariant #52 asks for; what changed is only what happens once it arrives.
        /// </para>
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (quitting) return true;

#if UNITY_EDITOR
            // Never held in the Editor, where this same callback fires on play-mode exit and holding
            // it does the opposite of what it does in a build (#74).
            //
            // Application.Quit is a no-op in play mode, so the grace period can never finish what it
            // interrupted: the player presses Stop, the exit is cancelled, and nothing happens until
            // they press it again. And NGO has already run its whole teardown from
            // EditorApplication.playModeStateChanged by the time this is asked — so a cancelled exit
            // resumes play on a NetworkManager that has been shut down, and the second Stop shuts the
            // same one down all over again. That is the doubled shutdown path #74 is about.
            //
            // OnDestroy releases the lobby on this path instead, fire and forget, which is exactly
            // what it already documents itself as doing. The flag is still set, because from here on
            // this process is on its way out and ReturnToBoot must not queue a scene load into it.
            quitting = true;
            return true;
#else
            bool nothingToRelease = lobby == null &&
                                    (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
            if (nothingToRelease) return true;

            quitting = true;
            _ = QuitAsync();
            return false;
#endif
        }

        private async Task QuitAsync()
        {
            var work = TearDownAsync();

            // The loser of the race is cancelled rather than abandoned — the third instance of the
            // shape fixed in #76. An uncancelled Task.Delay stays armed for its full duration
            // whichever way the race goes, so a teardown that finishes in 200 ms would still leave a
            // three-second timer holding a continuation over a process that is trying to exit.
            //
            // Deliberately no ConfigureAwait(false), for the reason ServiceBootstrap gives: the
            // continuation below calls Application.Quit, which is Unity API and main-thread only.
            using var grace = new CancellationTokenSource();
            var deadline = Task.Delay(TimeSpan.FromSeconds(QuitGraceSeconds), grace.Token);

            var first = await Task.WhenAny(work, deadline);
            grace.Cancel();

            // A teardown we gave up waiting for still has to have its exception read, or it
            // resurfaces as an unobserved-exception log line during whatever happens next.
            if (first != work)
                _ = work.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

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
            // Cleared by arriving somewhere, not by arriving at the right place: a load that ends up
            // in a scene nobody asked for is still a load that finished, and holding the veil on for
            // it would be a black screen over a game that is running fine.
            AbandonSceneChange();

            // Whatever the fade was holding for has landed. Cleared here rather than in StartShift so
            // a shift that is left and started again in one process still works.
            shiftLoadQueued = false;

            bool started = to.IsValid() && to.name == labSceneName;
            if (started == ShiftStarted) return;

            ShiftStarted = started;

            // The lobby is over the moment the lab is up — on the host and on every client alike.
            // Sealing rather than closing: LabNetwork has not spawned yet and, when it does, it reads
            // the room's stable-id map to seat everybody who was already standing in it.
            if (started) lobbyRoom.Seal();

            Changed?.Invoke();
        }

        // -- Scene transitions -------------------------------------------------------------------------

        /// <summary>
        /// Decide what the screen should be covering, then advance the fade over it.
        /// <para>
        /// A predicate re-read every frame rather than a state machine with its own transitions. The
        /// waits it covers overlap and hand over to each other — a client goes from Connecting to
        /// connected-with-no-lab to a scene load to waiting for the first publish without a break —
        /// and one predicate is how that stays a single unbroken cover instead of four fades that
        /// each blink at the seam.
        /// </para>
        /// </summary>
        private void TickTransition(float seconds)
        {
            string step = HoldReason();

            // Accumulated against the reason, not against the cover, so giving up latches: once the
            // cap is passed this keeps climbing and the cover does not come back.
            if (step != null)
            {
                holdSeconds += Mathf.Max(seconds, 0f);
            }
            else
            {
                holdSeconds = 0f;
                holdGaveUp = false;
            }

            if (step != null && holdSeconds >= MaxHoldSeconds)
            {
                // Once, not every frame: the reason is still true on the next one, which is the whole
                // point of the latch.
                if (!holdGaveUp)
                {
                    holdGaveUp = true;
                    Debug.LogWarning($"[LabConnection] Gave up waiting on '{step}' after " +
                                     $"{MaxHoldSeconds:0} s and lifted the loading screen. Whatever " +
                                     "it was waiting for has not arrived.", this);
                }

                step = null;
            }

            if (step != null) fade.Cover(step);
            else fade.Release();

            LoadingNote = step != null && holdSeconds >= PatienceSeconds ? PatienceNote() : null;

            fade.Tick(seconds);
        }

        /// <summary>
        /// The line to hold the screen on, or null when there is nothing to wait for.
        /// <para>
        /// Ordered by which answer is most specific. The connect steps come near the top because
        /// they are the half a loading screen over the scene load would miss entirely: a host spends
        /// a Relay allocation and a lobby create before there is any scene to load, and that is the
        /// wait #51 is actually about.
        /// </para>
        /// </summary>
        private string HoldReason()
        {
            // First, and it is a refusal rather than an ordering. A session that ended has a sentence
            // on screen that the player is in the middle of reading; black over it takes away the one
            // thing telling them what happened. ReturnToBoot skips the fade on that path for the same
            // reason, so nothing here is left holding a load it will never run.
            if (Ended.HasValue) return null;

            // Signing in, reserving a relay, opening the lobby, connecting. Status is already written
            // to be read by a player, so the veil names the step by showing it.
            if (ConnectStates.IsBusy(State)) return Status;

            // A scene asked for and not arrived in — including the netcode one, which a client is
            // pulled into without asking (see OnNetworkSceneLoad).
            if (awaitingScene != null) return awaitingStep;

            // Connected, no lobby, no lab. NGO has this client and the host has not pushed a scene at
            // it yet, which is the longest wait in the game: the lobby room stays shut for anyone
            // joining a shift already in progress, so there is nothing else on screen to look at.
            if (IsLive && !InLobby && !ShiftStarted) return WaitingForHostStep;

            // Arrived, but the room is not furnished. See LabReady.
            if (ShiftStarted && !LabReady)
                return LabRuntime.SimulatesLocally ? OpeningLabStep : WaitingForLabStep;

            return null;
        }

        /// <summary>
        /// What to add once a wait has outlived <see cref="PatienceSeconds"/>. Answers the question
        /// the player is about to act on — "is it me, and should I restart?" — because the action
        /// they would take is the one that makes it worse.
        /// </summary>
        private string PatienceNote()
        {
            if (IsLive && !ShiftStarted)
                return "Still connected. The host has not started the shift yet — you do not need " +
                       "to rejoin.";

            if (ShiftStarted && !LabRuntime.SimulatesLocally)
                return "The lab is still arriving from the host. Leaving now would put you back in " +
                       "the queue behind it.";

            return "Still working. This can take a moment on a slow connection.";
        }

        /// <summary>
        /// Cover the screen, then load. The queued half runs from <see cref="SceneFade.Tick"/> the
        /// moment the black is opaque — see <see cref="SceneFade"/> for why it is not simply called
        /// on the next line.
        /// </summary>
        private void BeginSceneChange(string scene, string step, Action load)
        {
            awaitingScene = scene;
            awaitingStep = step;
            holdSeconds = 0f;
            holdGaveUp = false;
            fade.Cover(step, load);
        }

        /// <summary>
        /// Stop waiting on a scene that is not coming. Called by every load that could not be
        /// started, because the cover is held by <see cref="awaitingScene"/> and nothing else would
        /// ever clear it.
        /// </summary>
        private void AbandonSceneChange()
        {
            awaitingScene = null;
            awaitingStep = null;
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
            Ended = null;
            LabRuntime.SimulatesLocally = true;
            Set(ConnectState.SinglePlayer);

            // Was a blocking LoadScene, which froze the last frame of the menu for the length of the
            // load and then cut straight into a first-person camera at a spawn point (#51). Async and
            // behind the fade: the menu stays drawn and alive until the black is up, and the black
            // stays up until LabReady says there is a room to look at.
            BeginSceneChange(labSceneName, LoadingLabStep, LoadLabLocally);
        }

        /// <summary>Single player's own load. No netcode, exactly as before — only asynchronous.</summary>
        private void LoadLabLocally()
        {
            if (quitting || !Application.isPlaying) return;

            if (SceneManager.LoadSceneAsync(labSceneName) != null) return;

            AbandonSceneChange();
            Fail($"Could not load '{labSceneName}'. Is it in Build Settings?");
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

            // The scene manager exists from here and not before.
            HookScenes(manager);

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
        /// <para>
        /// <b>The fade defers this load; it does not replace it</b> (#51). NGO's own
        /// <c>LoadScene</c> already runs on <c>LoadSceneAsync</c> underneath and reports through
        /// <c>OnSceneEvent</c>, so there is nothing to make non-blocking here — what was missing was
        /// the black in front of it, and the host being the one that starts it means the host is
        /// already covered when every client receives the message.
        /// </para>
        /// </summary>
        public void StartShift()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening) return;
            if (ShiftStarted) return;

            // The load now sits queued behind the fade for a few frames, during which ShiftStarted is
            // still false and a second press — or a second countdown ending — would queue it twice.
            if (shiftLoadQueued) return;
            shiftLoadQueued = true;

            Set(ConnectState.Hosting, string.IsNullOrEmpty(JoinCodeText)
                ? "Starting the shift…"
                : $"Starting the shift — join code {JoinCode.ForReading(JoinCodeText)}");

            BeginSceneChange(labSceneName, LoadingLabStep, PushLabOverTheNetwork);
        }

        /// <summary>
        /// The netcode load, unchanged and deliberately so: it replicates to every client and it is
        /// the only kind of load that spawns the scene-placed <c>NetworkObject</c>s the lab is made
        /// of. Swapping it for a plain Unity load to make a fade easier would leave every client in
        /// an empty room with no <c>LabNetwork</c> to fill it.
        /// </summary>
        private void PushLabOverTheNetwork()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening ||
                manager.SceneManager == null)
            {
                // The session went away while the screen was going black.
                shiftLoadQueued = false;
                AbandonSceneChange();
                return;
            }

            var progress = manager.SceneManager.LoadScene(labSceneName, LoadSceneMode.Single);
            if (progress == SceneEventProgressStatus.Started) return;

            Debug.LogError($"[LabConnection] Could not load '{labSceneName}' over the network " +
                           $"({progress}). Is it in Build Settings?", this);

            // Nothing is coming, so lift the black rather than holding the lobby behind it.
            shiftLoadQueued = false;
            AbandonSceneChange();
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

            // Kept for RejoinAsync. The normalised code rather than what was typed, so a rejoin sends
            // the same six characters the join that worked did.
            joinedWithCode = code;
            everConnected = false;

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
                return;
            }

            // Before the handshake finishes, and that matters: a client joining a shift already in
            // progress is sent the lab scene as part of its synchronisation, which can land before
            // OnClientConnected does.
            HookScenes(manager);
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

            // Leaving is the player's own decision, so there is nothing to explain back to them —
            // and a disconnect notice left standing here would route the menu straight back onto it.
            Ended = null;

            Set(ConnectState.Idle);
            ReturnToBoot();
        }

        /// <summary>
        /// Back to the menu, unless that is already where we are. Unity's scene manager rather than
        /// NGO's — by the time this runs there is no session left to load a scene for.
        /// <para>
        /// Refused while the process is on its way out (#74). A scene load queued during a quit or a
        /// play-mode exit unloads a scene underneath a <c>NetworkManager</c> that is halfway through
        /// its own teardown, and the thing that throws when that goes wrong is the scene manager.
        /// There is also nobody left to read the menu it would be loading.
        /// </para>
        /// <para>
        /// <b>Faded on the way out, except when something is being read.</b> A session that ended
        /// already has its notice up over the lab and the same notice will be up over the menu, so
        /// there is no cut to hide — and a fade to black there would black out the sentence
        /// mid-read. That path still gets the asynchronous load, which is the half that stops the
        /// game looking hung.
        /// </para>
        /// </summary>
        private void ReturnToBoot()
        {
            if (quitting || !Application.isPlaying) return;
            if (string.IsNullOrWhiteSpace(bootSceneName)) return;
            if (SceneManager.GetActiveScene().name == bootSceneName) return;

            if (Ended.HasValue)
            {
                LoadBoot();
                return;
            }

            BeginSceneChange(bootSceneName, ReturningStep, LoadBoot);
        }

        private void LoadBoot()
        {
            if (quitting || !Application.isPlaying) return;
            if (SceneManager.GetActiveScene().name == bootSceneName) return;

            if (SceneManager.LoadSceneAsync(bootSceneName) != null) return;

            AbandonSceneChange();
            Debug.LogError($"[LabConnection] Could not load '{bootSceneName}'. Is it in Build " +
                           "Settings? The player is stranded in the lab with no menu.", this);
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
                if (manager.IsServer && manager.IsListening) SayGoodbye(manager);
                RequestShutdown(manager);
                manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            }

            // A shift that never loaded must not leave the next one refused by its own guard.
            shiftLoadQueued = false;

            // joinedWithCode deliberately survives this. Teardown runs *before* the player has read
            // the notice, so clearing it here would take the code away from the RECONNECT button on
            // the very path that offers one. Ended is the gate, not the code: every way of starting
            // something new clears that, so a stale code can never be rejoined with.
            everConnected = false;

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
        private bool sceneHooked;

        private void Hook(NetworkManager manager)
        {
            if (hooked) return;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            hooked = true;
        }

        private void Unhook(NetworkManager manager)
        {
            if (sceneHooked)
            {
                // Null once NGO has shut down: it disposes its scene manager and nulls the field.
                // Every caller runs this before RequestShutdown, so in practice it is still there.
                if (manager.SceneManager != null) manager.SceneManager.OnLoad -= OnNetworkSceneLoad;
                sceneHooked = false;
            }

            if (!hooked) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            hooked = false;
        }

        /// <summary>
        /// Listen for scene loads this process did not ask for.
        /// <para>
        /// This is how a client in the lobby gets a fade at all: the host starts the shift, NGO
        /// replicates the load, and the client is dragged into the lab with no local call to hang a
        /// cover on. <c>OnLoad</c> is raised on both ends as the load begins — it is handed the
        /// <c>AsyncOperation</c>, which is also the answer to whether NGO's own path blocks: it does
        /// not, and never did.
        /// </para>
        /// <para>
        /// Hooked after the start call rather than in <see cref="Hook"/>, because
        /// <c>NetworkManager.SceneManager</c> does not exist until then.
        /// </para>
        /// </summary>
        private void HookScenes(NetworkManager manager)
        {
            if (sceneHooked || manager == null || manager.SceneManager == null) return;

            manager.SceneManager.OnLoad += OnNetworkSceneLoad;
            sceneHooked = true;
        }

        private void OnNetworkSceneLoad(ulong clientId, string sceneName, LoadSceneMode mode,
                                        AsyncOperation operation)
        {
            if (mode != LoadSceneMode.Single) return;

            // The host's own load, already covered by StartShift — and re-entering BeginSceneChange
            // here would queue a second one.
            if (awaitingScene == sceneName) return;

            awaitingScene = sceneName;
            awaitingStep = sceneName == labSceneName ? LoadingLabStep : GenericLoadStep;
            holdSeconds = 0f;
            holdGaveUp = false;

            // Not BeginSceneChange: there is no load to queue. This one is already running, which is
            // why the cover here starts mid-load rather than in front of it. The host has been black
            // since before it sent the message, so what a client is catching up with is the tail of a
            // fade rather than a cut.
            fade.Cover(awaitingStep);
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

            // Recorded here and nowhere else. This is the moment that separates "the host turned me
            // away" from "the host had me and lost me", and those are two different sentences and
            // two different answers to whether a rejoin is worth offering (see SessionEnd).
            everConnected = true;

            Set(ConnectState.Joined);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.IsServer) return;      // a remote client leaving us
            if (clientId != manager.LocalClientId) return;

            _ = DroppedAsync(SessionEnd.Classify(everConnected, manager.DisconnectReason));
        }

        /// <summary>
        /// Losing the session, as opposed to choosing to leave. Same unwind, and the same return to
        /// the menu for the same reason: the sentence written here is for the player to read, and the
        /// only screen that shows it lives in the Boot scene. Being dropped mid-shift and left
        /// standing in a dead lab with no explanation is the worse half of the bug
        /// <see cref="ReturnToBoot"/> exists to fix, because nobody chose it.
        /// <para>
        /// <b>The announcement comes first and the unwind second.</b> Reversing the two is what
        /// leaves a client walking around a lab that has stopped answering: <see cref="TearDownAsync"/>
        /// waits on a voice leave and a lobby call over the connection that has just failed, which is
        /// precisely when those take longest, and nothing on screen changes until it returns.
        /// </para>
        /// </summary>
        private async Task DroppedAsync(SessionEnd end)
        {
            EndSession(end);
            await TearDownAsync();
            ReturnToBoot();
        }

        /// <summary>
        /// Record the end of a session and say so, at once. A failure state like <see cref="Fail"/>,
        /// with the addition that <see cref="Ended"/> tells a screen which of the four things
        /// happened rather than leaving it to read the sentence.
        /// </summary>
        private void EndSession(SessionEnd end)
        {
            Ended = end;
            Error = end.Detail;
            State = ConnectState.Failed;
            Status = end.Headline;

            Debug.LogWarning($"[LabConnection] Session ended ({end.Kind}): {end.Detail}", this);
            Changed?.Invoke();
        }

        /// <summary>
        /// The player has read the notice and wants the menu back. Clears <see cref="Ended"/>, which
        /// is what routes the screen off the disconnect page.
        /// <para>
        /// It also asks for Boot again, because the player can reach the button before
        /// <see cref="TearDownAsync"/> has finished — a lobby delete over a network that has just
        /// failed is not quick — and a menu the player has dismissed on top of a dead lab is the
        /// state this whole path exists to avoid. <see cref="ReturnToBoot"/> is a no-op when we are
        /// already there.
        /// </para>
        /// </summary>
        public void AcknowledgeEnd()
        {
            if (!Ended.HasValue) return;

            Ended = null;
            Error = null;
            Set(ConnectState.Idle);
            ReturnToBoot();
        }

        /// <summary>
        /// Take the seat back. Only meaningful after a <see cref="SessionEndKind.Dropped"/>, and
        /// refused otherwise — see <see cref="SessionEnd"/> for why the other three cases are not
        /// offered this at all.
        /// <para>
        /// The same <see cref="JoinAsync"/> as the first time, with the code the player already
        /// typed. That matters: the host keys its roster on <see cref="IPlayerIdentity.StableId"/>,
        /// which <c>ServiceBootstrap</c> resolves to the same value across the whole process, so a
        /// second join on the same identity is recognised by <c>SessionRegistry</c> as a rejoin and
        /// restores the pose rather than seating a stranger.
        /// </para>
        /// </summary>
        public async Task RejoinAsync()
        {
            if (Ended is not { OffersRejoin: true }) return;

            string code = joinedWithCode;
            if (string.IsNullOrEmpty(code)) return;

            Ended = null;
            await JoinAsync(code);
        }

        // -- Plumbing ----------------------------------------------------------------------------------

        private async Task<bool> PrepareAsync()
        {
            Error = null;
            Ended = null;

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
