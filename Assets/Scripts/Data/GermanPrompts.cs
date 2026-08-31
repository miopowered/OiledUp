using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the prompt lines. See <see cref="German"/> for the rules that apply to every
    /// entry here — duzen throughout, placeholders kept exactly as the English declares them, and
    /// nothing translated that is an id or content-table data.
    /// </summary>
    public static class GermanPrompts
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            // -- Shared ------------------------------------------------------------------------------

            table["prompt.take_item"] = "{item} nehmen";
            table["prompt.inventory_full"] = "Inventar voll";
            table["prompt.hands_full"] = "Hände voll";
            table["prompt.item_set_down"] = "{item} abgestellt.";

            // -- Vials -------------------------------------------------------------------------------

            table["prompt.vial.inspection"] = "PROBE {id}\nKundenetikett: {label}";

            // -- Results slips -----------------------------------------------------------------------

            table["prompt.printout.name"] = "Ausdruck {machine} — {tag}";
            table["prompt.printout.take"] = "Ausdruck nehmen — {tag}";
            table["prompt.printout.take_blank"] = "Blindwert-Ausdruck nehmen — {machine}";
            table["prompt.printout.use_hint"] = "Ausdruck lesen";
            table["prompt.printout.heading"] = "{tag} — {machine}";
            table["prompt.printout.heading_blank"] = "{machine} — BLINDWERT";
            table["prompt.printout.paper_blank"] = "Dieses Blatt ist leer.";
            table["prompt.printout.numbers_pending"] =
                "Die Werte auf diesem Blatt sind noch nicht da.";
            table["prompt.printout.no_values"] = "Keine Werte gemeldet.";

            // -- Delivery notes ----------------------------------------------------------------------

            table["prompt.note.name"] = "Lieferschein {job}";
            table["prompt.note.take"] = "Lieferschein {job} nehmen ({sender})";
            table["prompt.note.take_unnamed"] = "Lieferschein {job} nehmen (Absender ohne Namen)";
            table["prompt.note.printed_heading"] = "LIEFERSCHEIN {job}";
            table["prompt.note.printed_sender_unknown"] = "Absender nicht angegeben";
            // "Warenausgang" rather than "ausgebucht": this is the goods-out date on a Lieferschein,
            // and "ausgebucht" reads as fully booked or sold out in German — the wrong sense of the
            // English idiom, on the one document #32 expects the player to read carefully.
            table["prompt.note.printed_booked_out"] = "Warenausgang Tag {day}";
            table["prompt.note.printed_line"] = "{n}. {tag}";
            table["prompt.note.printed_line_with_profile"] = "{n}. {tag}  [{profile}]";
            table["prompt.note.printed_declared"] = "{count} Fläschchen angegeben.";

            // -- Cartons -----------------------------------------------------------------------------

            table["prompt.carton.name"] = "Karton {job}";
            table["prompt.carton.inspection"] = "KARTON {job}\nVon {sender}";
            table["prompt.carton.inspection_unnamed"] = "KARTON {job}\nVon einem Absender ohne Namen";
            table["prompt.carton.flatten"] = "Karton {job} zusammenfalten";
            table["prompt.carton.flattened"] = "Karton {job} zusammengefaltet.";
            table["prompt.carton.empty_note_inside"] =
                "Karton {job} — leer. Der Lieferschein liegt noch darin.";
            table["prompt.carton.take_sealed"] = "Karton {job} nehmen — {sender}";
            table["prompt.carton.take_sealed_unnamed"] = "Karton {job} nehmen — Absender ohne Namen";
            table["prompt.carton.take_one_vial"] = "Karton {job} nehmen (noch 1 Fläschchen darin)";
            table["prompt.carton.take_vials"] =
                "Karton {job} nehmen (noch {count} Fläschchen darin)";
            table["prompt.carton.set_down_first"] = "Karton vor dem Öffnen abstellen";
            table["prompt.carton.hold_to_open"] = "Halten, um Karton {job} zu öffnen";
            table["prompt.carton.opened"] =
                "Karton {job} offen — {count} Fläschchen und ein Lieferschein.";
            table["prompt.carton.opened_unknown"] = "Karton offen.";

            // -- Solvent bottles ---------------------------------------------------------------------

            table["prompt.bottle.name"] = "Lösungsmittelflasche ({charges}/{capacity})";
            table["prompt.bottle.inspection"] =
                "LÖSUNGSMITTEL\n{charges} / {capacity} Spülungen übrig";
            table["prompt.bottle.inspection_empty"] =
                "LÖSUNGSMITTEL\nLEER\n\nAn der Waschstation auffüllen.";
            table["prompt.bottle.say_empty"] =
                "Lösungsmittelflasche: leer. Füll sie an der Waschstation auf.";
            table["prompt.bottle.say_one_flush"] = "Lösungsmittelflasche: noch 1 Spülung.";
            table["prompt.bottle.say_flushes"] = "Lösungsmittelflasche: noch {count} Spülungen.";
            table["prompt.bottle.use_hint"] = "Flasche prüfen";

            // -- The solvent tap ---------------------------------------------------------------------

            table["prompt.valve.wrong_item"] =
                "Lösungsmittelhahn — du brauchst eine Flasche, nicht das";
            table["prompt.valve.no_bottle"] =
                "Lösungsmittelhahn — hol eine Flasche aus der Halterung";
            table["prompt.valve.bottle_full"] = "Flasche ist voll ({capacity} Spülungen)";
            table["prompt.valve.drum_empty"] =
                "Lösungsmittelfass ist leer — am Terminal nachbestellen";
            table["prompt.valve.hold_to_fill_one"] =
                "Halten, um zu füllen ({seconds}s, +1 Spülung, noch {drum} im Fass)";
            table["prompt.valve.hold_to_fill"] =
                "Halten, um zu füllen ({seconds}s, +{charges} Spülungen, noch {drum} im Fass)";
            table["prompt.valve.filled"] = "Lösungsmittelflasche aufgefüllt.";

            // -- The wash station --------------------------------------------------------------------
            //
            // "Fass reicht für …" is the nested phrase: a complete clause, so the three sentences it
            // lands in can put it where German wants it rather than only at the end.

            table["prompt.wash.drum_one"] = "Fass reicht für 1 Spülung";
            table["prompt.wash.drum"] = "Fass reicht für {count} Spülungen";
            table["prompt.wash.set_down"] = "Waschstation — {item} abstellen ({drum})";
            table["prompt.wash.set_down_unknown"] = "Waschstation — {item} abstellen";
            table["prompt.wash.no_cradle"] = "Waschstation — keine freie Halterung";
            table["prompt.wash.solvent_only"] = "Waschstation — nur Lösungsmittel ({drum})";
            table["prompt.wash.solvent_only_unknown"] = "Waschstation — nur Lösungsmittel";
            table["prompt.wash.idle"] = "Waschstation — {drum}. Nimm eine Flasche zum Füllen.";
            table["prompt.wash.idle_unknown"] = "Waschstation — nimm eine Flasche zum Füllen.";
            table["prompt.wash.stowed"] = "Lösungsmittelflasche verstaut.";

            // -- Reference manuals -------------------------------------------------------------------

            table["prompt.book.inspection_help"] = "Zum Blättern auf eine geknickte Seitenecke klicken";
            table["prompt.bookrack.one_manual"] =
                "Handbuchregal — 1 Handbuch. Schau eines an, um es zu nehmen.";
            table["prompt.bookrack.manuals"] =
                "Handbuchregal — {count} Handbücher. Schau eines an, um es zu nehmen.";
            table["prompt.bookrack.empty"] = "Handbuchregal — alle Handbücher sind entnommen.";
            table["prompt.bookrack.manuals_only"] = "Das Regal ist für Handbücher.";
            table["prompt.bookrack.shelve"] = "{item} einräumen";
            table["prompt.bookrack.full"] = "Regal voll";
            table["prompt.bookrack.shelved"] = "{item} eingeräumt.";

            // -- Sample racks ------------------------------------------------------------------------

            table["prompt.rack.empty"] = "Probenständer — leer";
            table["prompt.rack.one_sample"] =
                "Probenständer — 1 Probe. Schau eine an, um sie zu nehmen.";
            table["prompt.rack.samples"] =
                "Probenständer — {count} Proben. Schau eine an, um sie zu nehmen.";
            table["prompt.rack.set_down"] = "In den Probenständer stellen ({free} frei)";
            table["prompt.rack.full"] = "Probenständer voll";

            // -- Instruments -------------------------------------------------------------------------

            table["prompt.machine.running"] = "{machine} — läuft, noch {seconds}s";
            table["prompt.machine.hold_to_load"] = "Halten, um in {machine} zu laden";
            table["prompt.machine.hold_to_shake_and_load"] =
                "Halten, um zu schütteln und in {machine} zu laden";
            table["prompt.machine.not_enough_volume"] =
                "{machine} braucht {needed} ml — noch {left} ml";
            table["prompt.machine.needs_preheat"] =
                "{machine}: Probe ist kalt, muss vorgewärmt werden";
            table["prompt.machine.occupied"] = "{machine} ist belegt";
            table["prompt.machine.take_vial"] = "Fläschchen aus {machine} nehmen";
            table["prompt.machine.shift_over"] = "{machine} — Schicht vorbei, keine neuen Läufe";
            table["prompt.machine.run"] = "{machine} starten ({seconds}s)";
            table["prompt.machine.empty"] = "{machine} — leer";
            table["prompt.machine.started"] = "{machine}: läuft. {seconds}s.";
            table["prompt.machine.calibration_headline"] = "KAL {delta}%";
            table["prompt.machine.calibration_suspect"] = "{count} BEFUNDE FRAGLICH";
            table["prompt.machine.calibration_clear"] = "NICHTS FRAGLICH";

            // -- Instrument buttons ------------------------------------------------------------------

            table["prompt.action.flush_while_running"] = "Spülen im Betrieb nicht möglich";
            table["prompt.action.needs_bottle_not_that"] =
                "{machine} braucht eine Lösungsmittelflasche, nicht das";
            table["prompt.action.fetch_bottle"] =
                "{machine}: hol eine Lösungsmittelflasche von der Waschstation";
            table["prompt.action.bottle_empty"] =
                "Lösungsmittelflasche ist leer — an der Waschstation auffüllen";
            table["prompt.action.hold_to_flush_one"] =
                "Halten, um {machine} zu spülen ({seconds}s, 1 von 1 Spülung)";
            table["prompt.action.hold_to_flush"] =
                "Halten, um {machine} zu spülen ({seconds}s, 1 von {charges} Spülungen)";
            table["prompt.action.busy"] = "Messgerät belegt";
            table["prompt.action.remove_vial_calibrate"] =
                "Vor dem Kalibrieren das Fläschchen entnehmen";
            table["prompt.action.needs_fresh_check"] =
                "Erst den heutigen zertifizierten Standard messen";
            table["prompt.action.cannot_afford"] = "Geld reicht nicht für die Kalibrierung";
            table["prompt.action.recalibrate"] = "{machine} neu kalibrieren ({seconds}s, £{cost})";
            table["prompt.action.remove_vial_standard"] =
                "Vor dem Standard das Fläschchen entnehmen";
            table["prompt.action.shift_over"] = "Schicht vorbei — keine neuen Läufe";
            table["prompt.action.no_standards"] =
                "Keine zertifizierten Standards — am Terminal bestellen";
            table["prompt.action.run_standard"] =
                "Zertifizierten Standard messen ({seconds}s, 1 Ampulle) — danach spülen";
            table["prompt.action.remove_vial_blank"] =
                "Vor dem Blindwert das Fläschchen entnehmen";
            table["prompt.action.run_blank"] = "Lösungsmittel-Blindwert messen ({seconds}s)";
            table["prompt.action.flushed"] = "{machine}: gespült. Rückstände entfernt.";
            table["prompt.action.standard_running"] =
                "{machine}: zertifizierter Standard läuft. Vergleich ihn am Terminal mit dem " +
                "Zertifikat.";
            table["prompt.action.recalibrating"] = "{machine}: kalibriert neu.";
            table["prompt.action.blank_running"] =
                "{machine}: Blindwert läuft. Schau am Terminal, was er findet.";

            // -- The terminal ------------------------------------------------------------------------

            table["prompt.terminal.name"] = "Terminal";
            table["prompt.terminal.open"] = "Terminal öffnen";
            table["prompt.terminal.open_with_count"] = "Terminal öffnen ({count} offen)";
            table["prompt.terminal.file_blank"] = "Blindwert-Ausdruck einreichen ({machine})";
            table["prompt.terminal.file_results"] = "Ergebnisse einreichen — {tag}";
            table["prompt.terminal.rack_first"] =
                "Erst das Fläschchen in den Probenständer stellen";
            table["prompt.terminal.no_display"] = "Terminal — kein Display für dich";
            table["prompt.terminal.no_display_toast"] = "Dieses Terminal hat kein Display für dich.";
            table["prompt.terminal.slip_blank"] = "Dieser Ausdruck ist leer.";
            table["prompt.terminal.blank_filed"] = "Blindwert-Ausdruck von {machine} eingereicht.";
            table["prompt.terminal.results_filed"] = "Ergebnisse von {machine} eingereicht.";
            table["prompt.terminal.results_filed_tagged"] =
                "{tag}: Ergebnisse von {machine} eingereicht.";

            // -- The delivery bay --------------------------------------------------------------------

            table["prompt.bay.delivery_due"] = "Lieferung an der Rampe in etwa {seconds}s.";
            table["prompt.bay.arrived"] =
                "Lieferung an der Rampe — Karton {job}. Er muss hereingetragen werden.";
            table["prompt.bay.arrived_more"] =
                "Lieferung an der Rampe — Karton {job} und {count} weitere. Sie müssen " +
                "hereingetragen werden.";
            table["prompt.bay.full"] =
                "Rampe voll — noch {count} Karton(s) auf dem Lkw. Trag einen herein, dann kommt " +
                "der Rest herunter.";

            // -- Setting something down wherever you are -----------------------------------------------

            table["prompt.drop.no_player"] = "Hier ist niemand, der das abstellen könnte.";
            table["prompt.drop.no_room"] = "Dafür ist dort kein Platz.";
            table["prompt.drop.nowhere"] = "Hier gibt es keinen Platz zum Abstellen.";
            table["prompt.drop.nothing_underfoot"] =
                "Unter deinen Füßen ist nichts, worauf du das abstellen könntest.";
            table["prompt.drop.hands_empty"] = "Deine Hände sind leer.";

            // Door signs. "LABOR" rather than "LABORATORIUM": the plate is 26 cm wide and the German
            // word is set to fit it, so the short form is the readable one.
            table["prompt.sign.lab"] = "LABOR";
            table["prompt.sign.store"] = "LAGER";
            table["prompt.sign.office"] = "BÜRO";
        }
    }
}
