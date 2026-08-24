using Residue.Data;
using UnityEngine;
using UnityEngine.InputSystem;

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

        private MachineDef machine;
        private InspectableBookSurface pageSurface;

        public BookKind Kind => kind;
        public string InventoryId => $"{kind}:{machineId ?? string.Empty}";

        public override string DisplayName => BookContent.TitleFor(kind, Machine);

        // The words live on the generated 3D paper surface, never in the HUD reading overlay.
        public override string InspectionText => null;
        public override string InspectionHelp => "Arrow keys / wheel to turn pages";
        public override Quaternion InspectionRotation => Quaternion.Euler(-90f, 0f, 0f);

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

        public void Configure(BookKind bookKind, string targetMachineId)
        {
            kind = bookKind;
            machineId = targetMachineId;
            name = $"Book_{bookKind}{(string.IsNullOrEmpty(targetMachineId) ? "" : "_" + targetMachineId)}";
        }

        public override void BeginInspection()
        {
            if (pageSurface == null) pageSurface = GetComponent<InspectableBookSurface>();
            if (pageSurface == null) pageSurface = gameObject.AddComponent<InspectableBookSurface>();
            pageSurface.SetContent(DisplayName,
                BookContent.Build(kind, Machine, LabRuntime.Instance?.Catalog));
            pageSurface.Show(true);
        }

        public override void TickInspection()
        {
            if (pageSurface == null) return;
            if (Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame)
                pageSurface.Turn(1);
            else if (Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame)
                pageSurface.Turn(-1);

            if (Mouse.current == null) return;
            float wheel = Mouse.current.scroll.ReadValue().y;
            if (wheel > 0.01f) pageSurface.Turn(-1);
            else if (wheel < -0.01f) pageSurface.Turn(1);
        }

        public override void EndInspection()
        {
            if (pageSurface != null) pageSurface.Show(false);
        }
    }
}
