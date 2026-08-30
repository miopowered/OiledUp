namespace Residue.Data
{
    /// <summary>
    /// Every line the lab says when it refuses, and every word the reference books contribute of
    /// their own (#55).
    ///
    /// <para>
    /// <b>Two groups, and they are not the same kind of text.</b> <c>refusal.</c> is the lab
    /// explaining why it will not do something — hard rule 3 rests on these, because
    /// "never punish something the player could not have checked" is only honoured by the sentence
    /// that says what to check. <c>book.</c> is the reference material's connective prose. Neither
    /// group carries chemistry: every figure, element name, fault and root cause the books print
    /// comes out of the content tables, is balance data with its own pipeline, and is passed in
    /// through a placeholder rather than written here.
    /// </para>
    ///
    /// <para>
    /// <b>One key is one finished sentence.</b> Refusals used to be assembled — a reason plus a
    /// subject, <c>"You are not standing at " + what</c> — which reads correctly in English and is
    /// untranslatable, because the fragment handed over has no room to move the subject. Each
    /// place-you-are-not-standing is therefore its own line here even though the English of two of
    /// them differs by a single noun. For the same reason a refusal that reads the same but
    /// <i>explains</i> something different keeps its own id: <see cref="NotCarryingABottle"/> and
    /// <see cref="FlushNeedsABottle"/> are both "you have no bottle", but only one of them is also
    /// telling you where to get one, and merging them would quietly delete that half.
    /// </para>
    ///
    /// <para>
    /// <b>Numbers arrive pre-formatted.</b> A placeholder takes the string, not the float, because
    /// <c>{cost}</c> cannot carry a <c>:N0</c> and a translator should not have to learn one. The
    /// call site keeps the format specifier it always had.
    /// </para>
    /// </summary>
    public static class LabStrings
    {
        // -- Refusals: the request itself --------------------------------------------------------

        public static readonly LocKey NoSuchPlayer =
            new("refusal.no_such_player", "No such player.");

        public static readonly LocKey LabNotRunning =
            new("refusal.lab_not_running", "The lab is not running.");

        public static readonly LocKey CommandNotUnderstood =
            new("refusal.not_understood", "The lab did not understand that.");

        /// <summary>What a gateway that forgot to phrase its own refusal falls back to.</summary>
        public static readonly LocKey RefusedWithoutAReason =
            new("refusal.unexplained", "The lab will not do that right now.");

        // -- Refusals: hands and inventory -------------------------------------------------------

        public static readonly LocKey InventoryFull =
            new("refusal.inventory_full", "Your inventory is full.");

        public static readonly LocKey NoSuchSample =
            new("refusal.no_such_sample", "No such sample.");

        public static readonly LocKey PlayerHasNoInventory =
            new("refusal.no_inventory", "This player has no inventory.");

        public static readonly LocKey NoSuchInventoryItem =
            new("refusal.no_such_inventory_item", "No such inventory item.");

        public static readonly LocKey ItemNotInInventory =
            new("refusal.item_not_in_inventory", "That item is not in your inventory.");

        public static readonly LocKey CarryingNothing =
            new("refusal.carrying_nothing", "You are not carrying anything.");

        public static readonly LocKey NowhereToPutThatDown =
            new("refusal.nowhere_to_put_down", "Nowhere to put that down.");

        public static readonly LocKey NotHoldingASample =
            new("refusal.not_holding_sample", "You are not holding a sample.");

        // -- Refusals: vials ---------------------------------------------------------------------

        public static readonly LocKey VialIsInAnInstrument = new("refusal.vial_in_instrument",
            "{tag} is inside {instrument}. Take it out at the instrument.");

        public static readonly LocKey VialHeldBySomeoneElse = new("refusal.vial_held_by_other",
            "Someone else is holding {tag}.");

        public static readonly LocKey VialIsSpent = new("refusal.vial_spent",
            "{tag} is spent — there is nothing left to carry.");

        // -- Refusals: deliveries (#30, #31) -----------------------------------------------------

        public static readonly LocKey NoSuchCarton =
            new("refusal.no_such_carton", "No such carton.");

        public static readonly LocKey VialStillOnTheTruck = new("refusal.vial_on_truck",
            "{tag} is still on the truck — the delivery has not been unloaded yet.");

        public static readonly LocKey CartonStillSealed = new("refusal.carton_sealed",
            "Carton {job} is still sealed. Open it before taking anything out.");

        public static readonly LocKey CartonIsInYourArms = new("refusal.carton_in_your_arms",
            "Set the carton down before taking vials out of it.");

        public static readonly LocKey CartonCarriedBySomeoneElse =
            new("refusal.carton_held_by_other", "Someone else is carrying carton {job}.");

        // -- Refusals: paperwork -----------------------------------------------------------------

        public static readonly LocKey SlipAlreadyFiled =
            new("refusal.slip_already_filed", "That slip has already been filed.");

        public static readonly LocKey NotCarryingThatSlip =
            new("refusal.not_carrying_that_slip", "You are not carrying that slip.");

        public static readonly LocKey NotAVerdict =
            new("refusal.not_a_verdict", "That is not a verdict.");

        // -- Refusals: solvent -------------------------------------------------------------------

        public static readonly LocKey NoSuchBottle =
            new("refusal.no_such_bottle", "No such solvent bottle.");

        /// <summary>
        /// Filling: you are at the drum with nothing to fill. Deliberately not shared with
        /// <see cref="FlushNeedsABottle"/> — that one also has to say where a bottle comes from,
        /// because a player being refused a flush is not standing at the wash station.
        /// </summary>
        public static readonly LocKey NotCarryingABottle =
            new("refusal.not_carrying_bottle", "You are not carrying a solvent bottle.");

        public static readonly LocKey FlushNeedsABottle = new("refusal.flush_needs_bottle",
            "You need a solvent bottle in your hands. Fill one at the wash station.");

        // -- Refusals: where you are standing ----------------------------------------------------
        //
        // One complete sentence per thing you could be standing away from. See the type comment:
        // these are the lines that used to be a shared template plus a noun.

        public static readonly LocKey NotStandingAtInstrument =
            new("refusal.not_at_instrument", "You are not standing at {instrument}.");

        public static readonly LocKey NotStandingAtShelf =
            new("refusal.not_at_shelf", "You are not standing at that shelf.");

        public static readonly LocKey NotStandingAtSlip =
            new("refusal.not_at_slip", "You are not standing at that slip.");

        public static readonly LocKey NotStandingAtCarton =
            new("refusal.not_at_carton", "You are not standing at carton {job}.");

        public static readonly LocKey NotStandingAtDeliveryNote =
            new("refusal.not_at_delivery_note", "You are not standing at that delivery note.");

        public static readonly LocKey NotStandingAtWashStation =
            new("refusal.not_at_wash_station", "You are not standing at the wash station.");

        public static readonly LocKey NotStandingAtTerminal =
            new("refusal.not_at_terminal", "You are not standing at the terminal.");

        // -- Refusals: instruments ---------------------------------------------------------------

        /// <summary>
        /// The noun that fills <c>{instrument}</c> when the instrument has no definition to name
        /// it. A bare noun phrase rather than a sentence, which the type comment otherwise rules
        /// out: it stands in for a <c>MachineDef.DisplayName</c> — a name, in a slot a translator
        /// can already move — and the alternative is a second copy of every instrument refusal for
        /// the case where the name is missing.
        /// </summary>
        public static readonly LocKey TheInstrument =
            new("refusal.instrument_unnamed", "the instrument");

        /// <summary>As <see cref="TheInstrument"/>, where the sentence has not pointed at one yet.</summary>
        public static readonly LocKey AnInstrument =
            new("refusal.instrument_indefinite", "an instrument");

        public static readonly LocKey NoSuchInstrument =
            new("refusal.no_such_instrument", "No such instrument.");

        public static readonly LocKey ShiftOverNoNewRuns =
            new("refusal.shift_over", "Shift over — no new runs.");

        public static readonly LocKey InstrumentIsBusy =
            new("refusal.instrument_busy", "{instrument} is busy.");

        public static readonly LocKey InstrumentIsEmpty =
            new("refusal.instrument_empty", "{instrument} is empty.");

        public static readonly LocKey InstrumentWillNotStart = new("refusal.instrument_will_not_start",
            "{instrument} will not start a run right now.");

        public static readonly LocKey CannotFlushWhileRunning =
            new("refusal.cannot_flush_while_running", "Cannot flush {instrument} while it is running.");

        public static readonly LocKey BlankNeedsAnEmptyInstrument =
            new("refusal.blank_needs_empty_instrument", "Take the vial out before running a blank.");

        public static readonly LocKey InstrumentWillNotTakeABlank =
            new("refusal.instrument_will_not_blank", "{instrument} will not take a blank right now.");

        // -- Refusals: loading an instrument -----------------------------------------------------
        //
        // The sentences behind LoadRefusal. Each names the figure that made it refuse, because the
        // player has to be able to act on it — hard rule 3.

        public static readonly LocKey InstrumentAlreadyLoaded =
            new("refusal.instrument_occupied", "{instrument} already has a vial in it.");

        public static readonly LocKey NotEnoughVolume = new("refusal.not_enough_volume",
            "{instrument} needs {needed} ml and {tag} has {left} ml left.");

        public static readonly LocKey NeedsPreheat = new("refusal.needs_preheat",
            "{tag} is at {actual} °C — {instrument} needs it near {target} °C.");

        public static readonly LocKey HasSettledOut = new("refusal.not_settled",
            "{tag} has settled out. Agitate it before running it (§4.5).");

        public static readonly LocKey InstrumentWillNotTakeThat =
            new("refusal.instrument_refuses_load", "{instrument} will not take that.");

        // -- Refusals: the terminal --------------------------------------------------------------

        public static readonly LocKey OrderAtLeastOneUnit =
            new("refusal.order_at_least_one_unit", "Order at least one unit.");

        public static readonly LocKey CannotAffordSolvent = new("refusal.cannot_afford_solvent",
            "A {units}-unit restock costs £{cost}, and the account will not cover it.");

        public static readonly LocKey OrderAtLeastOneAmpoule =
            new("refusal.order_at_least_one_ampoule", "Order at least one ampoule.");

        public static readonly LocKey CannotAffordStandards = new("refusal.cannot_afford_standards",
            "{count} certified ampoules cost £{cost}, and the account will not cover it.");

        public static readonly LocKey DayAlreadyOver =
            new("refusal.day_already_over", "The day is already over.");

        public static readonly LocKey ShiftStillRunning =
            new("refusal.shift_still_running", "The shift is still running.");

        public static readonly LocKey RunIsOver =
            new("refusal.run_is_over", "The run is over — there is no next day.");

        // -- Books: covers -----------------------------------------------------------------------

        public static readonly LocKey BookOperatorManualFor =
            new("book.title_operator_manual_for", "{instrument} — Operator Manual");

        public static readonly LocKey BookOperatorManual =
            new("book.title_operator_manual", "Operator Manual");

        public static readonly LocKey BookElementIndex =
            new("book.title_element_index", "Elements & Sources");

        public static readonly LocKey BookDiagnosticGuide =
            new("book.title_diagnostic_guide", "Diagnostic Guide");

        public static readonly LocKey BookThresholdTables =
            new("book.title_threshold_tables", "Threshold Tables");

        public static readonly LocKey BookReference =
            new("book.title_reference", "Reference");

        // -- Books: element categories -----------------------------------------------------------
        //
        // The chapter headings of the element index, and the group headings on the terminal's
        // results panel. One set of words, because the two disagreeing would have the manual
        // teaching an organisation the screen contradicts.

        public static readonly LocKey BookCategoryWearMetals =
            new("book.category_wear_metals", "Wear metals");

        public static readonly LocKey BookCategoryContaminants =
            new("book.category_contaminants", "Contaminants");

        public static readonly LocKey BookCategoryAdditives =
            new("book.category_additives", "Additives");

        public static readonly LocKey BookCategoryFluidProperties =
            new("book.category_fluid_properties", "Fluid properties");

        // -- Books: the operator manual ----------------------------------------------------------
        //
        // Line breaks are inside the English rather than made by consecutive appends, so a
        // translator is handed whole sentences. The pages are fixed-width paper: re-wrapping a
        // translated line is part of translating it.

        public static readonly LocKey BookManualOperationTitle =
            new("book.manual_operation_title", "Operation");

        public static readonly LocKey BookManualRunTime =
            new("book.manual_run_time", "Run time      {seconds} s");

        public static readonly LocKey BookManualSampleUsed =
            new("book.manual_sample_used", "Sample used   {millilitres} ml");

        public static readonly LocKey BookManualCostPerRun =
            new("book.manual_cost_per_run", "Cost per run  £{cost}");

        public static readonly LocKey BookManualNoise = new("book.manual_noise",
            "Typical spread on a reading is about {percent}%.");

        public static readonly LocKey BookManualDrift = new("book.manual_drift",
            "Calibration drifts roughly {percent}% per run,\n" +
            "in a direction that is re-rolled each day. Run a certified\n" +
            "reference sample if you suspect it.");

        public static readonly LocKey BookManualCarryover = new("book.manual_carryover",
            "Carryover: about {percent}% of whatever went\n" +
            "through last stays behind. Push a solvent blank to see it.\n" +
            "To clear it, fill a bottle at the wash station and hold\n" +
            "the FLUSH button here. One charge per instrument.");

        public static readonly LocKey BookManualFumeHood =
            new("book.manual_fume_hood", "Requires a fume hood.");

        public static readonly LocKey BookManualPreheat =
            new("book.manual_preheat", "Requires preheat to {celsius} C.");

        public static readonly LocKey BookManualReportsTitle =
            new("book.manual_reports_title", "Reports");

        public static readonly LocKey BookManualReportsIntro =
            new("book.manual_reports_intro", "This instrument reports:");

        public static readonly LocKey BookManualBlindSpotsTitle =
            new("book.manual_blind_spots_title", "Blind spots");

        /// <summary>
        /// The blind-spot page is the whole reason the manuals exist, so the "no blind spots" case
        /// still has to refuse to read as an all-clear.
        /// </summary>
        public static readonly LocKey BookManualNoBlindSpots = new("book.manual_no_blind_spots",
            "No known blind spots for the quantities this instrument reports.\n" +
            "\n" +
            "That does not mean a clean result clears the sample. It only\n" +
            "clears what this instrument measures. Check what it does NOT\n" +
            "report on the previous page.");

        public static readonly LocKey BookManualCannotDetect =
            new("book.manual_cannot_detect", "CANNOT DETECT");

        public static readonly LocKey BookManualCannotDetectClosing =
            new("book.manual_cannot_detect_closing",
                "These will be absent from the report even when present in\n" +
                "the sample. A clean result here is not a clean sample.");

        // -- Books: the threshold tables ---------------------------------------------------------

        public static readonly LocKey BookThresholdsGrade = new("book.thresholds_grade",
            "Oil grade {grade}   change interval {hours} h");

        public static readonly LocKey BookThresholdsColumns =
            new("book.thresholds_columns", "ELEMENT   NORMAL          CRITICAL");

        public static readonly LocKey BookThresholdsFooter = new("book.thresholds_footer",
            "Limits are per equipment type. The same iron figure can be\n" +
            "routine on one unit and cause to pull another.");

        // -- Books: the standing orders (#47) ----------------------------------------------------
        //
        // READ BEFORE EDITING ANY LINE BELOW. The brief's rule is that every line says where to
        // look and no line says what you will find: not one element, fault or root cause may be
        // named anywhere in it, because a brief that started listing symptoms would hand out on day
        // one the diagnostic table the game is about building for yourself (hard rule 1).
        // OnboardingTests.TheShiftBrief_NamesNoElementFaultOrRootCause holds that shut against the
        // real content tables, and reads these strings through BookContent.ShiftBrief().
        //
        // The other half is hard rule 3: contamination and drift are only fair because a blank run
        // and a certified standard reveal them, and this is the only place the player is told those
        // two tools exist at all. Do not translate the blank or the standard out of existence.

        public static readonly LocKey BookShiftBriefTitle =
            new("book.shift_brief_title", "STANDING ORDERS");

        public static readonly LocKey BookShiftBriefClosing = new("book.shift_brief_closing",
            "None of the above tells you what any sample is. That part is the job.");

        public static readonly LocKey BookShiftBriefManualsTitle =
            new("book.shift_brief_manuals_title", "The manuals are not decoration");

        /// <summary>
        /// The three books are named through <c>BookContent.TitleFor</c>'s own titles rather than
        /// spelled again here, so renaming one renames it in the brief too.
        /// </summary>
        public static readonly LocKey BookShiftBriefManualsBody =
            new("book.shift_brief_manuals_body",
                "Every instrument has its operator manual lying on the bench beside it, and the " +
                "rack by the terminal holds {elements}, {diagnostics} and {thresholds}. A manual " +
                "says what its instrument reports and — the part that matters — what it cannot " +
                "see. Look at one and press [E] to pick it up.");

        public static readonly LocKey BookShiftBriefLoadingTitle =
            new("book.shift_brief_loading_title", "Loading is a hold, and the hold is the shake");

        public static readonly LocKey BookShiftBriefLoadingBody =
            new("book.shift_brief_loading_body",
                "Hold [E] at an instrument to load a vial. That hold is where the sample gets " +
                "shaken, so it costs seconds you do not get back. A vial that came in cold has to " +
                "be warmed first; the instrument will say so when it refuses.");

        public static readonly LocKey BookShiftBriefFilingTitle =
            new("book.shift_brief_filing_title", "Nothing files itself");

        public static readonly LocKey BookShiftBriefFilingBody =
            new("book.shift_brief_filing_body",
                "A finished run prints a slip into the tray on the instrument. The reading joins " +
                "the record only when you carry that slip to the terminal. A slip left on a bench " +
                "is a test you paid for and cannot use.");

        public static readonly LocKey BookShiftBriefDirtyTitle =
            new("book.shift_brief_dirty_title", "An instrument is dirty until you prove it clean");

        public static readonly LocKey BookShiftBriefDirtyBody =
            new("book.shift_brief_dirty_body",
                "Some of the last sample stays behind and turns up in the next one. Pushing a " +
                "solvent blank through reads back what is in there. To clear it, fill a bottle at " +
                "the wash station and hold FLUSH at the instrument. The terminal marks every " +
                "instrument that has had no blank today.");

        public static readonly LocKey BookShiftBriefDriftTitle =
            new("book.shift_brief_drift_title", "An instrument drifts until you prove it has not");

        public static readonly LocKey BookShiftBriefDriftBody =
            new("book.shift_brief_drift_body",
                "Calibration wanders a little every run, in a direction re-rolled each day, and it " +
                "quietly scales everything the instrument tells you. A certified reference " +
                "standard is what measures it. The terminal marks every instrument that has had " +
                "no standard today.");

        public static readonly LocKey BookShiftBriefVerdictTitle =
            new("book.shift_brief_verdict_title", "A verdict is a bill that arrives later");

        public static readonly LocKey BookShiftBriefVerdictBody =
            new("book.shift_brief_verdict_body",
                "Filing closes a sample, but the consequence lands days afterwards. Both " +
                "directions cost: condemning a serviceable tank is expensive, and passing a bad " +
                "one is worse. Naming the cause correctly is what pays.");
    }
}
