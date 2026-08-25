using System;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The front door (#42): the game's name and the four things a player can do from a cold start.
    /// <para>
    /// A plain class rather than a MonoBehaviour, and built once in the constructor, for the reason
    /// <see cref="MenuScreen"/> gives at length: every page of this shell is refreshed in place so a
    /// status change never rebuilds a tree somebody is typing into.
    /// </para>
    /// <para>
    /// The identity line at the bottom is not decoration. Two test instances on one machine are
    /// otherwise indistinguishable, and handing the second window the first player's hands is a
    /// confusing half-hour. <see cref="Application.version"/> sits beside it because the first
    /// question about any reported bug is which build it came from.
    /// </para>
    /// </summary>
    public sealed class TitlePanel
    {
        private readonly LabConnection connection;
        private readonly Button soloButton;
        private readonly Button coOpButton;
        private readonly Label identityLabel;

        public TitlePanel(LabConnection connection, Action coOp, Action settings, Action quit)
        {
            this.connection = connection;

            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Title("OILED UP"));
            column.Add(UiKit.Hint("Heat-treatment oil analysis. Up to four of you in the lab."));

            var actions = UiKit.Column();

            soloButton = UiKit.ActionButton("SINGLE PLAYER",
                () => this.connection?.StartSinglePlayer());
            actions.Add(soloButton);

            coOpButton = UiKit.QuietButton("CO-OP", () => coOp?.Invoke());
            actions.Add(coOpButton);

            actions.Add(UiKit.QuietButton("SETTINGS", () => settings?.Invoke()));
            actions.Add(UiKit.QuietButton("QUIT", () => quit?.Invoke()));

            column.Add(actions);

            column.Add(UiKit.Hint(
                "Single player needs no sign-in, no lobby and no connection. It works offline."));

            column.Add(UiKit.Divider());

            identityLabel = UiKit.Hint(string.Empty);
            column.Add(identityLabel);

            Refresh();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        public void Refresh()
        {
            if (connection == null)
            {
                // Not a state the game has, but a menu that silently does nothing is worse than one
                // that says why. Single player is the path this would have broken, so name it.
                soloButton.SetEnabled(false);
                coOpButton.SetEnabled(false);
                identityLabel.text =
                    $"No LabConnection on this object, so nothing here can start a game.  " +
                    $"build {Application.version}";
                return;
            }

            bool open = ConnectStates.AcceptsCommands(connection.State);
            soloButton.SetEnabled(open);
            coOpButton.SetEnabled(open);

            var identity = connection.Identity;
            identityLabel.text = identity != null && identity.IsReady
                ? $"you are {identity.DisplayName} · {identity.StableId}    build {Application.version}"
                : $"build {Application.version}";
        }
    }
}
