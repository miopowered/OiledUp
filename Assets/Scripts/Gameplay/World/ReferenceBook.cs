using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A physical manual. Pick it up, read it, put it back.
    /// <para>
    /// Books are objects rather than a terminal tab on purpose. Reading occupies your hands and
    /// costs shift time — the day clock does not stop while you look something up — which is the
    /// pressure §6.1 days 15-20 depends on, where a new reference is issued mid-contract and
    /// reading it is "optional and expensive in time".
    /// </para>
    /// Which manuals you keep near which bench becomes part of the §5.5 layout problem later.
    /// </summary>
    public sealed class ReferenceBook : Carryable
    {
        [SerializeField] private BookKind kind = BookKind.ElementIndex;

        [Tooltip("MachineDef id, for a per-instrument manual. Ignored by the other kinds.")]
        [SerializeField] private string machineId;

        [Tooltip("Only a fallback. The reading player's own view always wins.")]
        [SerializeField] private BookScreen screen;

        private MachineDef machine;

        public BookKind Kind => kind;

        public override string DisplayName => BookContent.TitleFor(kind, Machine);

        private MachineDef Machine
        {
            get
            {
                if (machine != null) return machine;
                if (string.IsNullOrEmpty(machineId)) return null;

                var catalog = LabRuntime.Instance?.Catalog;
                machine = catalog != null ? catalog.Machine(machineId) : null;
                return machine;
            }
        }

        public void Configure(BookKind bookKind, string targetMachineId, BookScreen reader)
        {
            kind = bookKind;
            machineId = targetMachineId;
            screen = reader;
            name = $"Book_{bookKind}{(string.IsNullOrEmpty(targetMachineId) ? "" : "_" + targetMachineId)}";
        }

        public override string UseHint => "read";

        /// <summary>
        /// Which reading view this book should open in.
        /// <para>
        /// A book is passed around, so the view cannot be a property of the book — two players
        /// carrying two volumes to two corners of the room each need their own pages. The serialized
        /// field survives only as a fallback for a scene that still keeps a single shared view at the
        /// root; the reader wins whenever they have one.
        /// </para>
        /// <para>
        /// Public because the precedence is otherwise only observable through a live
        /// <c>UIDocument</c>, which no edit-mode test has.
        /// </para>
        /// </summary>
        public BookScreen ReaderFor(PlayerInteractor player)
        {
            var mine = player != null ? player.Manual : null;
            return mine != null ? mine : screen;
        }

        public override void UseInHand(PlayerInteractor player)
        {
            var reader = ReaderFor(player);
            if (reader == null)
            {
                // §9: an object that refuses without saying why reads as broken. Rare enough to be a
                // build fault rather than a rule, but silence would send the player looking for one.
                if (player != null) player.Say("Nowhere to read that.");
                return;
            }

            var catalog = LabRuntime.Instance?.Catalog;
            reader.Open(DisplayName, BookContent.Build(kind, Machine, catalog));
        }
    }
}
