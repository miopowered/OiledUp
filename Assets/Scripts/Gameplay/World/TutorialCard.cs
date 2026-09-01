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
    /// game on a first run. Done is <see cref="HudStyle.Faint"/>, pending is
    /// <see cref="HudStyle.Dim"/>, and whichever is next is <see cref="HudStyle.Accent"/> — the same
    /// three the standing orders card already uses. State is carried by the mark as well as by the
    /// colour, so the card survives a greyscale screenshot for the same reason the results table does
    /// (§2.2).
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
            // Anchored, sized and coloured off HudStyle rather than off numbers of its own: it stands
            // in the same corner as the standing-orders card and has to be indistinguishable from it
            // as a shape, or swapping between the two with [Tab] reads as the layout jumping.
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = HudStyle.Inset;
            Root.style.top = HudStyle.ContentTop;
            Root.style.width = LabHud.CardWidth;
            Root.style.paddingTop = HudStyle.S4;
            Root.style.paddingBottom = HudStyle.S4;
            Root.style.paddingLeft = HudStyle.S4;
            Root.style.paddingRight = HudStyle.S4;
            Root.style.backgroundColor = new StyleColor(HudStyle.Plate);
            Root.style.borderLeftWidth = 2;
            Root.style.borderLeftColor = new StyleColor(HudStyle.Accent);
            Root.style.display = DisplayStyle.None;
            HudStyle.Round(Root);

            Build();
        }

        /// <summary>The tree to parent. Built once; <see cref="Refresh"/> re-reads everything on it.</summary>
        public VisualElement Root { get; }

        private void Build()
        {
            heading = HudStyle.Text(TutorialStrings.CardTitle, HudStyle.MetricSize, HudStyle.Ink,
                                    bold: true);
            heading.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(heading);

            progress = HudStyle.Text(string.Empty, HudStyle.CaptionSize, HudStyle.Faint);
            progress.style.marginTop = HudStyle.S1;
            Root.Add(progress);

            list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.style.marginTop = HudStyle.S3;
            Root.Add(list);

            detail = HudStyle.Text(string.Empty, HudStyle.BodySize, HudStyle.Dim);
            detail.style.marginTop = HudStyle.S2;
            detail.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(detail);

            var closing = HudStyle.Text(TutorialStrings.Closing, HudStyle.CaptionSize,
                                        HudStyle.Faint);
            closing.style.marginTop = HudStyle.S3;
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
            var label = HudStyle.Text(
                day <= 1 ? TutorialStrings.DayOneHeading : TutorialStrings.DayTwoHeading,
                HudStyle.CaptionSize, HudStyle.Ink, bold: true);
            label.style.marginTop = spaced ? HudStyle.S3 : 0f;
            label.style.marginBottom = HudStyle.S1;
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
            var colour = done ? HudStyle.Faint : current ? HudStyle.Accent : HudStyle.Dim;

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = HudStyle.S1;

            var mark = HudStyle.Text(done ? MarkDone : current ? MarkNext : MarkPending,
                                     HudStyle.BodySize, colour);
            mark.style.width = 30;
            mark.style.flexShrink = 0f;
            row.Add(mark);

            var line = HudStyle.Text(objective.Line, HudStyle.BodySize, colour);
            line.style.flexShrink = 1f;
            line.style.whiteSpace = WhiteSpace.Normal;
            row.Add(line);

            return row;
        }
    }
}
