using System;
using System.Collections.Generic;
using Residue.Data;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The room everybody stands in before the shift starts (#66): the join code, who is here, who
    /// is ready, and the countdown.
    /// <para>
    /// Every seat is built once, including the empty ones. "We are waiting for one more" is a fact
    /// about the room that only shows if the room has a visible shape, and a roster that grows a row
    /// per arrival makes the panel jump under the player's hand — this list refreshes at up to
    /// several hertz.
    /// </para>
    /// <para>
    /// The host may start with people unready by design (see <see cref="LobbyRoom"/>), so START is
    /// never greyed out for that reason; it carries the count instead and lets the host decide. A
    /// client sees the same countdown and has no START, because it has no authority to have one —
    /// the same panel runs on both and the difference is drawn, not branched around.
    /// </para>
    /// </summary>
    public sealed class LobbyPanel
    {
        /// <summary>Seat rows, one per <see cref="LobbyRoom.Capacity"/>. Never rebuilt.</summary>
        private readonly List<SeatRow> rows = new();

        private readonly LabConnection connection;

        /// <summary>
        /// How long the copy button stays saying it worked. Long enough to be read after the click
        /// that caused it, short enough not to still be lying when the player looks back.
        /// </summary>
        private const long CopiedMilliseconds = 1600;

        private readonly Label codeLabel;
        private readonly Label codeHint;
        private readonly Label countdownLabel;
        private readonly Label waitingLabel;
        private readonly Button readyButton;
        private readonly Button startButton;
        private readonly Button copyButton;

        /// <summary>
        /// Set while the button is showing its confirmation, so <see cref="Refresh"/> — which runs
        /// several times a second — does not immediately overwrite it with the resting label.
        /// </summary>
        private bool copied;

        public LobbyPanel(LabConnection connection, Action leave)
        {
            this.connection = connection;

            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Heading(MenuStrings.LobbyHeading));

            var code = UiKit.Column(4f);

            var codeRow = UiKit.Row();

            codeLabel = UiKit.Title(string.Empty);
            codeLabel.style.letterSpacing = 6;
            codeLabel.style.color = new StyleColor(ConnectPalette.Code);
            codeRow.Add(codeLabel);

            codeRow.Add(UiKit.Spacer());

            copyButton = UiKit.QuietButton(MenuStrings.Copy, CopyCode);
            codeRow.Add(copyButton);
            code.Add(codeRow);

            codeHint = UiKit.Hint(MenuStrings.CodeHint);
            code.Add(codeHint);
            column.Add(code);

            var roster = UiKit.Column(4f);
            int capacity = connection != null ? connection.Lobby.Capacity : 4;
            for (int i = 0; i < capacity; i++)
            {
                var row = new SeatRow();
                rows.Add(row);
                roster.Add(row.Root);
            }
            column.Add(roster);

            waitingLabel = UiKit.Hint(string.Empty);
            column.Add(waitingLabel);

            countdownLabel = UiKit.Title(string.Empty);
            countdownLabel.style.color = new StyleColor(ConnectPalette.Live);
            column.Add(countdownLabel);

            column.Add(UiKit.Divider());

            var buttons = UiKit.Row();

            readyButton = UiKit.QuietButton(MenuStrings.ReadyUp,
                () => this.connection?.Lobby.ToggleReady());
            buttons.Add(readyButton);

            startButton = UiKit.ActionButton(MenuStrings.StartShift, StartOrCancel);
            buttons.Add(startButton);

            buttons.Add(UiKit.Spacer());
            buttons.Add(UiKit.DangerButton(MenuStrings.LeaveLobby, () => leave?.Invoke()));
            column.Add(buttons);

            Refresh();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        public void Refresh()
        {
            var lobby = connection != null ? connection.Lobby : null;
            if (lobby == null) return;

            bool host = lobby.IsHost;

            // Host only. A client already knows the code — they typed it — and printing it back at
            // them invites the wrong person to read it out.
            string code = connection.JoinCodeText;
            bool showCode = host && !string.IsNullOrEmpty(code);
            codeLabel.text = showCode ? JoinCode.ForReading(code) : string.Empty;
            codeLabel.style.display = showCode ? DisplayStyle.Flex : DisplayStyle.None;
            copyButton.style.display = showCode ? DisplayStyle.Flex : DisplayStyle.None;
            codeHint.style.display = showCode ? DisplayStyle.Flex : DisplayStyle.None;

            // Left alone while the copy confirmation is up; it owns both of these until it expires.
            if (!copied) codeHint.text = MenuStrings.CodeHint;

            var seats = lobby.Seats;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < seats.Count) rows[i].Show(seats[i]);
                else rows[i].ShowEmpty();
            }

            int here = Mathf.Min(seats.Count, rows.Count);
            int free = rows.Count - here;
            waitingLabel.text = free <= 0
                ? MenuStrings.LobbyFull.Text
                : MenuStrings.LobbyRoomLeft.Format(("here", here),
                                                   ("capacity", rows.Count),
                                                   ("free", free));

            bool counting = lobby.IsCountingDown;
            countdownLabel.text = counting
                ? MenuStrings.Countdown.Format(
                    ("seconds", Mathf.Max(1, Mathf.CeilToInt(lobby.CountdownRemaining))))
                : string.Empty;
            countdownLabel.style.display = counting ? DisplayStyle.Flex : DisplayStyle.None;

            readyButton.text = lobby.LocalReady ? MenuStrings.CancelReady : MenuStrings.ReadyUp;

            startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            startButton.text = counting
                ? MenuStrings.CancelCountdown.Text
                : MenuStrings.StartShiftReady.Format(("ready", lobby.ReadyCount),
                                                     ("seated", seats.Count));
        }

        /// <summary>
        /// Put the code on the system clipboard.
        /// <para>
        /// The copied string is <see cref="LabConnection.JoinCodeText"/> itself, not what the label
        /// shows — the label is a presentation of the code and has already had a space in it once.
        /// Whatever is pasted into the other player's field has to be the thing the service will
        /// accept.
        /// </para>
        /// The button says so afterwards rather than staying silent: a clipboard write produces no
        /// visible effect anywhere on this machine, so a copy button with no confirmation is
        /// indistinguishable from a copy button that is broken.
        /// </summary>
        private void CopyCode()
        {
            string code = connection != null ? connection.JoinCodeText : null;
            if (string.IsNullOrEmpty(code)) return;

            GUIUtility.systemCopyBuffer = code;

            copied = true;
            copyButton.text = MenuStrings.Copied;
            codeHint.text = MenuStrings.CodeCopied.Format(("code", code));

            copyButton.schedule.Execute(() =>
            {
                copied = false;
                copyButton.text = MenuStrings.Copy;
                codeHint.text = MenuStrings.CodeHint;
            }).StartingIn(CopiedMilliseconds);
        }

        private void StartOrCancel()
        {
            var lobby = connection != null ? connection.Lobby : null;
            if (lobby == null) return;

            if (lobby.IsCountingDown) lobby.CancelCountdown();
            else lobby.StartCountdown();
        }

        /// <summary>
        /// One seat, present whether or not anybody is standing in it. Private and nested because it
        /// is a piece of this panel's layout rather than a widget: it knows what an empty seat looks
        /// like, which means nothing anywhere else.
        /// </summary>
        private sealed class SeatRow
        {
            private readonly Label name;
            private readonly Label state;

            internal SeatRow()
            {
                Root = UiKit.Row();
                Root.style.paddingTop = 5;
                Root.style.paddingBottom = 5;
                Root.style.paddingLeft = 10;
                Root.style.paddingRight = 10;
                Root.style.backgroundColor = new StyleColor(UiPalette.SurfaceSunken);
                UiKit.Round(Root);

                name = UiKit.Body(string.Empty);
                name.style.flexGrow = 1f;
                Root.Add(name);

                state = UiKit.Body(string.Empty);
                state.style.fontSize = UiKit.HintSize;
                Root.Add(state);
            }

            internal VisualElement Root { get; }

            internal void Show(in LobbySeat seat)
            {
                // The name is a player's, so it is an argument and never a lookup.
                name.text = seat.IsHost
                    ? MenuStrings.SeatHost.Format(("name", seat.Name))
                    : seat.Name;
                name.style.color = new StyleColor(UiPalette.Ink);

                // Accent, never green. A ready tick is a state, not a verdict — hard rule 4.
                state.text = seat.Ready ? MenuStrings.SeatReady : MenuStrings.SeatDeciding;
                state.style.color = new StyleColor(
                    seat.Ready ? UiPalette.Accent : UiPalette.InkFaint);
            }

            internal void ShowEmpty()
            {
                name.text = MenuStrings.SeatEmpty;
                name.style.color = new StyleColor(UiPalette.InkFaint);
                state.text = string.Empty;
            }
        }
    }
}
