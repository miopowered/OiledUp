using System;
using System.Collections.Generic;
using Residue.Data;
using Residue.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Residue.Gameplay.UI
{
    /// <summary>
    /// The settings screen's body: three tabs over <see cref="GameSettings"/> and
    /// <see cref="KeyBindings"/> (#43, #45). Presentation only — it reads and writes the model and
    /// owns nothing else. Every value it changes is already applied and already persisted by the
    /// time the setter returns, so there is no Apply button here and nothing to flush on the way
    /// out.
    /// <para>
    /// <b>A plain class rather than a MonoBehaviour, built once and shown twice.</b> #43 needs the
    /// same settings reachable from the main menu and from the pause menu, and in this project both
    /// of those live on the same <c>DontDestroyOnLoad</c> object. Rebuilding the tree per scene
    /// would mean re-enumerating resolutions and re-walking the action asset every time somebody
    /// pauses, and — worse — it would throw away an in-flight rebind or a running revert timer at
    /// the exact moment a scene load happens to land. So the host builds one instance, keeps
    /// <see cref="Root"/> for the process, and parents or shows it wherever it is needed;
    /// <see cref="Refresh"/> re-reads the model and <see cref="Cancel"/> tidies up on hide.
    /// </para>
    /// <para>
    /// It deliberately does <b>not</b> subscribe to <see cref="GameSettings.Changed"/>. That is a
    /// static event and this object outlives every scene, so a subscription here is a reference
    /// nothing ever drops. The host calls <see cref="Refresh"/> when it shows the panel, which is
    /// the only moment a value can have changed behind the panel's back.
    /// </para>
    /// <para>
    /// The revert timer runs on <c>Root.schedule</c> and never on <c>Time.deltaTime</c>: the pause
    /// menu sets <c>Time.timeScale</c> to zero, and a confirmation countdown that stops with it is a
    /// soft-lock of exactly the kind the countdown exists to prevent. The scheduler is real-time and
    /// keeps ticking. It does stop if the host <i>removes</i> <see cref="Root"/> from the panel
    /// rather than hiding it, which is the other reason <see cref="Cancel"/> resolves the prompt
    /// instead of leaving it pending.
    /// </para>
    /// </summary>
    public sealed class SettingsPanel
    {
        /// <summary>
        /// How long an unconfirmed display mode is allowed to stay on screen. Long enough to find a
        /// button on a monitor that is still resyncing, short enough that a player looking at a
        /// black screen does not conclude the game has hung.
        /// </summary>
        private const int ConfirmSeconds = 10;

        /// <summary>
        /// A rebind that nobody answers cancels itself. Without this, a player who walked away —
        /// or who started a rebind and then reached for the mouse, which is excluded — is left with
        /// an operation that quietly eats the next keypress in the whole game.
        /// </summary>
        private const float RebindTimeoutSeconds = 8f;

        /// <summary>
        /// The wide card, shared with the credits screen through <see cref="UiKit"/> rather than
        /// restated here. Two panel widths exist in the whole shell; a third picked locally is how a
        /// front end ends up looking like several.
        /// </summary>
        private const float PanelWidth = UiKit.PanelWidthWide;

        /// <summary>
        /// Resolved once, at static construction, rather than per show. The tab strip is built once
        /// too — see the type doc — so there is no moment at which a re-read would reach it anyway.
        /// </summary>
        private static readonly string[] TabNames =
        {
            MenuStrings.TabDisplay.Text,
            MenuStrings.TabAudio.Text,
            MenuStrings.TabControls.Text
        };

        /// <summary>
        /// The window modes always offered. <c>MaximizedWindow</c> is macOS only, so it is appended
        /// only when the player is already in it — a dropdown that cannot describe the mode you are
        /// looking at is worse than one that is short.
        /// </summary>
        private static readonly FullScreenMode[] BaseWindowModes =
        {
            FullScreenMode.FullScreenWindow,
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.Windowed
        };

        private readonly InputActionAsset asset;
        private readonly Action back;

        // -- Display ---------------------------------------------------------------------------------

        private readonly DropdownField languageField;
        private readonly DropdownField resolutionField;

        /// <summary>
        /// The languages that actually ship, in the order they are offered. English is empty rather
        /// than "en" because it is the built-in table and not an override — see <see cref="Loc"/>.
        /// </summary>
        private static readonly (string Code, string Endonym)[] Languages =
        {
            (string.Empty, "English"),
            (German.Code, German.EndonymLabel)
        };
        private readonly DropdownField windowModeField;
        private readonly Toggle vSyncField;
        private readonly DropdownField qualityField;
        private readonly Slider fieldOfViewField;

        private readonly List<DisplayMode> resolutionOptions = new();
        private readonly List<string> resolutionChoices = new();
        private readonly List<FullScreenMode> windowModeOptions = new();
        private readonly List<string> windowModeChoices = new();

        private readonly VisualElement confirmPrompt;
        private readonly Label confirmLabel;
        private IVisualElementScheduledItem confirmTicker;
        private int confirmSecondsLeft;

        // -- Audio -----------------------------------------------------------------------------------

        private readonly Slider masterField;
        private readonly Slider effectsField;
        private readonly Slider ambienceField;
        private readonly Slider voiceField;

        // -- Controls --------------------------------------------------------------------------------

        private readonly Slider sensitivityField;
        private readonly Toggle invertLookField;
        private readonly Slider headBobField;
        private readonly Slider cameraShakeField;

        private readonly List<RebindRow> rebindRows = new();
        private readonly Button resetAllButton;
        private readonly Label bindingStatus;

        private readonly VisualElement[] pages;
        private int currentTab;

        /// <summary>
        /// True while <see cref="Refresh"/> is pushing model values into controls. Every control
        /// here is set with <c>SetValueWithoutNotify</c>, but a <see cref="DropdownField"/> also
        /// notifies when its <c>choices</c> list is replaced, so the guard is what actually stops a
        /// refresh from writing the model back at itself.
        /// </summary>
        private bool applying;

        private InputActionRebindingExtensions.RebindingOperation rebind;
        private RebindRow rebindingRow;
        private string rebindPreviousOverride;
        private bool rebindReenableAction;

        /// <param name="inputAsset">
        /// May be null. A host with no action asset to offer still gets the sensitivity controls;
        /// the binding list is replaced by a sentence saying why it is not there.
        /// </param>
        /// <param name="back">Invoked by the BACK button. Null hides the button entirely.</param>
        public SettingsPanel(InputActionAsset inputAsset, Action back)
        {
            asset = inputAsset;
            this.back = back;

            Root = UiKit.Panel(PanelWidth);

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Heading(MenuStrings.SettingsHeading));

            var body = new VisualElement();

            var display = BuildDisplayPage(out languageField, out resolutionField, out windowModeField,
                out vSyncField, out qualityField, out fieldOfViewField);
            var audio = BuildAudioPage(out masterField, out effectsField, out ambienceField,
                out voiceField);
            var controls = BuildControlsPage(out sensitivityField, out invertLookField,
                out headBobField, out cameraShakeField, out resetAllButton, out bindingStatus);

            pages = new[] { display, audio, controls };
            foreach (var page in pages) body.Add(page);

            column.Add(UiKit.Tabs(TabNames, 0, ShowPage));
            column.Add(body);

            confirmPrompt = BuildConfirmPrompt(out confirmLabel);
            column.Add(confirmPrompt);

            if (back != null)
            {
                column.Add(UiKit.Divider());

                var footer = UiKit.Row();
                footer.Add(UiKit.Spacer());
                footer.Add(UiKit.QuietButton(MenuStrings.Back, () => this.back?.Invoke()));
                column.Add(footer);
            }

            ShowPage(0);
            Refresh();
        }

        /// <summary>The tree to parent. Built once, in the constructor, and refreshed in place.</summary>
        public VisualElement Root { get; }

        /// <summary>
        /// Re-read every value from <see cref="GameSettings"/> and every key from
        /// <see cref="KeyBindings"/>. Call this whenever the panel becomes visible: a setting can
        /// move while the panel is hidden — FOV and look sensitivity are both seeded from the
        /// player prefab as it spawns — and a stale slider is a setting the player will "fix" back
        /// to a value it already had.
        /// </summary>
        public void Refresh()
        {
            applying = true;
            try
            {
                RefreshDisplay();
                RefreshAudio();
                RefreshControls();
            }
            finally
            {
                applying = false;
            }

            RefreshConfirmPrompt();
        }

        /// <summary>
        /// The screen is being hidden. Aborts an in-flight rebind and resolves a pending display
        /// confirmation by reverting it.
        /// <para>
        /// Both halves are about state that outlives the panel being on screen. A rebind operation
        /// left running consumes the next keypress anywhere in the game — the player closes settings
        /// and their first step forward is silently swallowed. And an unconfirmed display mode has,
        /// by definition, not been shown to work; leaving it in force because the player walked away
        /// from the prompt is the soft-lock this whole dance exists to avoid, so nobody-answered is
        /// resolved the same way here as it is on expiry.
        /// </para>
        /// </summary>
        public void Cancel()
        {
            AbortRebind();

            if (GameSettings.DisplayAwaitingConfirmation) GameSettings.RevertDisplay();

            StopConfirmTicker();
            RefreshConfirmPrompt();
        }

        // -- Tabs ----------------------------------------------------------------------------------

        private void ShowPage(int index)
        {
            currentTab = Mathf.Clamp(index, 0, pages.Length - 1);

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].style.display = i == currentTab ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // A player who arrived on a gamepad has no cursor to look for, so a tab that changes
            // nothing about where focus is has simply not responded to them.
            UiKit.FocusFirst(pages[currentTab]);
        }

        // -- Language ------------------------------------------------------------------------------

        private static List<string> LanguageChoices()
        {
            var choices = new List<string>(Languages.Length);
            foreach (var (_, endonym) in Languages) choices.Add(endonym);
            return choices;
        }

        private static int LanguageIndex()
        {
            for (int i = 0; i < Languages.Length; i++)
                if (Languages[i].Code == GameSettings.Language) return i;

            // A profile naming a language this build no longer ships reads as English, which is what
            // Loc falls back to anyway — so the dropdown agrees with the screen behind it.
            return 0;
        }

        /// <summary>
        /// Change language.
        ///
        /// <para>
        /// <b>This screen does not relabel itself, and that is a known limit rather than an
        /// oversight.</b> Every panel in the shell resolves its captions through <see cref="LocKey"/>
        /// once, at construction, and this one is deliberately built a single time and kept for the
        /// process — see the type doc for why, which is that rebuilding it would throw away an
        /// in-flight rebind or a running revert timer. So the words here change when the panel is next
        /// built, not on the frame the dropdown moves.
        /// </para>
        ///
        /// <para>
        /// Everything drawn fresh does follow immediately: prompts, instrument screens, the terminal,
        /// refusals and the reference books all resolve per draw. The visible symptom is confined to
        /// captions already on screen, which is why <see cref="MenuStrings.LanguageNote"/> says so
        /// under the dropdown rather than leaving the player to wonder whether it worked.
        /// </para>
        ///
        /// <para>
        /// Fixing it properly means <see cref="UiKit"/> holding the <see cref="LocKey"/> behind every
        /// caption so a tree can be re-resolved in place, which is a change to every widget signature
        /// in the kit and wants doing on its own rather than inside a translation.
        /// </para>
        /// </summary>
        private void ChooseLanguage(int index)
        {
            if (applying || index < 0 || index >= Languages.Length) return;

            GameSettings.Language = Languages[index].Code;
        }

        // -- Display -------------------------------------------------------------------------------

        private VisualElement BuildDisplayPage(out DropdownField language, out DropdownField resolution,
                                               out DropdownField window, out Toggle vsync,
                                               out DropdownField quality, out Slider fov)
        {
            var page = UiKit.Column();

            // First on the first tab, because it is the setting a player who cannot read the rest of
            // this screen is looking for. Every language is listed in itself — a player who needs
            // "Deutsch" cannot be expected to find it under "German".
            language = UiKit.ChoiceField(MenuStrings.Language, LanguageChoices(),
                LanguageIndex(), ChooseLanguage);
            page.Add(UiKit.RowFor(language));
            page.Add(UiKit.Hint(MenuStrings.LanguageNote));

            page.Add(UiKit.Divider());

            resolution = UiKit.ChoiceField(MenuStrings.Resolution, new List<string>(), 0,
                ChooseResolution);
            page.Add(UiKit.RowFor(resolution));

            window = UiKit.ChoiceField(MenuStrings.WindowMode, new List<string>(), 0,
                ChooseWindowMode);
            page.Add(UiKit.RowFor(window));

            page.Add(UiKit.Hint(MenuStrings.DisplayNote));

            vsync = UiKit.ToggleField(MenuStrings.VerticalSync, true,
                value => { if (!applying) GameSettings.VSync = value; });
            page.Add(UiKit.RowFor(vsync));

            quality = UiKit.ChoiceField(MenuStrings.Detail, new List<string>(), 0,
                index => { if (!applying) GameSettings.QualityLevel = index; });
            page.Add(UiKit.RowFor(quality));

            // The choices in both dropdowns are left alone. A resolution is "1920 x 1080" and the
            // detail levels are Unity's own quality-level names — numbers and ids, not sentences.
            fov = UiKit.SliderField(MenuStrings.FieldOfView,
                GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView,
                GameSettings.FieldOfView,
                value => { if (!applying) GameSettings.FieldOfView = value; },
                value => $"{Mathf.RoundToInt(value)}°");
            page.Add(UiKit.RowFor(fov));

            return page;
        }

        private void ChooseResolution(int index)
        {
            if (applying) return;
            if (index < 0 || index >= resolutionOptions.Count) return;

            var chosen = resolutionOptions[index].WithMode(GameSettings.Display.Mode);
            if (chosen.Equals(GameSettings.Display)) return;

            GameSettings.ApplyDisplay(chosen);
            BeginConfirmation();
        }

        private void ChooseWindowMode(int index)
        {
            if (applying) return;
            if (index < 0 || index >= windowModeOptions.Count) return;

            var chosen = GameSettings.Display.WithMode(windowModeOptions[index]);
            if (chosen.Equals(GameSettings.Display)) return;

            GameSettings.ApplyDisplay(chosen);
            BeginConfirmation();
        }

        private void RefreshDisplay()
        {
            // The dropdown, not the captions around it — see ChooseLanguage for why those wait. This
            // still matters: the panel is kept for the process, so without it a language changed from
            // the pause menu would show the old choice when settings is next opened from the title.
            languageField.SetValueWithoutNotify(LanguageChoices()[LanguageIndex()]);

            var current = GameSettings.Display;

            resolutionOptions.Clear();
            resolutionChoices.Clear();
            foreach (var mode in GameSettings.AvailableModes)
            {
                resolutionOptions.Add(mode);
                resolutionChoices.Add(mode.Label);
            }

            int selected = -1;
            for (int i = 0; i < resolutionOptions.Count; i++)
            {
                if (!resolutionOptions[i].SameResolution(current)) continue;
                selected = i;
                break;
            }

            // A fresh list every time: a dropdown handed the same instance back may decide nothing
            // changed and keep showing the previous window mode's resolutions.
            resolutionField.choices = new List<string>(resolutionChoices);
            resolutionField.SetValueWithoutNotify(
                selected >= 0 ? resolutionChoices[selected] : current.Label);

            windowModeOptions.Clear();
            windowModeChoices.Clear();
            foreach (var mode in BaseWindowModes)
            {
                windowModeOptions.Add(mode);
                windowModeChoices.Add(WindowModeLabel(mode));
            }

            if (!windowModeOptions.Contains(current.Mode))
            {
                windowModeOptions.Add(current.Mode);
                windowModeChoices.Add(WindowModeLabel(current.Mode));
            }

            windowModeField.choices = new List<string>(windowModeChoices);
            windowModeField.SetValueWithoutNotify(WindowModeLabel(current.Mode));

            vSyncField.SetValueWithoutNotify(GameSettings.VSync);

            var levels = GameSettings.QualityLevels;
            var levelChoices = new List<string>(levels.Count);
            for (int i = 0; i < levels.Count; i++) levelChoices.Add(levels[i]);

            qualityField.choices = levelChoices;
            qualityField.SetEnabled(levelChoices.Count > 0);
            if (levelChoices.Count > 0)
            {
                int level = Mathf.Clamp(GameSettings.QualityLevel, 0, levelChoices.Count - 1);
                qualityField.SetValueWithoutNotify(levelChoices[level]);
            }

            fieldOfViewField.SetValueWithoutNotify(GameSettings.FieldOfView);
        }

        private static string WindowModeLabel(FullScreenMode mode) => mode switch
        {
            FullScreenMode.ExclusiveFullScreen => MenuStrings.WindowExclusive,
            FullScreenMode.FullScreenWindow => MenuStrings.WindowBorderless,
            FullScreenMode.MaximizedWindow => MenuStrings.WindowMaximised,
            _ => MenuStrings.WindowWindowed
        };

        // -- The revert timer ----------------------------------------------------------------------

        /// <summary>
        /// Sits below the tabs rather than inside the DISPLAY page on purpose: the countdown has to
        /// stay visible and answerable if the player switches to AUDIO while it is running, and a
        /// prompt that vanishes with its tab is a prompt that expires unread.
        /// </summary>
        private VisualElement BuildConfirmPrompt(out Label label)
        {
            var holder = UiKit.Column();
            holder.style.display = DisplayStyle.None;
            holder.style.paddingTop = 12;
            holder.style.paddingBottom = 12;
            holder.style.paddingLeft = 14;
            holder.style.paddingRight = 14;
            holder.style.backgroundColor = new StyleColor(UiPalette.SurfaceRaised);
            UiKit.Round(holder);

            label = UiKit.Body(string.Empty);
            holder.Add(label);

            var buttons = UiKit.Row();
            buttons.Add(UiKit.Spacer());

            buttons.Add(UiKit.ActionButton(MenuStrings.KeepThisMode, CommitDisplayMode));
            buttons.Add(UiKit.QuietButton(MenuStrings.PutItBack, RevertDisplayMode));

            holder.Add(buttons);
            return holder;
        }

        private void BeginConfirmation()
        {
            Refresh();

            if (!GameSettings.DisplayAwaitingConfirmation) return;

            confirmSecondsLeft = ConfirmSeconds;
            PaintConfirmPrompt();

            StopConfirmTicker();

            // StartingIn, or the scheduler fires once on the very next update and the player is
            // told they have nine seconds before the first one has passed.
            confirmTicker = Root.schedule.Execute(TickConfirmation).Every(1000).StartingIn(1000);

            // Whoever is on a gamepad or a keyboard needs to reach KEEP without hunting for it, and
            // the resolution dropdown they just used is disabled anyway.
            UiKit.FocusFirst(confirmPrompt);
        }

        private void TickConfirmation()
        {
            if (!GameSettings.DisplayAwaitingConfirmation)
            {
                StopConfirmTicker();
                RefreshConfirmPrompt();
                return;
            }

            confirmSecondsLeft--;

            if (confirmSecondsLeft <= 0)
            {
                RevertDisplayMode();
                return;
            }

            PaintConfirmPrompt();
        }

        private void CommitDisplayMode()
        {
            GameSettings.CommitDisplay();
            StopConfirmTicker();
            Refresh();

            // The prompt the player was standing on has just gone away, so put them back on the tab
            // they can see rather than leaving focus on a removed element.
            UiKit.FocusFirst(pages[currentTab]);
        }

        private void RevertDisplayMode()
        {
            GameSettings.RevertDisplay();
            StopConfirmTicker();
            Refresh();
            UiKit.FocusFirst(pages[currentTab]);
        }

        private void StopConfirmTicker()
        {
            confirmTicker?.Pause();
            confirmTicker = null;
        }

        private void RefreshConfirmPrompt()
        {
            bool pending = GameSettings.DisplayAwaitingConfirmation;

            confirmPrompt.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;

            // While a mode is unconfirmed there is exactly one useful thing to do, and stacking a
            // second unconfirmed change on top of the first would leave nothing safe to revert to.
            resolutionField.SetEnabled(!pending);
            windowModeField.SetEnabled(!pending);

            if (pending) PaintConfirmPrompt();
        }

        /// <summary>
        /// The countdown sentence. Every moving part is a named argument, the seconds included: the
        /// number does not sit at the end of the sentence in every language, and a translator handed
        /// "… in " and " s." separately cannot move it (#55).
        /// <para>
        /// The window mode is no longer lowercased on the way in. Case is a property of the language
        /// — German capitalises its nouns mid-sentence — so a <c>ToLowerInvariant</c> applied to a
        /// looked-up label is a transformation of somebody else's grammar. The mode names now read
        /// as they do in the dropdown above, which is also what the player just chose from.
        /// </para>
        /// </summary>
        private void PaintConfirmPrompt()
        {
            var committed = GameSettings.CommittedDisplay;

            confirmLabel.text = MenuStrings.ConfirmDisplay.Format(
                ("mode", GameSettings.Display.Label),
                ("window", WindowModeLabel(GameSettings.Display.Mode)),
                ("previous", committed.Label),
                ("previousWindow", WindowModeLabel(committed.Mode)),
                ("seconds", confirmSecondsLeft));
        }

        // -- Audio ---------------------------------------------------------------------------------

        private VisualElement BuildAudioPage(out Slider master, out Slider effects,
                                             out Slider ambience, out Slider voice)
        {
            var page = UiKit.Column();

            master = Percent(page, MenuStrings.VolumeMaster, GameSettings.MasterVolume,
                value => GameSettings.MasterVolume = value);
            effects = Percent(page, MenuStrings.VolumeEffects, GameSettings.EffectsVolume,
                value => GameSettings.EffectsVolume = value);
            ambience = Percent(page, MenuStrings.VolumeAmbience, GameSettings.AmbienceVolume,
                value => GameSettings.AmbienceVolume = value);
            voice = Percent(page, MenuStrings.VolumeVoice, GameSettings.VoiceVolume,
                value => GameSettings.VoiceVolume = value);

            page.Add(UiKit.Hint(MenuStrings.AudioNote));

            return page;
        }

        /// <summary>
        /// A 0–100% slider over a 0–1 setting. Shared by the volumes and by #54's comfort scales,
        /// which are the same control with a different noun — and a second copy of it would be the
        /// place the two drifted apart on rounding.
        /// </summary>
        private Slider Percent(VisualElement page, string label, float value, Action<float> changed)
        {
            var slider = UiKit.SliderField(label, 0f, 100f, value * 100f,
                v => { if (!applying) changed(v / 100f); },
                v => $"{Mathf.RoundToInt(v)}%");

            page.Add(UiKit.RowFor(slider));
            return slider;
        }

        private void RefreshAudio()
        {
            masterField.SetValueWithoutNotify(GameSettings.MasterVolume * 100f);
            effectsField.SetValueWithoutNotify(GameSettings.EffectsVolume * 100f);
            ambienceField.SetValueWithoutNotify(GameSettings.AmbienceVolume * 100f);
            voiceField.SetValueWithoutNotify(GameSettings.VoiceVolume * 100f);
        }

        // -- Controls ------------------------------------------------------------------------------

        private VisualElement BuildControlsPage(out Slider sensitivity, out Toggle invert,
                                                out Slider bob, out Slider shake, out Button resetAll,
                                                out Label status)
        {
            var page = UiKit.Column();

            sensitivity = UiKit.SliderField(MenuStrings.LookSensitivity,
                GameSettings.MinLookSensitivity, GameSettings.MaxLookSensitivity,
                GameSettings.LookSensitivity,
                value => { if (!applying) GameSettings.LookSensitivity = value; },
                value => value.ToString("0.000"));
            page.Add(UiKit.RowFor(sensitivity));

            invert = UiKit.ToggleField(MenuStrings.InvertLook, false,
                value => { if (!applying) GameSettings.InvertLook = value; });
            page.Add(UiKit.RowFor(invert));

            bob = Percent(page, MenuStrings.HeadBob, GameSettings.HeadBobScale,
                value => GameSettings.HeadBobScale = value);

            shake = Percent(page, MenuStrings.CameraShake, GameSettings.CameraShakeScale,
                value => GameSettings.CameraShakeScale = value);

            page.Add(UiKit.Hint(MenuStrings.ComfortNote));

            page.Add(UiKit.Divider());

            resetAll = null;

            // Body and not Hint. This line is the only report a rebind produces — including the
            // refusal when the key is already spoken for — and Hint is documented as the size for
            // things that are never load-bearing. Say() sets its colour on every write, so it never
            // draws in Body's default ink.
            status = UiKit.Body(string.Empty);
            status.style.color = new StyleColor(UiPalette.InkFaint);

            if (asset == null)
            {
                page.Add(UiKit.Body(MenuStrings.NoBindingsHere));
                page.Add(UiKit.Hint(MenuStrings.NoBindingsHereNote));
                return page;
            }

            var bindings = KeyBindings.Bindable(asset);

            if (bindings.Count == 0)
            {
                page.Add(UiKit.Body(MenuStrings.NothingToRebind));
                page.Add(UiKit.Hint(MenuStrings.NothingToRebindNote));
                return page;
            }

            var header = UiKit.Row();
            header.Add(UiKit.Body(MenuStrings.KeyboardAndMouse));
            header.Add(UiKit.Spacer());

            // Still DangerButton, which is oxidised orange text and not a red fill — hard rule 4 has
            // no exception for a destructive control, and nothing about wording changes that.
            resetAll = UiKit.DangerButton(MenuStrings.ResetEveryKey, ResetAllBindings);
            header.Add(resetAll);
            page.Add(header);

            page.Add(UiKit.Hint(MenuStrings.RebindNote));

            bool anyHeld = false;
            var list = UiKit.Column(4f);

            foreach (var binding in bindings)
            {
                var row = new RebindRow(binding, BeginRebind, ResetBinding);
                rebindRows.Add(row);
                list.Add(row.Root);
                anyHeld |= row.IsHeld;
            }

            if (anyHeld) page.Add(UiKit.Hint(MenuStrings.HoldNote));

            var scroller = new ScrollView(ScrollViewMode.Vertical);
            scroller.style.maxHeight = UiKit.ScrollMaxHeight;
            scroller.style.flexShrink = 1f;
            scroller.Add(list);
            page.Add(scroller);

            page.Add(status);
            return page;
        }

        private void RefreshControls()
        {
            sensitivityField.SetValueWithoutNotify(GameSettings.LookSensitivity);
            invertLookField.SetValueWithoutNotify(GameSettings.InvertLook);
            headBobField.SetValueWithoutNotify(GameSettings.HeadBobScale * 100f);
            cameraShakeField.SetValueWithoutNotify(GameSettings.CameraShakeScale * 100f);

            foreach (var row in rebindRows)
            {
                // The listening row is showing a prompt, not a key. Repainting it here would wipe
                // the only thing telling the player the game is waiting on them.
                if (row == rebindingRow) continue;
                row.ShowKey();
            }
        }

        private void ResetAllBindings()
        {
            if (rebind != null) return;

            // ResetAll *clears* the overrides rather than writing the authored keys back as
            // overrides (#45). Re-saving here would defeat that: it would persist an empty override
            // set as a blob, and the next change to the authored bindings would be masked by it.
            KeyBindings.ResetAll(asset);

            RefreshControls();
            Say(MenuStrings.ResetEveryKeyDone, false);
        }

        private void ResetBinding(RebindRow row)
        {
            if (rebind != null) return;

            KeyBindings.Reset(row.Binding);
            RefreshControls();
            Say(MenuStrings.ResetKeyDone.Format(
                ("action", row.Binding.Label),
                ("key", KeyBindings.Display(row.Binding))), false);

            // The button they pressed has just hidden itself, so hand focus to the one still there
            // rather than dropping a keyboard player back at the top of the list.
            row.FocusRebind();
        }

        // -- The interactive rebind ------------------------------------------------------------------

        private void BeginRebind(RebindRow row)
        {
            if (asset == null || rebind != null) return;

            var binding = row.Binding;
            if (!binding.IsValid) return;

            // PerformInteractiveRebinding refuses an enabled action, and the action has to go back
            // the way it was found — enabling one the game had deliberately switched off would hand
            // the player a control the current context does not want them to have.
            rebindReenableAction = binding.Action.enabled;
            if (rebindReenableAction) binding.Action.Disable();

            rebindPreviousOverride = binding.Binding.overridePath;
            rebindingRow = row;

            SetRebindControlsEnabled(false);
            row.ShowListening();
            Say(MenuStrings.PressKeyFor.Format(("action", binding.Label)), false);

            rebind = binding.Action.PerformInteractiveRebinding(binding.BindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")

                // Without these the rebind finishes the instant the player breathes on the mouse:
                // the pointer is how they clicked REBIND, and it is still moving.
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")

                .WithTimeout(RebindTimeoutSeconds)
                .OnCancel(_ => FinishRebind(false))
                .OnComplete(_ => FinishRebind(true))
                .Start();
        }

        private void FinishRebind(bool completed)
        {
            var row = rebindingRow;
            var operation = rebind;

            rebind = null;
            rebindingRow = null;

            string previous = rebindPreviousOverride;
            rebindPreviousOverride = null;

            // Dispose before anything else can throw. A leaked operation stays hooked into the
            // input system and swallows the next keypress anywhere in the game, which presents as
            // "the game stopped responding" long after the player has left this screen.
            operation?.Dispose();

            if (row == null) return;

            var binding = row.Binding;

            if (rebindReenableAction && binding.IsValid) binding.Action.Enable();
            rebindReenableAction = false;

            SetRebindControlsEnabled(true);

            // Disabling the row's own button to start the rebind took focus off it. Put it back, or
            // a player on a keyboard finishes a rebind with the caret nowhere.
            row.FocusRebind();

            if (!completed)
            {
                row.ShowKey();
                Say(MenuStrings.RebindUnchanged.Format(
                    ("action", binding.Label),
                    ("key", KeyBindings.Display(binding))), false);
                return;
            }

            var live = binding.Binding;
            string path = string.IsNullOrEmpty(live.effectivePath) ? live.path : live.effectivePath;

            if (KeyBindings.Conflict(asset, binding, path, out string heldBy))
            {
                // Refuse rather than double-bind. Two actions on one key is a control scheme the
                // player did not choose and cannot see, and they meet it mid-shift with their hands
                // full.
                if (string.IsNullOrEmpty(previous))
                    binding.Action.RemoveBindingOverride(binding.BindingIndex);
                else
                    binding.Action.ApplyBindingOverride(binding.BindingIndex, previous);

                row.ShowKey();

                // Five moving parts, one sentence, and {heldBy} used twice — which is exactly what
                // named arguments buy over positional ones or a concatenation.
                Say(MenuStrings.RebindConflict.Format(
                    ("key", Readable(path)),
                    ("heldBy", heldBy),
                    ("action", binding.Label),
                    ("current", KeyBindings.Display(binding))), true);
                return;
            }

            KeyBindings.Save(asset);
            row.ShowKey();
            Say(MenuStrings.RebindDone.Format(
                ("action", binding.Label),
                ("key", KeyBindings.Display(binding))), false);
        }

        /// <summary>
        /// Tears down a rebind that is still listening. Separate from <see cref="FinishRebind"/>
        /// because <c>Cancel()</c> on a started operation calls back into <c>OnCancel</c>, and the
        /// path that runs on the way out of the screen must be correct even when it does not.
        /// </summary>
        private void AbortRebind()
        {
            var operation = rebind;
            if (operation == null) return;

            if (operation.started) operation.Cancel();

            // OnCancel normally reaches FinishRebind and clears these. Belt and braces for the case
            // where the operation was never started, or was already finished.
            if (rebind != null)
            {
                rebind = null;
                operation.Dispose();

                var row = rebindingRow;
                rebindingRow = null;
                rebindPreviousOverride = null;

                if (rebindReenableAction && row != null && row.Binding.IsValid)
                    row.Binding.Action.Enable();
                rebindReenableAction = false;

                row?.ShowKey();
                SetRebindControlsEnabled(true);
            }

            Say(string.Empty, false);
        }

        private void SetRebindControlsEnabled(bool enabled)
        {
            resetAllButton?.SetEnabled(enabled);
            foreach (var row in rebindRows) row.SetControlsEnabled(enabled);
        }

        private void Say(string text, bool refused)
        {
            if (bindingStatus == null) return;

            bindingStatus.text = text ?? string.Empty;
            bindingStatus.style.color = new StyleColor(refused ? UiPalette.Warn : UiPalette.InkFaint);
        }

        /// <summary>
        /// A key's own name, which is the input system's to give — a control path is data and never
        /// comes through a lookup. Only the stand-in for a key it could not name is a line of ours.
        /// </summary>
        private static string Readable(string path)
        {
            if (string.IsNullOrEmpty(path)) return MenuStrings.ThatKey;

            string text = InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);

            return string.IsNullOrEmpty(text) ? MenuStrings.ThatKey : text;
        }

        /// <summary>
        /// One line of the binding list. Private and nested because it is a piece of this panel's
        /// layout rather than a widget: it knows about <see cref="KeyBindings.Display"/> and about
        /// the listening state, neither of which means anything to another screen.
        /// </summary>
        private sealed class RebindRow
        {
            private readonly Label key;
            private readonly Button rebindButton;
            private readonly Button resetButton;

            internal RebindRow(BindableBinding binding, Action<RebindRow> beginRebind,
                               Action<RebindRow> reset)
            {
                Binding = binding;
                IsHeld = KeyBindings.IsHeld(binding);

                Root = UiKit.Row();
                Root.style.paddingTop = 3;
                Root.style.paddingBottom = 3;

                // The action's own name is data from the action asset; only the "(hold)" mark is a
                // line of ours, so it is a format around the name rather than a suffix glued to it.
                var label = UiKit.Body(IsHeld
                    ? MenuStrings.BindingHeld.Format(("action", binding.Label))
                    : binding.Label);
                label.style.fontSize = UiKit.LabelSize;
                label.style.color = new StyleColor(UiPalette.InkDim);

                // The same column the sliders and dropdowns above use. This row used to carry its
                // own wider one, so the CONTROLS tab had two left edges — the settings above the
                // divider started at one, the key list below it at another.
                label.style.width = UiKit.LabelColumn;
                label.style.flexShrink = 0f;
                Root.Add(label);

                key = UiKit.Body(KeyBindings.Display(binding));
                key.style.flexGrow = 1f;
                Root.Add(key);

                rebindButton = UiKit.QuietButton(MenuStrings.Rebind, () => beginRebind(this));
                Compact(rebindButton);
                Root.Add(rebindButton);

                resetButton = UiKit.QuietButton(MenuStrings.RebindDefault, () => reset(this));
                Compact(resetButton);
                Root.Add(resetButton);
            }

            internal BindableBinding Binding { get; }

            internal bool IsHeld { get; }

            internal VisualElement Root { get; }

            /// <summary>
            /// Back to showing the key, with the per-row revert present only while it would do
            /// something.
            /// <para>
            /// It is hidden with <c>visibility</c> rather than <c>display</c>: taking it out of the
            /// layout would change the width of the key column on that row alone, and the list
            /// would shuffle under the player's hand every time they rebound something. It is
            /// disabled at the same time so the invisible button is not still a tab stop.
            /// </para>
            /// </summary>
            internal void ShowKey()
            {
                key.text = KeyBindings.Display(Binding);
                key.style.color = new StyleColor(UiPalette.Ink);

                bool revertable = Binding.IsOverridden;
                resetButton.style.visibility = revertable ? Visibility.Visible : Visibility.Hidden;
                resetButton.SetEnabled(revertable);
            }

            /// <summary>The row is what the game is waiting on. Accent, not a signal colour: this is
            /// a state, not a verdict.</summary>
            internal void ShowListening()
            {
                key.text = MenuStrings.PressAKey;
                key.style.color = new StyleColor(UiPalette.Accent);
            }

            internal void SetControlsEnabled(bool enabled)
            {
                rebindButton.SetEnabled(enabled);
                resetButton.SetEnabled(enabled && Binding.IsOverridden);
            }

            /// <summary>Scheduled, not immediate: the button may have been re-enabled this frame and
            /// <c>Focus()</c> on a control that cannot yet grab focus is silently dropped.</summary>
            internal void FocusRebind() =>
                rebindButton.schedule.Execute(() => rebindButton.Focus());

            private static void Compact(Button button)
            {
                button.style.fontSize = UiKit.HintSize;
                button.style.paddingTop = 5;
                button.style.paddingBottom = 5;
                button.style.paddingLeft = 9;
                button.style.paddingRight = 9;
                button.style.flexShrink = 0f;
            }
        }
    }
}
