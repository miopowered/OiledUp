using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// German for the menu lines. See <see cref="German"/> for the rules that apply to every
    /// entry here — duzen throughout, placeholders kept exactly as the English declares them, and
    /// nothing translated that is an id or content-table data.
    /// </summary>
    public static class GermanMenu
    {
        public static void AddTo(Dictionary<string, string> table)
        {
            // -- The front door --------------------------------------------------------------------

            // The wordmark is the product's name, not a sentence. Left as it is printed on the box.
            table["menu.wordmark"] = "OILED UP";

            table["menu.tagline"] =
                "Analyse von Wärmebehandlungsölen. Bis zu viert im Labor.";

            table["menu.continue"] = "FORTSETZEN";
            table["menu.single_player"] = "EINZELSPIELER";
            table["menu.co_op"] = "KOOP";
            table["menu.settings"] = "EINSTELLUNGEN";
            table["menu.credits"] = "MITWIRKENDE";
            table["menu.quit"] = "BEENDEN";
            table["menu.back"] = "ZURÜCK";

            table["menu.offline_note"] =
                "Einzelspieler braucht keine Anmeldung, keine Lobby und keine Verbindung. Es " +
                "funktioniert offline.";

            table["menu.no_connection"] =
                "Keine LabConnection an diesem Objekt, hier kann also nichts ein Spiel starten.  " +
                "Build {build}";

            table["menu.identity"] = "du bist {name} · {id}    Build {build}";

            table["menu.build"] = "Build {build}";

            table["menu.continue_saved"] = "{run}  ·  £{money}  ·  gespeichert {when}";

            table["menu.continue_unreadable"] =
                "{run} wurde von einer anderen Version des Spiels gespeichert und lässt sich nicht " +
                "fortsetzen. Die Datei bleibt liegen, wo sie ist.";

            // -- Co-op -----------------------------------------------------------------------------

            table["menu.coop.heading"] = "KOOP";
            table["menu.coop.host"] = "SCHICHT HOSTEN";
            table["menu.coop.code_field"] = "Beitrittscode";
            table["menu.coop.join"] = "BEITRETEN";
            table["menu.coop.try_again"] = "ERNEUT VERSUCHEN";

            table["menu.coop.code_hint"] =
                "Sechs Buchstaben und Ziffern, vorgelesen von der Person, die hostet.";

            table["menu.coop.no_connection"] =
                "Keine LabConnection an diesem Objekt, Koop kann also nicht starten.";

            // -- Credits ---------------------------------------------------------------------------
            //
            // Headings and notes only. The licence bodies in CreditsContent are reproduced verbatim
            // and never acquire a translated variant.

            table["menu.credits.heading"] = "MITWIRKENDE";

            table["menu.credits.made_by"] =
                "OILED UP stammt von Emmanuel Lampe und Kevin-Timo Salmen.";

            table["menu.credits.art"] = "Grafik von Dritten";

            table["menu.credits.art_note"] =
                "Festgehalten in Assets/Art/Imported/CREDITS.md und hier wortgleich wiedergegeben, " +
                "weil manche dieser Lizenzen eine Nennung im Spiel selbst verlangen und nicht nur " +
                "im Repository.";

            table["menu.credits.software"] = "Software von Dritten";

            table["menu.credits.software_note"] =
                "Lizenzhinweise aus den Unity-Paketen, aus denen dieser Build gebaut ist.";

            // -- Pause -----------------------------------------------------------------------------

            table["menu.pause.heading"] = "PAUSIERT";
            table["menu.pause.resume"] = "WEITER";
            table["menu.pause.leave"] = "SCHICHT VERLASSEN";

            table["menu.pause.clock_stopped"] =
                "Das Labor steht still, solange das hier offen ist. Nichts bewegt sich, bis du " +
                "weitermachst.";

            table["menu.pause.clock_running"] =
                "Die Schichtuhr läuft weiter. Das ist eine Koop-Sitzung, Pausieren stoppt also nur " +
                "deine eigenen Hände — der Tag läuft für alle anderen im Labor weiter.";

            table["menu.pause.leave_note"] =
                "Verlassen schließt deine Sitzung und setzt dich zurück ins Menü. In Koop beendet " +
                "es die Schicht für niemanden sonst.";

            // -- Lobby -----------------------------------------------------------------------------

            table["lobby.heading"] = "LOBBY";
            table["lobby.copy"] = "KOPIEREN";
            table["lobby.copied"] = "KOPIERT";
            table["lobby.ready_up"] = "BEREIT";
            table["lobby.cancel_ready"] = "NICHT BEREIT";
            table["lobby.start"] = "SCHICHT STARTEN";
            table["lobby.cancel_countdown"] = "ABBRECHEN";
            table["lobby.leave"] = "VERLASSEN";

            table["lobby.code_hint"] = "Schick den an alle, die dir beitreten.";

            table["lobby.code_copied"] =
                "{code} liegt in deiner Zwischenablage. Füg ihn für alle ein, die dir beitreten.";

            table["lobby.full"] = "Das Labor ist voll.";

            table["lobby.room_left"] =
                "{here} von {capacity} da. Platz für {free} weitere.";

            table["lobby.countdown"] = "START IN {seconds}";

            table["lobby.start_ready"] = "SCHICHT STARTEN ({ready}/{seated} BEREIT)";

            table["lobby.seat_host"] = "{name}  (Host)";
            table["lobby.seat_ready"] = "BEREIT";
            table["lobby.seat_deciding"] = "überlegt noch";
            table["lobby.seat_empty"] = "freier Platz";

            // -- The corner card -------------------------------------------------------------------

            table["session.card_join_code"] = "BEITRITTSCODE  {code}";

            table["session.card_connected"] = "VERBUNDEN";

            table["session.card_voice_keys"] = "[M] MIKRO {mic}   [N] TON {sound}";

            table["session.on"] = "AN";
            table["session.off"] = "AUS";

            table["session.card_voice_connecting"] = "SPRACHE VERBINDET…";

            table["session.card_volume"] = "[-/+] LAUT {percent}%  {pointer}";

            table["session.card_volume_close"] = "[V/ESC] SCHLIESSEN";

            table["session.card_volume_mouse"] = "[V] MAUS";

            // -- The disconnect notice -------------------------------------------------------------

            table["session.reconnect"] = "NEU VERBINDEN";

            table["session.back_to_menu"] = "ZURÜCK ZUM MENÜ";

            table["session.rejoin_hint"] =
                "Neu verbinden nutzt denselben Beitrittscode und dieselbe Identität, der Host setzt " +
                "dich also zurück auf deinen eigenen Platz, statt dich neu zu setzen.";

            table["session.no_rejoin_host_closed"] =
                "Dafür gibt es kein Neuverbinden. Die Sitzung ist mit dem Host weg; jemand muss eine " +
                "neue hosten.";

            table["session.no_rejoin_kicked"] =
                "Dafür gibt es kein Neuverbinden. Der Host hat entschieden, und derselbe Code würde " +
                "wieder abgelehnt.";

            table["session.no_rejoin_refused"] =
                "Dafür gibt es kein Neuverbinden. Es wurde nie etwas gestartet, unter Koop ist also " +
                "die Stelle für einen neuen Versuch — mit geprüftem Code.";

            // -- How a session ended ---------------------------------------------------------------

            table["session.end.host_closed_headline"] = "DER HOST HAT DAS LABOR GESCHLOSSEN";

            table["session.end.host_closed_detail"] =
                "Die Schicht endete, als dein Host gegangen ist. Seine Lobby ist gelöscht und ihr " +
                "Beitrittscode antwortet nicht mehr, es ist also nichts mehr da, dem du wieder " +
                "beitreten könntest.";

            table["session.end.refused_headline"] = "DAS LABOR HAT DICH ABGEWIESEN";

            table["session.end.refused_detail_said"] =
                "{reason} Es wurde nichts gestartet, also ist nichts verloren.";

            table["session.end.refused_detail"] =
                "Der Host hat die Verbindung abgelehnt, ohne einen Grund zu nennen. Es wurde nichts " +
                "gestartet.";

            table["session.end.kicked_headline"] = "DER HOST HAT DICH GETRENNT";

            table["session.end.kicked_detail"] =
                "{reason} Ein erneuter Beitritt würde nur wieder abgelehnt; frag deinen Host nach " +
                "einem frischen Code.";

            table["session.end.dropped_headline"] = "DIE VERBINDUNG IST ABGERISSEN";

            table["session.end.dropped_detail"] =
                "Vom Host kam nichts mehr zurück. Dein Platz wird für dich frei gehalten, ein " +
                "erneuter Beitritt stellt dich also dorthin zurück, wo du gestanden hast, mit dem, " +
                "was du in der Hand hattest.";

            // -- What the loading screen is waiting on ----------------------------------------------

            table["session.step.loading"] = "Lädt…";

            table["session.step.waiting_for_host"] =
                "Warte darauf, dass der Host die Schicht startet…";

            table["session.step.waiting_for_lab"] =
                "Warte darauf, dass der Host das Labor schickt…";

            table["session.step.opening_lab"] = "Öffne das Labor…";

            table["session.step.loading_lab"] = "Lade das Labor…";

            table["session.step.returning"] = "Zurück zum Menü…";

            table["session.patience.host"] =
                "Immer noch verbunden. Der Host hat die Schicht noch nicht gestartet — du musst " +
                "nicht neu beitreten.";

            table["session.patience.lab"] =
                "Das Labor kommt noch vom Host. Jetzt zu gehen würde dich wieder hinter ihm in die " +
                "Warteschlange stellen.";

            table["session.patience.generic"] =
                "Läuft noch. Bei einer langsamen Verbindung kann das einen Moment dauern.";

            // -- Connect progress ------------------------------------------------------------------

            table["session.status.reserving_relay"] = "Reserviere ein Relay…";

            table["session.status.opening_lobby"] = "Öffne die Lobby…";

            table["session.status.starting_host"] = "Starte den Host…";

            table["session.status.hosting"] = "Hostet — Beitrittscode {code}";

            table["session.status.starting_shift"] = "Starte die Schicht…";

            table["session.status.starting_shift_hosting"] =
                "Starte die Schicht — Beitrittscode {code}";

            table["session.status.looking_up_code"] = "Suche diesen Beitrittscode…";

            table["session.status.joining_relay"] = "Trete dem Relay bei…";

            table["session.status.connecting"] = "Verbinde…";

            // -- Connect failures ------------------------------------------------------------------

            table["session.error.relay_failed"] =
                "Konnte kein Relay reservieren. Prüf deine Verbindung und versuch es erneut.";

            table["session.error.lobby_failed"] =
                "Relay reserviert, aber keine Lobby bekommen. Es wurde nichts gestartet; versuch es " +
                "erneut.";

            table["session.error.host_refused"] =
                "Netcode hat den Start des Hosts abgelehnt. Der Transportfehler steht in der Konsole.";

            table["session.error.client_refused"] =
                "Netcode hat den Start des Clients abgelehnt. Der Transportfehler steht in der " +
                "Konsole.";

            table["session.error.no_code"] =
                "Tipp den Beitrittscode ein, den dein Host vorgelesen hat.";

            table["session.error.code_malformed"] =
                "„{code}“ ist kein Beitrittscode — die haben {length} Buchstaben und Ziffern.";

            table["session.error.lobby_service"] =
                "Konnte den Lobby-Dienst nicht erreichen. Prüf deine Verbindung und versuch es " +
                "erneut.";

            table["session.error.lobby_not_playing"] =
                "In dieser Lobby läuft kein Spiel. Frag deinen Host nach einem frischen Code.";

            table["session.error.relay_gone"] =
                "Das Relay dieses Spiels ist weg. Der Host hat es wahrscheinlich geschlossen.";

            table["session.error.no_manager"] =
                "Kein NetworkManager in der Szene. Koop kann nicht starten; Einzelspieler schon.";

            table["session.error.no_transport"] =
                "Der NetworkManager hat kein UnityTransport. Koop kann nicht starten.";

            table["session.error.transport_missing"] =
                "Der NetworkManager hat kein UnityTransport.";

            table["session.error.offline"] = "{detail} Einzelspieler geht weiterhin.";

            table["session.error.no_identity"] =
                "Konnte keine Spieleridentität herstellen. Einzelspieler geht weiterhin.";

            table["session.error.no_endpoint"] =
                "Das Relay hat keinen Endpunkt angeboten, den dieser Build nutzen kann.";

            table["session.error.code_not_found"] =
                "Kein Spiel benutzt den Code {code}. Prüf ihn und versuch es erneut.";

            table["session.error.code_invalid"] = "{code} ist kein gültiger Beitrittscode.";

            table["session.error.lobby_full"] =
                "Dieses Spiel ist voll — {capacity} Spieler sind die Grenze.";

            table["session.error.join_failed"] = "Beitritt nicht möglich: {reason}";

            table["session.error.scene_missing"] =
                "Konnte '{scene}' nicht laden. Steht die Szene in den Build Settings?";

            // -- Settings: the shell ----------------------------------------------------------------

            table["settings.heading"] = "EINSTELLUNGEN";
            table["settings.tab_display"] = "ANZEIGE";
            table["settings.tab_audio"] = "AUDIO";
            table["settings.tab_controls"] = "STEUERUNG";

            // -- Settings: display ------------------------------------------------------------------

            table["settings.resolution"] = "Auflösung";
            table["settings.window_mode"] = "Fenstermodus";
            table["settings.vertical_sync"] = "Vertikale Synchronisation";
            table["settings.detail"] = "Detailgrad";
            table["settings.field_of_view"] = "Sichtfeld";

            table["settings.display_note"] =
                "Eine neue Auflösung oder ein neuer Fenstermodus wird sofort übernommen und dann zur " +
                "Bestätigung nachgefragt, ein Modus, den dein Monitor nicht zeigen kann, setzt sich " +
                "also selbst zurück.";

            table["settings.window_exclusive"] = "Exklusives Vollbild";

            table["settings.window_borderless"] = "Randloses Vollbild";

            table["settings.window_maximised"] = "Maximiertes Fenster";

            table["settings.window_windowed"] = "Fenster";

            table["settings.keep_mode"] = "MODUS BEHALTEN";
            table["settings.put_it_back"] = "ZURÜCKSTELLEN";

            table["settings.confirm_display"] =
                "Kannst du das lesen? {mode}, {window} behalten. In {seconds} s geht es zurück auf " +
                "{previous}, {previousWindow}.";

            // -- Settings: audio --------------------------------------------------------------------

            table["settings.volume_master"] = "Gesamt";

            table["settings.volume_effects"] = "Maschinen und Werkzeuge";

            table["settings.volume_ambience"] = "Raumatmosphäre";

            table["settings.volume_voice"] = "Sprachchat";

            table["settings.audio_note"] =
                "Raumatmosphäre ist die Lüftung, das Summen der Beleuchtung und das gelegentliche " +
                "Relais. Maschinen und Werkzeuge ist alles, was du oder ein Gerät auslöst.";

            // -- Settings: controls -----------------------------------------------------------------

            table["settings.look_sensitivity"] = "Blickempfindlichkeit";

            table["settings.invert_look"] = "Hochschauen umkehren";
            table["settings.head_bob"] = "Kopfbewegung";
            table["settings.camera_shake"] = "Kamerawackeln";

            table["settings.comfort_note"] =
                "Dreh die beiden herunter, wenn dir vom Laufen zwischen den Bänken flau wird. " +
                "Kopfbewegung ist das Schwanken deiner eigenen Schritte; Kamerawackeln ist das " +
                "Absacken beim Landen und der Ruck beim Sprinten. Null heißt aus, nicht reduziert. " +
                "Sonst ändert sich nichts: Du liest weiterhin dieselben Zahlen an denselben " +
                "Maschinen ab.";

            table["settings.no_bindings_here"] = "Von hier aus lassen sich keine Tasten ändern.";

            table["settings.no_bindings_here_note"] =
                "Dieser Bildschirm wurde ohne die Input Actions geöffnet, in denen deine " +
                "Tastenbelegung steht. Öffne die Einstellungen aus dem Hauptmenü oder aus dem " +
                "Pausenmenü, dann steht die vollständige Tastenliste hier.";

            table["settings.nothing_to_rebind"] =
                "Es gibt keine Tastatur- oder Maussteuerung zum Ändern.";

            table["settings.nothing_to_rebind_note"] =
                "Auf der Player-Action-Map liegt nichts auf einer Taste oder einer Maustaste, hier " +
                "gibt es also nichts neu zu belegen.";

            table["settings.keyboard_and_mouse"] = "Tastatur und Maus";

            table["settings.reset_every_key"] = "ALLE TASTEN ZURÜCKSETZEN";

            table["settings.rebind_note"] =
                "Drück NEU BELEGEN, dann die Taste, die du willst. Escape behält die Taste, die du " +
                "schon hattest, Abwarten ebenso.";

            table["settings.hold_note"] =
                "Eine Zeile mit (halten) ist eine Taste, die du gedrückt hältst. Neu belegen " +
                "verschiebt die Taste und nie die Zeit, die der Vorgang dauert.";

            table["settings.rebind"] = "NEU BELEGEN";
            table["settings.rebind_default"] = "STANDARD";
            table["settings.press_a_key"] = "drück eine Taste…";

            table["settings.binding_held"] = "{action} (halten)";

            table["settings.reset_every_key_done"] =
                "Jede Taste ist wieder so, wie sie ausgeliefert wurde.";

            table["settings.reset_key_done"] = "{action} ist wieder {key}.";

            table["settings.press_key_for"] = "Drück die Taste, die du für {action} willst.";

            table["settings.rebind_unchanged"] = "{action} ist weiterhin {key}.";

            table["settings.rebind_conflict"] =
                "{key} ist schon {heldBy}. {action} bleibt {current} — ändere erst {heldBy}, wenn du " +
                "diese Taste hier haben willst.";

            table["settings.rebind_done"] = "{action} ist jetzt {key}.";

            table["settings.that_key"] = "Diese Taste";

            // -- Connection state, the default line under the buttons -------------------------------

            table["session.connect_idle"] = "Nicht verbunden.";

            table["session.connect_preparing"] = "Melde an…";

            table["session.connect_allocating"] = "Öffne eine Sitzung…";

            table["session.connect_resolving"] = "Suche diesen Beitrittscode…";

            table["session.connect_connecting"] = "Verbinde…";

            table["session.connect_hosting"] = "Hostet.";

            table["session.connect_joined"] = "Verbunden.";

            table["session.connect_single_player"] = "Einzelspieler.";

            // -- Language ---------------------------------------------------------------------------

            table["settings.language"] = "Sprache";

            table["settings.language_note"] =
                "Menüs, die schon offen sind, behalten ihren alten Wortlaut, bis sie das nächste Mal " +
                "geöffnet werden. Alles im Labor wechselt sofort.";
        }
    }
}
