using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the lab lines. See <see cref="German"/> for the rules that apply to every
    /// entry here — duzen throughout, placeholders kept exactly as the English declares them, and
    /// nothing translated that is an id or content-table data.
    ///
    /// <para>
    /// <b><c>{instrument}</c> is a name, not a noun phrase.</b> It is filled by
    /// <c>MachineDef.DisplayName</c> — "Viscometer", "Karl Fischer Titrator" — which carries no
    /// article, so the German around it is written for an article-less noun the way the English is.
    /// Where German case would otherwise have forced an article onto it, the sentence puts it in the
    /// nominative instead: "{instrument} hat schon ein Fläschchen drin" rather than
    /// "In {instrument} steckt schon eines". <see cref="LabStrings.AnInstrument"/> only ever reaches
    /// the two slots governed by a preposition, so it is given in the dative; the definite fallback
    /// reaches mostly nominative slots and is given in the nominative.
    /// </para>
    ///
    /// <para>
    /// <b>The fixed-width pages are re-measured, not re-typed.</b> The manual's figure column starts
    /// at 14 characters and the threshold header sits over a <c>{Id,-9} {normal,-15}</c> layout, so
    /// the German labels are padded to the same columns rather than to the same word lengths.
    /// </para>
    ///
    /// <para>
    /// <b>The standing orders keep their rule.</b> Every line says where to look and no line says
    /// what you will find: no element, fault or root cause is named anywhere in the
    /// <c>book.shift_brief_</c> block, and the blank and the certified standard — the only tells the
    /// player is ever given for contamination and drift — survive the translation intact.
    /// </para>
    /// </summary>
    public static class GermanLab
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            // -- Refusals: the request itself -----------------------------------------------------

            table["refusal.no_such_player"] = "Kein solcher Spieler.";
            table["refusal.lab_not_running"] = "Das Labor läuft nicht.";
            table["refusal.not_understood"] = "Das Labor hat das nicht verstanden.";
            table["refusal.unexplained"] = "Das Labor tut das gerade nicht.";

            // -- Refusals: hands and inventory ----------------------------------------------------

            table["refusal.inventory_full"] = "Dein Inventar ist voll.";
            table["refusal.no_such_sample"] = "Keine solche Probe.";
            table["refusal.no_inventory"] = "Dieser Spieler hat kein Inventar.";
            table["refusal.no_such_inventory_item"] = "Kein solcher Inventargegenstand.";
            table["refusal.item_not_in_inventory"] = "Dieser Gegenstand ist nicht in deinem Inventar.";
            table["refusal.carrying_nothing"] = "Du trägst nichts.";
            table["refusal.nowhere_to_put_down"] = "Hier ist kein Platz, um das abzustellen.";
            table["refusal.not_holding_sample"] = "Du hältst keine Probe in der Hand.";

            // -- Refusals: vials ------------------------------------------------------------------

            table["refusal.vial_in_instrument"] =
                "{tag} steckt in {instrument}. Nimm es am Messgerät heraus.";
            table["refusal.vial_held_by_other"] = "Jemand anderes hält {tag}.";
            table["refusal.vial_spent"] = "{tag} ist aufgebraucht — da ist nichts mehr zu tragen.";

            // -- Refusals: deliveries (#30, #31) ---------------------------------------------------

            table["refusal.no_such_carton"] = "Kein solcher Karton.";
            table["refusal.vial_on_truck"] =
                "{tag} ist noch auf dem Lkw — die Lieferung ist noch nicht abgeladen.";
            table["refusal.carton_sealed"] =
                "Karton {job} ist noch versiegelt. Öffne ihn, bevor du etwas herausnimmst.";
            table["refusal.carton_in_your_arms"] =
                "Stell den Karton ab, bevor du Fläschchen herausnimmst.";
            table["refusal.carton_held_by_other"] = "Jemand anderes trägt Karton {job}.";

            // -- Refusals: paperwork ---------------------------------------------------------------

            table["refusal.slip_already_filed"] = "Dieser Ausdruck wurde bereits eingereicht.";
            table["refusal.not_carrying_that_slip"] = "Du trägst diesen Ausdruck nicht bei dir.";
            table["refusal.not_a_verdict"] = "Das ist kein Befund.";

            // -- Refusals: solvent -----------------------------------------------------------------

            table["refusal.no_such_bottle"] = "Keine solche Lösungsmittelflasche.";
            table["refusal.not_carrying_bottle"] = "Du trägst keine Lösungsmittelflasche.";
            table["refusal.flush_needs_bottle"] =
                "Du brauchst eine Lösungsmittelflasche in den Händen. Füll eine an der " +
                "Waschstation.";

            // -- Refusals: where you are standing --------------------------------------------------

            table["refusal.not_at_instrument"] = "Du stehst nicht an {instrument}.";
            table["refusal.not_at_shelf"] = "Du stehst nicht an diesem Regal.";
            table["refusal.not_at_slip"] = "Du stehst nicht an diesem Ausdruck.";
            table["refusal.not_at_carton"] = "Du stehst nicht an Karton {job}.";
            table["refusal.not_at_delivery_note"] = "Du stehst nicht an diesem Lieferschein.";
            table["refusal.not_at_wash_station"] = "Du stehst nicht an der Waschstation.";
            table["refusal.not_at_terminal"] = "Du stehst nicht am Terminal.";

            // -- Refusals: instruments -------------------------------------------------------------

            table["refusal.instrument_unnamed"] = "das Messgerät";
            table["refusal.instrument_indefinite"] = "einem Messgerät";
            table["refusal.no_such_instrument"] = "Kein solches Messgerät.";
            table["refusal.shift_over"] = "Schicht vorbei — keine neuen Läufe.";
            table["refusal.instrument_busy"] = "{instrument} ist Ausdruckt.";
            table["refusal.instrument_empty"] = "{instrument} ist leer.";
            table["refusal.instrument_will_not_start"] = "{instrument} startet gerade keinen Lauf.";
            table["refusal.cannot_flush_while_running"] =
                "{instrument} kann während eines Laufs nicht gespült werden.";
            table["refusal.blank_needs_empty_instrument"] =
                "Nimm das Fläschchen heraus, bevor du einen Blindwert fährst.";
            table["refusal.instrument_will_not_blank"] =
                "{instrument} nimmt gerade keinen Blindwert an.";

            // -- Refusals: loading an instrument -----------------------------------------------------
            //
            // Each still names the figure that made it refuse — hard rule 3 does not survive a
            // translation that says "zu wenig Öl" where the English said how much was needed.

            table["refusal.instrument_occupied"] = "{instrument} hat schon ein Fläschchen drin.";
            table["refusal.not_enough_volume"] =
                "{instrument} braucht {needed} ml, und {tag} hat noch {left} ml.";
            table["refusal.needs_preheat"] =
                "{tag} hat {actual} °C — {instrument} braucht rund {target} °C.";
            table["refusal.not_settled"] =
                "{tag} hat sich abgesetzt. Schüttle es auf, bevor du es fährst (§4.5).";
            table["refusal.instrument_refuses_load"] = "{instrument} nimmt das nicht an.";

            // -- Refusals: the terminal --------------------------------------------------------------

            table["refusal.order_at_least_one_unit"] = "Bestell mindestens eine Einheit.";
            table["refusal.cannot_afford_solvent"] =
                "Eine Nachbestellung über {units} Einheiten kostet £{cost}, und das Konto deckt " +
                "das nicht.";
            table["refusal.order_at_least_one_ampoule"] = "Bestell mindestens eine Ampulle.";
            table["refusal.cannot_afford_standards"] =
                "{count} zertifizierte Ampullen kosten £{cost}, und das Konto deckt das nicht.";
            table["refusal.day_already_over"] = "Der Tag ist schon vorbei.";
            table["refusal.shift_still_running"] = "Die Schicht läuft noch.";
            table["refusal.run_is_over"] = "Der Durchlauf ist vorbei — es gibt keinen nächsten Tag.";

            // -- Books: covers -------------------------------------------------------------------

            table["book.title_operator_manual_for"] = "{instrument} — Bedienungsanleitung";
            table["book.title_operator_manual"] = "Bedienungsanleitung";
            table["book.title_element_index"] = "Elemente & Quellen";
            table["book.title_diagnostic_guide"] = "Diagnoseleitfaden";
            table["book.title_threshold_tables"] = "Grenzwerttabellen";
            table["book.title_reference"] = "Nachschlagewerk";

            // -- Books: element categories -----------------------------------------------------------

            table["book.category_wear_metals"] = "Verschleißmetalle";
            table["book.category_contaminants"] = "Verunreinigungen";
            table["book.category_additives"] = "Additive";
            table["book.category_fluid_properties"] = "Fluideigenschaften";

            // -- Books: the operator manual ----------------------------------------------------------
            //
            // The figure column starts at column 14 and the line breaks are paper, not prose. Both
            // are re-measured for the German rather than copied from the English.

            table["book.manual_operation_title"] = "Betrieb";
            table["book.manual_run_time"] = "Laufzeit      {seconds} s";
            table["book.manual_sample_used"] = "Probenmenge   {millilitres} ml";
            table["book.manual_cost_per_run"] = "Kosten/Lauf   £{cost}";
            table["book.manual_noise"] =
                "Die typische Streuung eines Messwerts liegt bei rund {percent}%.";
            table["book.manual_drift"] =
                "Die Kalibrierung driftet rund {percent}% pro Lauf, in einer\n" +
                "Richtung, die jeden Tag neu ausgewürfelt wird. Fahre eine\n" +
                "zertifizierte Referenzprobe, wenn du sie im Verdacht hast.";
            table["book.manual_carryover"] =
                "Verschleppung: Rund {percent}% von dem, was zuletzt\n" +
                "durchlief, bleibt zurück. Ein Lösungsmittel-Blindwert macht\n" +
                "es sichtbar. Zum Entfernen füllst du eine Flasche an der\n" +
                "Waschstation und hältst hier die FLUSH-Taste. Eine Ladung\n" +
                "pro Messgerät.";
            table["book.manual_fume_hood"] = "Benötigt einen Abzug.";
            table["book.manual_preheat"] = "Benötigt Vorwärmung auf {celsius} C.";
            table["book.manual_reports_title"] = "Bericht";
            table["book.manual_reports_intro"] = "Dieses Messgerät berichtet:";
            table["book.manual_blind_spots_title"] = "Blinde Flecken";
            table["book.manual_no_blind_spots"] =
                "Keine bekannten blinden Flecken für die Größen, die dieses\n" +
                "Messgerät berichtet.\n" +
                "\n" +
                "Das heißt nicht, dass ein sauberes Ergebnis die Probe\n" +
                "entlastet. Es entlastet nur das, was dieses Messgerät misst.\n" +
                "Was es NICHT berichtet, steht auf der vorigen Seite.";
            table["book.manual_cannot_detect"] = "NICHT NACHWEISBAR";
            table["book.manual_cannot_detect_closing"] =
                "Diese fehlen im Bericht, auch wenn sie in der Probe\n" +
                "vorhanden sind. Ein sauberes Ergebnis hier ist keine\n" +
                "saubere Probe.";

            // -- Books: the threshold tables ---------------------------------------------------------
            //
            // The header sits over "{Id,-9} {normal,-15} {critical}": NORMAL at column 10, the
            // critical column at 26. The padding is the layout, so it is kept to the character.

            table["book.thresholds_grade"] = "Ölsorte {grade}   Wechselintervall {hours} h";
            table["book.thresholds_columns"] = "ELEMENT   NORMAL          KRITISCH";
            table["book.thresholds_footer"] =
                "Grenzwerte gelten je Anlagentyp. Derselbe Eisenwert kann an\n" +
                "einer Anlage Routine sein und an einer anderen ein Grund,\n" +
                "sie außer Betrieb zu nehmen.";

            // -- Books: the standing orders (#47) ------------------------------------------------------
            //
            // READ BEFORE EDITING ANY LINE BELOW, and read the English block first. Every line says
            // where to look and no line says what you will find: not one element, fault or root
            // cause may be named anywhere in it. The German adds nothing the English does not say —
            // and it must not drop the blank or the certified standard, because those two sentences
            // are the only place the player is told those tools exist (hard rule 3).

            table["book.shift_brief_title"] = "DIENSTANWEISUNG";
            table["book.shift_brief_closing"] =
                "Nichts davon sagt dir, was eine Probe ist. Genau das ist die Arbeit.";
            table["book.shift_brief_manuals_title"] = "Die Anleitungen sind keine Deko";
            table["book.shift_brief_manuals_body"] =
                "Neben jedem Messgerät liegt seine Bedienungsanleitung auf der Bank, und im Regal " +
                "beim Terminal stehen {elements}, {diagnostics} und {thresholds}. Eine Anleitung " +
                "sagt, was ihr Messgerät berichtet und — das ist der Teil, auf den es ankommt — " +
                "was es nicht sehen kann. Schau eine an und drücke [E], um sie aufzuheben.";
            table["book.shift_brief_loading_title"] =
                "Beladen ist ein Halten, und das Halten ist das Schütteln";
            table["book.shift_brief_loading_body"] =
                "Halte [E] an einem Messgerät, um ein Fläschchen zu laden. Bei diesem Halten wird " +
                "die Probe geschüttelt, es kostet also Sekunden, die du nicht zurückbekommst. Ein " +
                "Fläschchen, das kalt angekommen ist, muss erst erwärmt werden; das Messgerät " +
                "sagt es dir, wenn es sich weigert.";
            table["book.shift_brief_filing_title"] = "Nichts reicht sich von selbst ein";
            table["book.shift_brief_filing_body"] =
                "Ein fertiger Lauf druckt einen Ausdruck in das Fach am Messgerät. Der Messwert kommt " +
                "erst dann ins Protokoll, wenn du diesen Ausdruck zum Terminal trägst. Ein Ausdruck, der " +
                "auf einer Bank liegen bleibt, ist ein Test, den du bezahlt hast und nicht nutzen " +
                "kannst.";
            table["book.shift_brief_dirty_title"] =
                "Ein Messgerät ist schmutzig, bis du beweist, dass es sauber ist";
            table["book.shift_brief_dirty_body"] =
                "Ein Teil der letzten Probe bleibt zurück und taucht in der nächsten wieder auf. " +
                "Ein Lösungsmittel-Blindwert liest zurück, was darin steckt. Zum Entfernen füllst " +
                "du eine Flasche an der Waschstation und hältst am Messgerät FLUSH. Das Terminal " +
                "markiert jedes Messgerät, das heute noch keinen Blindwert hatte.";
            table["book.shift_brief_drift_title"] =
                "Ein Messgerät driftet, bis du beweist, dass es das nicht tut";
            table["book.shift_brief_drift_body"] =
                "Die Kalibrierung wandert bei jedem Lauf ein Stück, in einer Richtung, die jeden " +
                "Tag neu ausgewürfelt wird, und sie skaliert unbemerkt alles, was das Messgerät " +
                "dir sagt. Gemessen wird sie mit einem zertifizierten Standard. Das Terminal " +
                "markiert jedes Messgerät, das heute noch keinen Standard hatte.";
            table["book.shift_brief_verdict_title"] = "Ein Befund ist eine Rechnung, die später kommt";
            table["book.shift_brief_verdict_body"] =
                "Einreichen schließt eine Probe ab, aber die Folge trifft erst Tage später ein. " +
                "Beide Richtungen kosten: einen brauchbaren Tank zu verurteilen ist teuer, und " +
                "einen schlechten durchgehen zu lassen ist schlimmer. Bezahlt wird das richtige " +
                "Benennen der Ursache.";
        }
    }
}
