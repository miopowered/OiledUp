using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the tutorial's objective card. See <see cref="German"/> for the rules that apply to
    /// every entry here — duzen throughout, placeholders kept exactly as the English declares them,
    /// and nothing translated that is an id or content-table data.
    /// <para>
    /// The rule in <see cref="TutorialStrings"/> applies to this file too and is not a property of the
    /// English: a translated line that named an element, a fault or a root cause would break hard
    /// rule 1 in German only, where nobody reviewing the English would ever see it. The bracketed keys
    /// — [E], [F1], [Tab] — are bindings and stay as they are.
    /// </para>
    /// </summary>
    public static class GermanTutorial
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            // -- The card ----------------------------------------------------------------------------

            table["tutorial.title"] = "EINARBEITUNG";

            table["tutorial.day_one"] = "TAG EINS — DER ABLAUF";

            table["tutorial.day_two"] = "TAG ZWEI — WAS EIN SAUBERES ERGEBNIS NICHT ABDECKT";

            table["tutorial.progress"] = "{done} von {total}";

            table["tutorial.closing"] =
                "Nichts davon musst du tun, und nichts im Labor wartet darauf. [F1] legt die Karte " +
                "weg und holt sie zurück; [Tab] sind die Dienstanweisungen, die sagen, warum sich " +
                "das alles lohnt.";

            // -- Day one: the loop -------------------------------------------------------------------

            table["tutorial.take_carton_line"] = "Hol einen Lieferkarton von der Rampe.";

            table["tutorial.take_carton_detail"] =
                "Der Lastwagen kommt nach einem Viertel der Schicht, und die Kartons bleiben liegen, " +
                "wo er sie abgestellt hat. Schau einen an und drück [E]. Deine Hände tragen immer " +
                "nur eine Sache, der Weg nach drinnen ist also ein Weg, den du sonst nichts tragend " +
                "gehst.";

            table["tutorial.open_carton_line"] = "Schneide den Karton auf.";

            table["tutorial.open_carton_detail"] =
                "Stell die Kiste erst ab, dann halte [E] gedrückt. Der Lieferschein des Absenders " +
                "liegt oben auf den Fläschchen und ist das einzige Papier, das sagt, was drin sein " +
                "sollte.";

            table["tutorial.take_vial_line"] = "Nimm ein Fläschchen aus der Kiste.";

            table["tutorial.take_vial_detail"] =
                "Schau in einen geöffneten Karton und drück [E]. Jede Flasche trägt das Schild ihres " +
                "Tanks, und genau darunter legt das Labor sie ab — es gibt nirgends etwas " +
                "einzutippen.";

            table["tutorial.load_instrument_line"] = "Setz ein Fläschchen in ein Gerät ein.";

            table["tutorial.load_instrument_detail"] =
                "Halte [E] am Gerät gedrückt. Dieses Halten ist das Aufschütteln, es kostet also " +
                "Sekunden, die du nicht zurückbekommst. Jedes Gerät braucht eine andere Menge Öl, " +
                "und in einem Fläschchen ist nicht genug für alle — worauf du es verteilst, ist die " +
                "Entscheidung.";

            table["tutorial.start_run_line"] = "Starte den Lauf.";

            table["tutorial.start_run_detail"] =
                "Der START-Knopf sitzt an der Blende des Geräts. Sein Bedienhandbuch liegt daneben " +
                "auf der Bank und sagt, wie lange ein Lauf dauert und was das Gerät meldet — und was " +
                "nicht.";

            table["tutorial.run_finished_line"] = "Lass einen Lauf zu Ende laufen.";

            table["tutorial.run_finished_detail"] =
                "Er läuft von allein und belegt das Gerät die ganze Zeit. Danebenstehen und zusehen " +
                "ist das Einzige, was nie hilft: fang solange etwas anderes an.";

            table["tutorial.file_slip_line"] =
                "Bring den gedruckten Beleg zum Terminal und trag ihn ein.";

            table["tutorial.file_slip_detail"] =
                "Ein fertiger Lauf druckt in die Ablage am Gerät. Der Messwert kommt erst in die " +
                "Akte, wenn jemand diesen Beleg zum Schreibtisch trägt — ein Beleg, der auf der Bank " +
                "liegen bleibt, ist ein bezahlter Test, den du nicht verwenden kannst.";

            table["tutorial.file_verdict_line"] = "Fälle ein Urteil zu einer Probe.";

            table["tutorial.file_verdict_detail"] =
                "Am Terminal, sobald du etwas zum Lesen eingetragen hast. Nichts auf dieser Karte " +
                "wird dir je sagen, welches Urteil du fällen sollst oder zu welcher Probe — das ist " +
                "die Arbeit, und zwar die ganze.";

            table["tutorial.end_day_line"] = "Beende den Tag am Terminal.";

            table["tutorial.end_day_detail"] =
                "Die Schicht endet, wenn du es sagst. Urteile werden nicht heute abgerechnet: sie " +
                "kommen Tage später zurück, und beide Richtungen kosten Geld, wenn du danebenliegst.";

            // -- Day two: the two tells --------------------------------------------------------------

            table["tutorial.run_blank_line"] = "Schick eine Lösemittel-Blindprobe durch ein Gerät.";

            table["tutorial.run_blank_detail"] =
                "Nimm vorher das Fläschchen heraus. Eine Blindprobe liest zurück, was die letzte " +
                "Probe darin hinterlassen hat und sonst still zu allem Nächsten dazukäme. Das ist " +
                "der einzige Weg, es zu sehen, und das Terminal markiert jedes Gerät, das heute " +
                "keine hatte.";

            table["tutorial.fill_bottle_line"] = "Füll eine Lösemittelflasche an der Waschstation.";

            table["tutorial.fill_bottle_detail"] =
                "Halte [E] am Fass gedrückt, mit einer Flasche in den Händen. Das Fass ist Bestand, " +
                "den du bezahlt hast, und der Rückweg ist ein Weg, auf dem du sonst nichts trägst.";

            table["tutorial.flush_line"] = "Spül ein Gerät.";

            table["tutorial.flush_detail"] =
                "Halte am Gerät FLUSH gedrückt. Eine Ladung aus der Flasche pro Gerät, und es ist " +
                "die Behebung, nicht der Hinweis — ob sie nötig war, sagt dir die Blindprobe.";

            table["tutorial.run_standard_line"] = "Lass einen zertifizierten Referenzstandard laufen.";

            table["tutorial.run_standard_detail"] =
                "Jede Zahl auf dem Zertifikat dieser Ampulle steht in den Handbüchern, was " +
                "zurückkommt, sagt dir also, wie weit das Gerät seit der letzten Justage abgewandert " +
                "ist. Die Kalibrierung verschiebt sich mit jedem Lauf ein wenig, in einer Richtung, " +
                "die täglich neu ausgewürfelt wird, und sie skaliert alles, was das Gerät dir sagt.";

            table["tutorial.recalibrate_line"] = "Justiere gegen den heutigen Standard nach.";

            table["tutorial.recalibrate_detail"] =
                "Das kostet Geld und belegt das Gerät, solange es läuft. Es listet dir außerdem jede " +
                "Akte auf, die du eingereicht hast, während das Gerät danebenlag — die mit Restöl " +
                "kannst du wieder öffnen.";
        }
    }
}
