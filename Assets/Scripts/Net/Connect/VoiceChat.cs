using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Residue.Gameplay.Settings;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Net.Connect
{
    /// <summary>
    /// Owns the Vivox login and positional channel for exactly as long as the co-op session exists.
    /// <para>
    /// Voice follows the Lobby id rather than the human join code: invite codes may be recycled,
    /// while every member of one Lobby sees the same stable id. That also lets a returning player
    /// join the same channel with the same UGS PlayerId, matching the session rejoin identity.
    /// </para>
    /// This is deliberately driven by <see cref="LabConnection"/> instead of being another scene
    /// component. There is then one teardown path for Relay, Lobby, NGO and voice, so leaving cannot
    /// strand a channel after the lab scene has gone away.
    /// </summary>
    public sealed class VoiceChat
    {
        private const float PositionIntervalSeconds = 0.1f;
        private const int AudibleDistanceMetres = 24;
        private const int ConversationalDistanceMetres = 1;

        /// <summary>
        /// Vivox does not put a deadline on initialization, login, or channel connection. A dead
        /// socket can therefore leave its task pending forever, so voice needs its own deadline.
        /// This only disables voice; Relay, Lobby, and the game session remain live.
        /// </summary>
        public static float ConnectTimeoutSeconds = 12f;

        private readonly Dictionary<string, VivoxParticipant> participants = new();
        private readonly SemaphoreSlim lifecycle = new(1, 1);

        private string desiredChannel;
        private string activeChannel;
        private PlayerAvatar localPlayer;
        private float nextPositionAt;
        private int operation;
        private bool eventsHooked;
        private bool microphoneMuted;
        private bool outputMuted;
        private string unavailableText = "VOICE UNAVAILABLE";
        private RelayVoice relayVoice;

        /// <summary>Raised when connection, controls, or the speaking roster changes.</summary>
        public event Action Changed;

        public bool IsConnected => activeChannel != null;
        public bool IsConnecting => desiredChannel != null && activeChannel == null;
        public bool MicrophoneMuted => microphoneMuted;
        public bool OutputMuted => outputMuted;
        public string UnavailableText => unavailableText;

        /// <summary>
        /// Playback gain, held in <see cref="GameSettings.VoiceVolume"/> rather than in a field here.
        /// <para>
        /// There are three ways to move this — the card's slider, the <c>-</c>/<c>+</c> keys, and the
        /// audio tab of the settings screen — and the settings screen is in <c>Residue.Gameplay</c>,
        /// which cannot reference this assembly. A local field would mean the two ends each held their
        /// own idea of the volume and whichever the player touched last would silently win. Reading
        /// the setting directly makes that impossible, and gets persistence across sessions for free.
        /// </para>
        /// </summary>
        public float OutputVolume => GameSettings.VoiceVolume;

        /// <summary>
        /// Every peer in one session must use the same voice transport. Selecting this only on
        /// Linux creates two silent islands when a Linux player joins a Windows Vivox player, so
        /// the current branch uses Relay voice everywhere. Vivox can return once transport choice
        /// is negotiated as session data rather than guessed independently by each client.
        /// </summary>
        private static bool UseRelayFallback
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// A compact, shape-led roster for the persistent HUD card. The triangle communicates
        /// speech without borrowing red, amber, or green from verdict state (hard rule 4).
        /// </summary>
        public string SpeakingText
        {
            get
            {
                var names = new List<string>();
                foreach (var participant in participants.Values)
                {
                    if (participant.SpeechDetected && !participant.IsMuted)
                        names.Add(string.IsNullOrWhiteSpace(participant.DisplayName)
                            ? participant.PlayerId
                            : participant.DisplayName);
                }

                if (names.Count == 0) return "";
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return $"▶ {string.Join("  ▶ ", names)}";
            }
        }

        /// <summary>Join the Lobby's positional channel. Voice failure never ends the game session.</summary>
        public async Task JoinAsync(string lobbyId, string displayName)
        {
            int ticket = ++operation;
            desiredChannel = ChannelName(lobbyId);
            activeChannel = null;
            localPlayer = null;
            ClearParticipants();
            Changed?.Invoke();

            if (string.IsNullOrEmpty(desiredChannel)) return;

            if (UseRelayFallback)
            {
                relayVoice = new RelayVoice();
                if (relayVoice.Join())
                {
                    activeChannel = desiredChannel;
                    unavailableText = "VOICE UNAVAILABLE";
                }
                else
                {
                    desiredChannel = null;
                    unavailableText = relayVoice.Error ?? "VOICE UNAVAILABLE";
                    relayVoice = null;
                }
                Changed?.Invoke();
                return;
            }

            unavailableText = "VOICE UNAVAILABLE";

            await lifecycle.WaitAsync();

            string joining = ChannelName(lobbyId);
            IVivoxService service = null;
            Task connecting = null;

            try
            {
                // Acquiring the service is inside the try on purpose: VivoxService.Instance throws
                // when UnityServices was never initialized, and anything thrown between WaitAsync
                // and the try would strand the semaphore for the lifetime of the process. Every
                // later JoinAsync would hang, and so would the LeaveAsync that teardown waits on.
                service = VivoxService.Instance;
                HookEvents(service);

                if (ticket != operation) return;

                connecting = ConnectServiceAsync(service, joining, displayName,
                    () => ticket == operation);
                await AwaitWithTimeoutAsync(connecting,
                    Mathf.Max(1f, ConnectTimeoutSeconds));

                if (ticket != operation) return;

                activeChannel = joining;
                desiredChannel = joining;
                nextPositionAt = 0f;
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                if (ticket != operation) return;

                // Become terminal before touching the SDK again. Cleanup is best effort and may be
                // waiting on the same damaged connection; the HUD must never wait on it.
                ++operation;
                desiredChannel = null;
                activeChannel = null;
                ClearParticipants();
                Debug.LogWarning($"[VoiceChat] Proximity voice is unavailable for this session " +
                                 $"({e.GetType().Name}: {e.Message}). The game remains connected.");
                Changed?.Invoke();

                // service is null only when acquiring it is what failed, in which case there is
                // nothing logged in and nothing joined for cleanup to release.
                if (service == null) return;

                if (connecting != null && !connecting.IsCompleted)
                    _ = ReleaseWhenSettledAsync(connecting, service, joining);
                else
                    _ = ReleaseServiceAsync(service, joining);
            }
            finally
            {
                lifecycle.Release();
            }
        }

        /// <summary>Leave voice before the Lobby and transport are released. Idempotent.</summary>
        public async Task LeaveAsync()
        {
            ++operation;
            string leaving = activeChannel ?? desiredChannel;
            desiredChannel = null;
            activeChannel = null;
            localPlayer = null;
            ClearParticipants();
            Changed?.Invoke();

            if (relayVoice != null)
            {
                relayVoice.Leave();
                relayVoice = null;
                return;
            }

            await lifecycle.WaitAsync();
            IVivoxService service = null;
            try
            {
                // Inside the try for the same reason as JoinAsync — a semaphore stranded here would
                // hang every future teardown, which is the one path that must always complete.
                service = VivoxService.Instance;
                await ReleaseServiceAsync(service, leaving);
            }
            finally
            {
                if (service != null) UnhookEvents(service);
                lifecycle.Release();
            }
        }

        /// <summary>Update controls and the local speaker/listener position while voice is live.</summary>
        public void Tick(float realtime)
        {
            if (!IsConnected) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.mKey.wasPressedThisFrame) ToggleMicrophone();
                if (keyboard.nKey.wasPressedThisFrame) ToggleOutput();
                if (keyboard.minusKey.wasPressedThisFrame) AdjustOutputVolume(-0.1f);
                if (keyboard.equalsKey.wasPressedThisFrame) AdjustOutputVolume(0.1f);
            }

            if (relayVoice != null)
            {
                // Pushed every tick rather than only on change, so the settings screen's audio tab
                // reaches the transport without this class having to hear about it.
                relayVoice.Tick(microphoneMuted, outputMuted, OutputVolume);
                return;
            }

            if (realtime < nextPositionAt) return;
            nextPositionAt = realtime + PositionIntervalSeconds;

            if (localPlayer == null || !localPlayer.IsSpawned || !localPlayer.IsOwner)
                localPlayer = FindLocalPlayer();
            if (localPlayer == null) return;

            Transform pose = localPlayer.transform;
            VivoxService.Instance.Set3DPosition(
                pose.position,
                pose.position,
                pose.forward,
                pose.up,
                activeChannel);
        }

        public void ToggleMicrophone()
        {
            microphoneMuted = !microphoneMuted;
            if (relayVoice == null)
            {
                if (microphoneMuted) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();
            }
            Changed?.Invoke();
        }

        public void ToggleOutput()
        {
            outputMuted = !outputMuted;
            if (relayVoice == null)
            {
                if (outputMuted) VivoxService.Instance.MuteOutputDevice();
                else VivoxService.Instance.UnmuteOutputDevice();
            }
            Changed?.Invoke();
        }

        /// <summary>Set incoming voice volume. The UI and keyboard both use this one path.</summary>
        public void SetOutputVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            if (Mathf.Approximately(GameSettings.VoiceVolume, clamped)) return;

            GameSettings.VoiceVolume = clamped;
            relayVoice?.SetOutputVolume(clamped);
            Changed?.Invoke();
        }

        private void AdjustOutputVolume(float amount)
        {
            SetOutputVolume(Mathf.Round((OutputVolume + amount) * 10f) / 10f);
        }

        /// <summary>Derive a Vivox-safe channel name deterministically from the Lobby id.</summary>
        internal static string ChannelName(string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId)) return null;

            var safe = new StringBuilder("oiled-up-");
            foreach (char c in lobbyId.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') safe.Append(c);
                else safe.Append('-');
            }
            return safe.ToString();
        }

        private static PlayerAvatar FindLocalPlayer()
        {
            foreach (var avatar in UnityEngine.Object.FindObjectsByType<PlayerAvatar>())
            {
                if (avatar.IsSpawned && avatar.IsOwner) return avatar;
            }
            return null;
        }

        private void HookEvents(IVivoxService service)
        {
            if (eventsHooked) return;
            service.ParticipantAddedToChannel += OnParticipantAdded;
            service.ParticipantRemovedFromChannel += OnParticipantRemoved;
            eventsHooked = true;
        }

        private void UnhookEvents(IVivoxService service)
        {
            if (!eventsHooked) return;
            service.ParticipantAddedToChannel -= OnParticipantAdded;
            service.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            eventsHooked = false;
        }

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant == null || participant.ChannelName != desiredChannel) return;

            if (participants.TryGetValue(participant.PlayerId, out var previous))
                UnhookParticipant(previous);

            participants[participant.PlayerId] = participant;
            participant.ParticipantSpeechDetected += OnParticipantChanged;
            participant.ParticipantMuteStateChanged += OnParticipantChanged;
            Changed?.Invoke();
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == null || !participants.Remove(participant.PlayerId)) return;
            UnhookParticipant(participant);
            Changed?.Invoke();
        }

        private void OnParticipantChanged() => Changed?.Invoke();

        private void ClearParticipants()
        {
            foreach (var participant in participants.Values) UnhookParticipant(participant);
            participants.Clear();
        }

        private void UnhookParticipant(VivoxParticipant participant)
        {
            participant.ParticipantSpeechDetected -= OnParticipantChanged;
            participant.ParticipantMuteStateChanged -= OnParticipantChanged;
        }

        private void ApplyDeviceState(IVivoxService service)
        {
            if (microphoneMuted) service.MuteInputDevice();
            else service.UnmuteInputDevice();

            if (outputMuted) service.MuteOutputDevice();
            else service.UnmuteOutputDevice();
        }

        private async Task ConnectServiceAsync(IVivoxService service, string channel,
                                               string displayName, Func<bool> isCurrent)
        {
            if (service.InitializationState != VivoxInitializationState.Initialized)
                await service.InitializeAsync();

            if (!isCurrent()) return;

            if (!service.IsLoggedIn)
            {
                await service.LoginAsync(new LoginOptions
                {
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.StateChange
                });
            }

            if (!isCurrent()) return;

            ApplyDeviceState(service);

            var acoustics = new Channel3DProperties(
                AudibleDistanceMetres,
                ConversationalDistanceMetres,
                1f,
                AudioFadeModel.InverseByDistance);

            await service.JoinPositionalChannelAsync(
                channel,
                ChatCapability.AudioOnly,
                acoustics,
                new ChannelOptions { MakeActiveChannelUponJoining = true });
        }

        /// <summary>
        /// Await SDK work without allowing it to hold the voice UI forever.
        /// <para>
        /// The losing side of the race is <b>cancelled</b>, not abandoned. An uncancelled
        /// <see cref="Task.Delay(TimeSpan)"/> keeps a timer registration alive for its whole
        /// duration, so every successful connect used to leak one for
        /// <see cref="ConnectTimeoutSeconds"/> seconds — in the game, not only under test.
        /// </para>
        /// <para>
        /// Both awaits are <c>ConfigureAwait(false)</c> deliberately. Nothing after them touches the
        /// Unity API, and capturing the main-thread synchronization context here deadlocks any
        /// caller that blocks on the returned task: the continuation needs a thread that is already
        /// waiting on it. NUnit's <c>Assert.ThrowsAsync</c> is exactly such a caller — Unity's NUnit
        /// 3.5 fork implements it as a bare reflected <c>Task.Wait()</c> with no message pump — and
        /// that is what wedged the entire EditMode suite in #76.
        /// </para>
        /// </summary>
        public static async Task AwaitWithTimeoutAsync(Task work, float timeoutSeconds)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var deadline = new CancellationTokenSource();
            try
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0.001f, timeoutSeconds)),
                                         deadline.Token);

                bool workWon = await Task.WhenAny(work, timeout).ConfigureAwait(false) == work;

                // Before the throw, so the timer is disarmed on both exits. Cancel settles the delay
                // synchronously as Canceled; nothing here awaits it, so its TaskCanceledException is
                // never rethrown, and a Canceled task holds no fault for the finalizer to re-raise.
                deadline.Cancel();

                if (!workWon)
                {
                    throw new TimeoutException(
                        $"Vivox did not connect within {timeoutSeconds:0.#} seconds");
                }

                await work.ConfigureAwait(false);
            }
            finally
            {
                deadline.Dispose();
            }
        }

        private static async Task ReleaseWhenSettledAsync(Task connecting, IVivoxService service,
                                                           string channel)
        {
            try { await connecting; }
            catch { /* The original warning already records the player-facing failure. */ }

            await ReleaseServiceAsync(service, channel);
        }

        private static async Task ReleaseServiceAsync(IVivoxService service, string channel)
        {
            // Keep the two operations independent: if a damaged channel refuses to leave, logout
            // is the stronger cleanup and must still get its chance before Lobby teardown carries on.
            try
            {
                if (!string.IsNullOrEmpty(channel) && service.IsLoggedIn &&
                    service.ActiveChannels.ContainsKey(channel))
                {
                    await service.LeaveChannelAsync(channel);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceChat] Could not leave channel '{channel}' " +
                                 $"({e.GetType().Name}: {e.Message}).");
            }

            try
            {
                if (service.IsLoggedIn) await service.LogoutAsync();
            }
            catch (Exception e)
            {
                // Teardown is best-effort all the way down. The SDK/server will expire a broken
                // login; more importantly, Lobby and NGO teardown must still continue.
                Debug.LogWarning($"[VoiceChat] Could not log out " +
                                 $"({e.GetType().Name}: {e.Message}).");
            }
        }

    }
}
