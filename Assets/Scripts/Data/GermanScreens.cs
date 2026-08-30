using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the screen lines. See <see cref="German"/> for the rules that apply to every
    /// entry here — duzen throughout, placeholders kept exactly as the English declares them, and
    /// nothing translated that is an id or content-table data.
    ///
    /// <para>
    /// <b>The <c>screen.</c> block has two budgets the rest of the table does not.</b> Those lines
    /// are rasterised by <c>PixelText</c> onto a 128px texture at scale 2 — fifteen columns, cut
    /// with no ellipsis — so the shorter correct German is the right German there. They are also
    /// drawn with <c>PixelFont</c>, whose 3x5 glyph set has no Ä, Ö, Ü or ß and silently paints an
    /// unknown character as a space: an umlaut on the glass is a hole in the middle of a word. So
    /// the words chosen for the instrument readouts are the umlaut-free ones ("BEREIT", "MESSUNG",
    /// "VERLAUF", "BLINDWERT"), which is a vocabulary choice and not a transliteration — <c>ae</c>
    /// and <c>ss</c> spellings appear nowhere. The terminal and the HUD are UI Toolkit and wrap, so
    /// they carry the ordinary spelling.
    /// </para>
    /// </summary>
    public static class GermanScreens
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            // -- Verdict words --------------------------------------------------------------------
            //
            // Half of §2.2's colourblind rule, so they have to be distinguishable from each other at
            // a glance and not merely correct: ACHTUNG and KRITISCH share no prefix, and BEOBACHTEN
            // is visibly a decision rather than a reading. Uppercase as the English is.

            table["screen.severity_normal"] = "NORMAL";
            table["screen.severity_caution"] = "ACHTUNG";
            table["screen.severity_critical"] = "KRITISCH";

            table["screen.verdict_normal"] = "NORMAL";
            table["screen.verdict_monitor"] = "BEOBACHTEN";
            table["screen.verdict_critical"] = "KRITISCH";

            table["screen.untested"] = "UNGEPRÜFT";
            table["screen.marked"] = "{glyph} {label}";

            // -- Terminal: chrome -----------------------------------------------------------------

            table["terminal.title"] = "PROBENTERMINAL";
            table["terminal.header"] = "PROBENTERMINAL — TAG {day}";
            table["terminal.balance"] = "£{money}    RUF {reputation}";
            table["terminal.close"] = "SCHLIESSEN  (Esc)";
            table["terminal.end_day"] = "TAG BEENDEN";

            table["terminal.waiting_for_session"] =
                "Warte auf das Labor. Wenn das nicht verschwindet, ist die Sitzung nie hochgekommen.";

            table["terminal.waiting_for_host"] =
                "Warte auf die erste Übertragung vom Host. Die Messgeräte im Raum sind schon " +
                "ablesbar; dieser Tisch füllt sich gleich.";

            // -- Terminal: the open queue ---------------------------------------------------------

            table["terminal.open_samples"] = "OFFENE PROBEN";
            table["terminal.nothing_open"] = "Nichts offen. Beende den Tag.";
            table["terminal.sample_meta"] = "{id} · {profile} · {volume} ml · {runs}";
            table["terminal.run_count_one"] = "1 Lauf";
            table["terminal.run_count_many"] = "{count} Läufe";
            table["terminal.unknown_fluid"] = "unbekanntes Fluid";

            // -- Terminal: instruments ------------------------------------------------------------

            table["terminal.instruments"] = "MESSGERÄTE";
            table["terminal.runs_since_flush"] = "{instrument} · {runs} Läufe seit der Spülung";
            table["terminal.blank_missing"] = "kein Blindwert — Rückstand unbekannt";
            table["terminal.blank_clean"] = "Blindwert Tag {day}: sauber";
            table["terminal.blank_residue"] = "Blindwert Tag {day}: {residue}";
            table["terminal.solvent_stock"] = "LÖSUNGSMITTEL  {units} Einheiten";
            table["terminal.order"] = "{count} BESTELLEN  (£{cost})";
            table["terminal.cannot_afford_restock"] = "Nachschub nicht bezahlbar";

            // -- Terminal: calibration (§5.3) -----------------------------------------------------

            table["terminal.calibration"] = "KALIBRIERUNG";
            table["terminal.standards_stock"] = "STANDARDS  {count} Ampullen";

            table["terminal.standard_certified"] =
                "{standard} — zertifiziert auf die gesunden Basiswerte aus dem Handbuch.";

            table["terminal.check_missing"] = "heute kein Standard gelaufen — Drift unbekannt";
            table["terminal.check_in_tolerance"] = "{standard} Tag {day}: zeigt {error}  in Toleranz";

            table["terminal.check_out_of_tolerance"] =
                "{standard} Tag {day}: zeigt {error}  AUSSER TOLERANZ";

            table["terminal.certified_cell"] = "zert {value}";
            table["terminal.measured_cell"] = "gem. {value}";

            table["terminal.calibrated"] =
                "kalibriert Tag {day}: {drift} korrigiert, {runs} Läufe jetzt fraglich in " +
                "{records} eingereichten Berichten";

            table["terminal.records_in_doubt"] = "FRAGLICHE BERICHTE";

            table["terminal.no_records_in_doubt"] =
                "Kein eingereichter Bericht stützt sich auf ein driftendes Messgerät.";

            table["terminal.in_doubt_title"] = "{mark} {tag} — {verdict} eingereicht, Tag {day}";

            table["terminal.retest_impossible"] =
                "{volume} ml übrig · kein Messgerät hier kann diese Tests wiederholen";

            table["terminal.retest_needs"] = "{volume} ml übrig · ein Nachtest braucht {needed} ml";
            table["terminal.reopen"] = "FÜR NACHTEST WIEDER ÖFFNEN";

            // -- Terminal: one sample -------------------------------------------------------------

            table["terminal.select_a_sample"] = "Wähle eine Probe.";

            table["terminal.sample_subtitle"] =
                "{profile} · {grade} · {hours} h Standzeit · {volume} ml übrig";

            table["terminal.redraw_of"] =
                "ZWEITE PROBE von {origin} — du hast für diese Anlage {verdict} eingereicht.";

            table["terminal.field_note"] = "Notiz vor Ort: \"{note}\"";

            // -- Terminal: reconciling an ambiguous vial (#32) ------------------------------------

            table["terminal.reconcile"] = "MIT DEM LIEFERSCHEIN ABGLEICHEN";

            table["terminal.reconcile_unreadable"] =
                "Die Tankkennung dieses Fläschchens ist nicht lesbar. Der Lieferschein aus seinem " +
                "Karton listet die Tanks, aus denen der Absender gezogen haben will — der übrige " +
                "ist dieser.";

            table["terminal.reconcile_duplicate"] =
                "Ein anderes Fläschchen in diesem Karton trägt dieselbe Kennung, und der " +
                "Lieferschein bucht diesen Tank doppelt. Sag, welche Ziehung das ist, oder dass " +
                "sie nicht auseinanderzuhalten sind.";

            table["terminal.no_note_for_job"] =
                "Kein Lieferschein zu {job} abgelegt. Ruf den Kunden an, oder lies das Papier aus " +
                "dem Karton.";

            table["terminal.no_note_for_vial"] =
                "Kein Lieferschein zu diesem Fläschchen abgelegt. Ruf den Kunden an, oder lies das " +
                "Papier aus dem Karton.";

            table["terminal.not_registered"] = "NICHT ERFASST — Schein {job}";
            table["terminal.registered_inseparable"] = "ALS NICHT TRENNBAR ERFASST — Schein {job}";
            table["terminal.registered_as"] = "ERFASST ALS {tag} — Schein {job}";
            table["terminal.note_line"] = "{number}. {tank}";
            table["terminal.cannot_tell"] = "NICHT UNTERSCHEIDBAR";
            table["terminal.ring_customer"] = "KUNDEN ANRUFEN  ({seconds} s)";

            // -- Terminal: the results table ------------------------------------------------------

            table["terminal.no_results"] =
                "Noch keine Ergebnisse. Lass diese Probe auf einem Messgerät laufen.";

            table["terminal.profile_missing"] =
                "Das Profil dieses Fluids fehlt im Inhaltskatalog, also kann hier nichts bewertet " +
                "werden. Definitionen neu bauen.";

            table["terminal.nothing_scored"] = "Noch nichts gemessen, was dieses Profil bewertet.";

            table["terminal.category_all_normal"] = "{mark} alles normal";

            table["terminal.category_flagged_one"] =
                "{mark} 1 außerhalb der Grenze · max. {verdict}";

            table["terminal.category_flagged_many"] =
                "{mark} {count} außerhalb der Grenzen · max. {verdict}";

            table["terminal.measured_value"] = "{value} {unit}";
            table["terminal.limit_upper"] = "normal ≤ {limit}";
            table["terminal.limit_lower"] = "normal ≥ {limit}";
            table["terminal.limit_band"] = "{baseline} ±{percent}%";

            table["terminal.column_element"] = "ELEMENT";
            table["terminal.column_measured"] = "MESSWERT";
            table["terminal.column_limit"] = "GRENZE";
            table["terminal.column_state"] = "STATUS";

            table["terminal.suspect"] = "FRAGLICH";
            table["terminal.runs"] = "LÄUFE";
            table["terminal.run_log_line"] = "Tag {day} · {machine} · {volume} ml · £{cost}{marks}";
            table["terminal.run_mark_blank"] = "BLINDWERT";

            // -- Terminal: filing a verdict -------------------------------------------------------

            table["terminal.root_cause"] = "Ursache";
            table["terminal.no_root_cause"] = "(keine Ursache)";
            table["terminal.file_normal"] = "NORMAL EINREICHEN";
            table["terminal.file_monitor"] = "BEOBACHTEN EINREICHEN";
            table["terminal.file_critical"] = "KRITISCH EINREICHEN — SPERREN";
            table["terminal.verdict_button"] = "{mark}  {action}";

            // -- Terminal: the end-of-day report (§4.3) -------------------------------------------

            table["terminal.end_of_day"] = "ENDE TAG {day}";
            table["terminal.closing_day"] = "Tag wird abgeschlossen…";
            table["terminal.nothing_due"] = "Heute ist nichts fällig geworden.";
            table["terminal.good_call"] = "{mark} RICHTIG ENTSCHIEDEN";
            table["terminal.bad_call"] = "{mark} FALSCH ENTSCHIEDEN";
            table["terminal.report_money"] = "{sign}£{amount}";
            table["terminal.report_money_with_bonus"] = "{sign}£{amount}   Bonus für die Ursache";
            table["terminal.report_net"] = "NETTO  {sign}£{net}    SALDO £{balance}";
            table["terminal.outpost_closed"] = "AUSSENSTELLE GESCHLOSSEN — das Konto ist überzogen.";

            table["terminal.contract_complete"] =
                "AUFTRAG ABGESCHLOSSEN — {contract}, {days} Tage.";

            table["terminal.run_summary"] =
                "Schlusssaldo £{closing} aus £{opening} · Ruf {reputation} · " +
                "£{earned} verdient, £{lost} verloren";

            table["terminal.start_next_day"] = "NÄCHSTEN TAG STARTEN";

            // -- HUD ------------------------------------------------------------------------------
            //
            // The bracketed keys are bindings, not words: they stay exactly as the English has them
            // and only the verbs around them move.

            table["hud.controls"] =
                "[WASD] bewegen    [E] benutzen    [1–3] wählen    [G] ablegen    " +
                "[Space] ansehen    [LMB drag] drehen    [Wheel] zoomen    [Tab] Daueraufträge";

            table["hud.hands"] = "in Händen: {item}    [G] ablegen    [Space] ansehen";

            table["hud.hands_with_use"] =
                "in Händen: {item}    [LMB] {use}    [G] ablegen    [Space] ansehen";

            table["hud.inspect_help"] =
                "LMB halten + Maus bewegen zum Drehen    Wheel zum Zoomen    " +
                "Space / Esc zum Schließen";

            table["hud.inspect_help_with_hint"] =
                "LMB halten + Maus bewegen zum Drehen    Wheel zum Zoomen    {hint}    " +
                "Space / Esc zum Schließen";

            table["hud.brief_closing"] = "{closing}\n[Tab] legt das weg — [Tab] holt es zurück.";
            table["hud.shift_over"] = "SCHICHTENDE — reiche deine Befunde ein";
            table["hud.time_left"] = "noch {time}";

            table["hud.status"] =
                "TAG {day}   {clock}\n£{money}   RUF {reputation}   FASS {solvent}   " +
                "STD {standards}\n{open}";

            table["hud.open_samples_one"] = "1 Probe offen";
            table["hud.open_samples_many"] = "{count} Proben offen";

            // -- In-world screens -----------------------------------------------------------------
            //
            // Fifteen columns, cut not wrapped, and no umlaut glyph in the font. Every line below is
            // shorter than or level with its English and spells its way around Ä, Ö, Ü and ß.

            table["screen.instrument_unknown"] = "INSTRUMENT";
            table["screen.instrument_fallback"] = "Messgerät";
            table["screen.ready"] = "BEREIT";
            table["screen.running"] = "MESSUNG {seconds}S";
            table["screen.no_reading"] = "KEIN MESSWERT";
            table["screen.value"] = "{value} {unit}";
            table["screen.history"] = "VERLAUF";
            table["screen.history_line"] = "T{day} {caption}";
            table["screen.caption_blank"] = "BLINDWERT";
            table["screen.caption_standard"] = "ZERT STANDARD";
        }
    }
}
