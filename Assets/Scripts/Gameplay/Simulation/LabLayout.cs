using System;
using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>What a §5.5 grid cell physically provides.</summary>
    public enum LabCellKind
    {
        Unusable,
        Bench,
        Floor,
        FumeHood
    }

    /// <summary>Why the host refused an instrument placement.</summary>
    public enum LabPlacementRefusal
    {
        None,
        NoDefinition,
        InvalidInstanceId,
        DuplicateInstanceId,
        InvalidFootprint,
        OutOfBounds,
        NoEquipmentSpace,
        NoPower,
        Occupied,
        RequiresFumeHood
    }

    /// <summary>
    /// Host-authoritative placement rules for §5.5's 0.5 m lab grid.
    /// <para>
    /// This is deliberately geometry-free. A build-mode screen may preview these answers and a
    /// scene rebuilder may turn accepted placements into objects, but neither gets to redefine
    /// bounds, power, collision, or the fume-hood bottleneck locally.
    /// </para>
    /// </summary>
    public sealed class LabLayout
    {
        public const float CellSizeMetres = 0.5f;

        /// <summary>One configured cell. Hood cells are fixtures, never equipment space.</summary>
        public readonly struct Cell
        {
            public readonly LabCellKind Kind;
            public readonly bool HasPower;

            public Cell(LabCellKind kind, bool hasPower = false)
            {
                Kind = kind;
                HasPower = hasPower;
            }

            public bool SupportsEquipment => Kind is LabCellKind.Bench or LabCellKind.Floor;
        }

        /// <summary>An accepted instrument footprint. Its occupied cells never change in place.</summary>
        public sealed class Placement
        {
            private readonly IReadOnlyList<Vector2Int> occupiedCells;

            public string InstanceId { get; }
            public MachineDef Definition { get; }
            public Vector2Int Anchor { get; }
            public bool Rotated { get; }
            public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;

            internal Placement(string instanceId, MachineDef definition, Vector2Int anchor,
                               bool rotated, List<Vector2Int> occupiedCells)
            {
                InstanceId = instanceId;
                Definition = definition;
                Anchor = anchor;
                Rotated = rotated;
                this.occupiedCells = occupiedCells.AsReadOnly();
            }
        }

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
        };

        private readonly Cell[,] cells;
        private readonly Dictionary<string, Placement> byInstanceId = new(StringComparer.Ordinal);
        private readonly Dictionary<Vector2Int, Placement> byCell = new();
        private bool cellsSealed;

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyCollection<Placement> Placements => byInstanceId.Values;

        public LabLayout(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            cells = new Cell[width, height];
        }

        /// <summary>Configure the immutable room beneath placements. Refuses out-of-bounds cells.</summary>
        public bool TrySetCell(Vector2Int position, Cell cell)
        {
            // Room services are authored before instruments. Letting a caller move power or the
            // hood afterwards could make an accepted placement illegal behind the host's back.
            if (cellsSealed || !Contains(position)) return false;
            cells[position.x, position.y] = cell;
            return true;
        }

        public bool TryGetCell(Vector2Int position, out Cell cell)
        {
            if (!Contains(position))
            {
                cell = default;
                return false;
            }

            cell = cells[position.x, position.y];
            return true;
        }

        public Placement Find(string instanceId) =>
            !string.IsNullOrEmpty(instanceId) && byInstanceId.TryGetValue(instanceId, out var placement)
                ? placement
                : null;

        public Placement OccupantAt(Vector2Int position) =>
            byCell.TryGetValue(position, out var placement) ? placement : null;

        /// <summary>
        /// Reserve a definition's complete footprint, or leave the grid byte-for-byte unchanged.
        /// Rotation is a quarter turn: width and height exchange; square instruments are unchanged.
        /// </summary>
        public bool TryPlace(MachineDef definition, string instanceId, Vector2Int anchor, bool rotated,
                             out Placement placement, out LabPlacementRefusal refusal)
        {
            placement = null;

            if (definition == null)
            {
                refusal = LabPlacementRefusal.NoDefinition;
                return false;
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                refusal = LabPlacementRefusal.InvalidInstanceId;
                return false;
            }

            if (byInstanceId.ContainsKey(instanceId))
            {
                refusal = LabPlacementRefusal.DuplicateInstanceId;
                return false;
            }

            Vector2Int footprint = definition.Footprint;
            if (footprint.x <= 0 || footprint.y <= 0)
            {
                refusal = LabPlacementRefusal.InvalidFootprint;
                return false;
            }

            if (rotated) footprint = new Vector2Int(footprint.y, footprint.x);
            var occupied = new List<Vector2Int>(footprint.x * footprint.y);

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    var position = anchor + new Vector2Int(x, y);
                    if (!Contains(position))
                    {
                        refusal = LabPlacementRefusal.OutOfBounds;
                        return false;
                    }

                    Cell cell = cells[position.x, position.y];
                    if (!cell.SupportsEquipment)
                    {
                        refusal = LabPlacementRefusal.NoEquipmentSpace;
                        return false;
                    }

                    if (!cell.HasPower)
                    {
                        refusal = LabPlacementRefusal.NoPower;
                        return false;
                    }

                    if (byCell.ContainsKey(position))
                    {
                        refusal = LabPlacementRefusal.Occupied;
                        return false;
                    }

                    occupied.Add(position);
                }
            }

            if (definition.RequiresFumeHood && !TouchesFumeHood(occupied))
            {
                refusal = LabPlacementRefusal.RequiresFumeHood;
                return false;
            }

            placement = new Placement(instanceId, definition, anchor, rotated, occupied);
            byInstanceId.Add(instanceId, placement);
            foreach (var position in occupied) byCell.Add(position, placement);
            cellsSealed = true;

            refusal = LabPlacementRefusal.None;
            return true;
        }

        /// <summary>Remove one footprint. False is a no-op, including for an empty id.</summary>
        public bool Remove(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) ||
                !byInstanceId.TryGetValue(instanceId, out var placement)) return false;

            foreach (var position in placement.OccupiedCells) byCell.Remove(position);
            byInstanceId.Remove(instanceId);
            return true;
        }

        private bool TouchesFumeHood(IReadOnlyList<Vector2Int> occupied)
        {
            foreach (var position in occupied)
            {
                foreach (var direction in CardinalDirections)
                {
                    Vector2Int neighbour = position + direction;
                    if (Contains(neighbour) &&
                        cells[neighbour.x, neighbour.y].Kind == LabCellKind.FumeHood) return true;
                }
            }

            return false;
        }

        private bool Contains(Vector2Int position) =>
            position.x >= 0 && position.x < Width && position.y >= 0 && position.y < Height;
    }
}
