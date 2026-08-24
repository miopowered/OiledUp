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
        private bool pageContentReady;

        public BookKind Kind => kind;
        public string InventoryId => $"{kind}:{machineId ?? string.Empty}";

        public override string DisplayName => BookContent.TitleFor(kind, Machine);

        // The words live on the generated 3D paper surface, never in the HUD reading overlay.
        public override string InspectionText => null;
        public override string InspectionHelp => "Arrow keys to turn pages";
        public override Quaternion InspectionRotation => Quaternion.Euler(-90f, 0f, 0f);
        public override Quaternion InventoryIconRotation => InspectionRotation;

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

        private void Awake()
        {
            // Give the item its physical pages immediately. The catalog may not have completed its
            // own Awake yet, so this first pass at least writes the cover/title onto the paper.
            EnsurePageSurface(forceContentRefresh: true);
        }

        private void Start()
        {
            // All scene Awakes have now run, so authored reference content is available. This is the
            // final rasterisation; opening inspection never creates or rewrites the pages.
            EnsurePageSurface(forceContentRefresh: true);
        }

        public void Configure(BookKind bookKind, string targetMachineId)
        {
            kind = bookKind;
            machineId = targetMachineId;
            name = $"Book_{bookKind}{(string.IsNullOrEmpty(targetMachineId) ? "" : "_" + targetMachineId)}";
            if (pageSurface != null) EnsurePageSurface(forceContentRefresh: true);
        }

        public override void BeginInspection()
        {
            EnsurePageSurface(forceContentRefresh: false);
        }

        public override void TickInspection()
        {
            if (pageSurface == null) return;
            if (Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame)
                pageSurface.Turn(1);
            else if (Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame)
                pageSurface.Turn(-1);

        }

        public override void EndInspection()
        {
            // The physical pages and their text remain part of the item in the world and in-hand.
        }

        private void EnsurePageSurface(bool forceContentRefresh)
        {
            if (pageSurface == null) pageSurface = GetComponent<InspectableBookSurface>();
            if (pageSurface == null) pageSurface = gameObject.AddComponent<InspectableBookSurface>();

            if (forceContentRefresh || !pageContentReady)
            {
                pageSurface.SetContent(DisplayName,
                    BookContent.Build(kind, Machine, LabRuntime.Instance?.Catalog));
                pageContentReady = true;
            }

            pageSurface.Show(true);
        }
    }
}
