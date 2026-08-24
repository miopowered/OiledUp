using System;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The player's three carried items. Inventory ownership and the selected hand are deliberately
    /// separate: stations act on <see cref="Selected"/>, while pickup only needs an empty slot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        public const int SlotCount = 3;

        private readonly Carryable[] slots = new Carryable[SlotCount];
        private Transform handSocket;
        private int selectedIndex;

        public event Action Changed;

        public int SelectedIndex => selectedIndex;
        public Carryable Selected => slots[selectedIndex];
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null) count++;
                return count;
            }
        }

        public bool HasSpace => Count < SlotCount;
        public IReadOnlyList<Carryable> Slots => slots;

        public void Initialize(Transform socket)
        {
            handSocket = socket != null ? socket : transform;
            RefreshPresentation();
        }

        public Carryable ItemAt(int index) => index >= 0 && index < SlotCount ? slots[index] : null;

        public bool Add(Carryable item)
        {
            if (item == null || !HasSpace) return false;

            int empty = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == item) return false;
                if (empty < 0 && slots[i] == null) empty = i;
            }

            if (empty < 0) return false;
            slots[empty] = item;
            selectedIndex = empty;
            item.AttachTo(handSocket != null ? handSocket : transform, interactable: false);
            RefreshPresentation();
            Changed?.Invoke();
            return true;
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= SlotCount || index == selectedIndex) return false;
            selectedIndex = index;
            RefreshPresentation();
            Changed?.Invoke();
            return true;
        }

        public Carryable RemoveSelected()
        {
            var removed = slots[selectedIndex];
            if (removed == null) return null;

            slots[selectedIndex] = null;
            removed.SetHeldVisible(true);

            for (int offset = 1; offset < SlotCount; offset++)
            {
                int candidate = (selectedIndex + offset) % SlotCount;
                if (slots[candidate] != null)
                {
                    selectedIndex = candidate;
                    break;
                }
            }

            RefreshPresentation();
            Changed?.Invoke();
            return removed;
        }

        public void RefreshPresentation()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                if (handSocket != null && slots[i].transform.parent != handSocket)
                    slots[i].AttachTo(handSocket, interactable: false);
                slots[i].SetHeldVisible(i == selectedIndex);
            }
        }
    }
}
