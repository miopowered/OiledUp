using System.Reflection;
using NUnit.Framework;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    public sealed class LabLayoutTests
    {
        private MachineDef machine;

        [SetUp]
        public void SetUp() => machine = Machine(new Vector2Int(2, 1));

        [TearDown]
        public void TearDown()
        {
            if (machine != null) Object.DestroyImmediate(machine);
        }

        [Test]
        public void Footprint_StaysInsidePoweredEquipmentCells()
        {
            var layout = PoweredBench(3, 2);

            Assert.IsFalse(layout.TryPlace(machine, "ftir", new Vector2Int(2, 0), false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.OutOfBounds, refusal);
            Assert.AreEqual(0, layout.Placements.Count, "A refused footprint must reserve no cells.");

            layout.TrySetCell(new Vector2Int(1, 0), new LabLayout.Cell(LabCellKind.Unusable));
            Assert.IsFalse(layout.TryPlace(machine, "ftir", Vector2Int.zero, false,
                out _, out refusal));
            Assert.AreEqual(LabPlacementRefusal.NoEquipmentSpace, refusal);
            Assert.IsNull(layout.OccupantAt(Vector2Int.zero));
        }

        [Test]
        public void Rotation_ExchangesFootprintAxes()
        {
            var layout = PoweredBench(1, 2);

            Assert.IsFalse(layout.TryPlace(machine, "wide", Vector2Int.zero, false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.OutOfBounds, refusal);

            Assert.IsTrue(layout.TryPlace(machine, "turned", Vector2Int.zero, true,
                out var placement, out refusal));
            Assert.AreEqual(LabPlacementRefusal.None, refusal);
            Assert.AreEqual(2, placement.OccupiedCells.Count);
            CollectionAssert.Contains(placement.OccupiedCells, new Vector2Int(0, 1));
        }

        [Test]
        public void EveryFootprintCell_NeedsPower()
        {
            var layout = PoweredBench(2, 1);
            layout.TrySetCell(new Vector2Int(1, 0), new LabLayout.Cell(LabCellKind.Bench));

            Assert.IsFalse(layout.TryPlace(machine, "half-powered", Vector2Int.zero, false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.NoPower, refusal);
            Assert.AreEqual(0, layout.Placements.Count);
        }

        [Test]
        public void CollisionAndDuplicateId_AreSpecificAndDoNotReserveMoreCells()
        {
            var layout = PoweredBench(4, 1);
            Assert.IsTrue(layout.TryPlace(machine, "first", Vector2Int.zero, false,
                out var first, out _));

            Assert.IsFalse(layout.TryPlace(machine, "second", new Vector2Int(1, 0), false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.Occupied, refusal);
            Assert.IsNull(layout.OccupantAt(new Vector2Int(2, 0)));

            Assert.IsFalse(layout.TryPlace(machine, "first", new Vector2Int(2, 0), false,
                out _, out refusal));
            Assert.AreEqual(LabPlacementRefusal.DuplicateInstanceId, refusal);
            Assert.AreSame(first, layout.OccupantAt(Vector2Int.zero));
            Assert.AreEqual(1, layout.Placements.Count);
        }

        [Test]
        public void FumeHood_RequiresOrthogonalAdjacency()
        {
            Object.DestroyImmediate(machine);
            machine = Machine(Vector2Int.one, requiresFumeHood: true);
            var layout = PoweredBench(3, 3);
            layout.TrySetCell(new Vector2Int(1, 1), new LabLayout.Cell(LabCellKind.FumeHood));

            Assert.IsFalse(layout.TryPlace(machine, "diagonal", Vector2Int.zero, false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.RequiresFumeHood, refusal);

            Assert.IsTrue(layout.TryPlace(machine, "adjacent", new Vector2Int(1, 0), false,
                out _, out refusal));
            Assert.AreEqual(LabPlacementRefusal.None, refusal);
        }

        [Test]
        public void FumeHood_AdjacentEquipmentCellsAreFiniteAndContended()
        {
            Object.DestroyImmediate(machine);
            machine = Machine(Vector2Int.one, requiresFumeHood: true);
            var layout = new LabLayout(3, 1);
            layout.TrySetCell(new Vector2Int(0, 0), new LabLayout.Cell(LabCellKind.Bench, true));
            layout.TrySetCell(new Vector2Int(1, 0), new LabLayout.Cell(LabCellKind.FumeHood));
            layout.TrySetCell(new Vector2Int(2, 0), new LabLayout.Cell(LabCellKind.Bench, true));

            Assert.IsTrue(layout.TryPlace(machine, "tan-a", new Vector2Int(0, 0), false, out _, out _));
            Assert.IsTrue(layout.TryPlace(machine, "tan-b", new Vector2Int(2, 0), false, out _, out _));
            Assert.IsFalse(layout.TryPlace(machine, "tan-c", new Vector2Int(0, 0), false,
                out _, out var refusal));
            Assert.AreEqual(LabPlacementRefusal.Occupied, refusal);
        }

        [Test]
        public void Remove_FreesExactlyThePlacementsCells()
        {
            var layout = PoweredBench(4, 1);
            Assert.IsTrue(layout.TryPlace(machine, "left", Vector2Int.zero, false,
                out var left, out _));
            Assert.IsTrue(layout.TryPlace(machine, "right", new Vector2Int(2, 0), false,
                out var right, out _));

            Assert.IsTrue(layout.Remove("left"));
            Assert.IsNull(layout.Find("left"));
            Assert.IsNull(layout.OccupantAt(Vector2Int.zero));
            Assert.IsNull(layout.OccupantAt(new Vector2Int(1, 0)));
            Assert.AreSame(right, layout.OccupantAt(new Vector2Int(2, 0)));
            Assert.AreSame(right, layout.OccupantAt(new Vector2Int(3, 0)));
            Assert.IsFalse(layout.Remove("left"));

            Assert.IsTrue(layout.TryPlace(machine, "replacement", Vector2Int.zero, false,
                out var replacement, out _));
            CollectionAssert.AreEqual(left.OccupiedCells, replacement.OccupiedCells);
        }

        [Test]
        public void RoomServices_CannotMoveAfterAPlacementWasAccepted()
        {
            var layout = PoweredBench(2, 1);
            Assert.IsTrue(layout.TryPlace(machine, "installed", Vector2Int.zero, false,
                out _, out _));
            Assert.IsTrue(layout.Remove("installed"));

            Assert.IsFalse(layout.TrySetCell(Vector2Int.zero,
                new LabLayout.Cell(LabCellKind.Unusable)));
            Assert.IsTrue(layout.TryGetCell(Vector2Int.zero, out var cell));
            Assert.AreEqual(LabCellKind.Bench, cell.Kind);
            Assert.IsTrue(cell.HasPower);
        }

        private static LabLayout PoweredBench(int width, int height)
        {
            var layout = new LabLayout(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                layout.TrySetCell(new Vector2Int(x, y), new LabLayout.Cell(LabCellKind.Bench, true));
            return layout;
        }

        private static MachineDef Machine(Vector2Int footprint, bool requiresFumeHood = false)
        {
            var definition = ScriptableObject.CreateInstance<MachineDef>();
            Set(definition, "footprint", footprint);
            Set(definition, "requiresFumeHood", requiresFumeHood);
            return definition;
        }

        private static void Set<T>(MachineDef definition, string fieldName, T value) =>
            typeof(MachineDef).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(definition, value);
    }
}
