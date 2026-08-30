using System;
using Residue.Data;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The front door (#42): the game's name and the things a player can do from a cold start.
    /// <para>
    /// A plain class rather than a MonoBehaviour, and built once in the constructor, for the reason
    /// <see cref="MenuScreen"/> gives at length: every page of this shell is refreshed in place so a
    /// status change never rebuilds a tree somebody is typing into.
    /// </para>
    /// <para>
    /// <b>CONTINUE appears only when there is something to continue</b> (#49), and it is read fresh
    /// on every <see cref="Refresh"/> rather than once at construction — the screen survives into the
    /// lab and back out again, so a run saved this session has to show up on the menu the player
    /// returns to. It reads a <see cref="RunSaveHeadline"/> and never a <see cref="RunSnapshot"/>:
    /// a snapshot carries ground truth and this is the client-facing assembly.
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
        private readonly Button continueButton;
        private readonly Label continueHint;
        private readonly Button soloButton;
        private readonly Button coOpButton;
        private readonly Label identityLabel;

        public TitlePanel(LabConnection connection, Action coOp, Action settings, Action credits,
                          Action quit)
        {
            this.connection = connection;

            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Title(MenuStrings.Wordmark));
            column.Add(UiKit.Hint(MenuStrings.Tagline));

            var actions = UiKit.Column();

            // Above SINGLE PLAYER, because a player with a contract half run wants it and a player
            // without one never sees it.
            continueButton = UiKit.ActionButton(MenuStrings.Continue,
                () => this.connection?.ContinueSinglePlayer());
            actions.Add(continueButton);

            soloButton = UiKit.ActionButton(MenuStrings.SinglePlayer,
                () => this.connection?.StartSinglePlayer());
            actions.Add(soloButton);

            coOpButton = UiKit.QuietButton(MenuStrings.CoOp, () => coOp?.Invoke());
            actions.Add(coOpButton);

            actions.Add(UiKit.QuietButton(MenuStrings.Settings, () => settings?.Invoke()));
            actions.Add(UiKit.QuietButton(MenuStrings.Credits, () => credits?.Invoke()));
            actions.Add(UiKit.QuietButton(MenuStrings.Quit, () => quit?.Invoke()));

            column.Add(actions);

            continueHint = UiKit.Hint(string.Empty);
            column.Add(continueHint);

            column.Add(UiKit.Hint(MenuStrings.OfflineNote));

            column.Add(UiKit.Divider());

            identityLabel = UiKit.Hint(string.Empty);
            column.Add(identityLabel);

            Refresh();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        public void Refresh()
        {
            RefreshContinue();

            if (connection == null)
            {
                // Not a state the game has, but a menu that silently does nothing is worse than one
                // that says why. Single player is the path this would have broken, so name it.
                soloButton.SetEnabled(false);
                coOpButton.SetEnabled(false);
                continueButton.SetEnabled(false);
                identityLabel.text = MenuStrings.NoConnectionOnTitle.Format(
                    ("build", Application.version));
                return;
            }

            bool open = ConnectStates.AcceptsCommands(connection.State);
            soloButton.SetEnabled(open);
            coOpButton.SetEnabled(open);
            continueButton.SetEnabled(open && loadable);

            // The display name and the stable id are arguments, never translated: an id run through
            // a lookup is a bug that only shows up in one language.
            var identity = connection.Identity;
            identityLabel.text = identity != null && identity.IsReady
                ? MenuStrings.Identity.Format(("name", identity.DisplayName),
                                              ("id", identity.StableId),
                                              ("build", Application.version))
                : MenuStrings.Build.Format(("build", Application.version));
        }

        private bool loadable;
        private bool slotRead;

        /// <summary>
        /// Forget what the save slot said, so the next <see cref="Refresh"/> goes back to disk.
        /// <para>
        /// Called by <see cref="MenuScreen"/> when this page comes up. <see cref="Refresh"/> itself
        /// runs on every connection, voice and lobby change — several times a second in a lobby — and
        /// reading, checksumming and parsing a 20-day contract that often would be a file read per
        /// frame for a line of text that only changes when a shift ends.
        /// </para>
        /// </summary>
        public void RereadSaveSlot() => slotRead = false;

        /// <summary>
        /// Show, hide or disable CONTINUE from what is actually in the save slot.
        /// <para>
        /// A save this build cannot read leaves the button visible and disabled with the reason
        /// underneath, rather than hidden. Hiding it would tell a player who knows they were on day
        /// 14 that their run is simply gone, which is both alarming and — since the file is still on
        /// disk and a matching build could still open it — untrue.
        /// </para>
        /// </summary>
        private void RefreshContinue()
        {
            if (slotRead) return;
            slotRead = true;

            if (!RunSaveSlot.TryReadHeadline(out var headline))
            {
                loadable = false;
                continueButton.style.display = DisplayStyle.None;
                continueHint.style.display = DisplayStyle.None;
                return;
            }

            loadable = headline.IsLoadable;
            continueButton.style.display = DisplayStyle.Flex;
            continueHint.style.display = DisplayStyle.Flex;

            continueHint.text = loadable
                ? MenuStrings.ContinueSaved.Format(
                    ("run", headline.Describe()),
                    ("money", headline.Money.ToString("N0")),
                    ("when", headline.SavedLocal.ToString("d MMM HH:mm")))
                : MenuStrings.ContinueUnreadable.Format(("run", headline.Describe()));
        }
    }
}
