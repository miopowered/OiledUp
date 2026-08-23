using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Unity.Collections;
using UnityEngine;

namespace Residue.Net.Views
{
    /// <summary>
    /// <see cref="IMachineView"/> over a replicated <see cref="MachineView"/> plus the client's own
    /// <see cref="MachineDef"/>. This is what makes an instrument readable to a player who is not
    /// hosting.
    /// <para>
    /// <b>Two halves, deliberately.</b> The snapshot carries only what changes — busy, loaded, seconds
    /// left, and the §5.2 / §5.3 tells. Everything static comes out of the
    /// <see cref="ContentCatalog"/> this process already ships, resolved once from
    /// <see cref="MachineView.DefId"/>. Sending a copy of the definition instead would put balance
    /// data on the wire and give the lab two sources of truth for a threshold.
    /// </para>
    /// <para>
    /// <b>It re-reads the list on every access.</b> Nothing is cached here except the definition,
    /// because a station asks these questions every frame and the answer has to be the latest publish
    /// rather than whatever was current when this object was built.
    /// </para>
    /// </summary>
    public sealed class ReplicatedMachineView : IMachineView
    {
        private readonly LabNetwork network;
        private readonly string instanceId;

        /// <summary>
        /// The placed id in wire form, packed once. Comparing two <c>FixedString</c>s is a byte
        /// compare; comparing against a managed string means converting one of them per candidate per
        /// frame, which for a question asked this often is pure waste.
        /// </summary>
        private readonly FixedString64Bytes packedInstanceId;

        /// <summary>
        /// A single publish, frozen. Null on the live adapter, which reads whatever the list holds
        /// this frame. See <see cref="Of"/>.
        /// </summary>
        private readonly MachineView? frozen;

        /// <summary>
        /// The catalog to resolve <see cref="MachineView.DefId"/> against, or null to use this
        /// process's own. Injectable only so that the parity test can run without a scene.
        /// </summary>
        private readonly ContentCatalog suppliedCatalog;

        private MachineDef def;
        private FixedString64Bytes resolvedFrom;

        public ReplicatedMachineView(LabNetwork network, string instanceId)
        {
            this.network = network;
            this.instanceId = instanceId;
            packedInstanceId = ViewText.Fixed64(instanceId);
        }

        private ReplicatedMachineView(MachineView snapshot, ContentCatalog catalog)
        {
            frozen = snapshot;
            instanceId = snapshot.InstanceId.ToString();
            packedInstanceId = snapshot.InstanceId;
            suppliedCatalog = catalog;
        }

        /// <summary>
        /// Read one publish rather than a live list — what the adapter above reduces to at an instant.
        /// <para>
        /// Exists so the two <see cref="IMachineView"/> implementations can be held against each other
        /// in a test with no session and no scene. That comparison is the only thing keeping "what the
        /// host draws" and "what a client draws" from quietly diverging, and it is worth a
        /// constructor.
        /// </para>
        /// </summary>
        public static ReplicatedMachineView Of(MachineView snapshot, ContentCatalog catalog) =>
            new(snapshot, catalog);

        public string InstanceId => instanceId;

        /// <summary>
        /// The snapshot this frame, or <c>default</c> if the instrument has not been published yet —
        /// which is the normal state for the first few frames after the lab scene loads. A default
        /// snapshot reads as an idle, empty, nameless instrument, which is exactly what the room
        /// should show while it waits.
        /// </summary>
        private MachineView Snapshot
        {
            get
            {
                if (frozen.HasValue) return frozen.Value;

                var list = network != null ? network.Machines : null;
                if (list == null) return default;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].InstanceId.Equals(packedInstanceId)) return list[i];
                }
                return default;
            }
        }

        /// <summary>
        /// Look the definition up in this process's own catalog, once. Cached against the def id it
        /// was resolved from, so a lab that ever installs a different instrument under the same placed
        /// id corrects itself instead of showing the old name for the rest of the session.
        /// </summary>
        public MachineDef Def
        {
            get
            {
                var defId = Snapshot.DefId;
                if (defId.Length == 0) return def;
                if (def != null && resolvedFrom.Equals(defId)) return def;

                var catalog = suppliedCatalog != null
                    ? suppliedCatalog
                    : LabRuntime.Instance != null ? LabRuntime.Instance.Catalog : null;

                if (catalog == null) return def;

                def = catalog.Machine(defId.ToString());
                resolvedFrom = defId;
                return def;
            }
        }

        public string DisplayName
        {
            get
            {
                var definition = Def;
                return definition != null ? definition.DisplayName : "Instrument";
            }
        }

        public bool IsRunning => Snapshot.IsRunning;

        public bool IsEmpty => !Snapshot.IsLoaded;

        public bool HasResultWaiting => Snapshot.HasResultWaiting;

        public float SecondsRemaining => Snapshot.SecondsRemaining;

        public float Progress => Snapshot.Progress;

        public float RunSeconds => Snapshot.RunSeconds;

        /// <summary>
        /// Half a run, floor and all — the exact expression <see cref="MachineInstance.CalibrationSeconds"/>
        /// uses. Derived rather than replicated because it is that ratio and nothing else; a second
        /// wire field would be a second place for it to go wrong. The floor is copied rather than
        /// dropped because a fully scaled-down test lab lands under it, and a station quoting 0 s to
        /// one player and 0.1 s to another is exactly the divergence this whole seam exists to avoid.
        /// </summary>
        public float CalibrationSeconds => Mathf.Max(0.1f, Snapshot.RunSeconds * 0.5f);

        public bool HasFreshCheck(int day)
        {
            var snapshot = Snapshot;
            return snapshot.HasCalibrationCheck && snapshot.CalibrationCheckDay == day;
        }

        /// <summary>
        /// What this side can tell about a load, which is occupancy and nothing else.
        /// <para>
        /// Volume, temperature and settling are properties of a <see cref="SampleState"/>, and a client
        /// holds none — §3.1 keeps the sample vault on the host. So a client that can see no reason to
        /// refuse says <see cref="LoadRefusal.Accepted"/> and lets the host be the one to object: an
        /// optimistic prompt costs the player a refusal sentence they can read, whereas a pessimistic
        /// one greys out a button that would have worked. That trade is the house pattern — see
        /// <see cref="LabCommands"/> — and it is why none of the §4.5 rules are re-implemented here,
        /// where they could drift from the copy that is actually enforced.
        /// </para>
        /// </summary>
        public LoadRefusal CanAccept(SampleState sample)
        {
            var snapshot = Snapshot;
            if (snapshot.IsRunning) return LoadRefusal.MachineBusy;
            if (snapshot.IsLoaded) return LoadRefusal.MachineOccupied;
            return LoadRefusal.Accepted;
        }
    }
}
