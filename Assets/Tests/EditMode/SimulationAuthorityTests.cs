using System.Linq;
using NUnit.Framework;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the half of hard rule 2 that lives outside the network layer.
    /// <para>
    /// <c>NetworkLayer_NeverMentionsGroundTruth</c> proves nothing crosses the wire. It cannot prove
    /// a client did not compute the answers for itself, and <see cref="LabState"/> owns a generator
    /// that produces ground truth as a matter of course. If a client ran that constructor it would
    /// hold a working truth-bearing simulation locally — the wire would still be clean, and the rule
    /// would still be broken.
    /// </para>
    /// So the guarantee is "a client never builds one", and this is the test that says so.
    /// </summary>
    public sealed class SimulationAuthorityTests
    {
        private ContentSet content;
        private ContentCatalog catalog;
        private GameObject host;
        private bool originalAuthority;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);
            originalAuthority = LabRuntime.SimulatesLocally;
        }

        [TearDown]
        public void TearDown()
        {
            // Static, and every other test in the suite assumes single player. Leaving it false
            // would fail them somewhere else entirely, which is a miserable thing to debug.
            LabRuntime.SimulatesLocally = originalAuthority;

            if (host != null) Object.DestroyImmediate(host);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (content == null) return;

            foreach (var o in content.Elements.Values.Cast<Object>()
                         .Concat(content.Causes.Values)
                         .Concat(content.Profiles.Values)
                         .Concat(content.Faults.Values)
                         .Concat(content.Machines.Values))
            {
                Object.DestroyImmediate(o);
            }
            content = null;
        }

        /// <summary>
        /// Build the runtime with its catalog wired, then run the step <c>Awake</c> would have run.
        /// <para>
        /// Unity does not run MonoBehaviour lifecycle methods in edit mode. Creating the component
        /// and activating the object does <b>not</b> fire <c>Awake</c> — so a test written that way
        /// finds <c>Lab</c> null every time and "passes" the client case for a reason that has
        /// nothing to do with authority. That is why <see cref="LabRuntime.BuildLabIfAuthoritative"/>
        /// exists: <c>Awake</c> delegates to it, and calling it here exercises what actually runs.
        /// </para>
        /// </summary>
        private LabRuntime Spawn()
        {
            host = new GameObject("LabRuntime_UnderTest");
            host.SetActive(false);

            var runtime = host.AddComponent<LabRuntime>();

            var so = new UnityEditor.SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("seed").intValue = 20260823;
            so.ApplyModifiedPropertiesWithoutUndo();

            runtime.BuildLabIfAuthoritative();
            return runtime;
        }

        [Test]
        public void AHost_BuildsTheLab()
        {
            LabRuntime.SimulatesLocally = true;
            var runtime = Spawn();

            Assert.IsNotNull(runtime.Lab,
                "The host simulates. Without a LabState there is no game to be authoritative over.");
            Assert.IsFalse(runtime.IsReplicatedClient);
        }

        [Test]
        public void AClient_BuildsNoLab_AndThereforeNoGroundTruth()
        {
            LabRuntime.SimulatesLocally = false;
            var runtime = Spawn();

            Assert.IsNull(runtime.Lab,
                "A client built a LabState. That is a SampleGenerator in every player's process, " +
                "producing SampleGroundTruth alongside every sample it makes — hard rule 2 broken " +
                "without a single byte crossing the wire.");

            Assert.IsTrue(runtime.IsReplicatedClient,
                "A client with no lab must report itself as replicated, so world components can " +
                "tell 'not the host' from 'host, but something failed during startup'.");
        }

        /// <summary>
        /// The default matters: single player is the same scene with nobody connected, and it must
        /// simulate without anyone remembering to switch this on.
        /// </summary>
        [Test]
        public void SinglePlayer_SimulatesWithoutBeingAskedTo()
        {
            Assert.IsTrue(originalAuthority,
                "LabRuntime.SimulatesLocally must default to true, or launching the game outside " +
                "a netcode session gives an empty lab and no error explaining why.");
        }
    }
}
