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

        public override void UseInHand(PlayerInteractor player)
        {
            if (screen == null) return;
            var catalog = LabRuntime.Instance?.Catalog;
            screen.Open(DisplayName, BookContent.Build(kind, Machine, catalog));
        }
    }
}
