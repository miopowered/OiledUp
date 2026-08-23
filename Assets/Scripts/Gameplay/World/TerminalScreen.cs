using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The terminal: sample list, results, verdict filing, end-of-day report.
    /// <para>
    /// This screen is where the game actually happens. Everything it shows comes from
    /// <see cref="SampleState"/> — measured values, thresholds, the player's own notes — and
    /// nothing from ground truth. The fault name appears exactly once: in the end-of-day report,
    /// after the consequence has already landed (§4.3).
    /// </para>
    /// <para>
    /// There is one of these <i>per player</i>, not one per terminal. Everything it shows is read
    /// from this process's <see cref="LabRuntime"/> and everything it does is a §3.1 request from
    /// <see cref="interactor"/>, so two players at the desk are two keyboards onto one host record
    /// rather than two authorities — see <see cref="TerminalStation"/> for how a station finds the
    /// one belonging to whoever walked up to it.
    /// </para>
    /// </summary>
    public sealed class TerminalScreen : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInteractor interactor;

        private SampleId selected = SampleId.None;
        private RootCauseDef pendingCause;
        private IReadOnlyList<ConsequenceReport> reportOverlay;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// The panel to draw into, or null when there is none to draw into.
        /// <para>
        /// A <see cref="UIDocument"/> only owns a <c>rootVisualElement</c> while it is enabled, and a
        /// remote player's screen is switched off with the rest of that avatar. Caching one in
        /// <c>Awake</c> therefore throws on a replica and goes stale if the document is ever
        /// re-enabled, because the panel that comes back is a new element.
        /// </para>
        /// </summary>
        private VisualElement Root
        {
            get
            {
                if (document == null) document = GetComponent<UIDocument>();
                return document != null ? document.rootVisualElement : null;
            }
        }

        private void Awake()
        {
            // Whoever this screen hangs under is whose input it suspends while open. Wiring still
            // wins if the scene set it; a player prefab has no build step left to do the wiring.
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (interactor == null) interactor = GetComponentInParent<PlayerInteractor>();

            var root = Root;
            if (root != null) root.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Never leave a player locked out of their own body. If this screen is switched off while
        /// open — the avatar going away, a scene teardown — the walk-and-look controls it disabled
        /// have to come back, because nothing else will hand them over.
        /// </summary>
        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        public void Open()
        {
            var root = Root;
            if (root == null) return;

            IsOpen = true;
            root.style.display = DisplayStyle.Flex;
            PlayerController.SetCursorLocked(false);
            if (player != null) player.enabled = false;
            if (interactor != null) interactor.enabled = false;
            Rebuild();
        }

        public void Close()
        {
            IsOpen = false;
            reportOverlay = null;

            var root = Root;
            if (root != null) root.style.display = DisplayStyle.None;

            PlayerController.SetCursorLocked(true);
            if (player != null) player.enabled = true;
            if (interactor != null) interactor.enabled = true;
        }

        // -- Asking -----------------------------------------------------------------------------------

        /// <summary>
        /// Send a terminal action and redraw once it has been answered.
        /// <para>
        /// Every button on this screen goes through here, including on a host. Booking a tag in,
        /// filing a verdict, ordering stock and ending the day are all §3.1 requests the host
        /// validates — the terminal is a keyboard, not an authority — and routing the local player
        /// down the same path is what stops "works in single player" and "works in co-op" from
        /// becoming two different code paths that drift.
        /// </para>
        /// <para>
        /// A refusal goes into a label beside the button that refused rather than into a toast,
        /// because on this screen there is somewhere to put it and the sentence belongs next to the
        /// thing it is about. It is shown verbatim: these were written for the player by the gateway
        /// that produced them (§9), and re-phrasing one here would put a second voice in front of the
        /// rule.
        /// </para>
        /// </summary>
        private void Ask(LabCommand command, Label refusal, Action onAccepted = null)
        {
            LabCommands.Send(interactor, command, result =>
            {
                if (!result.Accepted)
                {
                    if (refusal != null) refusal.text = result.Refusal;
                    else if (interactor != null) interactor.Say(result.Refusal);
                    return;
                }

                onAccepted?.Invoke();
                Rebuild();
            });
        }

        // -- Build ------------------------------------------------------------------------------------

        private void Rebuild()
        {
            var root = Root;
            if (root == null) return;

            var records = ReadRecords();
            root.Clear();

            root.style.flexGrow = 1f;
            root.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.07f, 0.97f));
            root.style.paddingLeft = 24;
            root.style.paddingRight = 24;
            root.style.paddingTop = 18;
            root.style.paddingBottom = 18;

            if (records == null) { root.Add(WaitingForTheLab()); return; }

            root.Add(Header(records));

            var host = HostLab;
            if (reportOverlay != null && host != null) { root.Add(ReportPanel(host)); return; }

            // The one thing a joined desk cannot draw. A ConsequenceReport names the fault (§4.3) and
            // nothing puts one on the wire, so the summary stays where the lab is rather than being
            // half-reconstructed here.
            if (!records.IsLive)
            {
                root.Add(Tiny("Joined terminal — the end-of-day report is drawn on the host's screen.",
                    SignalPalette.Dim));
            }

            var body = Row();
            body.style.flexGrow = 1f;
            body.style.marginTop = 12;
            body.Add(SampleList(records));
            body.Add(Detail(records));
            body.Add(CalibrationPanel(records));
            root.Add(body);
        }

        /// <summary>This process's own lab, or null on a joined client. Nothing else asks which side it is on.</summary>
        private static LabState HostLab =>
            LabRuntime.Instance != null ? LabRuntime.Instance.Lab : null;

        /// <summary>
        /// Gather what this desk is looking at.
        /// <para>
        /// A process that simulates reads its own <see cref="LabState"/> and always did — single
        /// player has no wire and must not acquire one. A client reads the same shapes off
        /// <see cref="RecordFeed"/>, rebuilt from what the host published. Everything below this line
        /// draws <see cref="LabRecords"/> and cannot tell which it was handed, which is what stops
        /// "works in single player" and "works in co-op" becoming two screens.
        /// </para>
        /// </summary>
        private static LabRecords ReadRecords()
        {
            var lab = HostLab;
            if (lab != null) return LabRecords.FromHost(lab);

            var feed = RecordFeed.Source;
            return feed != null ? feed.ReadLab() : null;
        }

        /// <summary>
        /// No lab and no feed: the session has not come up, or this screen outlived it. Said out loud
        /// rather than drawn as an empty desk, because a terminal with nothing on it reads as broken.
        /// </summary>
        private VisualElement WaitingForTheLab()
        {
            var panel = Panel();
            panel.style.flexGrow = 1f;
            panel.Add(SectionTitle("SAMPLE TERMINAL"));

            panel.Add(Dim(LabView.Current == null
                ? "Waiting for the lab. If this does not clear, the session never came up."
                : "Waiting for the first publish from the host. The instruments in the room are " +
                  "already readable; this desk fills in a moment."));

            var close = new Button(Close) { text = "CLOSE  (Esc)" };
            StyleButton(close, SignalPalette.PanelSoft);
            close.style.marginTop = 14;
            close.style.marginLeft = 0;
            close.style.alignSelf = Align.FlexStart;
            panel.Add(close);

            return panel;
        }

        private VisualElement Header(LabRecords records)
        {
            var bar = Row();
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.alignItems = Align.Center;

            var left = new Label($"SAMPLE TERMINAL — DAY {records.Day}");
            left.style.fontSize = 18;
            left.style.color = new StyleColor(SignalPalette.Ink);
            left.style.unityFontStyleAndWeight = FontStyle.Bold;
            bar.Add(left);

            var right = Row();
            right.style.alignItems = Align.Center;

            var money = new Label($"£{records.Money:N0}    REP {records.Reputation:F0}");
            money.style.fontSize = 15;
            money.style.color = new StyleColor(SignalPalette.Dim);
            money.style.marginRight = 16;
            right.Add(money);

            var endDay = new Button(() => Ask(LabCommand.EndDay(), null, () =>
            {
                // The reports are read back off the lab rather than returned by the command: a
                // client has no LabState to read them from and the day summary is replicated
                // separately, so making the command carry them would put a list of consequences on
                // the wire for the one process that already has it.
                var host = HostLab;
                reportOverlay = host != null ? host.LastReports : null;
            }))
            { text = "END DAY" };
            StyleButton(endDay, SignalPalette.Accent);
            right.Add(endDay);

            var close = new Button(Close) { text = "CLOSE  (Esc)" };
            StyleButton(close, SignalPalette.PanelSoft);
            right.Add(close);

            bar.Add(right);
            return bar;
        }

        private VisualElement SampleList(LabRecords records)
        {
            var panel = Panel();
            panel.style.width = 320;
            panel.style.marginRight = 12;

            panel.Add(SectionTitle("OPEN SAMPLES"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            var open = records.Open;
            if (open.Count == 0) scroll.Add(Dim("Nothing open. End the day."));

            foreach (var sample in open)
            {
                var row = new Button(() => { selected = sample.Id; pendingCause = null; Rebuild(); });
                row.style.flexDirection = FlexDirection.Column;
                row.style.alignItems = Align.FlexStart;
                row.style.marginBottom = 4;
                row.style.paddingTop = 7;
                row.style.paddingBottom = 7;
                row.style.paddingLeft = 9;
                row.style.paddingRight = 9;
                row.style.backgroundColor = new StyleColor(
                    sample.Id == selected ? SignalPalette.PanelSoft : new Color(0.09f, 0.10f, 0.11f));
                row.style.borderLeftWidth = 3;
                row.style.borderLeftColor = new StyleColor(
                    sample.Results.Count == 0 ? SignalPalette.Off : SignalPalette.For(sample.WorstReading()));
                LabHud.Round(row, 3);

                // RecordTag, not EquipmentTag: the terminal shows what the player typed, never the
                // paper label. Printing the true tag here would make a mis-log both impossible to
                // commit and trivial to spot, which deletes the §5.1 logging step.
                var tag = new Label(sample.RecordTag);
                tag.style.fontSize = 14;
                tag.style.color = new StyleColor(
                    sample.IsLogged ? SignalPalette.Ink : SignalPalette.Dim);
                row.Add(tag);

                // The id is printed because an instrument's own screen captions a run with it — the
                // paper label is on the bottle, not on a display (§5.1) — so this is what lets a
                // player match the numbers on the machine to the record on the desk.
                var meta = new Label(
                    $"{sample.Id} · {ProfileName(sample)} · {sample.VolumeMl:F0} ml · " +
                    $"{sample.Results.Count} run{(sample.Results.Count == 1 ? "" : "s")}");
                meta.style.fontSize = 11;
                meta.style.color = new StyleColor(SignalPalette.Dim);
                row.Add(meta);

                scroll.Add(row);
            }

            panel.Add(scroll);
            panel.Add(InstrumentsPanel(records));
            return panel;
        }

        /// <summary>
        /// The fluid's name, or a stand-in. Null is not expected on either side — both processes ship
        /// the same tables — but a screen is the wrong place to find out that a catalog is missing a
        /// profile, and an exception here would take the whole desk down mid-shift.
        /// </summary>
        private static string ProfileName(SampleState sample) =>
            sample?.Profile != null ? sample.Profile.DisplayName : "unknown fluid";

        /// <summary>
        /// Per-instrument state, including the last solvent blank.
        /// <para>
        /// A blank belongs to the machine, not to any sample, so it has nowhere to live in the
        /// results table — and without this the player pays for a blank and never sees it. §5.2 only
        /// works because contamination is checkable; an unreadable tell is the same as no tell.
        /// </para>
        /// </summary>
        private VisualElement InstrumentsPanel(LabRecords records)
        {
            var box = new VisualElement();
            box.style.marginTop = 10;
            box.style.paddingTop = 8;
            box.style.borderTopWidth = 1;
            box.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            box.Add(SectionTitle("INSTRUMENTS"));

            foreach (var machine in records.Instruments)
            {
                if (machine == null) continue;

                var header = new Label($"{machine.DisplayName} · {machine.RunsSinceFlush} run(s) since flush");
                header.style.fontSize = 11;
                header.style.color = new StyleColor(SignalPalette.Ink);
                box.Add(header);

                if (machine.LastBlank == null)
                {
                    box.Add(Tiny("no blank run — residue unknown", SignalPalette.Caution));
                    continue;
                }

                string residue = "";
                foreach (var kv in machine.LastBlank.Values)
                {
                    if (kv.Value <= 0.0001f) continue;
                    if (residue.Length > 0) residue += "  ";
                    residue += $"{kv.Key} {kv.Value:0.###}";
                }

                box.Add(residue.Length == 0
                    ? Tiny($"blank day {machine.LastBlankDay}: clean", SignalPalette.Normal)
                    : Tiny($"blank day {machine.LastBlankDay}: {residue}", SignalPalette.Caution));
            }

            box.Add(SolventRow(records));
            return box;
        }

        /// <summary>
        /// The §5.1 book-in step: type the tank tag off the vial's paper label.
        /// <para>
        /// Nothing is prefilled and nothing is checked against
        /// <see cref="SampleState.EquipmentTag"/>. A wrong tag is accepted exactly as readily as a
        /// right one — that is the mechanic, not an oversight. The tell is physical: the label is on
        /// the bottle, so catching your own mistake means walking back and reading it.
        /// </para>
        /// Amendable while the sample is only logged, fixed once work starts. Hard rule 3 punishes
        /// never checking, not the typo itself.
        /// </summary>
        private VisualElement BookInRow(SampleState sample)
        {
            var box = new VisualElement();
            box.style.marginTop = 4;
            box.style.marginBottom = 6;

            bool amendable = sample.Stage <= SampleStage.Logged;

            if (!amendable)
            {
                if (sample.IsLogged) return box;

                box.Add(Tiny("Not booked in, and work has already started — the record is closed.",
                    SignalPalette.Caution));
                return box;
            }

            var row = Row();
            row.style.alignItems = Align.Center;

            var field = new TextField { value = sample.IsLogged ? sample.RecordTag : "" };
            field.style.width = 220;
            field.style.marginRight = 6;
            row.Add(field);

            var refusal = Tiny("", SignalPalette.Caution);

            var book = new Button(() => Ask(LabCommand.BookIn(sample.Id, field.value), refusal))
            { text = sample.IsLogged ? "AMEND TAG" : "BOOK IN" };

            StyleButton(book, SignalPalette.Accent);
            row.Add(book);

            box.Add(row);
            box.Add(sample.IsLogged
                ? Tiny("Amendable until the first run. Check it against the label on the bottle.",
                    SignalPalette.Dim)
                : Tiny("Type the tank tag from the vial's label. Nothing will run until it is booked in.",
                    SignalPalette.Dim));
            box.Add(refusal);

            return box;
        }

        /// <summary>
        /// Solvent stock and a way to restock it.
        /// <para>
        /// §5.2 wants skipping the flush to be tempting; it must never be <i>compulsory</i>. A run
        /// starts with twelve units and spends one per flush, so without a way to buy more, a
        /// twenty-day contract across five instruments runs dry within days and residue then
        /// accumulates with nothing the player can do about it.
        /// </para>
        /// Ordering is paperwork, so it lives here rather than being a physical action. The time
        /// cost that makes §9 work sits on the flush itself, which is the part you are tempted to
        /// skip — not on buying the bottle.
        /// </summary>
        private VisualElement SolventRow(LabRecords records)
        {
            const int packSize = 10;

            var row = Row();
            row.style.marginTop = 8;
            row.style.paddingTop = 8;
            row.style.alignItems = Align.Center;
            row.style.borderTopWidth = 1;
            row.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));

            float cost = records.SolventUnitCost * packSize;
            bool dry = records.SolventUnits < 1f;

            var stock = new Label($"SOLVENT  {records.SolventUnits:F0} unit(s)");
            stock.style.fontSize = 12;
            stock.style.width = 220;

            // Caution only when actually out. A low-but-usable stock is information, not an alarm,
            // and hard rule 4 keeps signal colours meaning verdict state.
            stock.style.color = new StyleColor(dry ? SignalPalette.Caution : SignalPalette.Ink);
            row.Add(stock);

            var buy = new Button(() => Ask(LabCommand.OrderSolvent(packSize), null))
            { text = $"ORDER {packSize}  (£{cost:N0})" };

            StyleButton(buy, SignalPalette.PanelSoft);

            // Greyed out from the balance this screen is already showing. No round trip: the same
            // affordability check runs again on the host when the order lands, so the worst a stale
            // balance can do is turn a press into a refusal.
            buy.SetEnabled(records.Money >= cost);
            row.Add(buy);

            if (records.Money < cost)
                row.Add(Tiny("  cannot afford a restock", SignalPalette.Dim));

            return row;
        }

        /// <summary>
        /// The §5.3 column: certified standards to order, what the last one said about each
        /// instrument, and the archive a revealed drift has put in doubt.
        /// <para>
        /// Both halves have to be here or neither works. An instrument that silently scales every
        /// reading is a fair mechanic only because a standard measures it (hard rule 3), and the
        /// retroactive list is escalating pressure rather than an arbitrary punishment only because
        /// the records it names can be re-opened — while there is still oil in the bottle to re-open
        /// them with.
        /// </para>
        /// </summary>
        private VisualElement CalibrationPanel(LabRecords records)
        {
            const int packSize = 3;

            var panel = Panel();
            panel.style.width = 340;
            panel.style.marginLeft = 12;
            panel.Add(SectionTitle("CALIBRATION"));

            var order = Row();
            order.style.alignItems = Align.Center;
            order.style.marginBottom = 6;

            float cost = records.ReferenceStandardUnitCost * packSize;

            var stock = new Label($"STANDARDS  {records.ReferenceStandards} ampoule(s)");
            stock.style.fontSize = 12;
            stock.style.flexGrow = 1f;

            // Caution only when the tell is actually unavailable. A low stock is information, and
            // hard rule 4 keeps the signal colours meaning verdict state.
            stock.style.color = new StyleColor(
                records.ReferenceStandards < 1 ? SignalPalette.Caution : SignalPalette.Ink);
            order.Add(stock);

            var buy = new Button(() => Ask(LabCommand.OrderStandards(packSize), null))
            { text = $"ORDER {packSize}  (£{cost:N0})" };

            StyleButton(buy, SignalPalette.PanelSoft);
            buy.SetEnabled(records.Money >= cost);
            order.Add(buy);
            panel.Add(order);

            // Where the certificate comes from, so the numbers are checkable before one is ever run.
            panel.Add(Tiny(
                $"{records.StandardId} — certified at the healthy baselines the manual publishes.",
                SignalPalette.Dim));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            foreach (var machine in records.Instruments)
            {
                if (machine != null) scroll.Add(InstrumentCheck(machine));
            }

            scroll.Add(SuspectArchive(records));
            panel.Add(scroll);
            return panel;
        }

        /// <summary>
        /// One instrument's certificate against its readout. The per-element rows are printed rather
        /// than summarised away, because the average error underneath them is only trustworthy if the
        /// numbers it came from are on the same screen.
        /// </summary>
        private static VisualElement InstrumentCheck(InstrumentRecord machine)
        {
            var box = new VisualElement();
            box.style.marginBottom = 8;

            var header = new Label(machine.DisplayName);
            header.style.fontSize = 11;
            header.style.color = new StyleColor(SignalPalette.Ink);
            box.Add(header);

            var check = machine.Check;
            if (check == null)
            {
                box.Add(Tiny("no standard run today — drift unknown", SignalPalette.Caution));
            }
            else
            {
                box.Add(Tiny(
                    $"{check.StandardId} day {check.Day}: reads {Signed(check.ErrorFraction)}" +
                    (check.IsOutOfTolerance ? "  OUT OF TOLERANCE" : "  in tolerance"),
                    check.IsOutOfTolerance ? SignalPalette.Caution : SignalPalette.Dim));

                foreach (var line in check.Lines)
                {
                    var row = Row();
                    row.Add(Cell(line.Element.DisplayName, 130, SignalPalette.Dim));
                    row.Add(Cell($"cert {line.Certified:0.###}", 90, SignalPalette.Dim));
                    row.Add(Cell($"read {line.Measured:0.###}", 90, SignalPalette.Ink));
                    row.Add(Cell(Signed(line.ErrorFraction), 60, SignalPalette.Dim));
                    box.Add(row);
                }
            }

            if (machine.LastCalibration.HasValue)
            {
                var last = machine.LastCalibration.Value;
                box.Add(Tiny(
                    $"calibrated day {last.Day}: corrected {Signed(last.CorrectedDrift)}, " +
                    $"{last.FlaggedResults} run(s) now suspect across {last.AffectedArchived} filed record(s)",
                    SignalPalette.Dim));
            }

            return box;
        }

        /// <summary>
        /// §5.3's retroactive list: every closed record whose numbers came off a drifting instrument.
        /// <para>
        /// The refusal is the sharp end of this and is therefore rendered in full rather than hidden
        /// behind a disabled button. A record with no oil left cannot be checked by anyone, ever, and
        /// being told exactly that — with the millilitres you have and the millilitres you needed — is
        /// what turns a bad reading into a decision the player remembers making.
        /// </para>
        /// </summary>
        private VisualElement SuspectArchive(LabRecords records)
        {
            var box = new VisualElement();
            box.style.marginTop = 8;
            box.style.paddingTop = 8;
            box.style.borderTopWidth = 1;
            box.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            box.Add(SectionTitle("RECORDS IN DOUBT"));

            var suspect = records.InDoubt;
            if (suspect.Count == 0)
            {
                box.Add(Dim("No filed record rests on a drifting instrument."));
                return box;
            }

            foreach (var sample in suspect)
            {
                float need = records.SmallestReTestDraw(sample);

                var card = new VisualElement();
                card.style.marginBottom = 6;

                var title = new Label(
                    $"{sample.RecordTag} — filed {sample.FiledVerdict.Value.ToString().ToUpperInvariant()} " +
                    $"day {sample.FiledOnDay}");
                title.style.fontSize = 12;
                title.style.whiteSpace = WhiteSpace.Normal;
                title.style.color = new StyleColor(SignalPalette.For(sample.FiledVerdict.Value));
                card.Add(title);

                card.Add(Tiny(
                    float.IsInfinity(need)
                        ? $"{sample.VolumeMl:F1} ml left · no instrument here can repeat those tests"
                        : $"{sample.VolumeMl:F1} ml left · a re-test needs {need:F0} ml",
                    SignalPalette.Dim));

                var refusal = Tiny("", SignalPalette.Caution);

                var reopen = new Button(() => Ask(LabCommand.ReopenSuspect(sample.Id), refusal))
                { text = "RE-OPEN FOR RE-TEST" };

                StyleButton(reopen, SignalPalette.PanelSoft);
                reopen.style.marginLeft = 0;
                card.Add(reopen);
                card.Add(refusal);

                box.Add(card);
            }

            return box;
        }

        private static string Signed(float fraction) =>
            $"{(fraction >= 0f ? "+" : "−")}{Mathf.Abs(fraction) * 100f:0.#}%";

        private static Label Tiny(string text, Color colour)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = new StyleColor(colour);
            label.style.marginBottom = 4;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private VisualElement Detail(LabRecords records)
        {
            var panel = Panel();
            panel.style.flexGrow = 1f;

            var sample = selected.IsValid ? records.Sample(selected) : null;
            if (sample == null)
            {
                panel.Add(Dim("Select a sample."));
                return panel;
            }

            panel.Add(SectionTitle(sample.RecordTag));
            panel.Add(BookInRow(sample));

            var sub = new Label(
                $"{ProfileName(sample)} · {(sample.Profile != null ? sample.Profile.BaseOilGrade : "—")} · " +
                $"{sample.HoursSinceOilChange:F0} h on the oil · {sample.VolumeMl:F1} ml remaining");
            sub.style.fontSize = 12;
            sub.style.color = new StyleColor(SignalPalette.Dim);
            sub.style.marginBottom = 6;
            panel.Add(sub);

            if (sample.IsResample)
            {
                var history = new Label($"RE-DRAW of {sample.ResampleOf} — you filed MONITOR on this unit.");
                history.style.fontSize = 12;
                history.style.color = new StyleColor(SignalPalette.Caution);
                history.style.marginBottom = 6;
                panel.Add(history);
            }

            if (!string.IsNullOrEmpty(sample.FieldTechNote))
            {
                var note = new Label($"Field note: \"{sample.FieldTechNote}\"");
                note.style.fontSize = 12;
                note.style.color = new StyleColor(SignalPalette.Dim);
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginBottom = 8;
                panel.Add(note);
            }

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            scroll.Add(ResultsTable(sample));
            scroll.Add(RunLog(sample));
            panel.Add(scroll);

            panel.Add(VerdictBar(records, sample));
            return panel;
        }

        /// <summary>
        /// Results grouped by <see cref="ElementCategory"/>, in the same order the manual indexes
        /// them (<see cref="BookContent.CategoryOrder"/>).
        /// <para>
        /// A flat list in profile-declaration order makes the fastest strategy "find the coloured
        /// row, look up that element" — which is table lookup, and hard rule 1 says understanding
        /// has to beat memorisation. Faults are patterns across a subsystem: additive depletion plus
        /// a rising TAN plus a falling flash point is a different story from any one of them alone.
        /// Grouping is what makes that pattern visible, and the per-group summary is what lets you
        /// see which subsystem is unhappy without adding rows up yourself.
        /// </para>
        /// </summary>
        private VisualElement ResultsTable(SampleState sample)
        {
            var box = new VisualElement();
            box.style.marginBottom = 10;

            if (sample.Results.Count == 0)
            {
                box.Add(Dim("No results yet. Run this sample on an instrument."));
                return box;
            }

            // Nothing to score against. Both processes ship the same tables, so this is a broken
            // catalog rather than a state the game has — but the desk saying so beats it throwing
            // mid-shift with a panel of numbers already on the glass.
            if (sample.Profile == null)
            {
                box.Add(Dim("This fluid's profile is missing from the content catalog, so nothing " +
                            "here can be scored. Rebuild definitions."));
                return box;
            }

            box.Add(ColumnHeader());

            int rows = 0;
            foreach (var category in BookContent.CategoryOrder)
                rows += AddCategory(box, sample, category);

            // Results exist but none of them measure anything this profile scores. Rare, but a bare
            // column header with nothing under it reads as a broken screen rather than an empty one.
            if (rows == 0) box.Add(Dim("Nothing measured yet that this profile scores."));

            return box;
        }

        private static int AddCategory(VisualElement box, SampleState sample, ElementCategory category)
        {
            var rows = new List<VisualElement>();
            var worst = ReadingSeverity.Normal;
            int flagged = 0;

            foreach (var threshold in sample.Profile.Thresholds)
            {
                if (threshold?.Element == null || threshold.Element.Category != category) continue;
                if (!sample.TryGetLatest(threshold.Element.Id, out float value, out var source)) continue;

                var severity = threshold.Evaluate(value);
                if (severity > worst) worst = severity;
                if (severity != ReadingSeverity.Normal) flagged++;

                rows.Add(ResultRow(threshold, value, severity, source));
            }

            // Same rule the manual uses: a category with nothing in it gets no heading. WearMetal is
            // empty in the heat-treatment domain, and a blank section reads as a bug.
            if (rows.Count == 0) return 0;

            box.Add(CategoryHeading(category, worst, flagged));
            foreach (var row in rows) box.Add(row);
            return rows.Count;
        }

        private static VisualElement CategoryHeading(ElementCategory category, ReadingSeverity worst, int flagged)
        {
            var row = Row();
            row.style.marginTop = 10;
            row.style.marginBottom = 3;

            var title = new Label(BookContent.Readable(category).ToUpperInvariant());
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(SignalPalette.Accent);
            title.style.width = 320;
            row.Add(title);

            // Dim rather than green when everything is in band: signal colours are for things that
            // need attention, and four green "all normal" tags would drown the one that is not.
            row.Add(flagged == 0
                ? Cell("all normal", 240, SignalPalette.Dim)
                : Cell($"{flagged} outside limit{(flagged == 1 ? "" : "s")}", 240, SignalPalette.For(worst)));

            return row;
        }

        private static VisualElement ResultRow(Threshold threshold, float value,
                                               ReadingSeverity severity, TestResult source)
        {
            var row = Row();
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.05f));

            row.Add(Cell(threshold.Element.DisplayName, 190, SignalPalette.Ink));
            row.Add(Cell($"{value:0.###} {threshold.Element.Unit}", 130, SignalPalette.Ink));
            row.Add(Cell(LimitText(threshold), 150, SignalPalette.Dim));
            row.Add(Cell(SignalPalette.Label(severity), 90, SignalPalette.For(severity)));
            row.Add(Cell(source != null && source.Suspect ? "SUSPECT" : "", 80, SignalPalette.Caution));

            return row;
        }

        private static string LimitText(Threshold t) => t.Mode switch
        {
            ThresholdMode.UpperLimit => $"normal ≤ {t.NormalLimit:0.###}",
            ThresholdMode.LowerLimit => $"normal ≥ {t.NormalLimit:0.###}",
            ThresholdMode.DeviationBand => $"{t.Baseline:0.#} ±{t.NormalLimit * 100f:0}%",
            _ => ""
        };

        private VisualElement RunLog(SampleState sample)
        {
            var box = new VisualElement();
            if (sample.Results.Count == 0) return box;

            box.Add(SectionTitle("RUNS"));
            foreach (var r in sample.Results)
            {
                var line = new Label(
                    $"day {r.DayRun} · {r.MachineId}{(r.IsBlank ? " · BLANK" : "")} · " +
                    $"{r.VolumeConsumedMl:F0} ml · £{r.Cost:F0}{(r.Suspect ? " · SUSPECT" : "")}");
                line.style.fontSize = 11;
                line.style.color = new StyleColor(r.Suspect ? SignalPalette.Caution : SignalPalette.Dim);
                box.Add(line);
            }
            return box;
        }

        private VisualElement VerdictBar(LabRecords records, SampleState sample)
        {
            var box = new VisualElement();
            box.style.marginTop = 10;
            box.style.paddingTop = 10;
            box.style.borderTopWidth = 1;
            box.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));

            var causes = new List<string> { "(no root cause)" };
            foreach (var c in records.Causes)
            {
                if (c != null) causes.Add(c.DisplayName);
            }

            var dropdown = new DropdownField("Root cause", causes, 0);
            dropdown.style.marginBottom = 8;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                pendingCause = null;
                foreach (var c in records.Causes)
                {
                    if (c != null && c.DisplayName == evt.newValue) pendingCause = c;
                }
            });
            box.Add(dropdown);

            var buttons = Row();
            buttons.Add(VerdictButton(sample, Verdict.Normal, "FILE NORMAL"));
            buttons.Add(VerdictButton(sample, Verdict.Monitor, "FILE MONITOR"));
            buttons.Add(VerdictButton(sample, Verdict.Critical, "FILE CRITICAL — PULL"));
            box.Add(buttons);

            return box;
        }

        /// <summary>
        /// File the call. The root cause travels as its id rather than as the definition, because a
        /// <c>RootCauseDef</c> is a <c>ScriptableObject</c> living in this process and every client
        /// already ships the same content — an id is the only part of it that means anything on the
        /// other side of the wire.
        /// </summary>
        private Button VerdictButton(SampleState sample, Verdict verdict, string text)
        {
            var button = new Button(() =>
            {
                var command = LabCommand.FileVerdict(sample.Id, verdict,
                    pendingCause != null ? pendingCause.Id : null);

                Ask(command, null, () =>
                {
                    selected = SampleId.None;
                    pendingCause = null;
                });
            })
            { text = text };

            StyleButton(button, SignalPalette.PanelSoft);
            button.style.color = new StyleColor(SignalPalette.For(verdict));
            button.style.flexGrow = 1f;
            return button;
        }

        private VisualElement ReportPanel(LabState lab)
        {
            var panel = Panel();
            panel.style.flexGrow = 1f;
            panel.style.marginTop = 12;
            panel.Add(SectionTitle($"END OF DAY {lab.Day}"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            if (reportOverlay.Count == 0)
                scroll.Add(Dim("Nothing came due today."));

            float net = 0f;
            foreach (var report in reportOverlay)
            {
                net += report.MoneyDelta;

                var card = new VisualElement();
                card.style.marginBottom = 6;
                card.style.paddingTop = 8;
                card.style.paddingBottom = 8;
                card.style.paddingLeft = 10;
                card.style.paddingRight = 10;
                card.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.11f));
                card.style.borderLeftWidth = 3;
                card.style.borderLeftColor = new StyleColor(
                    report.IsGood ? SignalPalette.Normal : SignalPalette.Critical);
                LabHud.Round(card, 3);

                var headline = new Label(report.Headline);
                headline.style.fontSize = 13;
                headline.style.color = new StyleColor(SignalPalette.Ink);
                headline.style.whiteSpace = WhiteSpace.Normal;
                card.Add(headline);

                var money = new Label(
                    $"{(report.MoneyDelta >= 0 ? "+" : "−")}£{Mathf.Abs(report.MoneyDelta):N0}" +
                    (report.RootCauseCorrect ? "   root cause bonus" : ""));
                money.style.fontSize = 12;
                money.style.color = new StyleColor(
                    report.MoneyDelta >= 0 ? SignalPalette.Normal : SignalPalette.Critical);
                card.Add(money);

                scroll.Add(card);
            }

            panel.Add(scroll);

            var total = new Label($"NET  {(net >= 0 ? "+" : "−")}£{Mathf.Abs(net):N0}    " +
                                  $"BALANCE £{lab.Economy.Money:N0}");
            total.style.fontSize = 15;
            total.style.marginTop = 8;
            total.style.color = new StyleColor(net >= 0 ? SignalPalette.Normal : SignalPalette.Critical);
            panel.Add(total);

            // §1.2: a run ends on contract completion or financial failure. Without this the
            // fixed-length contract never resolves and the game has no win or loss state at all.
            if (lab.IsRunOver)
            {
                bool bankrupt = lab.Economy.IsBankrupt;

                var verdict = new Label(bankrupt
                    ? "OUTPOST CLOSED — the account is overdrawn."
                    : $"CONTRACT COMPLETE — {lab.Plan.DisplayName}, {lab.Plan.Length} days.");
                verdict.style.fontSize = 17;
                verdict.style.unityFontStyleAndWeight = FontStyle.Bold;
                verdict.style.marginTop = 10;
                verdict.style.whiteSpace = WhiteSpace.Normal;
                verdict.style.color = new StyleColor(bankrupt ? SignalPalette.Critical : SignalPalette.Normal);
                panel.Add(verdict);

                var summary = new Label(
                    $"Closing balance £{lab.Economy.Money:N0} from £{lab.Tuning.StartingMoney:N0} · " +
                    $"reputation {lab.Economy.Reputation:F0} · " +
                    $"earned £{lab.Economy.TotalEarned:N0}, lost £{lab.Economy.TotalLost:N0}");
                summary.style.fontSize = 13;
                summary.style.marginTop = 4;
                summary.style.whiteSpace = WhiteSpace.Normal;
                summary.style.color = new StyleColor(SignalPalette.Dim);
                panel.Add(summary);

                return panel;
            }

            var next = new Button(() => Ask(LabCommand.StartNextDay(), null, () => reportOverlay = null))
            { text = "START NEXT DAY" };
            StyleButton(next, SignalPalette.Accent);
            next.style.marginTop = 8;
            panel.Add(next);

            return panel;
        }

        // -- Small builders ---------------------------------------------------------------------------

        private static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            return e;
        }

        private static VisualElement Panel()
        {
            var e = new VisualElement();
            e.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            e.style.paddingTop = 12;
            e.style.paddingBottom = 12;
            e.style.paddingLeft = 12;
            e.style.paddingRight = 12;
            LabHud.Round(e, 4);
            return e;
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(SignalPalette.Accent);
            label.style.marginBottom = 6;
            return label;
        }

        private static Label Dim(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = new StyleColor(SignalPalette.Dim);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement ColumnHeader()
        {
            var row = Row();
            row.style.marginBottom = 4;
            row.Add(Cell("ELEMENT", 190, SignalPalette.Dim));
            row.Add(Cell("MEASURED", 130, SignalPalette.Dim));
            row.Add(Cell("LIMIT", 150, SignalPalette.Dim));
            row.Add(Cell("STATE", 90, SignalPalette.Dim));
            row.Add(Cell("", 80, SignalPalette.Dim));
            return row;
        }

        private static Label Cell(string text, float width, Color colour)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.fontSize = 12;
            label.style.color = new StyleColor(colour);
            return label;
        }

        private static void StyleButton(Button button, Color background)
        {
            button.style.backgroundColor = new StyleColor(background);
            button.style.color = new StyleColor(SignalPalette.Ink);
            button.style.fontSize = 12;
            button.style.paddingTop = 7;
            button.style.paddingBottom = 7;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.marginLeft = 4;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            LabHud.Round(button, 3);
        }
    }
}
