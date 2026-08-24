using Residue.Gameplay.World;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// Host / join / single player, and the join code once there is one.
    /// <para>
    /// Built in C# with no UXML, matching <c>TerminalScreen</c> and <c>LabHud</c>. It differs from
    /// them in one way that matters: the tree is built <b>once</b> and then refreshed in place,
    /// where the terminal rebuilds wholesale. The terminal can do that because nothing on it holds
    /// focus; this screen has a text field the player is mid-way through typing into, and a rebuild
    /// on every status change would drop their caret and their characters with it.
    /// </para>
    /// <para>
    /// It also outlives the connect scene on purpose. The join code is the thing the host reads
    /// aloud, and they will be asked for it again five minutes into the shift — so once a session
    /// exists the panel collapses to a small unclickable card in the corner rather than
    /// disappearing with the menu. In-lab the cursor is locked, so the card is display-only;
    /// leaving a session mid-game belongs on a pause menu, which does not exist yet.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConnectScreen : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private LabConnection connection;

        private VisualElement root;
        private VisualElement panel;
        private VisualElement card;

        private Button hostButton;
        private Button joinButton;
        private Button soloButton;
        private Button retryButton;
        private TextField codeField;

        private Label statusLabel;
        private Label errorLabel;
        private Label identityLabel;
        private Label codeLabel;
        private Label cardLabel;
        private Label voiceLabel;
        private Label speakingLabel;
        private bool gameplayActive;

        private void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (connection == null) connection = LabConnection.Instance;
            if (connection == null) connection = FindAnyObjectByType<LabConnection>();

            root = document.rootVisualElement;
            Build();

            if (connection != null) connection.Changed += Refresh;
            if (connection != null) connection.Voice.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (connection != null) connection.Changed -= Refresh;
            if (connection != null) connection.Voice.Changed -= Refresh;
        }

        // -- Build -------------------------------------------------------------------------------------

        private void Build()
        {
            root.Clear();
            root.style.flexGrow = 1f;

            panel = BuildPanel();
            root.Add(panel);

            card = BuildCard();
            root.Add(card);
        }

        private VisualElement BuildPanel()
        {
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0;
            backdrop.style.right = 0;
            backdrop.style.top = 0;
            backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new StyleColor(ConnectPalette.Backdrop);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;

            var box = new VisualElement();
            box.style.width = 460;
            box.style.paddingTop = 26;
            box.style.paddingBottom = 26;
            box.style.paddingLeft = 26;
            box.style.paddingRight = 26;
            box.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            Round(box, 5);
            backdrop.Add(box);

            var title = new Label("OILED UP");
            title.style.fontSize = 26;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(SignalPalette.Ink);
            box.Add(title);

            var subtitle = new Label("heat-treatment oil analysis · up to 4 in the lab");
            subtitle.style.fontSize = 12;
            subtitle.style.color = new StyleColor(SignalPalette.Dim);
            subtitle.style.marginBottom = 20;
            box.Add(subtitle);

            hostButton = new Button(() => { if (connection != null) _ = connection.HostAsync(); })
            { text = "HOST A SHIFT" };
            StyleButton(hostButton, ConnectPalette.Working);
            hostButton.style.marginLeft = 0;
            hostButton.style.marginBottom = 14;
            box.Add(hostButton);

            var joinRow = new VisualElement();
            joinRow.style.flexDirection = FlexDirection.Row;
            joinRow.style.alignItems = Align.Center;

            codeField = new TextField { value = "" };
            codeField.style.flexGrow = 1f;
            codeField.style.fontSize = 18;
            codeField.style.marginRight = 6;

            // Normalise as they type, so the field shows what will actually be sent. A code read out
            // over voice arrives lowercase and a pasted one arrives with a newline; neither should
            // reach a network call, and neither should have to be fixed by the player.
            codeField.RegisterValueChangedCallback(evt =>
            {
                string tidy = JoinCode.Normalise(evt.newValue);
                if (tidy != evt.newValue) codeField.SetValueWithoutNotify(tidy);
                RefreshEnabled();
            });

            codeField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                Join();
            });

            joinRow.Add(codeField);

            joinButton = new Button(Join) { text = "JOIN" };
            StyleButton(joinButton, SignalPalette.PanelSoft);
            joinRow.Add(joinButton);
            box.Add(joinRow);

            var hint = new Label("Six letters and digits, read out by whoever is hosting.");
            hint.style.fontSize = 11;
            hint.style.color = new StyleColor(SignalPalette.Dim);
            hint.style.marginTop = 4;
            hint.style.marginBottom = 20;
            box.Add(hint);

            soloButton = new Button(() => connection?.StartSinglePlayer()) { text = "SINGLE PLAYER" };
            StyleButton(soloButton, SignalPalette.PanelSoft);
            soloButton.style.marginLeft = 0;
            box.Add(soloButton);

            var soloHint = new Label("No sign-in, no lobby, no connection. Works offline.");
            soloHint.style.fontSize = 11;
            soloHint.style.color = new StyleColor(SignalPalette.Dim);
            soloHint.style.marginTop = 4;
            box.Add(soloHint);

            // -- feedback --
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 18;
            divider.style.marginBottom = 12;
            divider.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            box.Add(divider);

            codeLabel = new Label();
            codeLabel.style.fontSize = 34;
            codeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            codeLabel.style.letterSpacing = 6;
            codeLabel.style.color = new StyleColor(ConnectPalette.Code);
            codeLabel.style.display = DisplayStyle.None;
            box.Add(codeLabel);

            statusLabel = new Label();
            statusLabel.style.fontSize = 13;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.color = new StyleColor(SignalPalette.Dim);
            box.Add(statusLabel);

            errorLabel = new Label();
            errorLabel.style.fontSize = 12;
            errorLabel.style.marginTop = 6;
            errorLabel.style.whiteSpace = WhiteSpace.Normal;
            errorLabel.style.color = new StyleColor(ConnectPalette.Fault);
            box.Add(errorLabel);

            retryButton = new Button(() =>
            {
                // The player has plugged the network back in. Without forgetting the cached
                // decision, the first failed sign-in would refuse every attempt until a restart.
                ServiceBootstrap.Forget();
                if (connection != null) _ = connection.LeaveAsync();
            })
            { text = "TRY AGAIN" };
            StyleButton(retryButton, SignalPalette.PanelSoft);
            retryButton.style.marginLeft = 0;
            retryButton.style.marginTop = 10;
            retryButton.style.display = DisplayStyle.None;
            box.Add(retryButton);

            // Which of the two windows you are looking at, when testing two instances on one
            // machine. Worth the line: -playerId is the difference between a rejoin test and
            // handing the second window the first player's hands.
            identityLabel = new Label();
            identityLabel.style.fontSize = 10;
            identityLabel.style.marginTop = 14;
            identityLabel.style.color = new StyleColor(SignalPalette.Off);
            box.Add(identityLabel);

            return backdrop;
        }

        private VisualElement BuildCard()
        {
            var holder = new VisualElement();
            holder.pickingMode = PickingMode.Ignore;
            holder.style.position = Position.Absolute;
            holder.style.right = 16;
            holder.style.top = 12;
            holder.style.paddingTop = 8;
            holder.style.paddingBottom = 8;
            holder.style.paddingLeft = 12;
            holder.style.paddingRight = 12;
            holder.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            holder.style.display = DisplayStyle.None;
            Round(holder, 3);

            cardLabel = new Label();
            cardLabel.pickingMode = PickingMode.Ignore;
            cardLabel.style.fontSize = 15;
            cardLabel.style.letterSpacing = 3;
            cardLabel.style.color = new StyleColor(ConnectPalette.Code);
            holder.Add(cardLabel);

            voiceLabel = new Label();
            voiceLabel.pickingMode = PickingMode.Ignore;
            voiceLabel.style.fontSize = 10;
            voiceLabel.style.marginTop = 4;
            voiceLabel.style.color = new StyleColor(SignalPalette.Dim);
            holder.Add(voiceLabel);

            speakingLabel = new Label();
            speakingLabel.pickingMode = PickingMode.Ignore;
            speakingLabel.style.fontSize = 12;
            speakingLabel.style.marginTop = 3;
            speakingLabel.style.color = new StyleColor(ConnectPalette.Code);
            holder.Add(speakingLabel);

            return holder;
        }

        private void Join()
        {
            if (connection == null) return;
            _ = connection.JoinAsync(codeField.value);
        }

        // -- Refresh -----------------------------------------------------------------------------------

        private void Refresh()
        {
            if (connection == null)
            {
                statusLabel.text = "No LabConnection in the scene.";
                statusLabel.style.color = new StyleColor(ConnectPalette.Fault);
                return;
            }

            var state = connection.State;
            bool live = ConnectStates.IsLive(state);
            bool solo = state == ConnectState.SinglePlayer;
            bool gameplay = live || solo;

            // The menu goes away once the decision is made; the join code does not.
            panel.style.display = gameplay ? DisplayStyle.None : DisplayStyle.Flex;
            root.pickingMode = gameplay ? PickingMode.Ignore : PickingMode.Position;

            if (gameplay && !gameplayActive)
            {
                // The local player can spawn while the handshake is still on screen. Its OnEnable
                // locks the cursor, then the connect screen correctly unlocks it again so the menu
                // remains usable. Lock once more at the actual menu -> game transition or
                // PlayerController deliberately ignores every mouse delta.
                PlayerController.SetCursorLocked(true);
            }
            else if (!gameplay)
            {
                // Fully qualified: UnityEngine.UIElements has a Cursor of its own, and this file
                // has both namespaces open.
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            gameplayActive = gameplay;

            statusLabel.text = connection.Status;
            statusLabel.style.color = new StyleColor(
                ConnectStates.IsBusy(state) ? ConnectPalette.Working :
                live ? ConnectPalette.Live : SignalPalette.Dim);

            errorLabel.text = connection.Error ?? string.Empty;
            errorLabel.style.display = string.IsNullOrEmpty(connection.Error)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            retryButton.style.display = state == ConnectState.Failed
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            string code = connection.JoinCodeText;
            bool hasCode = !string.IsNullOrEmpty(code);

            codeLabel.text = hasCode ? JoinCode.ForReading(code) : string.Empty;
            codeLabel.style.display = hasCode ? DisplayStyle.Flex : DisplayStyle.None;

            cardLabel.text = state == ConnectState.Hosting && hasCode
                ? $"JOIN CODE  {JoinCode.ForReading(code)}"
                : "CONNECTED";
            card.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;

            var voice = connection.Voice;
            voiceLabel.text = voice.IsConnected
                ? $"[M] MIC {(voice.MicrophoneMuted ? "OFF" : "ON")}   " +
                  $"[N] SOUND {(voice.OutputMuted ? "OFF" : "ON")}"
                : voice.IsConnecting ? "VOICE CONNECTING…" : voice.UnavailableText;
            string speaking = voice.SpeakingText;
            speakingLabel.text = speaking;
            speakingLabel.style.display = string.IsNullOrEmpty(speaking)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            var identity = connection.Identity;
            identityLabel.text = identity != null && identity.IsReady
                ? $"you are {identity.DisplayName} · {identity.StableId}"
                : string.Empty;

            RefreshEnabled();
        }

        private void RefreshEnabled()
        {
            if (connection == null) return;

            bool open = ConnectStates.AcceptsCommands(connection.State);
            hostButton.SetEnabled(open);
            soloButton.SetEnabled(open);
            codeField.SetEnabled(open);
            joinButton.SetEnabled(open && JoinCode.IsWellFormed(codeField.value));
        }

        // -- Small builders ----------------------------------------------------------------------------

        private static void StyleButton(Button button, Color background)
        {
            button.style.backgroundColor = new StyleColor(background);
            button.style.color = new StyleColor(SignalPalette.Ink);
            button.style.fontSize = 14;
            button.style.paddingTop = 10;
            button.style.paddingBottom = 10;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.marginLeft = 4;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            Round(button, 3);
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
