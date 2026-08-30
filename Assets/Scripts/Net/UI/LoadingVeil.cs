using System.Collections.Generic;
using Residue.Gameplay.UI;
using Residue.Net.Connect;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Net.UI
{
    /// <summary>
    /// The black between two scenes, drawn (#51): the fade itself, the step being waited on, and one
    /// piece of evidence that the process is still running.
    /// <para>
    /// <b>It decides nothing.</b> The opacity and the step are read off
    /// <see cref="LabConnection.Transition"/>, which is driven from <c>LabConnection.Update</c> and
    /// keeps the queued scene load moving whether or not this ever draws. A veil that owned the fade
    /// would mean a menu that failed to build is a game that never loads the lab.
    /// </para>
    /// <para>
    /// <b>Hard rule 4 has no exception for loading screens.</b> Red, amber and green mean verdict
    /// state; a progress bar is the single most common place that rule dies. The face is
    /// <see cref="UiPalette.Backdrop"/> — the same near-black the menu already sits on, so a fade to
    /// it cannot be mistaken for a signal — and the only thing that moves is a sweep between
    /// <see cref="UiPalette.SurfaceRaised"/> and <see cref="UiPalette.Accent"/>, both cool.
    /// </para>
    /// <para>
    /// <b>The sweep is on a real-time clock and never on <c>Time.deltaTime</c>.</b> LEAVE is pressed
    /// from behind a pause menu holding <c>Time.timeScale</c> at zero, and an indicator that freezes
    /// on the one screen whose job is to prove the game has not is worse than no indicator at all —
    /// see <c>SettingsPanel</c>, whose revert countdown documents the same trap and answers it the
    /// same way. <see cref="Refresh"/> is handed the clock rather than reading one, so there is one
    /// place it can be got wrong.
    /// </para>
    /// </summary>
    public sealed class LoadingVeil
    {
        /// <summary>Number of marks in the sweep. Enough to read as motion, few enough to ignore.</summary>
        private const int Marks = 5;

        private const float MarkSize = 6f;
        private const float SweepSeconds = 1.5f;

        /// <summary>
        /// Opacity at which this starts swallowing clicks. Below it the screen underneath is still
        /// legible, so it is still the thing the player is aiming at; above it they would be
        /// clicking blind at something they cannot see.
        /// </summary>
        private const float BlocksInputAbove = 0.5f;

        /// <summary>The colour the screen fades to. Stated for the test that guards hard rule 4.</summary>
        public static readonly Color Face = UiPalette.Backdrop;

        /// <summary>The lit end of the sweep. Cool, and never a signal colour.</summary>
        public static readonly Color Pulse = UiPalette.Accent;

        /// <summary>The unlit end.</summary>
        public static readonly Color Rest = UiPalette.SurfaceRaised;

        private readonly List<VisualElement> marks = new(Marks);
        private readonly Label step;
        private readonly Label note;

        public LoadingVeil()
        {
            Root = UiKit.Backdrop();
            Root.style.backgroundColor = new StyleColor(Face);

            // Gone rather than merely transparent when there is nothing to show. An invisible
            // full-screen element left in the layout is the classic "the menu stopped taking clicks"
            // bug, and display:None takes it out of picking as well as out of the draw.
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;

            var column = UiKit.Column(UiKit.GapWide);
            column.style.alignItems = Align.Center;
            column.style.maxWidth = 460f;
            Root.Add(column);

            step = UiKit.Body(string.Empty);
            step.style.unityTextAlign = TextAnchor.MiddleCenter;
            column.Add(step);

            column.Add(Sweep());

            note = UiKit.Hint(string.Empty);
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            note.style.display = DisplayStyle.None;
            column.Add(note);
        }

        /// <summary>The tree to parent. Add it last, so it covers every page in the shell.</summary>
        public VisualElement Root { get; }

        /// <summary>
        /// Draw the current state of the transition. Call every frame with a real-time clock —
        /// <c>Time.unscaledTime</c> — for the reason on the type.
        /// </summary>
        public void Refresh(LabConnection connection, float realTimeSeconds)
        {
            var transition = connection != null ? connection.Transition : null;
            float opacity = transition != null ? transition.Opacity : 0f;

            if (opacity <= 0f)
            {
                Root.style.display = DisplayStyle.None;
                Root.pickingMode = PickingMode.Ignore;
                return;
            }

            Root.style.display = DisplayStyle.Flex;
            Root.style.opacity = opacity;
            Root.pickingMode = opacity >= BlocksInputAbove
                ? PickingMode.Position
                : PickingMode.Ignore;

            step.text = transition.Step ?? string.Empty;

            string patience = connection.LoadingNote;
            note.text = patience ?? string.Empty;
            note.style.display = string.IsNullOrEmpty(patience)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            PaintSweep(realTimeSeconds);
        }

        private VisualElement Sweep()
        {
            var row = UiKit.Row(MarkSize);

            for (int i = 0; i < Marks; i++)
            {
                var mark = new VisualElement();
                mark.style.width = MarkSize;
                mark.style.height = MarkSize;
                mark.style.backgroundColor = new StyleColor(Rest);
                mark.pickingMode = PickingMode.Ignore;
                UiKit.Round(mark, MarkSize * 0.5f);

                marks.Add(mark);
                row.Add(mark);
            }

            return row;
        }

        /// <summary>
        /// One lit mark travelling along the row and wrapping. Deliberately slow and small: this is
        /// evidence that frames are still being drawn, not a progress bar — there is no honest
        /// percentage to show for a wait whose length belongs to somebody else's machine.
        /// </summary>
        private void PaintSweep(float realTimeSeconds)
        {
            float head = Mathf.Repeat(realTimeSeconds / SweepSeconds, 1f) * Marks;

            for (int i = 0; i < marks.Count; i++)
            {
                float gap = Mathf.Abs(i - head);
                gap = Mathf.Min(gap, Marks - gap);   // the row is a loop, so the ends are neighbours

                marks[i].style.backgroundColor = new StyleColor(
                    Color.Lerp(Rest, Pulse, Mathf.Clamp01(1f - gap)));
            }
        }
    }
}
