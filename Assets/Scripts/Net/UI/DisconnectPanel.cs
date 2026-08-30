using System;
using Residue.Data;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// What happened to the session, and the one or two things left to do about it (#52).
    /// <para>
    /// <b>Why this is its own page rather than a line on the co-op screen.</b> A session that ends
    /// under the player is not the same event as a join that failed: it arrives while they are
    /// standing in the lab, it is not a prompt to try the same thing again, and — for exactly one of
    /// the four <see cref="SessionEndKind"/>s — there is a seat still being held that a button can
    /// walk back into. <c>CoOpPanel</c>'s error line answers "that did not work"; this answers "that
    /// stopped working", which needs the heading, the sentence and a decision.
    /// </para>
    /// <para>
    /// <b>RECONNECT appears only where it would work.</b> <see cref="SessionEnd.OffersRejoin"/> is
    /// the whole rule and this page does not second-guess it. A retry that cannot succeed is worse
    /// than no retry at all: it spends the player's last idea about what to do and then fails in a
    /// way that reads as the game being broken rather than the session being over. Where it is
    /// withheld the panel says so in words instead of leaving a gap.
    /// </para>
    /// <para>
    /// Hard rule 4: nothing here is red, amber or green. The heading is
    /// <see cref="ConnectPalette.Fault"/>, the oxidised orange that exists so connection state never
    /// has to borrow from the signal set.
    /// </para>
    /// </summary>
    public sealed class DisconnectPanel
    {
        private readonly LabConnection connection;
        private readonly Label headline;
        private readonly Label detail;
        private readonly Button rejoinButton;
        private readonly Label rejoinHint;

        /// <param name="reconnect">
        /// Routed back out to <see cref="MenuScreen"/> rather than calling
        /// <c>LabConnection.RejoinAsync</c> from here, for the same reason LEAVE is: a rejoin that
        /// fails has to be explained somewhere, and this page will have routed itself away by then.
        /// Nothing on a page decides where the shell goes next.
        /// </param>
        public DisconnectPanel(LabConnection connection, Action dismiss, Action reconnect)
        {
            this.connection = connection;

            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            headline = UiKit.Heading(string.Empty);
            headline.style.color = new StyleColor(ConnectPalette.Fault);
            column.Add(headline);

            detail = UiKit.Body(string.Empty);
            column.Add(detail);

            column.Add(UiKit.Divider());

            var actions = UiKit.Column();

            rejoinButton = UiKit.ActionButton(MenuStrings.Reconnect, () => reconnect?.Invoke());
            actions.Add(rejoinButton);

            actions.Add(UiKit.QuietButton(MenuStrings.BackToTheMenu,
                () => dismiss?.Invoke()));

            column.Add(actions);

            rejoinHint = UiKit.Hint(string.Empty);
            column.Add(rejoinHint);

            Refresh();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        public void Refresh()
        {
            var ended = connection?.Ended;
            if (!ended.HasValue)
            {
                // Only ever seen for the frame between the player pressing something and the router
                // moving off this page. Blanked rather than left showing the last session's notice.
                headline.text = string.Empty;
                detail.text = string.Empty;
                rejoinButton.style.display = DisplayStyle.None;
                rejoinHint.style.display = DisplayStyle.None;
                return;
            }

            var end = ended.Value;

            headline.text = end.Headline;
            detail.text = end.Detail;

            rejoinButton.style.display = end.OffersRejoin ? DisplayStyle.Flex : DisplayStyle.None;
            rejoinHint.style.display = DisplayStyle.Flex;

            rejoinHint.text = end.OffersRejoin
                ? MenuStrings.RejoinHint.Text
                : Why(end.Kind);
        }

        /// <summary>
        /// Why there is no RECONNECT. Named per case rather than written once, because "you cannot
        /// rejoin" and "there is nothing there to rejoin" send a player to look in different places.
        /// <para>
        /// A whole sentence per case rather than a shared "There is no reconnect for this." stem
        /// with a variable tail (#55). The three repeat their opening in English, and stitching them
        /// would be cheaper — but a translator handed the stem cannot move that clause into the
        /// middle of the sentence, which is where several languages want it.
        /// </para>
        /// </summary>
        private static string Why(SessionEndKind kind) => kind switch
        {
            SessionEndKind.HostClosed => MenuStrings.NoRejoinHostClosed,
            SessionEndKind.Kicked => MenuStrings.NoRejoinKicked,
            _ => MenuStrings.NoRejoinRefused
        };
    }
}
