using System.Collections.Generic;
using System.Text;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The delivery note that came in a carton, as a thing you can pick up (#31).
    /// <para>
    /// <b>It is an object rather than a terminal tab</b> for the reason <see cref="ReferenceBook"/>
    /// is: reading occupies your hands and costs shift time, and #32's reconciliation is only a
    /// decision if fetching the paper is one. It competes for the same three slots as a vial and a
    /// slip, so carrying the note to the bench means not carrying something else.
    /// </para>
    /// <para>
    /// The words are baked in at <see cref="Bind"/> rather than read live off
    /// <c>DeliveryNote</c>. The note is a claim about what was sent (see that type), and a piece of
    /// paper that quietly re-typeset itself as the box emptied would answer #32's question for the
    /// player. What is printed on it is what the customer wrote, once.
    /// </para>
    /// <para>
    /// Since #82 the same text is also baked onto the note's physical "Sheet" child, through
    /// <see cref="PrintedSheetSurface"/> — see that type for why it is a new overlay rather than a
    /// material on the sheet the scene builder already made. This prop has no serialized reference to
    /// that child the way <see cref="PrintoutProp"/> has for its own sheet, because
    /// <c>LabSceneBuilder</c> never wires one for a note; it is found by name instead, the same way
    /// <c>CartonProp</c> addresses its lid.
    /// </para>
    /// </summary>
    public sealed class DeliveryNoteProp : Carryable
    {
        /// <summary>Which box this came out of. The host names the paper by its carton.</summary>
        public string CartonId { get; private set; }

        public string JobNumber { get; private set; } = "—";

        private string sender = "an unnamed sender";
        private string body = string.Empty;

        private MeshRenderer sheet;
        private PrintedSheetSurface surface;

        public override string DisplayName => $"Delivery note {JobNumber}";

        public override string InspectionText => body;

        public override Quaternion InspectionRotation => Quaternion.Euler(-90f, 0f, 0f);

        public override Quaternion InventoryIconRotation => InspectionRotation;

        /// <summary>
        /// Whether anything has actually been typed on this page yet.
        /// <para>
        /// It exists for the one case a host never sees: on a joined client the paper is built from a
        /// replicated snapshot, and the box's row and its note's lines can land a frame apart. A prop
        /// built from the half that arrived first would be blank for ever — and the note is the whole
        /// of #32's evidence, so <see cref="CartonReconciler"/> uses this to print it late rather than
        /// leaving the player holding an empty sheet. It is deliberately not a licence to re-type: the
        /// page is written once (see the type doc) and this is false exactly once.
        /// </para>
        /// </summary>
        public bool IsPrinted => !string.IsNullOrEmpty(body);

        public void Bind(string cartonId, string jobNumber, string senderName, string printed)
        {
            CartonId = cartonId;
            JobNumber = string.IsNullOrEmpty(jobNumber) ? "—" : jobNumber;
            sender = string.IsNullOrEmpty(senderName) ? "an unnamed sender" : senderName;
            body = printed ?? string.Empty;
            name = $"Note_{JobNumber}";
            RenderOntoPaper();
        }

        /// <summary>
        /// Put <see cref="body"/> onto the physical page. One source of truth for both readings: the
        /// HUD overlay is <see cref="InspectionText"/>, this is <see cref="PrintedSheetSurface.Draw"/>,
        /// and both are handed the same <see cref="body"/> — so the note in your hand and the words on
        /// its own paper cannot disagree with each other.
        /// <para>
        /// Silently does nothing without a "Sheet" child. Several EditMode fixtures build a bare
        /// <see cref="DeliveryNoteProp"/> with no geometry under it at all, and that is a legitimate
        /// shape for this type to be in — a prop that has not been through the scene builder simply
        /// has no paper to write on yet.
        /// </para>
        /// </summary>
        private void RenderOntoPaper()
        {
            if (surface == null)
            {
                if (sheet == null) sheet = transform.Find("Sheet")?.GetComponent<MeshRenderer>();
                if (sheet == null) return;
                surface = new PrintedSheetSurface(sheet, name);
            }

            var lines = new List<string>();
            PixelText.Wrap(body, surface.Columns, lines);
            surface.Draw(lines);
        }

        private void OnDestroy() => surface?.Dispose();

        public override string Prompt(PlayerInteractor player) =>
            player.InventoryHasSpace
                ? $"Take delivery note {JobNumber} ({sender})"
                : "Inventory full";

        /// <summary>
        /// What the customer typed, as it appears on the paper.
        /// <para>
        /// Every line is the tag the sender <i>says</i> they drew, and nothing here compares it with
        /// what is in the box — that comparison is #32 and it belongs to the player. Deliberately no
        /// tick marks, no counts against actual, no "1 missing": a note that reconciled itself would
        /// leave nothing to find (see <see cref="DeliveryNote"/>).
        /// </para>
        /// </summary>
        public static string Printed(DeliveryNote note)
        {
            if (note == null) return string.Empty;

            var text = new StringBuilder();
            text.Append("DELIVERY NOTE ").Append(note.JobNumber).Append('\n');
            text.Append(note.Customer != null ? note.Customer.DisplayName : "Sender not stated")
                .Append("\nBooked out day ").Append(note.Day).Append("\n\n");

            for (int i = 0; i < note.Lines.Count; i++)
            {
                var line = note.Lines[i];
                text.Append(i + 1).Append(". ").Append(line.TankTag ?? "—");

                if (line.Profile != null) text.Append("  [").Append(line.Profile.DisplayName).Append(']');
                text.Append('\n');
            }

            text.Append('\n').Append(note.Count).Append(" vial(s) declared.");
            return text.ToString();
        }
    }
}
