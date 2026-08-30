using System;
using Residue.Data;
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

            column.Add(UiKit.Heading(MenuStrings.PausedHeading));

            clockLabel = UiKit.Body(string.Empty);
            column.Add(clockLabel);

            var actions = UiKit.Column();
            actions.Add(UiKit.ActionButton(MenuStrings.Resume, () => resume?.Invoke()));
            actions.Add(UiKit.QuietButton(MenuStrings.Settings, () => settings?.Invoke()));
            actions.Add(UiKit.DangerButton(MenuStrings.LeaveTheShift, () => leave?.Invoke()));
            column.Add(actions);

            column.Add(UiKit.Hint(MenuStrings.LeaveNote));
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
                ? MenuStrings.ClockStopped
                : MenuStrings.ClockRunning;

            clockLabel.style.color = new StyleColor(
                clockStopped ? UiPalette.InkDim : UiPalette.Warn);
        }
    }
}
