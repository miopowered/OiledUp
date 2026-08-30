using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the consequence lines. See <see cref="German"/> for the rules that apply to every
    /// entry here — duzen throughout, placeholders kept exactly as the English declares them, and
    /// nothing translated that is an id or content-table data.
    ///
    /// <para>
    /// <b><c>{fault}</c>, <c>{cause}</c> and <c>{consequence}</c> arrive in English.</b> They are
    /// content-table data, and hard rule 1 keeps them out of this file — a fault that read
    /// differently in two languages would be the chemistry lying in one of them. Each German
    /// sentence is therefore built so the English noun phrase lands in a slot that takes a name:
    /// after a dash, after a colon, or as the complement of "bestätigt als". None of them is bent
    /// into a German case or given a German article.
    /// </para>
    ///
    /// <para>
    /// <b>The report keeps its two sentences.</b> <c>consequence.headline_with_note</c> stays
    /// diagnosis-then-note joined by a space: German reads the verdict first here for the same
    /// reason English does — the paperwork note only makes sense once you know what the call was.
    /// </para>
    /// </summary>
    public static class GermanConsequences
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            table["consequence.headline_with_note"] = "{diagnosis} {note}";

            // -- The diagnosis ------------------------------------------------------------------

            table["consequence.correct_critical_cause_confirmed"] =
                "{tag}: rechtzeitig außer Betrieb genommen. Ursache bestätigt als {cause}. Volle " +
                "Vergütung plus Diagnosebonus.";
            table["consequence.correct_critical_wrong_cause"] =
                "{tag}: rechtzeitig außer Betrieb genommen — {fault}. Die eingereichte Ursache war " +
                "falsch; es war {cause}.";
            table["consequence.correct_critical_no_cause"] =
                "{tag}: rechtzeitig außer Betrieb genommen — {fault}. Keine Ursache eingereicht, " +
                "also kein Diagnosebonus.";
            table["consequence.false_positive"] =
                "{tag}: Tank auf deinen Befund hin abgelassen und neu befüllt. Das Öl war " +
                "brauchbar. Der Anlagenstillstand und die Neufüllung gehen auf uns.";
            table["consequence.monitor_on_imminent"] =
                "{tag}: auf deinen Rat hin weiter abgeschreckt, und das hätte nicht sein dürfen. " +
                "{fault}. {consequence}";
            table["consequence.monitor_developing"] =
                "{tag}: im Betrieb belassen und für eine weitere Entnahme vorgemerkt. Die Werte " +
                "sind diesen Zyklus schlechter.";
            table["consequence.monitor_unnecessary"] =
                "{tag}: auf deine Anforderung hin neu entnommen, weiterhin in der Spezifikation.";
            table["consequence.missed_fault"] =
                "{tag}: ALS ABSCHRECKTAUGLICH FREIGEGEBEN. {fault}. {consequence} Namentlich in " +
                "der Vorfallakte vermerkt.";
            table["consequence.correct_normal"] =
                "{tag}: als einsatztauglich freigegeben. Routinevergütung.";

            // -- The paperwork note (#32) ---------------------------------------------------------

            table["consequence.registration_unregistered"] =
                "Das Fläschchen, aus dem sie stammt, wurde nie identifiziert; der Bericht ging " +
                "also auf gar keinen Tank hinaus. Nicht abrechenbar.";
            table["consequence.registration_wrong_tank"] =
                "Eingereicht auf {filed}. Das Öl stammt aus {actual}. Sie haben am falschen Bad " +
                "gehandelt.";
            table["consequence.registration_missed_split_draw"] =
                "Beide Fläschchen wurden aus einem Fass abgefüllt, und du hast sie als zwei " +
                "Entnahmen bescheinigt. Sie haben für eine Gegenprobe bezahlt und eine doppelt " +
                "gezählte Probe bekommen.";
            table["consequence.registration_imagined_split_draw"] =
                "Du wolltest die beiden Entnahmen nicht trennen. Ihr Versandprotokoll sagt, der " +
                "Tank wurde zweimal entnommen, und die Werte geben ihnen recht.";
            table["consequence.registration_same_drum_caught"] =
                "Beide Fläschchen lasen sich gleich, weil es dasselbe Öl war — ein Fass, gebucht " +
                "als zwei Entnahmen. Erkannt und in Rechnung gestellt.";
        }
    }
}
