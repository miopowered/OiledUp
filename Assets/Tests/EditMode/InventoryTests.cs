using NUnit.Framework;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Tests
{
    public sealed class InventoryTests
    {
        private GameObject root;
        private PlayerInventory inventory;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Inventory test player");
            inventory = root.AddComponent<PlayerInventory>();
            inventory.Initialize(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void Add_FillsExactlyThreeSlots_AndSelectsNewestItem()
        {
            var first = Item("one");
            var second = Item("two");
            var third = Item("three");
            var overflow = Item("four");

            Assert.IsTrue(inventory.Add(first));
            Assert.IsTrue(inventory.Add(second));
            Assert.IsTrue(inventory.Add(third));
            Assert.IsFalse(inventory.Add(overflow));

            Assert.AreEqual(PlayerInventory.SlotCount, inventory.Count);
            Assert.AreSame(third, inventory.Selected);
            Assert.IsFalse(inventory.HasSpace);
            Object.DestroyImmediate(overflow.gameObject);
        }

        [Test]
        public void RemoveSelected_FreesItsSlot_AndSelectsAnotherStoredItem()
        {
            var first = Item("one");
            var second = Item("two");
            inventory.Add(first);
            inventory.Add(second);

            Assert.AreSame(second, inventory.RemoveSelected());
            Assert.AreEqual(1, inventory.Count);
            Assert.AreSame(first, inventory.Selected);
            Assert.IsTrue(inventory.HasSpace);
            Object.DestroyImmediate(second.gameObject);
        }

        private TestInventoryItem Item(string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(root.transform);
            return go.AddComponent<TestInventoryItem>();
        }

    }

    public sealed class TestInventoryItem : Carryable
    {
        public override string DisplayName => name;
    }
}
