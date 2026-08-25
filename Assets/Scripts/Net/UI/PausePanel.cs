using System;
using Residue.Gameplay.UI;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The pause menu (#44): resume, settings, leave.
    /// <para>
    /// <b>Pausing does not pause a co-op shift</b>, and this panel's one job beyond three buttons is
    /// to say so. The day clock belongs to the host and keeps running whatever a client does with
    /// its own keyboard; #44 is explicit that quietly doing nothing while the clock ticks is the
    /// version that feels broken. <see cref="MenuScreen"/> decides which sentence is true — it is the
    /// thing that knows whether <c>Time.timeScale</c> was actually stopped — and this panel only
    /// draws it.
    /// </para>
    /// <para>
    /// LEAVE is drawn with <see cref="UiKit.DangerButton"/>, which marks it in oxidised orange
    /// <i>text</i> and not a red fill. Hard rule 4 has no exception for "but this one really is
    /// destructive"; see <c>UiPalette</c>.
    /// </para>
    /// </summary>
    public sealed class PausePanel
    {
        private readonly Label clockLabel;

        public PausePanel(Action resume, Action settings, Action leave)
        {
            Root = UiKit.Panel();

            var column = UiKit.Column(UiKit.GapWide);
            Root.Add(column);

            column.Add(UiKit.Heading("PAUSED"));

            clockLabel = UiKit.Body(string.Empty);
            column.Add(clockLabel);

            var actions = UiKit.Column();
            actions.Add(UiKit.ActionButton("RESUME", () => resume?.Invoke()));
            actions.Add(UiKit.QuietButton("SETTINGS", () => settings?.Invoke()));
            actions.Add(UiKit.DangerButton("LEAVE THE SHIFT", () => leave?.Invoke()));
            column.Add(actions);

            column.Add(UiKit.Hint(
                "Leaving closes your session and puts you back at the menu. In co-op it does not " +
                "end the shift for anybody else."));
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        /// <param name="clockStopped">
        /// True only when the lab is genuinely frozen — single player. In co-op the sentence has to
        /// be the other one, because the shift is still running while the player reads it.
        /// </param>
        public void Refresh(bool clockStopped)
        {
            clockLabel.text = clockStopped
                ? "The lab is stopped while this is up. Nothing moves until you resume."
                : "The shift clock is still running. This is a co-op session, so pausing only " +
                  "stops your own hands — the day carries on for everyone else in the lab.";

            clockLabel.style.color = new StyleColor(
                clockStopped ? UiPalette.InkDim : UiPalette.Warn);
        }
    }
}
