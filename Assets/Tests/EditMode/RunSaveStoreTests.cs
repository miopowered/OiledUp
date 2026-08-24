using System;
using System.IO;
using NUnit.Framework;
using Residue.Gameplay.Simulation;

namespace Residue.Tests.EditMode
{
    public sealed class RunSaveStoreTests
    {
        private string directory;
        private string slotPath;
        private RunSaveStore store;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "oiledup-save-tests", Guid.NewGuid().ToString("N"));
            slotPath = Path.Combine(directory, "run.save");
            store = new RunSaveStore(slotPath);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch { /* best effort: a failed assertion must remain the useful failure */ }
        }

        [Test]
        public void Utf8Payload_RoundTripsExactly()
        {
            const string expected = "day 12\nÖlprobe: 冷却油\n";

            Assert.IsTrue(store.TrySave(expected, out string saveFailure), saveFailure);
            Assert.IsTrue(store.TryLoad(out string actual, out var source, out string loadFailure), loadFailure);

            Assert.AreEqual(expected, actual);
            Assert.AreEqual(RunSaveSource.Primary, source);
            Assert.IsNull(loadFailure);
        }

        [Test]
        public void Overwrite_KeepsPreviousValidSaveAsBackup()
        {
            Assert.IsTrue(store.TrySave("day 3", out string firstFailure), firstFailure);
            Assert.IsTrue(store.TrySave("day 4", out string secondFailure), secondFailure);

            Assert.IsTrue(File.Exists(store.BackupPath));
            Assert.IsTrue(store.TryLoad(out string current, out var source, out string loadFailure), loadFailure);
            Assert.AreEqual("day 4", current);
            Assert.AreEqual(RunSaveSource.Primary, source);

            File.Move(store.SlotPath, store.SlotPath + ".removed");
            Assert.IsTrue(store.TryLoad(out string previous, out source, out loadFailure), loadFailure);
            Assert.AreEqual("day 3", previous);
            Assert.AreEqual(RunSaveSource.Backup, source);
        }

        [Test]
        public void CorruptPrimary_RecoversBackupAndReportsIt()
        {
            Assert.IsTrue(store.TrySave("known good", out _));
            Assert.IsTrue(store.TrySave("new primary", out _));

            byte[] damaged = File.ReadAllBytes(store.SlotPath);
            damaged[damaged.Length - 1] ^= 0x7f;
            File.WriteAllBytes(store.SlotPath, damaged);

            Assert.IsTrue(store.TryLoad(out string payload, out var source, out string warning));
            Assert.AreEqual("known good", payload);
            Assert.AreEqual(RunSaveSource.Backup, source);
            StringAssert.Contains("recovered its backup", warning);
        }

        [Test]
        public void UnsupportedVersion_IsRejectedWithExpectedVersion()
        {
            Assert.IsTrue(store.TrySave("future", out _));

            string text = File.ReadAllText(store.SlotPath);
            File.WriteAllText(store.SlotPath, text.Replace("OILEDUP-SAVE\n1\n", "OILEDUP-SAVE\n99\n"));

            Assert.IsFalse(store.TryLoad(out _, out var source, out string refusal));
            Assert.AreEqual(RunSaveSource.None, source);
            StringAssert.Contains("format 99 is unsupported", refusal);
            StringAssert.Contains("expected 1", refusal);
        }

        [Test]
        public void FailedWrite_PreservesLastLoadableSlot()
        {
            Assert.IsTrue(store.TrySave("keep me", out _));

            // The store removes a stale temp file but cannot replace a directory at that path. This
            // exercises a write failure after a valid primary exists without relying on OS ACLs.
            Directory.CreateDirectory(store.TemporaryPath);

            Assert.IsFalse(store.TrySave("do not install", out string refusal));
            StringAssert.Contains("Could not save the run", refusal);

            Assert.IsTrue(store.TryLoad(out string payload, out var source, out string loadFailure), loadFailure);
            Assert.AreEqual("keep me", payload);
            Assert.AreEqual(RunSaveSource.Primary, source);
        }

        [Test]
        public void CorruptPrimary_DoesNotReplaceKnownGoodBackupOnNextSave()
        {
            Assert.IsTrue(store.TrySave("oldest", out _));
            Assert.IsTrue(store.TrySave("last known good", out _));
            File.WriteAllText(store.SlotPath, "truncated");

            Assert.IsTrue(store.TrySave("repaired primary", out string saveFailure), saveFailure);
            File.WriteAllText(store.SlotPath, "damaged again");

            Assert.IsTrue(store.TryLoad(out string payload, out var source, out string warning), warning);
            Assert.AreEqual("oldest", payload);
            Assert.AreEqual(RunSaveSource.Backup, source);
        }
    }
}
