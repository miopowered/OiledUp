using System;
using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Settings
{
    /// <summary>
    /// Every player-facing option that is not a key binding, and the only thing that reads or writes
    /// them. Bindings live in <see cref="KeyBindings"/> because they persist as one opaque blob with
    /// its own version.
    /// <para>
    /// <b>Applied on change, not on a confirm step.</b> Every setter here pushes its effect at Unity
    /// and raises <see cref="Changed"/> immediately. A settings screen you have to press Apply on
    /// makes the player guess at the result of a slider they cannot see the effect of, and look
    /// sensitivity in particular is the single most likely reason someone bounces off a first-person
    /// game in the first minute — it has to be adjustable while looking around, not after an OK.
    /// </para>
    /// <para>
    /// The display mode is the one exception, and <see cref="ApplyDisplay"/> /
    /// <see cref="CommitDisplay"/> / <see cref="RevertDisplay"/> are that exception's mechanism. A
    /// resolution or refresh rate the monitor cannot show soft-locks the game: nothing is on screen
    /// to click, so nothing can undo it. So the mode is applied, the player is asked, and it reverts
    /// on its own if nobody answers. This class owns applying and reverting; the screen owns the
    /// countdown and the dialog.
    /// </para>
    /// <para>
    /// Persisted in <see cref="PlayerPrefs"/> under <c>oiledup.settings.*</c>, deliberately nowhere
    /// near <c>player-id.txt</c>. Rejoin is keyed on that file, and a settings write that truncated
    /// or rewrote it would cost a player their seat in the lab rather than their brightness slider.
    /// </para>
    /// <para>
    /// Setters record into PlayerPrefs' in-memory store but do not flush; <see cref="Save"/> flushes.
    /// Dragging a slider is hundreds of writes a second and a disk write per frame is not worth the
    /// crash-safety it buys, given Unity flushes on quit anyway.
    /// </para>
    /// </summary>
    public static class GameSettings
    {
        // -- Keys --------------------------------------------------------------------------------

        private const string Prefix = "oiledup.settings.";

        private const string KeyWidth = Prefix + "display.width";
        private const string KeyHeight = Prefix + "display.height";
        private const string KeyRefreshHz = Prefix + "display.hz";
        private const string KeyScreenMode = Prefix + "display.mode";
        private const string KeyVSync = Prefix + "display.vsync";
        private const string KeyQuality = Prefix + "display.quality";
        private const string KeyFieldOfView = Prefix + "display.fov";

        private const string KeyMaster = Prefix + "audio.master";
        private const string KeyEffects = Prefix + "audio.effects";
        private const string KeyAmbience = Prefix + "audio.ambience";
        private const string KeyVoice = Prefix + "audio.voice";

        private const string KeySensitivity = Prefix + "controls.sensitivity";
        private const string KeyInvertLook = Prefix + "controls.invert";
        private const string KeyHeadBob = Prefix + "controls.headbob";
        private const string KeyHeadBobScale = Prefix + "controls.headbobscale";
        private const string KeyCameraShake = Prefix + "controls.camerashake";

        private const string KeyShiftBrief = Prefix + "help.shiftbrief";

        private const string KeyLanguage = Prefix + "ui.language";

        // -- Ranges ------------------------------------------------------------------------------

        /// <summary>
        /// Below this a full turn is an arm sweep; above it the crosshair jumps past a 2.5 m
        /// interaction target between frames. Both ends are unusable rather than merely extreme, so
        /// the slider refuses to reach them.
        /// </summary>
        public const float MinLookSensitivity = 0.01f;

        public const float MaxLookSensitivity = 0.4f;

        /// <summary>
        /// §2.1 fixes eye height at 1.7 m and the room is read against it. Much below 60 the bench
        /// scale stops reading; much above 100 the flat-shaded geometry distorts at the edges.
        /// </summary>
        public const float MinFieldOfView = 60f;

        public const float MaxFieldOfView = 100f;

        /// <summary>Used only if no <c>PlayerController</c> ever seeded one — a menu-only scene.</summary>
        private const float FallbackLookSensitivity = 0.075f;

        private const float FallbackFieldOfView = 70f;

        // -- State -------------------------------------------------------------------------------

        /// <summary>Raised after any value changes, including by <see cref="Load"/> and <see cref="Apply"/>.</summary>
        public static event Action Changed;

        private static bool loaded;

        private static DisplayMode display;
        private static DisplayMode committedDisplay;
        private static bool vSync = true;
        private static int qualityLevel;
        private static float fieldOfView = FallbackFieldOfView;

        private static float masterVolume = 1f;
        private static float effectsVolume = 1f;
        private static float ambienceVolume = 1f;
        private static float voiceVolume = 1f;

        private static float lookSensitivity = FallbackLookSensitivity;
        private static bool invertLook;
        private static float headBobScale = 1f;
        private static float cameraShakeScale = 1f;

        private static bool shiftBriefSeen;

        private static string language = string.Empty;

        private static float defaultLookSensitivity = FallbackLookSensitivity;
        private static float defaultFieldOfView = FallbackFieldOfView;
        private static bool hasSavedLookSensitivity;
        private static bool hasSavedFieldOfView;

        private static List<DisplayMode> availableModes;
        private static FullScreenMode availableModesBuiltFor;
        private static string[] qualityLevels;

        // -- Lifecycle ---------------------------------------------------------------------------

        /// <summary>
        /// Reads the saved profile and pushes it at Unity. Idempotent — the second call and every
        /// call after it does nothing.
        /// <para>
        /// Runs at <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> so it is already done
        /// before any <c>Awake</c> in any scene, in a build and in the Editor alike. Nothing then
        /// has to remember to initialise it, and no screen can read a default that the player
        /// overrode three sessions ago.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            // The screen as it is right now is the only default that is guaranteed to be displayable,
            // so a profile with no saved mode adopts it rather than guessing at the monitor.
            committedDisplay = CurrentScreenMode();

            if (PlayerPrefs.HasKey(KeyWidth) && PlayerPrefs.HasKey(KeyHeight))
            {
                committedDisplay = new DisplayMode(
                    PlayerPrefs.GetInt(KeyWidth, committedDisplay.Width),
                    PlayerPrefs.GetInt(KeyHeight, committedDisplay.Height),
                    PlayerPrefs.GetInt(KeyRefreshHz, committedDisplay.RefreshHz),
                    (FullScreenMode)PlayerPrefs.GetInt(KeyScreenMode, (int)committedDisplay.Mode));
            }

            display = committedDisplay;

            vSync = PlayerPrefs.GetInt(KeyVSync, QualitySettings.vSyncCount > 0 ? 1 : 0) != 0;
            qualityLevel = ClampQuality(PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel()));

            hasSavedFieldOfView = PlayerPrefs.HasKey(KeyFieldOfView);
            fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(KeyFieldOfView, defaultFieldOfView),
                MinFieldOfView, MaxFieldOfView);

            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMaster, 1f));
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyEffects, 1f));
            ambienceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyAmbience, 1f));
            voiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVoice, 1f));

            hasSavedLookSensitivity = PlayerPrefs.HasKey(KeySensitivity);
            lookSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, defaultLookSensitivity),
                MinLookSensitivity, MaxLookSensitivity);

            invertLook = PlayerPrefs.GetInt(KeyInvertLook, 0) != 0;
            headBobScale = ReadHeadBobScale();
            cameraShakeScale = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyCameraShake, 1f));

            shiftBriefSeen = ReadShiftBriefSeen();

            // Before Apply, because Apply installs it. A profile that has never chosen takes the
            // system language: someone running a German Windows should not have to find a menu in
            // English to ask for German, and the menu is the first thing they see.
            language = PlayerPrefs.GetString(KeyLanguage, SystemLanguageCode());

            Apply();
        }

        /// <summary>Writes every value and flushes PlayerPrefs to disk.</summary>
        public static void Save()
        {
            PlayerPrefs.SetInt(KeyWidth, committedDisplay.Width);
            PlayerPrefs.SetInt(KeyHeight, committedDisplay.Height);
            PlayerPrefs.SetInt(KeyRefreshHz, committedDisplay.RefreshHz);
            PlayerPrefs.SetInt(KeyScreenMode, (int)committedDisplay.Mode);

            PlayerPrefs.SetInt(KeyVSync, vSync ? 1 : 0);
            PlayerPrefs.SetInt(KeyQuality, qualityLevel);
            PlayerPrefs.SetFloat(KeyFieldOfView, fieldOfView);

            PlayerPrefs.SetFloat(KeyMaster, masterVolume);
            PlayerPrefs.SetFloat(KeyEffects, effectsVolume);
            PlayerPrefs.SetFloat(KeyAmbience, ambienceVolume);
            PlayerPrefs.SetFloat(KeyVoice, voiceVolume);

            PlayerPrefs.SetFloat(KeySensitivity, lookSensitivity);
            PlayerPrefs.SetInt(KeyInvertLook, invertLook ? 1 : 0);
            PlayerPrefs.SetFloat(KeyHeadBobScale, headBobScale);
            PlayerPrefs.SetFloat(KeyCameraShake, cameraShakeScale);

            PlayerPrefs.SetInt(KeyShiftBrief, shiftBriefSeen ? 1 : 0);
            PlayerPrefs.SetString(KeyLanguage, language);

            hasSavedLookSensitivity = true;
            hasSavedFieldOfView = true;

            PlayerPrefs.Save();
        }

        /// <summary>
        /// Pushes the current values at Unity. Quality first: <c>SetQualityLevel</c> also writes
        /// <c>vSyncCount</c> from the quality asset, so setting vsync before it would be silently
        /// undone.
        /// </summary>
        public static void Apply()
        {
            if (ClampQuality(qualityLevel) != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(ClampQuality(qualityLevel), true);

            QualitySettings.vSyncCount = vSync ? 1 : 0;
            AudioListener.volume = masterVolume;
            InstallLanguage();
            PushDisplay(display);

            Raise();
        }

        /// <summary>
        /// Back to the authored defaults. Bindings are not touched — they are the other agent's blob
        /// and reachable only with the action asset in hand; the screen calls
        /// <see cref="KeyBindings.ResetAll"/> alongside this.
        /// <para>
        /// The display mode resets to whatever is on screen right now rather than to a stored
        /// "native" one, for the same reason the commit dance exists: the only mode certain to be
        /// displayable is the one the player is looking at.
        /// </para>
        /// </summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[]
                     {
                         KeyWidth, KeyHeight, KeyRefreshHz, KeyScreenMode, KeyVSync, KeyQuality,
                         KeyFieldOfView, KeyMaster, KeyEffects, KeyAmbience, KeyVoice,
                         KeySensitivity, KeyInvertLook, KeyHeadBob, KeyHeadBobScale,
                         KeyCameraShake, KeyShiftBrief, KeyLanguage
                     })
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();

            committedDisplay = CurrentScreenMode();
            display = committedDisplay;

            vSync = true;
            qualityLevel = ClampQuality(QualitySettings.GetQualityLevel());

            fieldOfView = Mathf.Clamp(defaultFieldOfView, MinFieldOfView, MaxFieldOfView);
            hasSavedFieldOfView = false;

            masterVolume = 1f;
            effectsVolume = 1f;
            ambienceVolume = 1f;
            voiceVolume = 1f;

            lookSensitivity = Mathf.Clamp(defaultLookSensitivity,
                MinLookSensitivity, MaxLookSensitivity);
            hasSavedLookSensitivity = false;

            invertLook = false;
            headBobScale = 1f;
            cameraShakeScale = 1f;

            // Deliberately part of the reset: someone who has wiped their profile is being handed a
            // first run, and the shift brief is what a first run is owed (#47).
            shiftBriefSeen = false;

            // Back to the system language rather than to English — a reset returns the profile to
            // how it arrived, and for a German player that was German.
            language = SystemLanguageCode();

            Apply();
        }

        // -- Authored defaults -------------------------------------------------------------------

        /// <summary>
        /// Adopt a component's authored value as the default, but only while nothing is saved.
        /// <para>
        /// The authored numbers live on <c>PlayerController</c> and <c>PlayerHeadMotion</c> where
        /// they can be tuned against the actual walk cycle, and this is the door they come through.
        /// A saved profile always wins: a designer retuning the default must not silently reach into
        /// the sensitivity of every player who already set their own.
        /// </para>
        /// </summary>
        public static void SeedDefaultLookSensitivity(float authoredDefault)
        {
            defaultLookSensitivity = Mathf.Clamp(authoredDefault,
                MinLookSensitivity, MaxLookSensitivity);

            if (hasSavedLookSensitivity) return;

            if (Mathf.Approximately(lookSensitivity, defaultLookSensitivity)) return;

            lookSensitivity = defaultLookSensitivity;
            Raise();
        }

        /// <inheritdoc cref="SeedDefaultLookSensitivity"/>
        public static void SeedDefaultFieldOfView(float authoredDefault)
        {
            defaultFieldOfView = Mathf.Clamp(authoredDefault, MinFieldOfView, MaxFieldOfView);

            if (hasSavedFieldOfView) return;

            if (Mathf.Approximately(fieldOfView, defaultFieldOfView)) return;

            fieldOfView = defaultFieldOfView;
            Raise();
        }

        /// <summary>The value a "reset this row" affordance should show, after seeding.</summary>
        public static float DefaultLookSensitivity => defaultLookSensitivity;

        public static float DefaultFieldOfView => defaultFieldOfView;

        // -- Display -----------------------------------------------------------------------------

        /// <summary>The mode currently on screen, committed or not.</summary>
        public static DisplayMode Display => display;

        /// <summary>The last mode the player confirmed. <see cref="RevertDisplay"/> returns here.</summary>
        public static DisplayMode CommittedDisplay => committedDisplay;

        /// <summary>True while a mode is on screen that nobody has confirmed yet.</summary>
        public static bool DisplayAwaitingConfirmation => !display.Equals(committedDisplay);

        /// <summary>
        /// Show this mode now. Deliberately does not persist: if it turns out the monitor cannot
        /// display it, the game is unusable and the next launch must not repeat the mistake.
        /// </summary>
        public static void ApplyDisplay(DisplayMode mode)
        {
            if (!mode.IsValid) return;

            display = mode;
            PushDisplay(mode);
            Raise();
        }

        /// <summary>The player confirmed they can see this. Now it is safe to write down.</summary>
        public static void CommitDisplay()
        {
            if (!display.IsValid) return;

            committedDisplay = display;

            PlayerPrefs.SetInt(KeyWidth, committedDisplay.Width);
            PlayerPrefs.SetInt(KeyHeight, committedDisplay.Height);
            PlayerPrefs.SetInt(KeyRefreshHz, committedDisplay.RefreshHz);
            PlayerPrefs.SetInt(KeyScreenMode, (int)committedDisplay.Mode);
            PlayerPrefs.Save();

            Raise();
        }

        /// <summary>Nobody confirmed. Put back the mode that was demonstrably visible.</summary>
        public static void RevertDisplay()
        {
            if (!committedDisplay.IsValid || display.Equals(committedDisplay)) return;

            display = committedDisplay;
            PushDisplay(committedDisplay);
            Raise();
        }

        /// <summary>
        /// Every size the platform reports, deduped and ascending, carried at the window mode
        /// currently selected. Deduped because <see cref="Screen.resolutions"/> lists one entry per
        /// resolution <i>per refresh rate</i> and reports rates as ratios, so an unfiltered list is
        /// the same handful of sizes over and over with rates that differ in the third decimal.
        /// </summary>
        public static IReadOnlyList<DisplayMode> AvailableModes
        {
            get
            {
                if (availableModes != null && availableModesBuiltFor == display.Mode)
                    return availableModes;

                availableModes = BuildAvailableModes(display.Mode);
                availableModesBuiltFor = display.Mode;
                return availableModes;
            }
        }

        public static bool VSync
        {
            get => vSync;
            set
            {
                if (vSync == value) return;
                vSync = value;
                QualitySettings.vSyncCount = value ? 1 : 0;
                PlayerPrefs.SetInt(KeyVSync, value ? 1 : 0);
                Raise();
            }
        }

        public static int QualityLevel
        {
            get => qualityLevel;
            set
            {
                int clamped = ClampQuality(value);
                if (qualityLevel == clamped) return;
                qualityLevel = clamped;

                // applyExpensiveChanges: the point of the setting is the expensive part.
                QualitySettings.SetQualityLevel(clamped, true);

                // SetQualityLevel rewrites vSyncCount from the quality asset, so put ours back.
                QualitySettings.vSyncCount = vSync ? 1 : 0;

                PlayerPrefs.SetInt(KeyQuality, clamped);
                Raise();
            }
        }

        /// <summary>Human-readable quality level names, index-aligned with <see cref="QualityLevel"/>.</summary>
        public static IReadOnlyList<string> QualityLevels =>
            qualityLevels ??= QualitySettings.names ?? Array.Empty<string>();

        /// <summary>
        /// Vertical FOV in degrees. Read by <c>PlayerHeadMotion</c> as the base the sprint kick
        /// modulates, so nothing here writes at a camera directly — there may be several, and only
        /// the owner's is live.
        /// </summary>
        public static float FieldOfView
        {
            get => fieldOfView;
            set
            {
                float clamped = Mathf.Clamp(value, MinFieldOfView, MaxFieldOfView);
                if (Mathf.Approximately(fieldOfView, clamped)) return;
                fieldOfView = clamped;
                hasSavedFieldOfView = true;
                PlayerPrefs.SetFloat(KeyFieldOfView, clamped);
                Raise();
            }
        }

        // -- Audio -------------------------------------------------------------------------------

        /// <summary>The one volume that reaches Unity today, as <see cref="AudioListener.volume"/>.</summary>
        public static float MasterVolume
        {
            get => masterVolume;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(masterVolume, clamped)) return;
                masterVolume = clamped;
                AudioListener.volume = clamped;
                PlayerPrefs.SetFloat(KeyMaster, clamped);
                Raise();
            }
        }

        /// <summary>
        /// Everything the player or a machine causes. Reaches Unity through <c>AudioBus</c>, which
        /// listens to <see cref="Changed"/> and re-applies the multiplier to every registered source
        /// — so nothing is pushed from here, and this stays the class that only stores and raises.
        /// <para>
        /// There is deliberately no <c>AudioMixer</c> behind it. Unity exposes no scripting API that
        /// creates one, so a mixer would have to be authored by hand and committed as opaque YAML;
        /// see <c>AudioBus</c> for why a four-entry gain table does not earn that exception.
        /// </para>
        /// </summary>
        public static float EffectsVolume
        {
            get => effectsVolume;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(effectsVolume, clamped)) return;
                effectsVolume = clamped;
                PlayerPrefs.SetFloat(KeyEffects, clamped);
                Raise();
            }
        }

        /// <inheritdoc cref="EffectsVolume"/>
        public static float AmbienceVolume
        {
            get => ambienceVolume;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(ambienceVolume, clamped)) return;
                ambienceVolume = clamped;
                PlayerPrefs.SetFloat(KeyAmbience, clamped);
                Raise();
            }
        }

        /// <summary>
        /// Voice chat playback gain. Stored here and read by the netcode layer, never pushed from
        /// here: <c>Residue.Gameplay</c> cannot reference <c>Residue.Net</c>, and that direction is
        /// the boundary that keeps ground truth off a serializer. A settings class is not worth
        /// puncturing it for, so <c>Residue.Net</c> reads this instead.
        /// </summary>
        public static float VoiceVolume
        {
            get => voiceVolume;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(voiceVolume, clamped)) return;
                voiceVolume = clamped;
                PlayerPrefs.SetFloat(KeyVoice, clamped);
                Raise();
            }
        }

        // -- Controls ----------------------------------------------------------------------------

        /// <summary>
        /// Degrees of yaw per unit of pointer delta. Read every frame by <c>PlayerController</c>
        /// rather than cached, so dragging the slider turns the room under the player's own hand —
        /// which is the only way anyone can tell whether the number is right.
        /// </summary>
        public static float LookSensitivity
        {
            get => lookSensitivity;
            set
            {
                float clamped = Mathf.Clamp(value, MinLookSensitivity, MaxLookSensitivity);
                if (Mathf.Approximately(lookSensitivity, clamped)) return;
                lookSensitivity = clamped;
                hasSavedLookSensitivity = true;
                PlayerPrefs.SetFloat(KeySensitivity, clamped);
                Raise();
            }
        }

        public static bool InvertLook
        {
            get => invertLook;
            set
            {
                if (invertLook == value) return;
                invertLook = value;
                PlayerPrefs.SetInt(KeyInvertLook, value ? 1 : 0);
                Raise();
            }
        }

        /// <summary>
        /// Head bob off is an accessibility switch, not a preference: the bob is the usual trigger
        /// for motion sickness in a game whose whole loop is reading small numbers off a display
        /// while walking between benches.
        /// </summary>
        /// <para>
        /// A scale rather than a switch since #54, because "off or full" is the wrong shape for this
        /// control: the players who need it are spread across a range, and the ones who can play with
        /// a third of the motion should not have to choose between queasy and floating. Zero is still
        /// exactly off — see <c>PlayerHeadMotion</c>, where the amplitude reaches zero rather than the
        /// code taking an early return, so the rig settles level instead of freezing mid-step.
        /// </para>
        /// <summary>
        /// Read the stored head bob scale, falling back to the boolean this setting used to be (#54).
        /// <para>
        /// A profile that only ever saw the switch still has the old key and no new one, so its
        /// answer is honoured. Someone who deliberately turned the bob off must not get it back on
        /// when they next update — for an accessibility setting that is the one migration failure
        /// that actually hurts, because it hands a motion-sickness trigger back to the person who
        /// went looking for the switch, at the moment they least expect it.
        /// </para>
        /// Public so <c>MotionComfortTests</c> can exercise the real migration rather than a copy of
        /// it pasted into a test, which would pass for ever regardless of what this file did. It is
        /// also why <see cref="Load"/> calls it instead of inlining the fallback.
        /// </summary>
        public static float ReadHeadBobScale() =>
            Mathf.Clamp01(PlayerPrefs.GetFloat(KeyHeadBobScale,
                PlayerPrefs.GetInt(KeyHeadBob, 1) != 0 ? 1f : 0f));

        /// <summary>The PlayerPrefs keys behind <see cref="ReadHeadBobScale"/>, for that test.</summary>
        public static string HeadBobScaleKey => KeyHeadBobScale;

        /// <inheritdoc cref="HeadBobScaleKey"/>
        public static string LegacyHeadBobKey => KeyHeadBob;

        public static float HeadBobScale
        {
            get => headBobScale;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(headBobScale, clamped)) return;
                headBobScale = clamped;
                PlayerPrefs.SetFloat(KeyHeadBobScale, clamped);
                Raise();
            }
        }

        /// <summary>
        /// The landing dip and the sprint field-of-view kick — camera motion the player did not ask
        /// for, as opposed to the motion their own input caused.
        /// <para>
        /// Both are on one scale because both are the same complaint: the view moves on its own. A
        /// player who turns the landing dip off and is still handed a lens punch every time they run
        /// has not been given the off switch #54 asks for, and would reasonably conclude the setting
        /// does not work.
        /// </para>
        /// Zero is exactly zero, not "reduced". That is the issue's own wording and it is the whole
        /// point: someone who needs this needs it entirely.
        /// </summary>
        public static float CameraShakeScale
        {
            get => cameraShakeScale;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(cameraShakeScale, clamped)) return;
                cameraShakeScale = clamped;
                PlayerPrefs.SetFloat(KeyCameraShake, clamped);
                Raise();
            }
        }

        // -- Onboarding --------------------------------------------------------------------------

        /// <summary>
        /// Whether this player has put the shift brief away at least once (#47).
        /// <para>
        /// Local and per-profile, exactly like the comfort settings above, and for the same reason:
        /// onboarding is a fact about a person, not about a lab. Nothing here goes on the wire, so a
        /// veteran hosting for a newcomer sees no card and the newcomer's card pauses nobody.
        /// </para>
        /// <para>
        /// False is the honest default for a key that does not exist yet, which is what makes a fresh
        /// install and a wiped profile both count as a first run. It is written when the player
        /// <i>dismisses</i> the card rather than when it first appears: a brief nobody acknowledged
        /// has not been read, and quitting mid-sentence should not cost it.
        /// </para>
        /// </summary>
        public static bool ShiftBriefSeen
        {
            get => shiftBriefSeen;
            set
            {
                if (shiftBriefSeen == value) return;
                shiftBriefSeen = value;
                PlayerPrefs.SetInt(KeyShiftBrief, value ? 1 : 0);
                Raise();
            }
        }

        /// <summary>
        /// The stored answer, defaulted. Public and called by <see cref="Load"/> rather than inlined
        /// so <c>OnboardingTests</c> exercises the real default instead of a copy of it — the same
        /// arrangement, and the same reason, as <see cref="ReadHeadBobScale"/>.
        /// </summary>
        public static bool ReadShiftBriefSeen() => PlayerPrefs.GetInt(KeyShiftBrief, 0) != 0;

        /// <summary>The PlayerPrefs key behind <see cref="ReadShiftBriefSeen"/>, for that test.</summary>
        public static string ShiftBriefKey => KeyShiftBrief;

        // -- Language ----------------------------------------------------------------------------

        /// <summary>
        /// The chosen language as a BCP 47 code, or empty for English (#55).
        /// <para>
        /// This is the one setting that changes text already on screen, so the screen showing it has
        /// to redraw itself — <c>SettingsPanel.Refresh</c> is called from <see cref="Changed"/>'s
        /// existing path, and every other panel rebuilds on show. A language that only took effect
        /// after a restart would be the Apply button this class exists to avoid.
        /// </para>
        /// </summary>
        public static string Language
        {
            get => language;
            set
            {
                string chosen = value ?? string.Empty;
                if (string.Equals(language, chosen, StringComparison.Ordinal)) return;

                language = chosen;
                PlayerPrefs.SetString(KeyLanguage, chosen);
                InstallLanguage();
                Raise();
            }
        }

        /// <summary>
        /// Push <see cref="Language"/> at <see cref="Loc"/>. The only place a translation is
        /// installed, so there is one line to read when asking what language the game is in.
        /// </summary>
        private static void InstallLanguage()
        {
            if (string.Equals(language, German.Code, StringComparison.Ordinal))
                Loc.Use(German.Code, German.Table);
            else
                Loc.UseEnglish();
        }

        /// <summary>
        /// What the machine is set to, mapped to a language actually shipped. Anything else is
        /// English, because offering a player their own language and then not having it is worse than
        /// not offering it.
        /// </summary>
        private static string SystemLanguageCode() =>
            Application.systemLanguage == SystemLanguage.German ? German.Code : string.Empty;

        // -- Internals ---------------------------------------------------------------------------

        private static void Raise() => Changed?.Invoke();

        private static int ClampQuality(int level)
        {
            int count = QualityLevels.Count;
            return count <= 0 ? 0 : Mathf.Clamp(level, 0, count - 1);
        }

        private static DisplayMode CurrentScreenMode() => new(
            Screen.width,
            Screen.height,
            RoundHz(Screen.currentResolution.refreshRateRatio),
            Screen.fullScreenMode);

        private static int RoundHz(RefreshRate rate) =>
            rate.denominator == 0 ? 0 : Mathf.RoundToInt((float)rate.value);

        /// <summary>
        /// Only touches <see cref="Screen"/> when something actually differs. Calling
        /// <c>SetResolution</c> with the values already in force still costs a mode switch on some
        /// drivers — a black flash every time a slider on an unrelated tab moves.
        /// </summary>
        private static void PushDisplay(DisplayMode mode)
        {
            if (!mode.IsValid) return;

            bool sameSize = Screen.width == mode.Width && Screen.height == mode.Height;
            bool sameMode = Screen.fullScreenMode == mode.Mode;
            bool sameRate = mode.RefreshHz <= 0 ||
                            RoundHz(Screen.currentResolution.refreshRateRatio) == mode.RefreshHz;

            if (sameSize && sameMode && sameRate) return;

            if (mode.RefreshHz > 0)
            {
                Screen.SetResolution(mode.Width, mode.Height, mode.Mode,
                    new RefreshRate { numerator = (uint)mode.RefreshHz, denominator = 1u });
            }
            else
            {
                Screen.SetResolution(mode.Width, mode.Height, mode.Mode);
            }
        }

        private static List<DisplayMode> BuildAvailableModes(FullScreenMode windowMode)
        {
            var seen = new HashSet<long>();
            var result = new List<DisplayMode>();

            var resolutions = Screen.resolutions;
            if (resolutions != null)
            {
                foreach (var r in resolutions)
                {
                    var mode = new DisplayMode(r.width, r.height, RoundHz(r.refreshRateRatio), windowMode);
                    if (!mode.IsValid) continue;
                    if (seen.Add(Key(mode))) result.Add(mode);
                }
            }

            // A platform that reports nothing (and the Editor, on some backends) still has to offer
            // the player the mode they are already in, or the dropdown is empty and unusable.
            var current = CurrentScreenMode().WithMode(windowMode);
            if (current.IsValid && seen.Add(Key(current))) result.Add(current);

            result.Sort(static (a, b) =>
            {
                int byWidth = a.Width.CompareTo(b.Width);
                if (byWidth != 0) return byWidth;
                int byHeight = a.Height.CompareTo(b.Height);
                return byHeight != 0 ? byHeight : a.RefreshHz.CompareTo(b.RefreshHz);
            });

            return result;

            static long Key(DisplayMode m) =>
                ((long)m.Width << 42) | ((long)m.Height << 21) | (uint)m.RefreshHz;
        }
    }
}
