using Residue.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The tutorial's objective card: the list, what is ticked, and one sentence about whichever is
    /// next.
    ///
    /// <para>
    /// A plain class owning a subtree, the way <c>TitlePanel</c> is, so <see cref="LabHud"/> keeps
    /// being a component that draws a crosshair rather than a component that also knows what an
    /// objective looks like. It draws from <see cref="TutorialObjectives"/> and asks nothing of the
    /// lab, so there is no path from a card to the simulation.
    /// </para>
    ///
    /// <para>
    /// <b>No verdict colours anywhere on it (hard rule 4).</b> A tick drawn in green would spend the
    /// one thing that makes red mean CRITICAL at a glance, and it would be the most-seen green in the
    /// game on a first run. Done is <see cref="SignalPalette.Off"/>, pending is
    /// <see cref="SignalPalette.Dim"/>, and whichever is next is <see cref="SignalPalette.Accent"/> —
    /// the same three the standing orders card already uses. State is carried by the mark as well as
    /// by the colour, so the card survives a greyscale screenshot for the same reason the results
    /// table does (§2.2).
    /// </para>
    ///
    /// <para>
    /// It shares the corner and the shape of the standing orders card on purpose — the two are the
    /// same kind of object, one saying what to do next and the other why it is worth doing — and
    /// <see cref="LabHud"/> never draws both at once, because they would overlap and because a first
    /// run should not open on two walls of text.
    /// </para>
    /// </summary>
    public sealed class TutorialCard
    {
        /// <summary>
        /// The marks, which are symbols rather than language — the same argument
        /// <c>SignalPalette.Glyph</c> makes. A translated tick would break the one channel that
        /// survives both colourblindness and a washed-out monitor, and there is no language in which
        /// a checked box reads better as a word.
        /// </summary>
        private const string MarkDone = "[x]";
        private const string MarkPending = "[ ]";
        private const string MarkNext = "[>]";

        private Label heading;
        private Label progress;
        private VisualElement list;
        private Label detail;

        private TutorialObjectives builtFor;
        private int builtVersion = -1;

        public TutorialCard()
        {
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = 16;
            Root.style.top = 96;
            Root.style.width = 372;
            Root.style.paddingTop = 14;
            Root.style.paddingBottom = 14;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;
            Root.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            Root.style.borderLeftWidth = 2;
            Root.style.borderLeftColor = new StyleColor(SignalPalette.Accent);
            Root.style.display = DisplayStyle.None;
            LabHud.Round(Root, 3);

            Build();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        private void Build()
        {
            heading = new Label(TutorialStrings.CardTitle);
            LabHud.Style(heading, 14, SignalPalette.Ink);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            Root.Add(heading);

            progress = new Label();
            LabHud.Style(progress, 11, SignalPalette.Off);
            progress.style.marginTop = 2;
            Root.Add(progress);

            list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.style.marginTop = 10;
            Root.Add(list);

            detail = new Label();
            LabHud.Style(detail, 11, SignalPalette.Dim);
            detail.style.marginTop = 8;
            detail.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(detail);

            var closing = new Label(TutorialStrings.Closing);
            LabHud.Style(closing, 11, SignalPalette.Off);
            closing.style.marginTop = 12;
            closing.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(closing);
        }

        /// <summary>
        /// Draw whatever the tracker now says, or nothing at all.
        /// <para>
        /// Rebuilt off <see cref="TutorialObjectives.Version"/> rather than every frame. A card that
        /// is up for a whole shift is not a place to spend fourteen string lookups and a row rebuild
        /// sixty times a second, and every line on it is a <see cref="LocKey"/> resolved through a
        /// dictionary.
        /// </para>
        /// </summary>
        public void Refresh(TutorialObjectives objectives, bool visible)
        {
            bool show = objectives != null && visible;
            Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            if (ReferenceEquals(objectives, builtFor) && objectives.Version == builtVersion) return;

            builtFor = objectives;
            builtVersion = objectives.Version;
            Paint(objectives);
        }

        private void Paint(TutorialObjectives objectives)
        {
            list.Clear();

            progress.text = TutorialStrings.Progress.Format(
                ("done", objectives.DoneCount), ("total", objectives.VisibleCount));

            var next = objectives.Next;
            int drawnDay = 0;
            LocKey nextDetail = default;

            foreach (var objective in objectives.All)
            {
                if (!objectives.IsVisible(objective)) continue;

                if (objective.Day != drawnDay)
                {
                    drawnDay = objective.Day;
                    list.Add(DayHeading(drawnDay, list.childCount > 0));
                }

                bool done = objectives.IsDone(objective.Step);
                bool current = objective.Step == next;

                if (current) nextDetail = objective.Detail;

                list.Add(Row(objective, done, current));
            }

            // Empty once every objective on the card is ticked, which is the honest end of it: the
            // card stops pointing rather than congratulating. Nothing else changes — the shift runs
            // exactly as it did.
            detail.text = next == TutorialStep.None ? string.Empty : nextDetail.Text;
            detail.style.display = next == TutorialStep.None ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static Label DayHeading(int day, bool spaced)
        {
            var label = new Label(day <= 1 ? TutorialStrings.DayOneHeading : TutorialStrings.DayTwoHeading);
            LabHud.Style(label, 11, SignalPalette.Ink);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = spaced ? 12 : 0;
            label.style.marginBottom = 4;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>
        /// One objective. The mark is its own fixed-width column so the sentences line up however
        /// long a translation makes them, and so a row that wraps wraps under its own text rather than
        /// under the box.
        /// </summary>
        private VisualElement Row(in TutorialObjectives.Objective objective, bool done, bool current)
        {
            var colour = done ? SignalPalette.Off : current ? SignalPalette.Accent : SignalPalette.Dim;

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = 3;

            var mark = new Label(done ? MarkDone : current ? MarkNext : MarkPending);
            LabHud.Style(mark, 12, colour);
            mark.style.width = 24;
            mark.style.flexShrink = 0f;
            row.Add(mark);

            var line = new Label(objective.Line);
            LabHud.Style(line, 12, colour);
            line.style.flexShrink = 1f;
            line.style.whiteSpace = WhiteSpace.Normal;
            row.Add(line);

            return row;
        }
    }
}
