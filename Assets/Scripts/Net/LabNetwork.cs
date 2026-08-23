using System;
using System.Text;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net.Session;
using Residue.Net.Views;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// The host's window onto the lab, and the only thing a client is allowed to look through.
    /// <para>
    /// §3.1 makes the host authoritative over everything that matters and a client authoritative
    /// only over its own transform and look direction. That split is enforced here by omission: the
    /// server holds <see cref="LabState"/> and publishes <i>views</i> of it, and there is no code
    /// path that sends a <c>SampleGroundTruth</c> because no view can express one. The guard is
    /// structural rather than remembered — see <c>ChemistryTests.NetworkLayer_NeverMentionsGroundTruth</c>,
    /// which greps this assembly by reflection.
    /// </para>
    /// The complementary half lives in <see cref="LabRuntime.SimulatesLocally"/>: a client never
    /// builds a lab at all, so there is no local simulation to leak from in the first place.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LabNetwork : NetworkBehaviour
    {
        public static LabNetwork Instance { get; private set; }

        /// <summary>
        /// Server-side roster. Keyed by a stable identity rather than NGO's client id, because a
        /// client id is per-connection and gets reused — see <see cref="SessionRegistry"/>.
        /// </summary>
        public SessionRegistry Sessions { get; } = new();

        // -- Replicated state ------------------------------------------------------------------------

        private readonly NetworkVariable<DayView> day = new();
        private readonly NetworkVariable<EconomyView> economy = new();

        private NetworkList<SampleView> samples;
        private NetworkList<MachineView> machines;

        public DayView Day => day.Value;
        public EconomyView Economy => economy.Value;
        public NetworkList<SampleView> Samples => samples;
        public NetworkList<MachineView> Machines => machines;

        /// <summary>Raised on the server when a dropped player's item must be put back (§M4).</summary>
        public event Action<PlayerSession, HeldItem> ItemReleased;

        private LabState Lab => LabRuntime.Instance != null ? LabRuntime.Instance.Lab : null;

        // NetworkList has to exist before the object spawns, so it cannot wait for OnNetworkSpawn.
        private void Awake()
        {
            samples = new NetworkList<SampleView>();
            machines = new NetworkList<MachineView>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (!IsServer)
            {
                // Belt and braces. The bootstrap should already have cleared this before the scene
                // loaded; if it did not, say so loudly rather than quietly simulating in parallel.
                if (LabRuntime.SimulatesLocally || Lab != null)
                {
                    Debug.LogError(
                        "[LabNetwork] This client built its own LabState. LabRuntime.SimulatesLocally " +
                        "must be false before the lab scene loads, or every player is running their " +
                        "own simulation — including its ground truth.", this);
                }
                return;
            }

            Sessions.ItemReleased += OnItemReleased;

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = Approve;
                manager.OnClientDisconnectCallback += OnClientDisconnect;
            }

            PublishAll();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;

            Sessions.ItemReleased -= OnItemReleased;

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = null;
                manager.OnClientDisconnectCallback -= OnClientDisconnect;
            }
        }

        // -- Connection ------------------------------------------------------------------------------

        /// <summary>
        /// Turn a connection request into a seat. The stable id rides in the payload; it is a
        /// convenience, not a credential, and is not verified — an attacker who forges someone
        /// else's id gets their empty-handed session, not their ground truth, because there is no
        /// ground truth on this side of the wire to get.
        /// </summary>
        private void Approve(NetworkManager.ConnectionApprovalRequest request,
                             NetworkManager.ConnectionApprovalResponse response)
        {
            string stableId = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : null;

            var join = Sessions.Join(stableId, request.ClientNetworkId, Time.realtimeSinceStartupAsDouble);

            response.Approved = join.Accepted;
            response.CreatePlayerObject = join.Accepted;
            response.Reason = join.RefusalReason;

            if (!join.Accepted)
            {
                Debug.Log($"[LabNetwork] Refused {request.ClientNetworkId}: {join.RefusalReason}", this);
                return;
            }

            // One identity, one body. The stale connection has already been unbound from the
            // session, so kicking it cannot take the seat with it.
            if (join.Outcome == JoinOutcome.Displaced)
            {
                Debug.Log($"[LabNetwork] {stableId} reconnected; dropping stale client " +
                          $"{join.DisplacedClientId}.", this);
                NetworkManager.Singleton.DisconnectClient(join.DisplacedClientId);
            }

            if (!join.IsRejoin) return;

            double absent = join.Session.AbsentSeconds(Time.realtimeSinceStartupAsDouble);
            Debug.Log($"[LabNetwork] {stableId} rejoined after {absent:F0}s away.", this);
        }

        private void OnClientDisconnect(ulong clientId)
        {
            // Before anything owned by that client despawns: the release handler needs the world
            // intact to put a carried vial back on the rack.
            Sessions.Disconnect(clientId, Time.realtimeSinceStartupAsDouble);
        }

        private void OnItemReleased(PlayerSession session, HeldItem item) => ItemReleased?.Invoke(session, item);

        // -- Publishing ------------------------------------------------------------------------------

        private float nextClockPush;

        private void Update()
        {
            if (!IsServer || Lab == null) return;

            // The clock moves every frame but nobody needs it at frame rate, and a NetworkVariable
            // write per frame per client is the easy way to spend a co-op session's bandwidth on a
            // number that changes in the third decimal place.
            if (Time.time < nextClockPush) return;
            nextClockPush = Time.time + 0.25f;

            PublishAll();
        }

        /// <summary>
        /// Rebuild every view from the server's state.
        /// <para>
        /// Deliberately a whole-snapshot rebuild rather than incremental diffing. The lab is a few
        /// dozen samples and five instruments — small enough that the simplest correct thing is also
        /// fast enough, and NGO's list already suppresses writes that do not change a value because
        /// the views are <c>IEquatable</c>. Incremental invalidation is where desync comes from, and
        /// desync is the one thing §M4 acceptance names.
        /// </para>
        /// </summary>
        public void PublishAll()
        {
            if (!IsServer || Lab == null) return;

            day.Value = DayView.From(Lab);
            economy.Value = EconomyView.From(Lab.Economy);

            Sync(samples);
            Sync(machines);
        }

        private void Sync(NetworkList<SampleView> list)
        {
            var open = Lab.Samples.All;
            int i = 0;

            foreach (var state in open)
            {
                var view = SampleView.From(state);
                if (i < list.Count)
                {
                    if (!list[i].Equals(view)) list[i] = view;
                }
                else list.Add(view);
                i++;
            }

            for (int extra = list.Count - 1; extra >= i; extra--) list.RemoveAt(extra);
        }

        private void Sync(NetworkList<MachineView> list)
        {
            int i = 0;

            foreach (var machine in Lab.Machines)
            {
                var view = MachineView.From(machine);
                if (i < list.Count)
                {
                    if (!list[i].Equals(view)) list[i] = view;
                }
                else list.Add(view);
                i++;
            }

            for (int extra = list.Count - 1; extra >= i; extra--) list.RemoveAt(extra);
        }
    }
}
