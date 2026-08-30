namespace Residue.Data
{
    /// <summary>
    /// Every line the front end draws: the menu shell, the lobby, the settings screen, and everything
    /// the connect layer says about a session while the player is watching (#55).
    ///
    /// <para>
    /// <b>Why one table rather than one per panel.</b> The id is the translator's working order, and
    /// the four prefixes here — <c>menu.</c>, <c>lobby.</c>, <c>settings.</c>, <c>session.</c> — are
    /// already the four screens a translator would sort the file into. Splitting the declarations
    /// across ten panel classes would put that ordering in the file system instead, where nobody can
    /// read it in one pass. The in-world text (prompts, terminal, HUD, the book) is a different
    /// audience drawn with a different font and lives in its own table for that reason.
    /// </para>
    ///
    /// <para>
    /// <b>What is deliberately not here.</b> Join codes, player ids, scene names, resolution strings
    /// like <c>"1920 x 1080"</c> and the licence bodies in <c>CreditsContent</c>. The first four are
    /// data — running an id through a translation table is a bug whose symptom is a lookup failing in
    /// one language only — and the licence text is legally required to appear verbatim, so it must
    /// never acquire a translated variant. Numbers with a unit on them (<c>72°</c>, <c>60%</c>) are
    /// left as literals for the same reason a resolution is: they are numbers, not sentences.
    /// </para>
    ///
    /// <para>
    /// <b>Sentences are whole.</b> Where a line varies by case — why a reconnect is not on offer, why
    /// a session ended — there is one key per complete sentence rather than a shared stem with a
    /// variable tail, because a translator handed a stem cannot move the part that changes. Where a
    /// line carries a value, the value is a named placeholder for the same reason.
    /// </para>
    /// </summary>
    public static class MenuStrings
    {
        // -- The front door ------------------------------------------------------------------------

        /// <summary>The wordmark. A translator will normally leave it, but transliterating it is
        /// theirs to decide, and it is a whole word rather than a fragment either way.</summary>
        public static readonly LocKey Wordmark = new("menu.wordmark", "OILED UP");

        public static readonly LocKey Tagline = new("menu.tagline",
            "Heat-treatment oil analysis. Up to four of you in the lab.");

        public static readonly LocKey Continue = new("menu.continue", "CONTINUE");
        public static readonly LocKey SinglePlayer = new("menu.single_player", "SINGLE PLAYER");
        public static readonly LocKey CoOp = new("menu.co_op", "CO-OP");
        public static readonly LocKey Settings = new("menu.settings", "SETTINGS");
        public static readonly LocKey Credits = new("menu.credits", "CREDITS");
        public static readonly LocKey Quit = new("menu.quit", "QUIT");

        /// <summary>Shared by every page that has one. A single word with one meaning everywhere it
        /// is drawn, so a second copy would only be a second thing to translate.</summary>
        public static readonly LocKey Back = new("menu.back", "BACK");

        public static readonly LocKey OfflineNote = new("menu.offline_note",
            "Single player needs no sign-in, no lobby and no connection. It works offline.");

        public static readonly LocKey NoConnectionOnTitle = new("menu.no_connection",
            "No LabConnection on this object, so nothing here can start a game.  build {build}");

        public static readonly LocKey Identity = new("menu.identity",
            "you are {name} · {id}    build {build}");

        public static readonly LocKey Build = new("menu.build", "build {build}");

        public static readonly LocKey ContinueSaved = new("menu.continue_saved",
            "{run}  ·  £{money}  ·  saved {when}");

        public static readonly LocKey ContinueUnreadable = new("menu.continue_unreadable",
            "{run} was saved by a different version of the game and cannot be continued. The file " +
            "has been left where it is.");

        // -- Co-op ---------------------------------------------------------------------------------

        public static readonly LocKey CoOpHeading = new("menu.coop.heading", "CO-OP");
        public static readonly LocKey Host = new("menu.coop.host", "HOST A SHIFT");
        public static readonly LocKey JoinCodeField = new("menu.coop.code_field", "Join code");
        public static readonly LocKey Join = new("menu.coop.join", "JOIN");
        public static readonly LocKey TryAgain = new("menu.coop.try_again", "TRY AGAIN");

        public static readonly LocKey JoinCodeHint = new("menu.coop.code_hint",
            "Six letters and digits, read out by whoever is hosting.");

        public static readonly LocKey NoConnectionOnCoOp = new("menu.coop.no_connection",
            "No LabConnection on this object, so co-op cannot start.");

        // -- Credits -------------------------------------------------------------------------------
        //
        // The headings and the BACK button only. CreditsContent is generated licence text and is
        // reproduced verbatim by law and by licence; it never comes through here.

        public static readonly LocKey CreditsHeading = new("menu.credits.heading", "CREDITS");

        public static readonly LocKey MadeBy = new("menu.credits.made_by",
            "OILED UP is made by Emmanuel Lampe and Kevin-Timo Salmen.");

        public static readonly LocKey ThirdPartyArt = new("menu.credits.art", "Third-party art");

        public static readonly LocKey ThirdPartyArtNote = new("menu.credits.art_note",
            "Recorded in Assets/Art/Imported/CREDITS.md and reproduced here verbatim, because some " +
            "of these licences require attribution in the game itself rather than only in the " +
            "repository.");

        public static readonly LocKey ThirdPartySoftware = new("menu.credits.software",
            "Third-party software");

        public static readonly LocKey ThirdPartySoftwareNote = new("menu.credits.software_note",
            "Licence notices shipped inside the Unity packages this build is made from.");

        // -- Pause ---------------------------------------------------------------------------------

        public static readonly LocKey PausedHeading = new("menu.pause.heading", "PAUSED");
        public static readonly LocKey Resume = new("menu.pause.resume", "RESUME");
        public static readonly LocKey LeaveTheShift = new("menu.pause.leave", "LEAVE THE SHIFT");

        public static readonly LocKey ClockStopped = new("menu.pause.clock_stopped",
            "The lab is stopped while this is up. Nothing moves until you resume.");

        public static readonly LocKey ClockRunning = new("menu.pause.clock_running",
            "The shift clock is still running. This is a co-op session, so pausing only stops your " +
            "own hands — the day carries on for everyone else in the lab.");

        public static readonly LocKey LeaveNote = new("menu.pause.leave_note",
            "Leaving closes your session and puts you back at the menu. In co-op it does not end " +
            "the shift for anybody else.");

        // -- Lobby ---------------------------------------------------------------------------------

        public static readonly LocKey LobbyHeading = new("lobby.heading", "LOBBY");
        public static readonly LocKey Copy = new("lobby.copy", "COPY");
        public static readonly LocKey Copied = new("lobby.copied", "COPIED");
        public static readonly LocKey ReadyUp = new("lobby.ready_up", "READY UP");
        public static readonly LocKey CancelReady = new("lobby.cancel_ready", "CANCEL READY");
        public static readonly LocKey StartShift = new("lobby.start", "START SHIFT");
        public static readonly LocKey CancelCountdown = new("lobby.cancel_countdown", "CANCEL");
        public static readonly LocKey LeaveLobby = new("lobby.leave", "LEAVE");

        public static readonly LocKey CodeHint = new("lobby.code_hint",
            "Send this to whoever is joining you.");

        public static readonly LocKey CodeCopied = new("lobby.code_copied",
            "{code} is on your clipboard. Paste it to whoever is joining you.");

        public static readonly LocKey LobbyFull = new("lobby.full", "The lab is full.");

        public static readonly LocKey LobbyRoomLeft = new("lobby.room_left",
            "{here} of {capacity} here. There is room for {free} more.");

        public static readonly LocKey Countdown = new("lobby.countdown", "STARTING IN {seconds}");

        public static readonly LocKey StartShiftReady = new("lobby.start_ready",
            "START SHIFT ({ready}/{seated} READY)");

        public static readonly LocKey SeatHost = new("lobby.seat_host", "{name}  (host)");
        public static readonly LocKey SeatReady = new("lobby.seat_ready", "READY");
        public static readonly LocKey SeatDeciding = new("lobby.seat_deciding", "still deciding");
        public static readonly LocKey SeatEmpty = new("lobby.seat_empty", "empty seat");

        // -- The corner card -----------------------------------------------------------------------

        public static readonly LocKey CardJoinCode = new("session.card_join_code",
            "JOIN CODE  {code}");

        public static readonly LocKey CardConnected = new("session.card_connected", "CONNECTED");

        public static readonly LocKey CardVoiceKeys = new("session.card_voice_keys",
            "[M] MIC {mic}   [N] SOUND {sound}");

        /// <summary>What a switch reads as. A whole word rather than a fragment — this is the label
        /// on the state itself, not half a sentence about it.</summary>
        public static readonly LocKey On = new("session.on", "ON");

        public static readonly LocKey Off = new("session.off", "OFF");

        public static readonly LocKey CardVoiceConnecting = new("session.card_voice_connecting",
            "VOICE CONNECTING…");

        public static readonly LocKey CardVolume = new("session.card_volume",
            "[-/+] VOL {percent}%  {pointer}");

        public static readonly LocKey CardVolumeClose = new("session.card_volume_close",
            "[V/ESC] CLOSE");

        public static readonly LocKey CardVolumeMouse = new("session.card_volume_mouse",
            "[V] MOUSE");

        // -- The disconnect notice -----------------------------------------------------------------

        public static readonly LocKey Reconnect = new("session.reconnect", "RECONNECT");

        public static readonly LocKey BackToTheMenu = new("session.back_to_menu",
            "BACK TO THE MENU");

        public static readonly LocKey RejoinHint = new("session.rejoin_hint",
            "Reconnecting uses the same join code and the same identity, so the host puts you back " +
            "in your own seat rather than seating you again.");

        // One whole sentence per case rather than a shared "There is no reconnect for this." stem:
        // the three send a player to look in three different places, and a translator cannot move a
        // stem's clause into the middle of a sentence that arrives already written.

        public static readonly LocKey NoRejoinHostClosed = new("session.no_rejoin_host_closed",
            "There is no reconnect for this. The session is gone with the host; someone will have " +
            "to host a new one.");

        public static readonly LocKey NoRejoinKicked = new("session.no_rejoin_kicked",
            "There is no reconnect for this. The host decided, and the same code would be refused " +
            "again.");

        public static readonly LocKey NoRejoinRefused = new("session.no_rejoin_refused",
            "There is no reconnect for this. Nothing was ever started, so co-op is where to try " +
            "again — with the code checked.");

        // -- How a session ended -------------------------------------------------------------------

        public static readonly LocKey EndHostClosedHeadline = new("session.end.host_closed_headline",
            "THE HOST CLOSED THE LAB");

        public static readonly LocKey EndHostClosedDetail = new("session.end.host_closed_detail",
            "The shift ended when your host left. Their lobby has been deleted and its join code " +
            "will not answer any more, so there is nothing left to rejoin.");

        public static readonly LocKey EndRefusedHeadline = new("session.end.refused_headline",
            "THE LAB TURNED YOU AWAY");

        /// <summary>
        /// The host's own refusal text, reproduced whole and then answered. <c>{reason}</c> rather
        /// than a concatenation, so a language that puts the reassurance first can.
        /// </summary>
        public static readonly LocKey EndRefusedDetailSpoken = new("session.end.refused_detail_said",
            "{reason} Nothing was started, so nothing was lost.");

        public static readonly LocKey EndRefusedDetail = new("session.end.refused_detail",
            "The host refused the connection without saying why. Nothing was started.");

        public static readonly LocKey EndKickedHeadline = new("session.end.kicked_headline",
            "THE HOST DISCONNECTED YOU");

        public static readonly LocKey EndKickedDetail = new("session.end.kicked_detail",
            "{reason} Rejoining would only be refused again; ask your host for a fresh code.");

        public static readonly LocKey EndDroppedHeadline = new("session.end.dropped_headline",
            "THE CONNECTION DROPPED");

        public static readonly LocKey EndDroppedDetail = new("session.end.dropped_detail",
            "Nothing more came back from the host. Your seat is held for you, so rejoining puts you " +
            "back where you were standing, with whatever you were holding.");

        // -- What the loading screen is waiting on --------------------------------------------------

        public static readonly LocKey StepLoading = new("session.step.loading", "Loading…");

        public static readonly LocKey StepWaitingForHost = new("session.step.waiting_for_host",
            "Waiting for the host to start the shift…");

        public static readonly LocKey StepWaitingForLab = new("session.step.waiting_for_lab",
            "Waiting for the host to send the lab…");

        public static readonly LocKey StepOpeningLab = new("session.step.opening_lab",
            "Opening the lab…");

        public static readonly LocKey StepLoadingLab = new("session.step.loading_lab",
            "Loading the lab…");

        public static readonly LocKey StepReturning = new("session.step.returning",
            "Returning to the menu…");

        public static readonly LocKey PatienceHostNotStarted = new("session.patience.host",
            "Still connected. The host has not started the shift yet — you do not need to rejoin.");

        public static readonly LocKey PatienceLabArriving = new("session.patience.lab",
            "The lab is still arriving from the host. Leaving now would put you back in the queue " +
            "behind it.");

        public static readonly LocKey PatienceGeneric = new("session.patience.generic",
            "Still working. This can take a moment on a slow connection.");

        // -- Connect progress ----------------------------------------------------------------------

        public static readonly LocKey StatusReservingRelay = new("session.status.reserving_relay",
            "Reserving a relay…");

        public static readonly LocKey StatusOpeningLobby = new("session.status.opening_lobby",
            "Opening the lobby…");

        public static readonly LocKey StatusStartingHost = new("session.status.starting_host",
            "Starting the host…");

        public static readonly LocKey StatusHosting = new("session.status.hosting",
            "Hosting — join code {code}");

        public static readonly LocKey StatusStartingShift = new("session.status.starting_shift",
            "Starting the shift…");

        public static readonly LocKey StatusStartingShiftHosting =
            new("session.status.starting_shift_hosting",
                "Starting the shift — join code {code}");

        public static readonly LocKey StatusLookingUpCode = new("session.status.looking_up_code",
            "Looking up that join code…");

        public static readonly LocKey StatusJoiningRelay = new("session.status.joining_relay",
            "Joining the relay…");

        public static readonly LocKey StatusConnecting = new("session.status.connecting",
            "Connecting…");

        // -- Connect failures ----------------------------------------------------------------------

        public static readonly LocKey ErrorRelayFailed = new("session.error.relay_failed",
            "Could not reserve a relay. Check your connection and try again.");

        public static readonly LocKey ErrorLobbyFailed = new("session.error.lobby_failed",
            "Reserved a relay but could not open a lobby. Nothing was started; try again.");

        public static readonly LocKey ErrorHostRefused = new("session.error.host_refused",
            "Netcode refused to start the host. See the console for the transport error.");

        public static readonly LocKey ErrorClientRefused = new("session.error.client_refused",
            "Netcode refused to start the client. See the console for the transport error.");

        public static readonly LocKey ErrorNoCode = new("session.error.no_code",
            "Type the join code your host read out.");

        public static readonly LocKey ErrorCodeMalformed = new("session.error.code_malformed",
            "“{code}” is not a join code — they are {length} letters and digits.");

        public static readonly LocKey ErrorLobbyService = new("session.error.lobby_service",
            "Could not reach the lobby service. Check your connection and try again.");

        public static readonly LocKey ErrorLobbyNotPlaying = new("session.error.lobby_not_playing",
            "That lobby is not running a game. Ask your host for a fresh code.");

        public static readonly LocKey ErrorRelayGone = new("session.error.relay_gone",
            "That game's relay is gone. The host has probably closed it.");

        public static readonly LocKey ErrorNoNetworkManager = new("session.error.no_manager",
            "No NetworkManager in the scene. Co-op cannot start; single player still can.");

        public static readonly LocKey ErrorNoTransport = new("session.error.no_transport",
            "The NetworkManager has no UnityTransport. Co-op cannot start.");

        /// <summary>
        /// The same fault seen from inside the transport setup, where "co-op cannot start" has
        /// already been said by whatever is about to unwind. Its own id rather than a reuse: the two
        /// read differently and a translator sizing a line needs to know which is which.
        /// </summary>
        public static readonly LocKey ErrorTransportMissing = new("session.error.transport_missing",
            "The NetworkManager has no UnityTransport.");

        public static readonly LocKey ErrorOffline = new("session.error.offline",
            "{detail} Single player still works.");

        public static readonly LocKey ErrorNoIdentity = new("session.error.no_identity",
            "Could not establish a player identity. Single player still works.");

        public static readonly LocKey ErrorNoEndpoint = new("session.error.no_endpoint",
            "The relay offered no endpoint this build can use.");

        public static readonly LocKey ErrorCodeNotFound = new("session.error.code_not_found",
            "No game is using the code {code}. Check it and try again.");

        public static readonly LocKey ErrorCodeInvalid = new("session.error.code_invalid",
            "{code} is not a valid join code.");

        public static readonly LocKey ErrorLobbyFull = new("session.error.lobby_full",
            "That game is full — {capacity} players is the limit.");

        /// <summary>
        /// Anything the lobby service reported that we have no better sentence for. The service's own
        /// wording is carried whole rather than flattened into "something went wrong" — see
        /// <c>LabConnection.Explain</c>.
        /// </summary>
        public static readonly LocKey ErrorJoinFailed = new("session.error.join_failed",
            "Could not join: {reason}");

        public static readonly LocKey ErrorSceneMissing = new("session.error.scene_missing",
            "Could not load '{scene}'. Is it in Build Settings?");

        // -- Settings: the shell ---------------------------------------------------------------------

        public static readonly LocKey SettingsHeading = new("settings.heading", "SETTINGS");
        public static readonly LocKey TabDisplay = new("settings.tab_display", "DISPLAY");
        public static readonly LocKey TabAudio = new("settings.tab_audio", "AUDIO");
        public static readonly LocKey TabControls = new("settings.tab_controls", "CONTROLS");

        // -- Settings: display -----------------------------------------------------------------------

        public static readonly LocKey Resolution = new("settings.resolution", "Resolution");
        public static readonly LocKey WindowMode = new("settings.window_mode", "Window mode");
        public static readonly LocKey VerticalSync = new("settings.vertical_sync", "Vertical sync");
        public static readonly LocKey Detail = new("settings.detail", "Detail");
        public static readonly LocKey FieldOfView = new("settings.field_of_view", "Field of view");

        public static readonly LocKey DisplayNote = new("settings.display_note",
            "A new resolution or window mode is applied straight away and then asks you to confirm " +
            "it, so a mode your monitor cannot show puts itself back.");

        public static readonly LocKey WindowExclusive = new("settings.window_exclusive",
            "Exclusive fullscreen");

        public static readonly LocKey WindowBorderless = new("settings.window_borderless",
            "Borderless fullscreen");

        public static readonly LocKey WindowMaximised = new("settings.window_maximised",
            "Maximised window");

        public static readonly LocKey WindowWindowed = new("settings.window_windowed", "Windowed");

        public static readonly LocKey KeepThisMode = new("settings.keep_mode", "KEEP THIS MODE");
        public static readonly LocKey PutItBack = new("settings.put_it_back", "PUT IT BACK");

        /// <summary>
        /// The revert countdown. The seconds are a named argument and not a suffix, because the
        /// number does not sit at the end of the sentence in every language — and the resolutions are
        /// arguments too, so a translator can put the mode before the question.
        /// </summary>
        public static readonly LocKey ConfirmDisplay = new("settings.confirm_display",
            "Can you read this? Keep {mode}, {window}. It goes back to {previous}, " +
            "{previousWindow}, in {seconds} s.");

        // -- Settings: audio -------------------------------------------------------------------------

        public static readonly LocKey VolumeMaster = new("settings.volume_master", "Master");

        public static readonly LocKey VolumeEffects = new("settings.volume_effects",
            "Machines and tools");

        public static readonly LocKey VolumeAmbience = new("settings.volume_ambience",
            "Room ambience");

        public static readonly LocKey VolumeVoice = new("settings.volume_voice", "Voice chat");

        public static readonly LocKey AudioNote = new("settings.audio_note",
            "Room ambience is the ventilation, the lighting hum and the occasional relay. Machines " +
            "and tools is everything you or an instrument sets off.");

        // -- Settings: controls ----------------------------------------------------------------------

        public static readonly LocKey LookSensitivity = new("settings.look_sensitivity",
            "Look sensitivity");

        public static readonly LocKey InvertLook = new("settings.invert_look", "Invert looking up");
        public static readonly LocKey HeadBob = new("settings.head_bob", "Head bob");
        public static readonly LocKey CameraShake = new("settings.camera_shake", "Camera shake");

        public static readonly LocKey ComfortNote = new("settings.comfort_note",
            "Turn these down if walking between benches makes you queasy. Head bob is the sway of " +
            "your own footsteps; camera shake is the dip when you land and the lens kick when you " +
            "sprint. Zero means off, not reduced. Nothing else changes: you still read the same " +
            "numbers off the same machines.");

        public static readonly LocKey NoBindingsHere = new("settings.no_bindings_here",
            "Keys cannot be changed from here.");

        public static readonly LocKey NoBindingsHereNote = new("settings.no_bindings_here_note",
            "This screen was opened without the input actions that hold your key bindings. Open " +
            "settings from the main menu or from the pause menu and the full list of keys will be " +
            "here.");

        public static readonly LocKey NothingToRebind = new("settings.nothing_to_rebind",
            "There are no keyboard or mouse controls to change.");

        public static readonly LocKey NothingToRebindNote = new("settings.nothing_to_rebind_note",
            "Nothing on the Player action map is bound to a key or a mouse button, so there is " +
            "nothing here to rebind.");

        public static readonly LocKey KeyboardAndMouse = new("settings.keyboard_and_mouse",
            "Keyboard and mouse");

        public static readonly LocKey ResetEveryKey = new("settings.reset_every_key",
            "RESET EVERY KEY");

        public static readonly LocKey RebindNote = new("settings.rebind_note",
            "Press REBIND, then press the key you want. Escape keeps the key you already had, and " +
            "so does waiting.");

        public static readonly LocKey HoldNote = new("settings.hold_note",
            "A row marked (hold) is a key you keep pressed. Rebinding moves the key and never the " +
            "time the job takes.");

        public static readonly LocKey Rebind = new("settings.rebind", "REBIND");
        public static readonly LocKey RebindDefault = new("settings.rebind_default", "DEFAULT");
        public static readonly LocKey PressAKey = new("settings.press_a_key", "press a key…");

        /// <summary>The action's name and the "(hold)" mark are one line, so a language that marks a
        /// held key with a prefix can move it.</summary>
        public static readonly LocKey BindingHeld = new("settings.binding_held", "{action} (hold)");

        public static readonly LocKey ResetEveryKeyDone = new("settings.reset_every_key_done",
            "Every key is back to how it shipped.");

        public static readonly LocKey ResetKeyDone = new("settings.reset_key_done",
            "{action} is back to {key}.");

        public static readonly LocKey PressKeyFor = new("settings.press_key_for",
            "Press the key you want for {action}.");

        public static readonly LocKey RebindUnchanged = new("settings.rebind_unchanged",
            "{action} is still {key}.");

        public static readonly LocKey RebindConflict = new("settings.rebind_conflict",
            "{key} is already {heldBy}. {action} is still {current} — change {heldBy} first if you " +
            "want that key here.");

        public static readonly LocKey RebindDone = new("settings.rebind_done",
            "{action} is now {key}.");

        /// <summary>Stands in for a key the input system could not name.</summary>
        public static readonly LocKey ThatKey = new("settings.that_key", "That key");

        // -- Connection state, the default line under the buttons ---------------------------------
        //
        // The fallback behind LabConnection.Status, so it is what a player reads whenever the flow
        // has not set something more specific. A blank line here reads as a frozen screen, which is
        // the impression a connect flow must never give.

        public static readonly LocKey ConnectIdle = new("session.connect_idle", "Not connected.");

        public static readonly LocKey ConnectPreparing = new("session.connect_preparing", "Signing in…");

        public static readonly LocKey ConnectAllocating = new(
            "session.connect_allocating", "Opening a session…");

        public static readonly LocKey ConnectResolving = new(
            "session.connect_resolving", "Looking up that join code…");

        public static readonly LocKey ConnectConnecting = new(
            "session.connect_connecting", "Connecting…");

        public static readonly LocKey ConnectHosting = new("session.connect_hosting", "Hosting.");

        public static readonly LocKey ConnectJoined = new("session.connect_joined", "Connected.");

        public static readonly LocKey ConnectSinglePlayer = new(
            "session.connect_single_player", "Single player.");

        // -- Language -----------------------------------------------------------------------------

        public static readonly LocKey Language = new("settings.language", "Language");

        public static readonly LocKey LanguageNote = new(
            "settings.language_note",
            "Menus already on screen keep their old wording until they are next opened. Everything " +
            "in the lab changes straight away.");
    }
}