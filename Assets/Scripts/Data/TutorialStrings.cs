namespace Residue.Data
{
    /// <summary>
    /// Every word on the tutorial's objective card (#55).
    ///
    /// <para>
    /// READ BEFORE EDITING ANY LINE BELOW. The tutorial's rule is the shift brief's rule, and it is
    /// hard rule 1: <b>every line says where to look and how the room works, and no line says what any
    /// sample is.</b> Not one element, fault or root cause may be named anywhere here. A tutorial that
    /// named a symptom would hand a first-time player the diagnostic table the whole game is about
    /// building for themselves — and it would do it before they had run anything, which is worse than
    /// a wiki, because a wiki does not arrive with the game's own authority behind it.
    /// <c>TutorialTests.NoObjectiveText_NamesAnElementFaultOrRootCause</c> holds that shut against the
    /// real content tables, the way <c>OnboardingTests</c> does for the brief.
    /// </para>
    ///
    /// <para>
    /// The other half is hard rule 3: contamination and drift are only fair because a blank run and a
    /// certified standard reveal them. Day two exists to point at those two, and nothing else in the
    /// game explains why anybody would ever push solvent through a working instrument. Do not
    /// translate the blank or the standard out of existence.
    /// </para>
    ///
    /// <para>
    /// <b>Two keys per objective, and they do different jobs.</b> The <c>_line</c> is the imperative
    /// and is drawn for every objective on the card; the <c>_detail</c> is the one extra sentence, and
    /// is drawn only under whichever objective is next. Fourteen two-line rows is a wall nobody reads,
    /// and an imperative with nowhere to carry "[E] does it" is a card that says what to do and not
    /// how. Keeping them separate is what buys both.
    /// </para>
    ///
    /// <para>
    /// The bracketed keys are bindings rather than words — a translator moves and translates the verbs
    /// around them and leaves the brackets alone, exactly as in <see cref="ScreenStrings.HudControls"/>.
    /// The tick marks are not here at all: <c>[x]</c> and <c>[ ]</c> are symbols in the sense
    /// <c>SignalPalette</c>'s glyphs are symbols, not language.
    /// </para>
    /// </summary>
    public static class TutorialStrings
    {
        // -- The card ------------------------------------------------------------------------------

        public static readonly LocKey CardTitle = new("tutorial.title", "INDUCTION");

        public static readonly LocKey DayOneHeading =
            new("tutorial.day_one", "DAY ONE — THE LOOP");

        public static readonly LocKey DayTwoHeading =
            new("tutorial.day_two", "DAY TWO — WHAT A CLEAN RESULT DOES NOT COVER");

        /// <summary>How much of the card is ticked. A count, so it never reads as a grade.</summary>
        public static readonly LocKey Progress = new("tutorial.progress", "{done} of {total}");

        public static readonly LocKey Closing = new("tutorial.closing",
            "None of this has to be done, and nothing in the lab waits for it. [F1] puts the card " +
            "away and brings it back; [Tab] is the standing orders, which say why each of these is " +
            "worth doing.");

        // -- Day one: the loop ---------------------------------------------------------------------

        public static readonly LocKey TakeACartonLine =
            new("tutorial.take_carton_line", "Lift a delivery carton out of the bay.");

        public static readonly LocKey TakeACartonDetail = new("tutorial.take_carton_detail",
            "The truck pulls in a quarter of the way into the shift and the boxes stay where it left " +
            "them. Look at one and press [E]. Your hands hold one thing at a time, so carrying it in " +
            "is a trip you cannot do while carrying anything else.");

        public static readonly LocKey OpenTheCartonLine =
            new("tutorial.open_carton_line", "Cut the carton open.");

        public static readonly LocKey OpenTheCartonDetail = new("tutorial.open_carton_detail",
            "Set the box down first, then hold [E] on it. The sender's delivery note is inside, on " +
            "top of the bottles, and it is the only paperwork saying what should be in there.");

        public static readonly LocKey TakeAVialLine =
            new("tutorial.take_vial_line", "Take a vial out of the box.");

        public static readonly LocKey TakeAVialDetail = new("tutorial.take_vial_detail",
            "Look into an opened carton and press [E]. Each bottle carries its tank's label, and the " +
            "lab files it under exactly that — there is nothing to type in anywhere.");

        public static readonly LocKey LoadAnInstrumentLine =
            new("tutorial.load_instrument_line", "Put a vial into an instrument.");

        public static readonly LocKey LoadAnInstrumentDetail = new("tutorial.load_instrument_detail",
            "Hold [E] at the instrument. That hold is where the bottle gets shaken, so it costs " +
            "seconds you do not get back. Each instrument takes a different amount of oil and there " +
            "is not enough in a vial for all of them, so which ones you spend it on is the decision.");

        public static readonly LocKey StartTheRunLine =
            new("tutorial.start_run_line", "Start the run.");

        public static readonly LocKey StartTheRunDetail = new("tutorial.start_run_detail",
            "The START button is on the instrument's own panel. Its operator manual is lying on the " +
            "bench beside it and says how long the run takes and what it will and will not report.");

        public static readonly LocKey LetARunFinishLine =
            new("tutorial.run_finished_line", "Let a run finish.");

        public static readonly LocKey LetARunFinishDetail = new("tutorial.run_finished_detail",
            "It runs on its own and it holds the instrument for the whole time. Standing and watching " +
            "is the one thing that never helps: go and start something else.");

        public static readonly LocKey FileTheSlipLine =
            new("tutorial.file_slip_line", "Carry the printed slip to the terminal and file it.");

        public static readonly LocKey FileTheSlipDetail = new("tutorial.file_slip_detail",
            "A finished run prints into the tray on the instrument. The reading joins the record only " +
            "when somebody carries that slip to the desk — a slip left on a bench is a test you paid " +
            "for and cannot use.");

        public static readonly LocKey FileAVerdictLine =
            new("tutorial.file_verdict_line", "File a verdict on a sample.");

        public static readonly LocKey FileAVerdictDetail = new("tutorial.file_verdict_detail",
            "At the terminal, once you have filed something to read. Nothing on this card will ever " +
            "tell you which verdict to file or which sample to file it on — that part is the job, and " +
            "it is the whole job.");

        public static readonly LocKey EndTheDayLine =
            new("tutorial.end_day_line", "End the day at the terminal.");

        public static readonly LocKey EndTheDayDetail = new("tutorial.end_day_detail",
            "The shift closes when you say so. Verdicts do not settle today: they come back days " +
            "later, and both directions of getting one wrong cost money.");

        // -- Day two: the two tells ----------------------------------------------------------------

        public static readonly LocKey RunABlankLine =
            new("tutorial.run_blank_line", "Push a solvent blank through an instrument.");

        public static readonly LocKey RunABlankDetail = new("tutorial.run_blank_detail",
            "Take the vial out first. A blank reads back what the last sample left behind in there, " +
            "which would otherwise be added quietly to whatever goes in next. This is the only way to " +
            "see it, and the terminal marks every instrument that has had no blank today.");

        public static readonly LocKey FillABottleLine =
            new("tutorial.fill_bottle_line", "Fill a solvent bottle at the wash station.");

        public static readonly LocKey FillABottleDetail = new("tutorial.fill_bottle_detail",
            "Hold [E] at the drum with a bottle in your hands. The drum is stock you paid for, and " +
            "the walk back is a trip during which you are not carrying anything else.");

        public static readonly LocKey FlushAnInstrumentLine =
            new("tutorial.flush_line", "Flush an instrument.");

        public static readonly LocKey FlushAnInstrumentDetail = new("tutorial.flush_detail",
            "Hold FLUSH at the instrument. One charge out of the bottle per instrument, and it is the " +
            "fix rather than the tell — the blank is what tells you whether it was needed.");

        public static readonly LocKey RunAStandardLine =
            new("tutorial.run_standard_line", "Run a certified reference standard.");

        public static readonly LocKey RunAStandardDetail = new("tutorial.run_standard_detail",
            "Every figure on that ampoule's certificate is printed in the manuals, so what comes back " +
            "tells you how far the instrument has wandered since it was last set right. Calibration " +
            "moves a little every run, in a direction re-rolled each day, and it scales everything " +
            "the instrument tells you.");

        public static readonly LocKey RecalibrateLine =
            new("tutorial.recalibrate_line", "Recalibrate against today's standard.");

        public static readonly LocKey RecalibrateDetail = new("tutorial.recalibrate_detail",
            "It costs money and it holds the instrument while it runs. It also lists every record you " +
            "filed while that instrument was out, so you can reopen the ones with oil left.");
    }
}
