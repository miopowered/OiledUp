using Residue.Gameplay.UI;
using Residue.Gameplay.World;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The whole front door of the game — title, co-op, lobby, settings and pause — as pages of one
    /// shell (#42, #44, #66).
    /// <para>
    /// <b>Why one object owns all of it.</b> This lives on the <c>Connect</c> GameObject, which
    /// <c>LabConnection.Awake</c> marks <c>DontDestroyOnLoad</c>, so the screen already survives into
    /// the lab scene — that is why the join-code card works mid-shift today. That also answers the
    /// open question in #43: settings are reachable from the main menu <i>and</i> from the pause
    /// menu, and rather than rebuilding the panel per scene, <b>it survives</b>. One
    /// <see cref="SettingsPanel"/> is built lazily, kept for the process and shown from both places;
    /// the lab scene needs no changes at all, no resolution list is re-enumerated on every pause, and
    /// an in-flight rebind or a running display-revert timer cannot be thrown away by a scene load
    /// landing at the wrong moment.
    /// </para>
    /// <para>
    /// <b>Built once, refreshed in place.</b> Inherited from <c>ConnectScreen</c> and still
    /// load-bearing: the co-op page has a text field the player is halfway through typing a join code
    /// into, and a rebuild on every status change would drop their caret and their characters with
    /// it. The lobby refreshes several times a second, so the same rule is what keeps it usable.
    /// </para>
    /// <para>
    /// <b>This class stays thin.</b> It routes, it reads the keyboard, and it owns the cursor.
    /// Everything drawn is a plain class in this folder, following how <see cref="SettingsPanel"/> is
    /// written — nothing on a page is a MonoBehaviour, and nothing here knows what a lobby seat looks
    /// like.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuScreen : MonoBehaviour
    {
        /// <summary>Countdown redraw rate. Fast enough that a whole second is never skipped.</summary>
        private const float CountdownRefreshSeconds = 0.1f;

        [SerializeField] private UIDocument document;
        [SerializeField] private LabConnection connection;
        [SerializeField] private InputActionAsset inputAsset;

        private readonly ShiftPause suspension = new();

        private VisualElement root;
        private VisualElement backdrop;

        private TitlePanel titlePage;
        private CoOpPanel coOpPage;
        private LobbyPanel lobbyPage;
        private PausePanel pausePage;
        private DisconnectPanel disconnectPage;
        private SettingsPanel settingsPage;
        private CreditsPanel creditsPage;
        private SessionCard card;

        /// <summary>Where the player navigated. Overruled by <see cref="Route"/> whenever the
        /// connection disagrees — see <see cref="MenuPage"/>.</summary>
        private MenuPage requested = MenuPage.Title;

        private MenuPage shown = MenuPage.None;

        private bool voiceControlsOpen;
        private bool voiceControlsOwnCursor;
        private bool cursorLockedLastFrame;
        private float nextCountdownRefresh;

        /// <summary>
        /// The player is in the lab rather than in front of a menu.
        /// <para>
        /// Keyed off the scene and off single player, and deliberately <b>not</b> off
        /// <c>ConnectStates.IsLive</c>. IsLive is now true for the whole length of the lobby, when
        /// there is no lab loaded at all — hiding the menu and locking the cursor on it drops the host
        /// into a locked-cursor empty Boot scene with nothing to press.
        /// </para>
        /// </summary>
        private bool InGame => connection != null &&
                               (connection.ShiftStarted ||
                                connection.State == ConnectState.SinglePlayer);

        // -- Lifecycle ---------------------------------------------------------------------------------

        private void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (connection == null) connection = LabConnection.Instance;
            if (connection == null) connection = FindAnyObjectByType<LabConnection>();

            root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            Build();

            if (connection != null)
            {
                connection.Changed += Refresh;
                connection.Voice.Changed += Refresh;
                connection.Lobby.Changed += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (connection != null)
            {
                connection.Changed -= Refresh;
                connection.Voice.Changed -= Refresh;
                connection.Lobby.Changed -= Refresh;
            }

            // Never leave the game frozen behind a screen that has gone away.
            if (suspension.Active) EndPause();

            voiceControlsOpen = false;
            voiceControlsOwnCursor = false;
        }

        private void Build()
        {
            root.Clear();
            root.style.flexGrow = 1f;

            backdrop = UiKit.Backdrop();
            backdrop.style.display = DisplayStyle.None;
            root.Add(backdrop);

            titlePage = new TitlePanel(connection, () => Go(MenuPage.CoOp), OpenSettings,
                () => Go(MenuPage.Credits), Quit);
            coOpPage = new CoOpPanel(connection, () => Go(MenuPage.Title));
            lobbyPage = new LobbyPanel(connection, Leave);
            pausePage = new PausePanel(Resume, OpenSettings, Leave);
            disconnectPage = new DisconnectPanel(connection, Acknowledge, Reconnect);
            creditsPage = new CreditsPanel(() => Go(MenuPage.Title));

            backdrop.Add(titlePage.Root);
            backdrop.Add(coOpPage.Root);
            backdrop.Add(lobbyPage.Root);
            backdrop.Add(pausePage.Root);
            backdrop.Add(disconnectPage.Root);
            backdrop.Add(creditsPage.Root);

            // Kept across a rebuild rather than remade: it holds rebind state and a revert timer.
            if (settingsPage != null) backdrop.Add(settingsPage.Root);

            card = new SessionCard(connection);
            root.Add(card.Root);

            shown = MenuPage.None;
        }

        // -- Routing -----------------------------------------------------------------------------------

        /// <summary>
        /// What should be on screen, decided from <see cref="LabConnection"/> first and from what the
        /// player asked for only when the connection has no opinion.
        /// <para>
        /// The busy-or-live clause is what stops the title screen flashing at somebody joining a shift
        /// already in progress: they are <c>Joined</c> but <c>InLobby</c> is false for about a round
        /// trip, because the host — having sealed its room — never sends them a roster.
        /// </para>
        /// </summary>
        private MenuPage Route()
        {
            if (connection == null) return MenuPage.Title;

            // First, and ahead of InGame. A session that ended under the player is the one case where
            // a page has to come up over a lab that is still standing there — see MenuPage.Disconnected
            // and the freeze in Refresh.
            if (connection.Ended.HasValue) return MenuPage.Disconnected;

            if (InGame)
            {
                if (!suspension.Active) return MenuPage.None;
                return requested == MenuPage.Settings ? MenuPage.Settings : MenuPage.Pause;
            }

            if (connection.InLobby) return MenuPage.Lobby;

            if (ConnectStates.IsBusy(connection.State) || ConnectStates.IsLive(connection.State))
                return MenuPage.CoOp;

            return requested switch
            {
                MenuPage.CoOp => MenuPage.CoOp,
                MenuPage.Settings => MenuPage.Settings,
                MenuPage.Credits => MenuPage.Credits,
                _ => MenuPage.Title
            };
        }

        private void Refresh()
        {
            if (root == null) return;

            // Take the player's hands the moment the wire dies, not when the scene finally changes
            // (#52). LabConnection records the end before it starts unwinding, and the unwind waits
            // on a voice leave and a lobby delete over the connection that has just failed — so
            // without this there are seconds, sometimes tens of them, in which a client walks around
            // its last replicated snapshot pressing things that will never be answered. That reads as
            // the game being slow rather than the session being over.
            //
            // Through the same ShiftPause the menu uses, and not stopping the clock: freezing a
            // client's timescale would stop it drawing a lab it is still looking at, and the pairing
            // that gives everything back lives in one place for the reason that type documents.
            if (connection != null && connection.Ended.HasValue && InGame && !suspension.Active)
                suspension.Begin(false);

            // The lab can go away underneath a pause — the host drops, or LeaveAsync lands. Nothing
            // else would put Time.timeScale back.
            if (suspension.Active && !InGame) EndPause();
            if (voiceControlsOpen && !InGame)
            {
                voiceControlsOpen = false;
                voiceControlsOwnCursor = false;
            }

            var page = Route();
            bool pageUp = page != MenuPage.None;
            bool wasUp = shown != MenuPage.None;

            if (pageUp)
            {
                // On every refresh, not just on the transition into a menu. The local player object
                // now spawns at the *start of the lobby*, and PlayerController.OnEnable locks the
                // cursor unconditionally; whichever order those two run in, this takes it back.
                // Fully qualified: UnityEngine.UIElements has a Cursor of its own and both
                // namespaces are open in this file.
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else if (wasUp && !voiceControlsOpen)
            {
                // Menu to game. Lock once, here: the player spawned while a page was still up and we
                // unlocked the cursor out from under its OnEnable, so nothing else will.
                PlayerController.SetCursorLocked(true);
            }

            if (page != shown)
            {
                // Coming back to the front door after a shift: the save slot has moved and the title
                // page is caching what it last read off disk. Asking here rather than on every
                // refresh keeps a file read out of the lobby's several-times-a-second redraw (#49).
                if (page == MenuPage.Title) titlePage.RereadSaveSlot();

                shown = page;
                Paint(page);

                // Whoever arrived on a gamepad has no pointer to hunt with.
                if (pageUp) UiKit.FocusFirst(PageRoot(page));
            }

            switch (page)
            {
                case MenuPage.Title: titlePage.Refresh(); break;
                case MenuPage.CoOp: coOpPage.Refresh(); break;
                case MenuPage.Lobby: lobbyPage.Refresh(); break;
                case MenuPage.Pause: pausePage.Refresh(suspension.ClockStopped); break;
                case MenuPage.Disconnected: disconnectPage.Refresh(); break;
            }

            // Or an invisible full-screen element eats every click the player aimed at the room.
            bool takesInput = pageUp || voiceControlsOpen;
            root.pickingMode = takesInput ? PickingMode.Position : PickingMode.Ignore;
            card.Refresh(takesInput, voiceControlsOpen);
        }

        private void Paint(MenuPage page)
        {
            if (page == MenuPage.Settings) EnsureSettings();

            backdrop.style.display = page == MenuPage.None ? DisplayStyle.None : DisplayStyle.Flex;

            // Translucent over live play, so a paused player keeps their bearings; opaque in the
            // menu, where there is nothing behind it worth showing.
            backdrop.style.backgroundColor = new StyleColor(
                suspension.Active ? UiPalette.Scrim : UiPalette.Backdrop);

            titlePage.Root.style.display = Shows(page == MenuPage.Title);
            coOpPage.Root.style.display = Shows(page == MenuPage.CoOp);
            lobbyPage.Root.style.display = Shows(page == MenuPage.Lobby);
            pausePage.Root.style.display = Shows(page == MenuPage.Pause);
            disconnectPage.Root.style.display = Shows(page == MenuPage.Disconnected);
            creditsPage.Root.style.display = Shows(page == MenuPage.Credits);
            if (settingsPage != null)
                settingsPage.Root.style.display = Shows(page == MenuPage.Settings);
        }

        private static DisplayStyle Shows(bool visible) =>
            visible ? DisplayStyle.Flex : DisplayStyle.None;

        private VisualElement PageRoot(MenuPage page) => page switch
        {
            MenuPage.Title => titlePage.Root,
            MenuPage.CoOp => coOpPage.Root,
            MenuPage.Lobby => lobbyPage.Root,
            MenuPage.Pause => pausePage.Root,
            MenuPage.Disconnected => disconnectPage.Root,
            MenuPage.Settings => settingsPage?.Root,
            MenuPage.Credits => creditsPage.Root,
            _ => null
        };

        private void Go(MenuPage page)
        {
            requested = page;
            Refresh();
        }

        // -- Input -------------------------------------------------------------------------------------

        private void Update()
        {
            // The cursor is the only reliable signal that nothing else owns the screen, and it has to
            // be read over *two* frames. TerminalScreen and ItemInspectionView both consume Escape and
            // re-lock the cursor as they close; depending on script execution order this component can
            // see an already-locked cursor in that same frame and open the pause menu on top of the
            // screen that just closed. The previous frame is the one that cannot lie.
            bool lockedNow = UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            bool ownsScreen = lockedNow && cursorLockedLastFrame;
            cursorLockedLastFrame = lockedNow;

            TickCountdown();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Escape on the disconnect notice means "yes, I have read it" and nothing else. Handled
            // ahead of the suspension block below, where it would otherwise be swallowed: the freeze
            // is active there, but Escape cannot mean resume when there is no session to resume into.
            if (shown == MenuPage.Disconnected)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) Acknowledge();
                return;
            }

            if (suspension.Active)
            {
                // Only from the pause page. On the settings page Escape belongs to an in-flight
                // rebind, and there is no way from here to know whether one is running — so backing
                // out of settings is the BACK button's job and nothing else's.
                if (shown == MenuPage.Pause && keyboard.escapeKey.wasPressedThisFrame) Resume();
                return;
            }

            if (voiceControlsOpen)
            {
                if (keyboard.vKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
                    CloseVoiceControls();
                return;
            }

            if (shown != MenuPage.None || !InGame || !ownsScreen) return;

            if (keyboard.escapeKey.wasPressedThisFrame) BeginPause();
            else if (keyboard.vKey.wasPressedThisFrame && connection.IsLive) OpenVoiceControls();
        }

        /// <summary>
        /// Keep the lobby countdown ticking. On <c>Time.unscaledTime</c> and never on
        /// <c>Time.deltaTime</c>: a countdown that stops with the timescale is a hang with a number
        /// on it, which is precisely what <c>LobbyRoom.Tick</c> avoids on its own side.
        /// </summary>
        private void TickCountdown()
        {
            if (shown != MenuPage.Lobby || connection == null) return;
            if (!connection.Lobby.IsCountingDown) return;
            if (Time.unscaledTime < nextCountdownRefresh) return;

            nextCountdownRefresh = Time.unscaledTime + CountdownRefreshSeconds;
            lobbyPage.Refresh();
        }

        // -- Pausing -----------------------------------------------------------------------------------

        private void BeginPause()
        {
            if (suspension.Active) return;

            requested = MenuPage.Pause;

            // Single player only. In co-op the day clock is the host's and keeps running whatever
            // this process does with its own timescale — see PausePanel, which says so on screen
            // rather than letting the player find out from the clock.
            suspension.Begin(connection.State == ConnectState.SinglePlayer);
            Refresh();
        }

        private void Resume()
        {
            if (!suspension.Active) return;
            EndPause();
            Refresh();
        }

        /// <summary>
        /// Undo everything <see cref="BeginPause"/> did. Separate from <see cref="Resume"/> because
        /// every other way out has to run it too — the screen being disabled, a LEAVE, or losing the
        /// host mid-pause. A timescale left at zero outlives the screen that set it.
        /// </summary>
        private void EndPause()
        {
            if (requested == MenuPage.Pause || requested == MenuPage.Settings)
                requested = MenuPage.Title;

            if (shown == MenuPage.Settings) settingsPage?.Cancel();
            suspension.End();
        }

        // -- Commands ----------------------------------------------------------------------------------

        /// <summary>
        /// Leaving, from the pause menu or from the lobby, always goes through
        /// <c>LabConnection.LeaveAsync</c>. That is the one path that closes the lobby, shuts the
        /// transport down, puts <c>LabRuntime.SimulatesLocally</c> back to true and returns to Boot. A
        /// process that has been a client once must not stay one, or the next SINGLE PLAYER is an
        /// empty lab with no error explaining it.
        /// </summary>
        private void Leave()
        {
            if (suspension.Active) EndPause();

            requested = MenuPage.Title;
            if (connection != null) _ = connection.LeaveAsync();
            Refresh();
        }

        /// <summary>
        /// The player has read the disconnect notice. Not routed through <see cref="Leave"/>: there
        /// is no session left to leave — <c>LabConnection</c> unwound it as it happened — and calling
        /// <c>LeaveAsync</c> a second time would run a whole teardown against a dead connection just
        /// to dismiss a page.
        /// <para>
        /// The suspension is ended here rather than left to the <c>InGame</c> clause in
        /// <see cref="Refresh"/>, because the return to Boot may still be in flight and a player who
        /// pressed the button should not keep watching a frozen lab until it lands.
        /// </para>
        /// </summary>
        private void Acknowledge()
        {
            if (suspension.Active) EndPause();

            requested = MenuPage.Title;
            connection?.AcknowledgeEnd();
            Refresh();
        }

        /// <summary>
        /// Take the held seat back. Offered only where <c>LabConnection</c> says it is honest — see
        /// <see cref="DisconnectPanel"/> — and this method does not second-guess that;
        /// <c>RejoinAsync</c> refuses anything else on its own.
        /// <para>
        /// <see cref="requested"/> is moved to <see cref="MenuPage.CoOp"/> before the attempt, and
        /// that is the load-bearing line. The rejoin clears the notice, so if it then fails there is
        /// nothing routing the player anywhere in particular, and the sentence saying why would land
        /// on a page that does not draw it — the title screen, which shows no errors by design.
        /// </para>
        /// </summary>
        private void Reconnect()
        {
            if (suspension.Active) EndPause();

            requested = MenuPage.CoOp;
            if (connection != null) _ = connection.RejoinAsync();
            Refresh();
        }

        /// <summary>
        /// Quit through Unity's own path and nothing else. <c>LabConnection</c> holds
        /// <c>Application.wantsToQuit</c> until the lobby is actually deleted; a button that tore the
        /// session down itself would leave a lobby answering its join code for its full timeout, and
        /// the next person to try that code joins a relay with nobody behind it.
        /// </summary>
        private static void Quit()
        {
            Application.Quit();

#if UNITY_EDITOR
            // Application.Quit does nothing in play mode, so without this the button is untestable.
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // -- Settings ----------------------------------------------------------------------------------

        private void EnsureSettings()
        {
            if (settingsPage != null) return;

            settingsPage = new SettingsPanel(inputAsset, CloseSettings);
            backdrop.Add(settingsPage.Root);
        }

        private void OpenSettings()
        {
            EnsureSettings();

            // A setting can move while the panel is hidden — FOV and look sensitivity are both seeded
            // from the player prefab as it spawns.
            settingsPage.Refresh();
            Go(MenuPage.Settings);
        }

        private void CloseSettings()
        {
            settingsPage?.Cancel();
            Go(suspension.Active ? MenuPage.Pause : MenuPage.Title);
        }

        // -- Voice controls ----------------------------------------------------------------------------

        private void OpenVoiceControls()
        {
            voiceControlsOpen = true;
            voiceControlsOwnCursor = UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            PlayerController.SetCursorLocked(false);
            Refresh();
        }

        private void CloseVoiceControls()
        {
            bool relock = voiceControlsOwnCursor && InGame && ShiftPause.LocalController() != null;
            voiceControlsOpen = false;
            voiceControlsOwnCursor = false;
            if (relock) PlayerController.SetCursorLocked(true);
            Refresh();
        }
    }
}
