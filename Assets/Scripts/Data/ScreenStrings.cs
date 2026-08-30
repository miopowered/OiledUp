namespace Residue.Data
{
    /// <summary>
    /// Every player-facing line the terminal, the HUD and the in-world instrument screens draw (#55).
    ///
    /// <para>
    /// Three groups, because a translator works a screen at a time: <c>terminal.</c> is the desk,
    /// <c>hud.</c> is the overlay a player wears, and <c>screen.</c> is glass in the room — the
    /// instrument readouts, which rasterise a pixel font onto a fixed-width texture and therefore
    /// have a hard column budget the other two do not. That budget is enforced where the line is
    /// drawn (<c>PixelText.Truncate</c>), not here: a table of English cannot know how wide a
    /// translated line will be, and the only honest answer is to keep cutting at the glass.
    /// </para>
    ///
    /// <para>
    /// <b>Whole sentences, named arguments.</b> Several of these look redundant in
    /// pairs — <see cref="TerminalCheckInTolerance"/> beside <see cref="TerminalCheckOutOfTolerance"/>,
    /// <see cref="HudOpenSamplesOne"/> beside <see cref="HudOpenSamplesMany"/> — and each pair
    /// replaces a line the old code assembled by appending a branch onto a stem. The duplication is
    /// the point: a translator handed <c>" outside limits"</c> cannot make it agree with a number in
    /// a language that inflects, and cannot move it in one that puts the count last. Do not fold a
    /// pair back into a stem plus a fragment.
    /// </para>
    ///
    /// <para>
    /// <b>Ids are not here.</b> Equipment tags, sample ids, element ids and machine instance ids
    /// travel through these lines as <i>arguments</i> and never as text to be looked up. The same
    /// goes for anything out of <c>ContentTables</c> — element display names, profile names, root
    /// cause names — which is balance data with its own pipeline.
    /// </para>
    /// </summary>
    public static class ScreenStrings
    {
        // -- Verdict words ------------------------------------------------------------------------
        //
        // The words half of §2.2's rule that hue is never the only carrier: every severity ships as a
        // colour, a glyph and a word, and at least two must be drawn. The glyphs are deliberately not
        // here — "X", "!", "=" and "?" are symbols PixelFont can raster, not language, and a
        // translated glyph would break the one channel that survives both colourblindness and a
        // greyscale screenshot.
        //
        // Severity and verdict keep separate ids even where the English matches, because CAUTION is a
        // number an instrument produced and MONITOR is a decision the player made. A translator has
        // to be able to tell those apart, and a shared key would quietly decide they are the same
        // word in every language.

        public static readonly LocKey SeverityNormal = new("screen.severity_normal", "NORMAL");

        public static readonly LocKey SeverityCaution = new("screen.severity_caution", "CAUTION");

        public static readonly LocKey SeverityCritical = new("screen.severity_critical", "CRITICAL");

        public static readonly LocKey VerdictNormal = new("screen.verdict_normal", "NORMAL");

        public static readonly LocKey VerdictMonitor = new("screen.verdict_monitor", "MONITOR");

        public static readonly LocKey VerdictCritical = new("screen.verdict_critical", "CRITICAL");

        /// <summary>A row with no runs behind it — see <c>SignalPalette.Off</c>.</summary>
        public static readonly LocKey Untested = new("screen.untested", "UNTESTED");

        /// <summary>
        /// Glyph and word together. A template rather than a concatenation because a language that
        /// reads right to left wants the marker on the other side, and that is not something a caller
        /// joining two strings can express.
        /// </summary>
        public static readonly LocKey Marked = new("screen.marked", "{glyph} {label}");

        // -- Terminal: chrome ---------------------------------------------------------------------

        public static readonly LocKey TerminalTitle = new(
            "terminal.title", "SAMPLE TERMINAL");

        public static readonly LocKey TerminalHeader = new(
            "terminal.header", "SAMPLE TERMINAL — DAY {day}");

        public static readonly LocKey TerminalBalance = new(
            "terminal.balance", "£{money}    REP {reputation}");

        public static readonly LocKey TerminalClose = new(
            "terminal.close", "CLOSE  (Esc)");

        public static readonly LocKey TerminalEndDay = new(
            "terminal.end_day", "END DAY");

        public static readonly LocKey TerminalWaitingForSession = new(
            "terminal.waiting_for_session",
            "Waiting for the lab. If this does not clear, the session never came up.");

        public static readonly LocKey TerminalWaitingForHost = new(
            "terminal.waiting_for_host",
            "Waiting for the first publish from the host. The instruments in the room are already " +
            "readable; this desk fills in a moment.");

        // -- Terminal: the open queue -------------------------------------------------------------

        public static readonly LocKey TerminalOpenSamples = new(
            "terminal.open_samples", "OPEN SAMPLES");

        public static readonly LocKey TerminalNothingOpen = new(
            "terminal.nothing_open", "Nothing open. End the day.");

        public static readonly LocKey TerminalSampleMeta = new(
            "terminal.sample_meta", "{id} · {profile} · {volume} ml · {runs}");

        /// <summary>
        /// The run count as a whole noun phrase rather than a number with a suffix stuck on it, so a
        /// language that inflects the noun after "1" can say so.
        /// </summary>
        public static readonly LocKey TerminalRunCountOne = new(
            "terminal.run_count_one", "1 run");

        public static readonly LocKey TerminalRunCountMany = new(
            "terminal.run_count_many", "{count} runs");

        /// <summary>Stands in for a profile the content catalog is missing. Not a profile name.</summary>
        public static readonly LocKey TerminalUnknownFluid = new(
            "terminal.unknown_fluid", "unknown fluid");

        // -- Terminal: instruments ----------------------------------------------------------------

        public static readonly LocKey TerminalInstruments = new(
            "terminal.instruments", "INSTRUMENTS");

        public static readonly LocKey TerminalRunsSinceFlush = new(
            "terminal.runs_since_flush", "{instrument} · {runs} run(s) since flush");

        public static readonly LocKey TerminalBlankMissing = new(
            "terminal.blank_missing", "no blank run — residue unknown");

        public static readonly LocKey TerminalBlankClean = new(
            "terminal.blank_clean", "blank day {day}: clean");

        public static readonly LocKey TerminalBlankResidue = new(
            "terminal.blank_residue", "blank day {day}: {residue}");

        public static readonly LocKey TerminalSolventStock = new(
            "terminal.solvent_stock", "SOLVENT  {units} unit(s)");

        public static readonly LocKey TerminalOrder = new(
            "terminal.order", "ORDER {count}  (£{cost})");

        public static readonly LocKey TerminalCannotAffordRestock = new(
            "terminal.cannot_afford_restock", "cannot afford a restock");

        // -- Terminal: calibration (§5.3) ---------------------------------------------------------

        public static readonly LocKey TerminalCalibration = new(
            "terminal.calibration", "CALIBRATION");

        public static readonly LocKey TerminalStandardsStock = new(
            "terminal.standards_stock", "STANDARDS  {count} ampoule(s)");

        public static readonly LocKey TerminalStandardCertified = new(
            "terminal.standard_certified",
            "{standard} — certified at the healthy baselines the manual publishes.");

        public static readonly LocKey TerminalCheckMissing = new(
            "terminal.check_missing", "no standard run today — drift unknown");

        public static readonly LocKey TerminalCheckInTolerance = new(
            "terminal.check_in_tolerance", "{standard} day {day}: reads {error}  in tolerance");

        public static readonly LocKey TerminalCheckOutOfTolerance = new(
            "terminal.check_out_of_tolerance",
            "{standard} day {day}: reads {error}  OUT OF TOLERANCE");

        public static readonly LocKey TerminalCertifiedCell = new(
            "terminal.certified_cell", "cert {value}");

        public static readonly LocKey TerminalMeasuredCell = new(
            "terminal.measured_cell", "read {value}");

        public static readonly LocKey TerminalCalibrated = new(
            "terminal.calibrated",
            "calibrated day {day}: corrected {drift}, {runs} run(s) now suspect across " +
            "{records} filed record(s)");

        public static readonly LocKey TerminalRecordsInDoubt = new(
            "terminal.records_in_doubt", "RECORDS IN DOUBT");

        public static readonly LocKey TerminalNoRecordsInDoubt = new(
            "terminal.no_records_in_doubt", "No filed record rests on a drifting instrument.");

        public static readonly LocKey TerminalInDoubtTitle = new(
            "terminal.in_doubt_title", "{mark} {tag} — filed {verdict} day {day}");

        public static readonly LocKey TerminalRetestImpossible = new(
            "terminal.retest_impossible",
            "{volume} ml left · no instrument here can repeat those tests");

        public static readonly LocKey TerminalRetestNeeds = new(
            "terminal.retest_needs", "{volume} ml left · a re-test needs {needed} ml");

        public static readonly LocKey TerminalReopen = new(
            "terminal.reopen", "RE-OPEN FOR RE-TEST");

        // -- Terminal: one sample -----------------------------------------------------------------

        public static readonly LocKey TerminalSelectASample = new(
            "terminal.select_a_sample", "Select a sample.");

        public static readonly LocKey TerminalSampleSubtitle = new(
            "terminal.sample_subtitle",
            "{profile} · {grade} · {hours} h on the oil · {volume} ml remaining");

        public static readonly LocKey TerminalRedrawOf = new(
            "terminal.redraw_of", "RE-DRAW of {origin} — you filed {verdict} on this unit.");

        public static readonly LocKey TerminalFieldNote = new(
            "terminal.field_note", "Field note: \"{note}\"");

        // -- Terminal: reconciling an ambiguous vial (#32) ----------------------------------------

        public static readonly LocKey TerminalReconcile = new(
            "terminal.reconcile", "RECONCILE AGAINST THE DELIVERY NOTE");

        public static readonly LocKey TerminalReconcileUnreadable = new(
            "terminal.reconcile_unreadable",
            "This bottle's tank tag cannot be read. The note that came in its carton lists the " +
            "tanks the sender says they drew — the spare one is this.");

        public static readonly LocKey TerminalReconcileDuplicate = new(
            "terminal.reconcile_duplicate",
            "Another bottle in this carton carries the same tag, and the note books that tank " +
            "twice. Say which draw this is, or that they cannot be told apart.");

        public static readonly LocKey TerminalNoNoteForJob = new(
            "terminal.no_note_for_job",
            "No delivery note on file for {job}. Ring the customer, or read the paper that came " +
            "in the box.");

        /// <summary>
        /// The same refusal for a vial with no job number on it. A separate sentence rather than
        /// "this vial" substituted into the one above: a fragment dropped into a slot cannot carry
        /// the case or the article the surrounding language wants.
        /// </summary>
        public static readonly LocKey TerminalNoNoteForVial = new(
            "terminal.no_note_for_vial",
            "No delivery note on file for this vial. Ring the customer, or read the paper that " +
            "came in the box.");

        public static readonly LocKey TerminalNotRegistered = new(
            "terminal.not_registered", "NOT REGISTERED — note {job}");

        public static readonly LocKey TerminalRegisteredInseparable = new(
            "terminal.registered_inseparable", "RECORDED AS INSEPARABLE — note {job}");

        public static readonly LocKey TerminalRegisteredAs = new(
            "terminal.registered_as", "REGISTERED AS {tag} — note {job}");

        public static readonly LocKey TerminalNoteLine = new(
            "terminal.note_line", "{number}. {tank}");

        public static readonly LocKey TerminalCannotTell = new(
            "terminal.cannot_tell", "CANNOT TELL");

        public static readonly LocKey TerminalRingCustomer = new(
            "terminal.ring_customer", "RING THE CUSTOMER  ({seconds} s)");

        // -- Terminal: the results table ----------------------------------------------------------

        public static readonly LocKey TerminalNoResults = new(
            "terminal.no_results", "No results yet. Run this sample on an instrument.");

        public static readonly LocKey TerminalProfileMissing = new(
            "terminal.profile_missing",
            "This fluid's profile is missing from the content catalog, so nothing here can be " +
            "scored. Rebuild definitions.");

        public static readonly LocKey TerminalNothingScored = new(
            "terminal.nothing_scored", "Nothing measured yet that this profile scores.");

        public static readonly LocKey TerminalCategoryAllNormal = new(
            "terminal.category_all_normal", "{mark} all normal");

        public static readonly LocKey TerminalCategoryFlaggedOne = new(
            "terminal.category_flagged_one", "{mark} 1 outside limit · worst {verdict}");

        public static readonly LocKey TerminalCategoryFlaggedMany = new(
            "terminal.category_flagged_many", "{mark} {count} outside limits · worst {verdict}");

        public static readonly LocKey TerminalMeasuredValue = new(
            "terminal.measured_value", "{value} {unit}");

        public static readonly LocKey TerminalLimitUpper = new(
            "terminal.limit_upper", "normal ≤ {limit}");

        public static readonly LocKey TerminalLimitLower = new(
            "terminal.limit_lower", "normal ≥ {limit}");

        public static readonly LocKey TerminalLimitBand = new(
            "terminal.limit_band", "{baseline} ±{percent}%");

        public static readonly LocKey TerminalColumnElement = new(
            "terminal.column_element", "ELEMENT");

        public static readonly LocKey TerminalColumnMeasured = new(
            "terminal.column_measured", "MEASURED");

        public static readonly LocKey TerminalColumnLimit = new(
            "terminal.column_limit", "LIMIT");

        public static readonly LocKey TerminalColumnState = new(
            "terminal.column_state", "STATE");

        public static readonly LocKey TerminalSuspect = new(
            "terminal.suspect", "SUSPECT");

        public static readonly LocKey TerminalRuns = new(
            "terminal.runs", "RUNS");

        public static readonly LocKey TerminalRunLogLine = new(
            "terminal.run_log_line", "day {day} · {machine} · {volume} ml · £{cost}{marks}");

        public static readonly LocKey TerminalRunMarkBlank = new(
            "terminal.run_mark_blank", "BLANK");

        // -- Terminal: filing a verdict -----------------------------------------------------------

        public static readonly LocKey TerminalRootCause = new(
            "terminal.root_cause", "Root cause");

        public static readonly LocKey TerminalNoRootCause = new(
            "terminal.no_root_cause", "(no root cause)");

        public static readonly LocKey TerminalFileNormal = new(
            "terminal.file_normal", "FILE NORMAL");

        public static readonly LocKey TerminalFileMonitor = new(
            "terminal.file_monitor", "FILE MONITOR");

        public static readonly LocKey TerminalFileCritical = new(
            "terminal.file_critical", "FILE CRITICAL — PULL");

        /// <summary>
        /// The face of a verdict button: the colourblind-safe glyph and the action, in that order in
        /// English. Templated rather than concatenated so a language that leads with the verb can
        /// put the marker after it — the glyph is a channel, not a prefix (#41).
        /// </summary>
        public static readonly LocKey TerminalVerdictButton = new(
            "terminal.verdict_button", "{mark}  {action}");

        // -- Terminal: the end-of-day report (§4.3) ------------------------------------------------

        public static readonly LocKey TerminalEndOfDay = new(
            "terminal.end_of_day", "END OF DAY {day}");

        public static readonly LocKey TerminalClosingDay = new(
            "terminal.closing_day", "Closing the day…");

        public static readonly LocKey TerminalNothingDue = new(
            "terminal.nothing_due", "Nothing came due today.");

        public static readonly LocKey TerminalGoodCall = new(
            "terminal.good_call", "{mark} GOOD CALL");

        public static readonly LocKey TerminalBadCall = new(
            "terminal.bad_call", "{mark} BAD CALL");

        public static readonly LocKey TerminalReportMoney = new(
            "terminal.report_money", "{sign}£{amount}");

        public static readonly LocKey TerminalReportMoneyWithBonus = new(
            "terminal.report_money_with_bonus", "{sign}£{amount}   root cause bonus");

        public static readonly LocKey TerminalReportNet = new(
            "terminal.report_net", "NET  {sign}£{net}    BALANCE £{balance}");

        public static readonly LocKey TerminalOutpostClosed = new(
            "terminal.outpost_closed", "OUTPOST CLOSED — the account is overdrawn.");

        public static readonly LocKey TerminalContractComplete = new(
            "terminal.contract_complete", "CONTRACT COMPLETE — {contract}, {days} days.");

        public static readonly LocKey TerminalRunSummary = new(
            "terminal.run_summary",
            "Closing balance £{closing} from £{opening} · reputation {reputation} · " +
            "earned £{earned}, lost £{lost}");

        public static readonly LocKey TerminalStartNextDay = new(
            "terminal.start_next_day", "START NEXT DAY");

        // -- HUD ----------------------------------------------------------------------------------

        /// <summary>
        /// The greybox control list. The bracketed keys are bindings rather than words — a
        /// translator moves and translates the verbs around them, and leaves the brackets alone.
        /// </summary>
        public static readonly LocKey HudControls = new(
            "hud.controls",
            "[WASD] move    [E] interact    [1–3] select    [G] set down    [Space] inspect    " +
            "[LMB drag] rotate    [Wheel] zoom    [Tab] standing orders");

        public static readonly LocKey HudHands = new(
            "hud.hands", "in hands: {item}    [G] set down    [Space] inspect");

        public static readonly LocKey HudHandsWithUse = new(
            "hud.hands_with_use",
            "in hands: {item}    [LMB] {use}    [G] set down    [Space] inspect");

        public static readonly LocKey HudInspectHelp = new(
            "hud.inspect_help",
            "Hold LMB + move mouse to rotate    Wheel to zoom    Space / Esc to close");

        public static readonly LocKey HudInspectHelpWithHint = new(
            "hud.inspect_help_with_hint",
            "Hold LMB + move mouse to rotate    Wheel to zoom    {hint}    Space / Esc to close");

        public static readonly LocKey HudBriefClosing = new(
            "hud.brief_closing", "{closing}\n[Tab] puts this away — [Tab] brings it back.");

        public static readonly LocKey HudShiftOver = new(
            "hud.shift_over", "SHIFT OVER — file your verdicts");

        public static readonly LocKey HudTimeLeft = new(
            "hud.time_left", "{time} left");

        public static readonly LocKey HudStatus = new(
            "hud.status",
            "DAY {day}   {clock}\n£{money}   REP {reputation}   DRUM {solvent}   STD {standards}\n" +
            "{open}");

        public static readonly LocKey HudOpenSamplesOne = new(
            "hud.open_samples_one", "1 sample open");

        public static readonly LocKey HudOpenSamplesMany = new(
            "hud.open_samples_many", "{count} samples open");

        // -- In-world screens ---------------------------------------------------------------------
        //
        // Everything below is drawn through PixelText.Truncate onto a fixed-width texture. Keep them
        // short: a line that outgrows the glass is cut, not wrapped, because each one sits at a fixed
        // y with a number underneath it.

        /// <summary>An instrument whose definition this process cannot see yet. Not a machine id.</summary>
        public static readonly LocKey ScreenInstrumentUnknown = new(
            "screen.instrument_unknown", "INSTRUMENT");

        /// <summary>The same stand-in in the terminal's sentence case.</summary>
        public static readonly LocKey ScreenInstrumentFallback = new(
            "screen.instrument_fallback", "Instrument");

        public static readonly LocKey ScreenReady = new(
            "screen.ready", "READY");

        public static readonly LocKey ScreenRunning = new(
            "screen.running", "RUNNING {seconds}S");

        public static readonly LocKey ScreenNoReading = new(
            "screen.no_reading", "NO READING");

        /// <summary>
        /// A measured number and its unit on a titrator's large readout. The element id beside it is
        /// data and is drawn straight; only the space between value and unit is a language decision.
        /// </summary>
        public static readonly LocKey ScreenValue = new(
            "screen.value", "{value} {unit}");

        public static readonly LocKey ScreenHistory = new(
            "screen.history", "HISTORY");

        public static readonly LocKey ScreenHistoryLine = new(
            "screen.history_line", "D{day} {caption}");

        /// <summary>
        /// A solvent blank has to name itself: a full panel of plausible numbers with no sample named
        /// beside it reads as somebody else's sample.
        /// </summary>
        public static readonly LocKey ScreenCaptionBlank = new(
            "screen.caption_blank", "SOLVENT BLANK");

        public static readonly LocKey ScreenCaptionStandard = new(
            "screen.caption_standard", "CERT STANDARD");
    }
}
