using System.Collections.Generic;
using Residue.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The bar across the top of the HUD: the shift clock, the workload, the ledger and the stores.
    ///
    /// <para>
    /// A plain class owning a subtree, the way <see cref="TutorialCard"/> is, so <see cref="LabHud"/>
    /// stays a component that draws a crosshair rather than one that also knows what a balance looks
    /// like. It reads <see cref="ILabView"/> and nothing else, so it is drawn identically on a host and
    /// on a joined client, and there is no path from a header cell to the simulation.
    /// </para>
    ///
    /// <para>
    /// <b>Order of prominence is a rule, not a taste.</b> Reading left to right, and in type size,
    /// cells are ranked by how often the number changes what the player does next:
    /// </para>
    /// <list type="number">
    /// <item><description><b>The clock</b>, at <see cref="HudStyle.HeroSize"/>. It moves every second
    /// and it is the pressure the whole game runs on (§6.1). There is exactly one thing this size on
    /// the screen.</description></item>
    /// <item><description><b>Open samples</b>, at <see cref="HudStyle.MetricSize"/> and bold. The
    /// workload: what the clock is being spent on.</description></item>
    /// <item><description><b>Balance and reputation</b>, the same size but regular weight. They move
    /// when a verdict is filed — a few times a day — and they are consequences rather than
    /// instructions.</description></item>
    /// <item><description><b>Drum and standards</b>, at <see cref="HudStyle.BodySize"/> and furthest
    /// right. Stock levels that change on a flush or a recalibration and are checked deliberately
    /// rather than glanced at.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Nothing here is ever drawn in a signal colour (hard rule 4).</b> A HUD showing money is
    /// precisely where red-on-negative and green-on-positive get invented, and spending row 4 on a
    /// balance is what stops red meaning CRITICAL on a results table. An overdrawn account changes its
    /// caption to <see cref="ScreenStrings.HudBalanceOverdrawnCaption"/>, gains a minus sign and goes
    /// bold — word, sign and weight, which is three of §2.2's non-hue channels and no hue at all. The
    /// end of the shift gets <see cref="HudStyle.Warn"/>, the warm-family orange the menus already use
    /// for something that bites, and it too carries a word and a weight beside the colour.
    /// </para>
    ///
    /// <para>
    /// <b>Built once, values set in place.</b> Every cell caches the number it last drew and reformats
    /// only when it changes, so a header that is on screen for a whole shift costs a handful of
    /// comparisons a frame rather than eight dictionary lookups and eight string builds. The clock is
    /// compared at whole-second resolution for the same reason.
    /// </para>
    /// </summary>
    public sealed class HudHeader
    {
        // Element names, so a test can find a cell without this class exposing its fields and without
        // matching on the text a translation is free to change. They are identifiers, not language.

        public const string ClockName = "hud-clock";
        public const string ShiftCaptionName = "hud-shift-caption";
        public const string OpenName = "hud-open";
        public const string BalanceName = "hud-balance";
        public const string BalanceCaptionName = "hud-balance-caption";
        public const string ReputationName = "hud-reputation";
        public const string DrumName = "hud-drum";
        public const string StandardsName = "hud-standards";
        public const string AlertName = "hud-alert";

        /// <summary>
        /// Reserved width for the clock cell, so the bar does not breathe as digits change width. Wide
        /// enough for the German caption (<c>TAG 2 · RESTZEIT</c>) as well as the English.
        /// </summary>
        private const float ShiftCellWidth = 190f;

        private const float OpenCellWidth = 210f;
        private const float LedgerCellWidth = 132f;
        private const float StoreCellWidth = 104f;

        /// <summary>
        /// The labels whose text is a fixed line rather than a reading, paired with the line.
        /// <para>
        /// Set once at build, which is what makes the header cost nothing per frame — and repainted
        /// from here the moment <see cref="Loc.Language"/> changes, which is what stops the settings
        /// screen leaving half the bar in the old language until the next scene load. The captions the
        /// refresh already rewrites (the shift caption and the balance caption) are deliberately not
        /// in this list: two places setting one label is how they end up disagreeing.
        /// </para>
        /// </summary>
        private readonly List<(Label Label, LocKey Key)> fixedLines = new();

        private string drawnLanguage;

        private Label shiftCaption;
        private Label clock;
        private Label open;
        private Label balanceCaption;
        private Label balance;
        private Label reputation;
        private Label drum;
        private Label standards;
        private VisualElement alert;

        // Last drawn values. int.MinValue is "nothing drawn yet", which no real reading can be.
        private int drawnDay = int.MinValue;
        private int drawnSeconds = int.MinValue;
        private int drawnOpen = int.MinValue;
        private int drawnMoney = int.MinValue;
        private int drawnReputation = int.MinValue;
        private int drawnDrum = int.MinValue;
        private int drawnStandards = int.MinValue;
        private bool drawnShiftOver;
        private bool drawnAnything;

        public HudHeader()
        {
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.right = 0;
            Root.style.top = 0;
            Root.style.flexDirection = FlexDirection.Column;
            Root.style.display = DisplayStyle.None;

            Build();
        }

        /// <summary>The tree to parent. Full width, anchored to the top edge.</summary>
        public VisualElement Root { get; }

        private void Build()
        {
            var metrics = HudStyle.Row();
            metrics.style.height = HudStyle.HeaderHeight;
            metrics.style.paddingLeft = HudStyle.Inset;
            metrics.style.paddingRight = HudStyle.Inset;
            metrics.style.paddingBottom = HudStyle.S3;
            metrics.style.backgroundColor = new StyleColor(HudStyle.Plate);
            metrics.style.overflow = Overflow.Hidden;

            // Values on one baseline rather than each cell centred in the bar. The cells are three
            // different heights — a 29 px clock beside a 15 px stock level — and centring each one
            // individually leaves seven numbers at seven different heights, which reads as a list of
            // widgets instead of a row of readings.
            metrics.style.alignItems = Align.FlexEnd;

            metrics.style.borderBottomWidth = 1;
            metrics.style.borderBottomColor = new StyleColor(HudStyle.Line);
            Root.Add(metrics);

            metrics.Add(ShiftCell());
            metrics.Add(HudStyle.Rule());

            open = HudStyle.Text(string.Empty, HudStyle.MetricSize, HudStyle.Ink, bold: true);
            open.name = OpenName;
            open.style.width = OpenCellWidth;
            open.style.flexShrink = 0f;
            HudStyle.Truncate(open);
            metrics.Add(open);

            // Everything after this is pushed to the right-hand edge, and this is what absorbs a
            // longer translation: German runs roughly 30% longer, and a caption that grows eats slack
            // here rather than pushing a number off the bar.
            metrics.Add(HudStyle.Spacer());

            metrics.Add(Cell(out balanceCaption, out balance, ScreenStrings.HudBalanceCaption,
                             HudStyle.MetricSize, LedgerCellWidth));
            metrics.Add(Cell(out var repCaption, out reputation, ScreenStrings.HudReputationCaption,
                             HudStyle.MetricSize, LedgerCellWidth));
            metrics.Add(HudStyle.Rule());
            metrics.Add(Cell(out var drumCaption, out drum, ScreenStrings.HudDrumCaption,
                             HudStyle.BodySize, StoreCellWidth));
            metrics.Add(Cell(out var standardsCaption, out standards,
                             ScreenStrings.HudStandardsCaption,
                             HudStyle.BodySize, StoreCellWidth));

            fixedLines.Add((repCaption, ScreenStrings.HudReputationCaption));
            fixedLines.Add((drumCaption, ScreenStrings.HudDrumCaption));
            fixedLines.Add((standardsCaption, ScreenStrings.HudStandardsCaption));

            balanceCaption.name = BalanceCaptionName;
            balance.name = BalanceName;
            reputation.name = ReputationName;
            drum.name = DrumName;
            standards.name = StandardsName;

            alert = BuildAlert();
            Root.Add(alert);
        }

        /// <summary>
        /// Day and clock. One caption for two facts because the number under it answers both, and a
        /// second caption beside a 29 px figure would be competing with it rather than naming it.
        /// </summary>
        private VisualElement ShiftCell()
        {
            var cell = HudStyle.Column();
            cell.style.width = ShiftCellWidth;
            cell.style.flexShrink = 0f;

            shiftCaption = HudStyle.Caption(string.Empty);
            shiftCaption.name = ShiftCaptionName;
            cell.Add(shiftCaption);

            clock = HudStyle.Text(string.Empty, HudStyle.HeroSize, HudStyle.Ink, bold: true);
            clock.name = ClockName;
            clock.style.marginTop = HudStyle.S1;
            clock.style.whiteSpace = WhiteSpace.NoWrap;
            cell.Add(clock);

            return cell;
        }

        /// <summary>
        /// A caption over a number, right-aligned in a fixed column so a changing digit moves nothing
        /// beside it.
        /// </summary>
        private static VisualElement Cell(out Label caption, out Label value, LocKey name,
                                          float valueSize, float width)
        {
            var cell = HudStyle.Column();
            cell.style.width = width;
            cell.style.flexShrink = 0f;
            cell.style.alignItems = Align.FlexEnd;
            cell.style.marginLeft = HudStyle.S6;

            caption = HudStyle.Caption(name);
            caption.style.unityTextAlign = TextAnchor.MiddleRight;
            cell.Add(caption);

            value = HudStyle.Text(string.Empty, valueSize, HudStyle.Ink);
            value.style.marginTop = HudStyle.S1;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.whiteSpace = WhiteSpace.NoWrap;
            cell.Add(value);

            return cell;
        }

        /// <summary>
        /// The band that appears when the clock runs out.
        /// <para>
        /// A band rather than a colour on the clock, because "the shift is over" is a sentence and the
        /// clock cell is a number: the sentence needs a full-width line to be readable in German as
        /// well as English, and putting it in the cell would either wrap it into the bar or size the
        /// bar to the longest translation of it. <see cref="HudStyle.ContentTop"/> already leaves room
        /// for it, so the standing-orders card never ends up underneath it.
        /// </para>
        /// </summary>
        private VisualElement BuildAlert()
        {
            var band = HudStyle.Row();
            band.name = AlertName;
            band.style.height = HudStyle.HeaderAlertHeight;
            band.style.justifyContent = Justify.Center;
            band.style.backgroundColor = new StyleColor(HudStyle.Plate);
            band.style.borderBottomWidth = 1;
            band.style.borderBottomColor = new StyleColor(HudStyle.Line);
            band.style.display = DisplayStyle.None;

            var label = HudStyle.Text(ScreenStrings.HudShiftOver, HudStyle.BodySize, HudStyle.Warn,
                                      bold: true);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            band.Add(label);
            fixedLines.Add((label, ScreenStrings.HudShiftOver));

            return band;
        }

        /// <summary>
        /// Draw whatever the lab now says, or nothing at all.
        /// <para>
        /// A null <paramref name="lab"/> is a real answer — there is a window during scene load where a
        /// client has a HUD and no replicated view yet — and the honest thing to show then is no
        /// header rather than a bar full of zeroes.
        /// </para>
        /// </summary>
        public void Refresh(ILabView lab)
        {
            if (lab == null)
            {
                Root.style.display = DisplayStyle.None;
                return;
            }

            Root.style.display = DisplayStyle.Flex;

            int day = lab.Day;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(lab.DaySecondsRemaining));
            bool shiftOver = lab.ShiftOver;
            int openCount = lab.OpenSampleCount;
            int money = Mathf.RoundToInt(lab.Money);
            int rep = Mathf.RoundToInt(lab.Reputation);
            int solvent = Mathf.RoundToInt(lab.SolventUnits);
            int certified = lab.ReferenceStandards;

            bool first = !drawnAnything;
            drawnAnything = true;

            // A language change invalidates every string on the bar, including the ones a reading
            // would not otherwise touch. Compared against the active language rather than hooked to an
            // event, because there is no event and a string comparison a frame is free.
            if (!string.Equals(drawnLanguage, Loc.Language, System.StringComparison.Ordinal))
            {
                drawnLanguage = Loc.Language;
                first = true;

                foreach (var (label, key) in fixedLines) label.text = key.Text;
            }

            if (first || day != drawnDay)
            {
                drawnDay = day;
                shiftCaption.text = ScreenStrings.HudShiftCaption.Format(("day", day));
            }

            if (first || seconds != drawnSeconds || shiftOver != drawnShiftOver)
            {
                drawnSeconds = seconds;
                int shown = shiftOver ? 0 : seconds;
                clock.text = $"{shown / 60:D2}:{shown % 60:D2}";
            }

            if (first || shiftOver != drawnShiftOver)
            {
                drawnShiftOver = shiftOver;

                // Colour, weight and a whole sentence in the band below: three channels, and not one
                // of them the signal set (§2.2, hard rule 4).
                clock.style.color = new StyleColor(shiftOver ? HudStyle.Warn : HudStyle.Ink);
                alert.style.display = shiftOver ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (first || openCount != drawnOpen)
            {
                drawnOpen = openCount;

                // Two keys rather than a stem and an "s": a translator handed the letter cannot
                // inflect the noun to agree with the count.
                open.text = openCount == 1
                    ? ScreenStrings.HudOpenSamplesOne.Text
                    : ScreenStrings.HudOpenSamplesMany.Format(("count", openCount));

                // Nothing open is not a warning and not an achievement — it is simply nothing to act
                // on, so the cell steps back. The numeral says it too; the colour is never alone.
                open.style.color = new StyleColor(openCount == 0 ? HudStyle.Dim : HudStyle.Ink);
            }

            if (first || money != drawnMoney)
            {
                drawnMoney = money;
                bool overdrawn = money < 0;

                balance.text = ScreenStrings.HudBalanceValue.Format(
                    ("sign", overdrawn ? "−" : string.Empty),
                    ("amount", Mathf.Abs(money).ToString("N0")));

                balanceCaption.text = overdrawn
                    ? ScreenStrings.HudBalanceOverdrawnCaption
                    : ScreenStrings.HudBalanceCaption;

                balance.style.unityFontStyleAndWeight = overdrawn ? FontStyle.Bold : FontStyle.Normal;
            }

            if (first || rep != drawnReputation)
            {
                drawnReputation = rep;
                reputation.text = rep.ToString();
            }

            if (first || solvent != drawnDrum)
            {
                drawnDrum = solvent;
                drum.text = solvent.ToString();
            }

            if (first || certified != drawnStandards)
            {
                drawnStandards = certified;
                standards.text = certified.ToString();
            }
        }
    }
}
