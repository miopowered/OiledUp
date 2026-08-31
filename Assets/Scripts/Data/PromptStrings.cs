namespace Residue.Data
{
    /// <summary>
    /// Every line the room itself says: interaction prompts, item names, inspection text and the
    /// toasts a fixture answers with (#55).
    ///
    /// <para>
    /// <b>One class rather than one per prop</b> because the project allows one public type per file
    /// and a table split across twenty files is a translator's file split across twenty files. The
    /// <c>prompt.</c> group is subdivided by fixture — <c>prompt.carton.</c>, <c>prompt.machine.</c>
    /// — which is the sorting order the id convention exists to give.
    /// </para>
    ///
    /// <para>
    /// <b>A prompt that varies by condition is a whole sentence per branch</b>, not a stem plus an
    /// ending. English pluralises by appending "s"; most languages do not, and a translator handed
    /// <c>"flush"</c> and <c>"es"</c> separately cannot fix it. So "1 vial still in it" and
    /// "{count} vials still in it" are two lines here rather than one line and a ternary at the call
    /// site. The cost is a few near-duplicate entries; the alternative is a table that can only be
    /// translated into languages that inflect like English.
    /// </para>
    ///
    /// <para>
    /// <b>The one nested phrase is the wash station's drum reading.</b> It appears inside three
    /// different sentences and has a singular form, which is six sentences written out — so
    /// <see cref="WashDrum"/> is passed into them as <c>{drum}</c>. That is not a fragment: it is a
    /// complete noun phrase a translator can render on its own, and every sentence it lands in can
    /// move it. Nothing else here is assembled from parts.
    /// </para>
    ///
    /// <para>
    /// <b>Ids are absent on purpose.</b> Equipment tags, sample ids, job numbers, carton ids and
    /// machine instance ids arrive as arguments and never as lines — including the placeholders that
    /// stand in for a missing one ("UNLABELLED", "UNKNOWN", "—"), which are data in the shape of a
    /// word. Machine display names come from <c>ContentTables</c> and travel the same way.
    /// </para>
    /// </summary>
    public static class PromptStrings
    {
        // -- Shared ----------------------------------------------------------------------------------
        //
        // Said by more than one fixture. One id each, so a rewording reaches every place it is drawn.

        public static readonly LocKey TakeItem =
            new("prompt.take_item", "Take {item}");

        public static readonly LocKey InventoryFull =
            new("prompt.inventory_full", "Inventory full");

        public static readonly LocKey HandsFull =
            new("prompt.hands_full", "Hands full");

        public static readonly LocKey ItemSetDown =
            new("prompt.item_set_down", "{item} set down.");

        // -- Vials -----------------------------------------------------------------------------------

        public static readonly LocKey VialInspection =
            new("prompt.vial.inspection", "SAMPLE {id}\nCustomer label: {label}");

        // -- Results slips ---------------------------------------------------------------------------

        public static readonly LocKey PrintoutName =
            new("prompt.printout.name", "{machine} printout — {tag}");

        public static readonly LocKey PrintoutTake =
            new("prompt.printout.take", "Take printout — {tag}");

        public static readonly LocKey PrintoutTakeBlank =
            new("prompt.printout.take_blank", "Take blank slip — {machine}");

        public static readonly LocKey PrintoutUseHint =
            new("prompt.printout.use_hint", "read slip");

        public static readonly LocKey PrintoutHeading =
            new("prompt.printout.heading", "{tag} — {machine}");

        public static readonly LocKey PrintoutHeadingBlank =
            new("prompt.printout.heading_blank", "{machine} — BLANK");

        public static readonly LocKey PrintoutPaperBlank =
            new("prompt.printout.paper_blank", "This paper is blank.");

        public static readonly LocKey PrintoutNumbersPending =
            new("prompt.printout.numbers_pending",
                "The numbers on this paper have not come through yet.");

        public static readonly LocKey PrintoutNoValues =
            new("prompt.printout.no_values", "No values reported.");

        // -- Delivery notes --------------------------------------------------------------------------

        public static readonly LocKey NoteName =
            new("prompt.note.name", "Delivery note {job}");

        public static readonly LocKey NoteTake =
            new("prompt.note.take", "Take delivery note {job} ({sender})");

        public static readonly LocKey NoteTakeUnnamed =
            new("prompt.note.take_unnamed", "Take delivery note {job} (an unnamed sender)");

        public static readonly LocKey NotePrintedHeading =
            new("prompt.note.printed_heading", "DELIVERY NOTE {job}");

        public static readonly LocKey NotePrintedSenderUnknown =
            new("prompt.note.printed_sender_unknown", "Sender not stated");

        public static readonly LocKey NotePrintedBookedOut =
            new("prompt.note.printed_booked_out", "Booked out day {day}");

        public static readonly LocKey NotePrintedLine =
            new("prompt.note.printed_line", "{n}. {tag}");

        public static readonly LocKey NotePrintedLineWithProfile =
            new("prompt.note.printed_line_with_profile", "{n}. {tag}  [{profile}]");

        public static readonly LocKey NotePrintedDeclared =
            new("prompt.note.printed_declared", "{count} vial(s) declared.");

        // -- Cartons ---------------------------------------------------------------------------------

        public static readonly LocKey CartonName =
            new("prompt.carton.name", "Carton {job}");

        public static readonly LocKey CartonInspection =
            new("prompt.carton.inspection", "CARTON {job}\nFrom {sender}");

        public static readonly LocKey CartonInspectionUnnamed =
            new("prompt.carton.inspection_unnamed", "CARTON {job}\nFrom an unnamed sender");

        public static readonly LocKey CartonFlatten =
            new("prompt.carton.flatten", "Flatten carton {job}");

        public static readonly LocKey CartonFlattened =
            new("prompt.carton.flattened", "Carton {job} flattened.");

        public static readonly LocKey CartonEmptyNoteInside =
            new("prompt.carton.empty_note_inside",
                "Carton {job} — empty. The delivery note is still in it.");

        public static readonly LocKey CartonTakeSealed =
            new("prompt.carton.take_sealed", "Take carton {job} — {sender}");

        public static readonly LocKey CartonTakeSealedUnnamed =
            new("prompt.carton.take_sealed_unnamed", "Take carton {job} — an unnamed sender");

        public static readonly LocKey CartonTakeOneVial =
            new("prompt.carton.take_one_vial", "Take carton {job} (1 vial still in it)");

        public static readonly LocKey CartonTakeVials =
            new("prompt.carton.take_vials", "Take carton {job} ({count} vials still in it)");

        public static readonly LocKey CartonSetDownFirst =
            new("prompt.carton.set_down_first", "Set the carton down before opening it");

        public static readonly LocKey CartonHoldToOpen =
            new("prompt.carton.hold_to_open", "Hold to open carton {job}");

        public static readonly LocKey CartonOpened =
            new("prompt.carton.opened", "Carton {job} open — {count} vial(s) and a delivery note.");

        public static readonly LocKey CartonOpenedUnknown =
            new("prompt.carton.opened_unknown", "Carton open.");

        // -- Solvent bottles -------------------------------------------------------------------------

        public static readonly LocKey BottleName =
            new("prompt.bottle.name", "Solvent bottle ({charges}/{capacity})");

        public static readonly LocKey BottleInspection =
            new("prompt.bottle.inspection", "SOLVENT\n{charges} / {capacity} flushes remaining");

        public static readonly LocKey BottleInspectionEmpty =
            new("prompt.bottle.inspection_empty", "SOLVENT\nEMPTY\n\nRefill at the wash station.");

        public static readonly LocKey BottleSayEmpty =
            new("prompt.bottle.say_empty", "Solvent bottle: empty. Refill it at the wash station.");

        public static readonly LocKey BottleSayOneFlush =
            new("prompt.bottle.say_one_flush", "Solvent bottle: 1 flush left.");

        public static readonly LocKey BottleSayFlushes =
            new("prompt.bottle.say_flushes", "Solvent bottle: {count} flushes left.");

        public static readonly LocKey BottleUseHint =
            new("prompt.bottle.use_hint", "check the bottle");

        // -- The solvent tap -------------------------------------------------------------------------

        public static readonly LocKey ValveWrongItem =
            new("prompt.valve.wrong_item", "Solvent tap — you need a bottle, not that");

        public static readonly LocKey ValveNoBottle =
            new("prompt.valve.no_bottle", "Solvent tap — fetch a bottle from the cradle");

        public static readonly LocKey ValveBottleFull =
            new("prompt.valve.bottle_full", "Bottle is full ({capacity} flushes)");

        public static readonly LocKey ValveDrumEmpty =
            new("prompt.valve.drum_empty", "Solvent drum is empty — order more at the terminal");

        public static readonly LocKey ValveHoldToFillOne =
            new("prompt.valve.hold_to_fill_one",
                "Hold to fill ({seconds}s, +1 flush, {drum} left in the drum)");

        public static readonly LocKey ValveHoldToFill =
            new("prompt.valve.hold_to_fill",
                "Hold to fill ({seconds}s, +{charges} flushes, {drum} left in the drum)");

        public static readonly LocKey ValveFilled =
            new("prompt.valve.filled", "Solvent bottle topped up.");

        // -- The wash station ------------------------------------------------------------------------
        //
        // WashDrum is the one phrase nested inside other lines here — see the type doc.

        public static readonly LocKey WashDrumOne =
            new("prompt.wash.drum_one", "drum holds 1 flush");

        public static readonly LocKey WashDrum =
            new("prompt.wash.drum", "drum holds {count} flushes");

        public static readonly LocKey WashSetDown =
            new("prompt.wash.set_down", "Wash station — set {item} down ({drum})");

        public static readonly LocKey WashSetDownUnknown =
            new("prompt.wash.set_down_unknown", "Wash station — set {item} down");

        public static readonly LocKey WashNoCradle =
            new("prompt.wash.no_cradle", "Wash station — no free cradle");

        public static readonly LocKey WashSolventOnly =
            new("prompt.wash.solvent_only", "Wash station — solvent only ({drum})");

        public static readonly LocKey WashSolventOnlyUnknown =
            new("prompt.wash.solvent_only_unknown", "Wash station — solvent only");

        public static readonly LocKey WashIdle =
            new("prompt.wash.idle", "Wash station — {drum}. Take a bottle to fill it.");

        public static readonly LocKey WashIdleUnknown =
            new("prompt.wash.idle_unknown", "Wash station — take a bottle to fill it.");

        public static readonly LocKey WashStowed =
            new("prompt.wash.stowed", "Solvent bottle stowed.");

        // -- Reference manuals -----------------------------------------------------------------------

        public static readonly LocKey BookInspectionHelp =
            new("prompt.book.inspection_help", "Click a folded page corner to turn");

        public static readonly LocKey BookRackOneManual =
            new("prompt.bookrack.one_manual", "Reference shelf — 1 manual. Look at one to take it.");

        public static readonly LocKey BookRackManuals =
            new("prompt.bookrack.manuals",
                "Reference shelf — {count} manuals. Look at one to take it.");

        public static readonly LocKey BookRackEmpty =
            new("prompt.bookrack.empty", "Reference shelf — every manual is out.");

        public static readonly LocKey BookRackManualsOnly =
            new("prompt.bookrack.manuals_only", "The shelf is for manuals.");

        public static readonly LocKey BookRackShelve =
            new("prompt.bookrack.shelve", "Shelve {item}");

        public static readonly LocKey BookRackFull =
            new("prompt.bookrack.full", "Shelf full");

        public static readonly LocKey BookRackShelved =
            new("prompt.bookrack.shelved", "{item} shelved.");

        // -- Sample racks ----------------------------------------------------------------------------

        public static readonly LocKey RackEmpty =
            new("prompt.rack.empty", "Rack — empty");

        public static readonly LocKey RackOneSample =
            new("prompt.rack.one_sample", "Rack — 1 sample. Look at one to take it.");

        public static readonly LocKey RackSamples =
            new("prompt.rack.samples", "Rack — {count} samples. Look at one to take it.");

        public static readonly LocKey RackSetDown =
            new("prompt.rack.set_down", "Set down in rack ({free} free)");

        public static readonly LocKey RackFull =
            new("prompt.rack.full", "Rack full");

        // -- Instruments -----------------------------------------------------------------------------

        public static readonly LocKey MachineRunning =
            new("prompt.machine.running", "{machine} — running, {seconds}s left");

        public static readonly LocKey MachineHoldToLoad =
            new("prompt.machine.hold_to_load", "Hold to load into {machine}");

        public static readonly LocKey MachineHoldToShakeAndLoad =
            new("prompt.machine.hold_to_shake_and_load", "Hold to shake and load into {machine}");

        public static readonly LocKey MachineNotEnoughVolume =
            new("prompt.machine.not_enough_volume", "{machine} needs {needed} ml — {left} ml left");

        public static readonly LocKey MachineNeedsPreheat =
            new("prompt.machine.needs_preheat", "{machine}: sample is cold, needs preheating");

        public static readonly LocKey MachineOccupied =
            new("prompt.machine.occupied", "{machine} is occupied");

        public static readonly LocKey MachineTakeVial =
            new("prompt.machine.take_vial", "Take vial from {machine}");

        public static readonly LocKey MachineShiftOver =
            new("prompt.machine.shift_over", "{machine} — shift over, no new runs");

        public static readonly LocKey MachineRun =
            new("prompt.machine.run", "Run {machine} ({seconds}s)");

        public static readonly LocKey MachineEmpty =
            new("prompt.machine.empty", "{machine} — empty");

        public static readonly LocKey MachineStarted =
            new("prompt.machine.started", "{machine}: running. {seconds}s.");

        public static readonly LocKey MachineCalibrationHeadline =
            new("prompt.machine.calibration_headline", "CAL {delta}%");

        public static readonly LocKey MachineCalibrationSuspect =
            new("prompt.machine.calibration_suspect", "{count} FILED SUSPECT");

        public static readonly LocKey MachineCalibrationClear =
            new("prompt.machine.calibration_clear", "NOTHING IN DOUBT");

        // -- Instrument buttons ----------------------------------------------------------------------

        public static readonly LocKey ActionFlushWhileRunning =
            new("prompt.action.flush_while_running", "Cannot flush while running");

        public static readonly LocKey ActionNeedsBottleNotThat =
            new("prompt.action.needs_bottle_not_that", "{machine} needs a solvent bottle, not that");

        public static readonly LocKey ActionFetchBottle =
            new("prompt.action.fetch_bottle",
                "{machine}: fetch a solvent bottle from the wash station");

        public static readonly LocKey ActionBottleEmpty =
            new("prompt.action.bottle_empty", "Solvent bottle is empty — refill at the wash station");

        public static readonly LocKey ActionHoldToFlushOne =
            new("prompt.action.hold_to_flush_one", "Hold to flush {machine} ({seconds}s, 1 of 1 charge)");

        public static readonly LocKey ActionHoldToFlush =
            new("prompt.action.hold_to_flush",
                "Hold to flush {machine} ({seconds}s, 1 of {charges} charges)");

        public static readonly LocKey ActionBusy =
            new("prompt.action.busy", "Instrument busy");

        public static readonly LocKey ActionRemoveVialCalibrate =
            new("prompt.action.remove_vial_calibrate", "Remove the vial before calibrating");

        public static readonly LocKey ActionNeedsFreshCheck =
            new("prompt.action.needs_fresh_check", "Run today's certified standard first");

        public static readonly LocKey ActionCannotAfford =
            new("prompt.action.cannot_afford", "Cannot afford the calibration");

        public static readonly LocKey ActionRecalibrate =
            new("prompt.action.recalibrate", "Recalibrate {machine} ({seconds}s, £{cost})");

        public static readonly LocKey ActionRemoveVialStandard =
            new("prompt.action.remove_vial_standard", "Remove the vial before running a standard");

        public static readonly LocKey ActionShiftOver =
            new("prompt.action.shift_over", "Shift over — no new runs");

        public static readonly LocKey ActionNoStandards =
            new("prompt.action.no_standards", "No certified standards — order them at the terminal");

        public static readonly LocKey ActionRunStandard =
            new("prompt.action.run_standard",
                "Run certified standard ({seconds}s, 1 ampoule) — flush afterwards");

        public static readonly LocKey ActionRemoveVialBlank =
            new("prompt.action.remove_vial_blank", "Remove the vial before running a blank");

        public static readonly LocKey ActionRunBlank =
            new("prompt.action.run_blank", "Run solvent blank ({seconds}s)");

        public static readonly LocKey ActionFlushed =
            new("prompt.action.flushed", "{machine}: flushed. Residue cleared.");

        public static readonly LocKey ActionStandardRunning =
            new("prompt.action.standard_running",
                "{machine}: certified standard running. Compare it against the certificate at the " +
                "terminal.");

        public static readonly LocKey ActionRecalibrating =
            new("prompt.action.recalibrating", "{machine}: recalibrating.");

        public static readonly LocKey ActionBlankRunning =
            new("prompt.action.blank_running",
                "{machine}: blank running. Check the terminal for what it finds.");

        // -- The terminal ----------------------------------------------------------------------------

        public static readonly LocKey TerminalName =
            new("prompt.terminal.name", "Terminal");

        public static readonly LocKey TerminalOpen =
            new("prompt.terminal.open", "Open terminal");

        public static readonly LocKey TerminalOpenWithCount =
            new("prompt.terminal.open_with_count", "Open terminal ({count} open)");

        public static readonly LocKey TerminalFileBlank =
            new("prompt.terminal.file_blank", "File blank slip ({machine})");

        public static readonly LocKey TerminalFileResults =
            new("prompt.terminal.file_results", "File results — {tag}");

        public static readonly LocKey TerminalRackFirst =
            new("prompt.terminal.rack_first", "Rack the vial before filing");

        public static readonly LocKey TerminalNoDisplay =
            new("prompt.terminal.no_display", "Terminal — no display for you");

        public static readonly LocKey TerminalNoDisplayToast =
            new("prompt.terminal.no_display_toast", "This terminal has no display for you.");

        public static readonly LocKey TerminalSlipBlank =
            new("prompt.terminal.slip_blank", "That slip is blank.");

        public static readonly LocKey TerminalBlankFiled =
            new("prompt.terminal.blank_filed", "{machine} blank slip filed.");

        public static readonly LocKey TerminalResultsFiled =
            new("prompt.terminal.results_filed", "{machine} results filed.");

        public static readonly LocKey TerminalResultsFiledTagged =
            new("prompt.terminal.results_filed_tagged", "{tag}: {machine} results filed.");

        // -- The delivery bay ------------------------------------------------------------------------

        public static readonly LocKey BayDeliveryDue =
            new("prompt.bay.delivery_due", "Delivery due at the bay in about {seconds}s.");

        public static readonly LocKey BayArrived =
            new("prompt.bay.arrived", "Delivery at the bay — carton {job}. It needs carrying in.");

        public static readonly LocKey BayArrivedMore =
            new("prompt.bay.arrived_more",
                "Delivery at the bay — carton {job} and {count} more. It needs carrying in.");

        public static readonly LocKey BayFull =
            new("prompt.bay.full",
                "Bay full — {count} carton(s) still on the truck. Carry one in and the rest come off.");

        // -- Setting something down wherever you are ---------------------------------------------------

        public static readonly LocKey DropNoPlayer =
            new("prompt.drop.no_player", "There is nobody here to set that down.");

        public static readonly LocKey DropNoRoom =
            new("prompt.drop.no_room", "There is no room for that there.");

        public static readonly LocKey DropNowhere =
            new("prompt.drop.nowhere", "There is nowhere to set that down here.");

        public static readonly LocKey DropNothingUnderfoot =
            new("prompt.drop.nothing_underfoot",
                "There is nothing under your feet to set that down on.");

        public static readonly LocKey DropHandsEmpty =
            new("prompt.drop.hands_empty", "Your hands are empty.");

        // -- Door signs ---------------------------------------------------------------------------
        //
        // Printed on the plates beside each door (SignPlate). Short by necessity: the plate is 26 cm
        // across and the word is set to fit it, so a long caption shrinks rather than wraps.

        public static readonly LocKey SignLab = new("prompt.sign.lab", "LABORATORY");

        public static readonly LocKey SignStore = new("prompt.sign.store", "STORE");

        public static readonly LocKey SignOffice = new("prompt.sign.office", "OFFICE");
    }
}
