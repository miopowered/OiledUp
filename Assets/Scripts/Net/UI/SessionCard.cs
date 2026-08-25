using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The small corner card that outlives the menu: the join code, the voice controls, and who is
    /// speaking.
    /// <para>
    /// The join code is the thing the host reads aloud, and they will be asked for it again five
    /// minutes into the shift — so once a session exists this stays on screen rather than
    /// disappearing with the page that produced it. That is the whole reason <c>MenuScreen</c> lives
    /// on the <c>DontDestroyOnLoad</c> connect object and not in the Boot scene.
    /// </para>
    /// <para>
    /// It is <see cref="PickingMode.Ignore"/> until somebody claims the cursor, because in the lab
    /// the cursor is locked and an invisible corner of clickable UI over a first-person view eats
    /// interactions the player aimed at the room. The mic, sound and volume keys are handled by
    /// <c>VoiceChat</c> itself; the only thing that needs a pointer is the slider, which is what the
    /// V key exists for.
    /// </para>
    /// </summary>
    public sealed class SessionCard
    {
        private readonly LabConnection connection;

        private readonly Label codeLabel;
        private readonly Label voiceLabel;
        private readonly Label speakingLabel;
        private readonly VisualElement volumeRow;
        private readonly Slider volumeSlider;
        private readonly Label volumeLabel;

        public SessionCard(LabConnection connection)
        {
            this.connection = connection;

            Root = new VisualElement();
            Root.pickingMode = PickingMode.Ignore;
            Root.style.position = Position.Absolute;
            Root.style.right = 16;
            Root.style.top = 12;
            Root.style.paddingTop = 8;
            Root.style.paddingBottom = 8;
            Root.style.paddingLeft = 12;
            Root.style.paddingRight = 12;
            Root.style.backgroundColor = new StyleColor(UiPalette.Surface);
            UiKit.Round(Root, 3f);

            codeLabel = UiKit.Body(string.Empty);
            codeLabel.style.fontSize = 15;
            codeLabel.style.letterSpacing = 3;
            codeLabel.style.color = new StyleColor(ConnectPalette.Code);
            Root.Add(codeLabel);

            voiceLabel = UiKit.Hint(string.Empty);
            voiceLabel.style.fontSize = 10;
            voiceLabel.style.marginTop = 4;
            voiceLabel.style.color = new StyleColor(UiPalette.InkDim);
            Root.Add(voiceLabel);

            volumeRow = new VisualElement();
            volumeRow.style.flexDirection = FlexDirection.Row;
            volumeRow.style.alignItems = Align.Center;
            volumeRow.style.marginTop = 4;

            volumeLabel = UiKit.Hint(string.Empty);
            volumeLabel.style.width = 106;
            volumeLabel.style.fontSize = 9;
            volumeLabel.style.color = new StyleColor(UiPalette.InkDim);
            volumeRow.Add(volumeLabel);

            volumeSlider = new Slider(0f, 100f);
            volumeSlider.style.width = 110;

            // Not made focusable, and deliberately not routed through UiKit.SliderField. This card is
            // drawn over first-person play, where a tab stop the player cannot see is a control that
            // steals the caret from whatever screen actually has their attention. Volume has explicit
            // -/+ shortcuts; the slider is here for the mouse.
            volumeSlider.focusable = false;
            volumeSlider.tabIndex = -1;
            volumeSlider.RegisterValueChangedCallback(evt =>
            {
                if (this.connection != null) this.connection.Voice.SetOutputVolume(evt.newValue / 100f);
            });

            // Runtime UI navigation maps the Player action map's A/D movement onto sliders, so with
            // one focused the walking keys silently drag it. Voice volume has its own shortcuts.
            volumeSlider.RegisterCallback<NavigationMoveEvent>(
                evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);

            volumeRow.Add(volumeSlider);
            Root.Add(volumeRow);

            speakingLabel = UiKit.Body(string.Empty);
            speakingLabel.style.fontSize = 12;
            speakingLabel.style.marginTop = 3;
            speakingLabel.style.color = new StyleColor(ConnectPalette.Code);
            Root.Add(speakingLabel);
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        /// <param name="clickable">
        /// The cursor is free and belongs to us — a page is up, or the V voice controls were opened
        /// from first-person play. Anything else has to leave the pointer alone.
        /// </param>
        /// <param name="voiceControlsOpen">
        /// The player claimed the cursor with V from inside the lab, so the hint has to tell them how
        /// to give it back. Separate from <paramref name="clickable"/> because a menu page also frees
        /// the pointer, and there Escape means something else entirely.
        /// </param>
        public void Refresh(bool clickable, bool voiceControlsOpen)
        {
            if (connection == null)
            {
                Root.style.display = DisplayStyle.None;
                return;
            }

            // Only once the shift is actually running. A session existing is not enough: during the
            // lobby this card sat in the corner repeating the join code the lobby panel is already
            // showing at four times the size, with voice hints for keys that do nothing useful while
            // nobody is in the lab yet. The card exists because the code is needed *after* the menu
            // has gone away — before that it is a second, worse copy of what is on screen.
            bool live = connection.IsLive && connection.ShiftStarted;
            Root.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            Root.pickingMode = clickable ? PickingMode.Position : PickingMode.Ignore;
            if (!live) return;

            string code = connection.JoinCodeText;
            bool hosting = connection.State == ConnectState.Hosting && !string.IsNullOrEmpty(code);
            codeLabel.text = hosting ? $"JOIN CODE  {JoinCode.ForReading(code)}" : "CONNECTED";

            var voice = connection.Voice;
            voiceLabel.text = voice.IsConnected
                ? $"[M] MIC {(voice.MicrophoneMuted ? "OFF" : "ON")}   " +
                  $"[N] SOUND {(voice.OutputMuted ? "OFF" : "ON")}"
                : voice.IsConnecting ? "VOICE CONNECTING…" : voice.UnavailableText;

            volumeLabel.text = $"[-/+] VOL {Mathf.RoundToInt(voice.OutputVolume * 100f)}%  " +
                               (voiceControlsOpen ? "[V/ESC] CLOSE" : "[V] MOUSE");
            volumeSlider.SetValueWithoutNotify(voice.OutputVolume * 100f);
            volumeRow.style.display = voice.IsConnected ? DisplayStyle.Flex : DisplayStyle.None;

            string speaking = voice.SpeakingText;
            speakingLabel.text = speaking;
            speakingLabel.style.display = string.IsNullOrEmpty(speaking)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }
}
