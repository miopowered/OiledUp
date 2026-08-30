using System;
using Residue.Data;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// Host a shift, or type the six characters somebody read out — and every way that can fail.
    /// <para>
    /// This is <c>ConnectScreen</c>'s connect half, moved behind a page rather than rewritten. The
    /// behaviour that had to survive intact: the code field normalises as you type so what is shown
    /// is what will be sent, Enter joins, TRY AGAIN forgets the cached sign-in decision before it
    /// unwinds, and <see cref="ConnectStates.AcceptsCommands"/> — not a guess about the state — is
    /// what decides which controls are live.
    /// </para>
    /// <para>
    /// The join code is deliberately <b>not</b> displayed here any more. Hosting now opens a lobby
    /// in the same breath, so the moment there is a code to show, <see cref="MenuScreen"/> has
    /// already routed to <see cref="LobbyPanel"/>, which shows it at reading size. A second copy
    /// would only ever be seen for the frame in between.
    /// </para>
    /// </summary>
    public sealed class CoOpPanel
    {
        private readonly LabConnection connection;

        private readonly Button hostButton;
        private readonly Button joinButton;
        private readonly Button retryButton;
        private readonly Button backButton;
        private readonly TextField codeField;
        private readonly Label statusLabel;
        private readonly Label errorLabel;

        public CoOpPanel(LabConnection connection, Action back)
        {
            this.connection = connection;

            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Heading(MenuStrings.CoOpHeading));

            var actions = UiKit.Column();

            hostButton = UiKit.ActionButton(MenuStrings.Host,
                () => { if (this.connection != null) _ = this.connection.HostAsync(); });
            actions.Add(hostButton);

            codeField = UiKit.TextEntry(MenuStrings.JoinCodeField, string.Empty, OnCodeTyped);

            // Normalise as they type, so the field shows what will actually be sent. A code read out
            // over voice arrives lowercase and a pasted one arrives with a newline or hyphens;
            // neither should reach a network call, and neither should have to be fixed by hand.
            codeField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                Join();
            });

            var codeRow = UiKit.RowFor(codeField);
            joinButton = UiKit.QuietButton(MenuStrings.Join, Join);
            codeRow.Add(joinButton);
            actions.Add(codeRow);

            actions.Add(UiKit.Hint(MenuStrings.JoinCodeHint));

            column.Add(actions);
            column.Add(UiKit.Divider());

            statusLabel = UiKit.Body(string.Empty);
            column.Add(statusLabel);

            errorLabel = UiKit.Body(string.Empty);
            errorLabel.style.fontSize = UiKit.LabelSize;
            errorLabel.style.color = new StyleColor(ConnectPalette.Fault);
            column.Add(errorLabel);

            retryButton = UiKit.QuietButton(MenuStrings.TryAgain, () =>
            {
                // The player has plugged the network back in. Without forgetting the cached
                // decision, the first failed sign-in would refuse every attempt until a restart.
                ServiceBootstrap.Forget();
                if (this.connection != null) _ = this.connection.LeaveAsync();
            });
            column.Add(retryButton);

            var footer = UiKit.Row();
            footer.Add(UiKit.Spacer());
            backButton = UiKit.QuietButton(MenuStrings.Back, () => back?.Invoke());
            footer.Add(backButton);
            column.Add(footer);

            Refresh();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        public void Refresh()
        {
            if (connection == null)
            {
                statusLabel.text = MenuStrings.NoConnectionOnCoOp;
                statusLabel.style.color = new StyleColor(ConnectPalette.Fault);
                errorLabel.style.display = DisplayStyle.None;
                retryButton.style.display = DisplayStyle.None;
                SetEnabled(false);
                return;
            }

            var state = connection.State;

            statusLabel.text = connection.Status;
            statusLabel.style.color = new StyleColor(
                ConnectStates.IsBusy(state) ? ConnectPalette.Working :
                ConnectStates.IsLive(state) ? ConnectPalette.Live : UiPalette.InkDim);

            errorLabel.text = connection.Error ?? string.Empty;
            errorLabel.style.display = string.IsNullOrEmpty(connection.Error)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            // A failure is a prompt, not a dead end — see ConnectStates.AcceptsCommands.
            retryButton.style.display = state == ConnectState.Failed
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            SetEnabled(ConnectStates.AcceptsCommands(state));
        }

        private void SetEnabled(bool open)
        {
            hostButton.SetEnabled(open);
            codeField.SetEnabled(open);
            joinButton.SetEnabled(open && JoinCode.IsWellFormed(codeField.value));

            // Going back mid-handshake would only be overruled by the router on the next refresh, so
            // the button says so by being unavailable rather than by doing nothing.
            backButton.SetEnabled(open);
        }

        private void OnCodeTyped(string typed)
        {
            string tidy = JoinCode.Normalise(typed);
            if (tidy != typed) codeField.SetValueWithoutNotify(tidy);
            Refresh();
        }

        private void Join()
        {
            if (connection == null) return;
            _ = connection.JoinAsync(codeField.value);
        }
    }
}
