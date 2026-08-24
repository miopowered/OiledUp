using System;
using System.Collections.Generic;
using System.Text;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net.Session;
using Residue.Net.Views;
using Unity.Collections;
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
    /// <para>
    /// <b>It is also the door actions go out of.</b> Views travel one way; §3.1's other half is that
    /// every interaction is a request the host validates, and <see cref="Route"/> is where
    /// <see cref="LabCommands.Send"/> ends up once a session exists. There is one RPC in, one reply
    /// out to the single client that asked, and one shared validator — <see cref="LabCommandExecutor"/>,
    /// which lives in <c>Residue.Gameplay</c> and has never heard of netcode. Nothing here decides
    /// whether an action is legal; it decides who is asking.
    /// </para>
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
        private NetworkList<VialView> vials;
        private NetworkList<SolventBottleView> solventBottles;
        private NetworkList<ResultView> results;
        private NetworkList<ReadingView> readings;

        public DayView Day => day.Value;
        public EconomyView Economy => economy.Value;
        public NetworkList<SampleView> Samples => samples;
        public NetworkList<MachineView> Machines => machines;

        /// <summary>
        /// Every finished run a screen may still need: filed against a record, sitting on an
        /// instrument, or the blank and the certificate that say whether an instrument can be trusted.
        /// <para>
        /// Flat and keyed rather than nested inside <see cref="Samples"/>, because a run does not
        /// always belong to a sample — a solvent blank belongs to the machine (§5.2) — and because a
        /// <c>NetworkList</c> element must be unmanaged, so "a sample with its results inside it" is
        /// not a shape this wire has. See <see cref="ResultView"/> for the budget argument.
        /// </para>
        /// </summary>
        public NetworkList<ResultView> Results => results;

        /// <summary>
        /// The numbers, each naming the run it came off. Separate from <see cref="Results"/> because
        /// a panel is not a fixed width and a cap on it would be a cap on what the player is shown
        /// while still being judged on all of it — see <see cref="ReadingView"/>.
        /// </summary>
        public NetworkList<ReadingView> Readings => readings;

        /// <summary>
        /// The bottles, as physical objects: where each one is and what its label says.
        /// <para>
        /// Separate from <see cref="Samples"/> on purpose — see <see cref="VialView"/>. That list is
        /// what screens read and carries the tag the player typed; this one is what the world reads
        /// and carries the tag on the bottle. Keeping them apart is what stops a terminal being able
        /// to point out a mis-log the player was supposed to catch by walking back and looking.
        /// </para>
        /// </summary>
        public NetworkList<VialView> Vials => vials;

        /// <summary>
        /// The solvent bottles (§5.2, #14). Two rows, and they are what keep a client able to flush.
        /// <para>
        /// Flushing needed no vial, which is why it worked from a client on day one. Moving the
        /// solvent into a carryable bottle would have quietly made it host-only if the bottle stayed
        /// on the host — so the bottle replicates, the prompt on a client's instrument reads the same
        /// charge count the host is about to spend, and the action goes through the same door
        /// everything else does.
        /// </para>
        /// </summary>
        public NetworkList<SolventBottleView> SolventBottles => solventBottles;

        /// <summary>Raised on the server when a dropped player's item must be put back (§M4).</summary>
        public event Action<PlayerSession, HeldItem> ItemReleased;

        private LabState Lab => LabRuntime.Instance != null ? LabRuntime.Instance.Lab : null;

        // NetworkList has to exist before the object spawns, so it cannot wait for OnNetworkSpawn.
        private void Awake()
        {
            samples = new NetworkList<SampleView>();
            machines = new NetworkList<MachineView>();
            vials = new NetworkList<VialView>();
            solventBottles = new NetworkList<SolventBottleView>();
            results = new NetworkList<ResultView>();
            readings = new NetworkList<ReadingView>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            // From here on, "ask the server" means something. Installed for a client and a host
            // alike: the host's own actions take the same route and get the same validation, which is
            // what stops single player and co-op becoming two sets of rules (see LabCommands).
            LabCommands.Router = Route;

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

                // The read seam, wired up. Until this lands, every station in the room has nothing to
                // read and switches itself off; from here they draw from the same snapshots the
                // terminal does. Client-only: a host reads its own lab, which is a publish ahead of
                // anything it has sent (see LabView).
                LabView.Replicated = new ReplicatedLabView(this);

                // And the numbers. Until this lands the terminal has no evidence to draw and says so
                // instead of offering a verdict, which is hard rule 3 refusing to ask for a call the
                // player cannot check.
                RecordFeed.Source = new ReplicatedRecords(this);
                return;
            }

            Sessions.ItemReleased += OnItemReleased;

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = Approve;
                manager.OnClientDisconnectCallback += OnClientDisconnect;
                manager.OnClientConnectedCallback += OnClientConnected;
                SeatTheHost(manager);
            }

            // This object lives in the lab scene, so by the time it spawns there is a floor to stand
            // on. Everyone connected is still frozen wherever they were created — place them now.
            PlaceEveryone();

            PublishAll();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
                LabCommands.Router = null;

                // Cleared together with the router: a station reading a despawned list would be
                // drawing a lab that is no longer on the other end of anything.
                if (LabView.Replicated is ReplicatedLabView) LabView.Replicated = null;
                if (RecordFeed.Source is ReplicatedRecords) RecordFeed.Source = null;
            }

            awaiting.Clear();

            if (!IsServer) return;

            actors.Clear();
            resultKeys.Clear();
            checkKeys.Clear();
            Sessions.ItemReleased -= OnItemReleased;

            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = null;
                manager.OnClientDisconnectCallback -= OnClientDisconnect;
                manager.OnClientConnectedCallback -= OnClientConnected;
            }
        }

        // -- Placing players ---------------------------------------------------------------------------

        /// <summary>
        /// Where players stand when the lab opens. Spread along the near wall, facing the instruments.
        /// <para>
        /// Four fixed spots rather than one, because everybody spawning on the same tile means four
        /// capsules resolving their overlap by shoving each other across the room.
        /// </para>
        /// </summary>
        private static readonly Vector3[] SpawnPoints =
        {
            new(-1.2f, 0.05f, 2.6f),
            new(0.0f, 0.05f, 2.6f),
            new(1.2f, 0.05f, 2.6f),
            new(2.4f, 0.05f, 2.6f)
        };

        private const float SpawnYaw = 180f;   // facing the bench, which is where the game is

        /// <summary>
        /// Tell every connected player where they belong.
        /// <para>
        /// A player object exists from the moment its connection is approved — for a host, during
        /// <c>StartHost</c>, long before the lab scene has loaded. It is created in whatever scene is
        /// current, which is the Boot menu: no floor, and nothing to stop it falling. Freezing it and
        /// placing it here is what turns "connected" into "standing in the lab".
        /// </para>
        /// Restores the saved pose for a rejoining player instead, so a reconnect puts you back where
        /// you were rather than at the door (§M4).
        /// </summary>
        private void PlaceEveryone()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            int index = 0;
            foreach (var client in manager.ConnectedClientsList)
            {
                Place(client, index);
                index++;
            }
        }

        private void Place(NetworkClient client, int index)
        {
            var player = client?.PlayerObject;
            if (player == null) return;

            var avatar = player.GetComponent<PlayerAvatar>();
            if (avatar == null) return;

            Vector3 position = SpawnPoints[index % SpawnPoints.Length];
            float yaw = SpawnYaw;

            if (Sessions.TryGet(client.ClientId, out var session) && session.Pose.HasValue)
            {
                position = session.Pose.Position;
                yaw = session.Pose.Yaw;
            }

            avatar.PlaceRpc(position, yaw);
        }

        /// <summary>
        /// Give the person hosting a seat, by hand.
        /// <para>
        /// <b>This looks redundant and is not.</b> <c>NetworkManager</c> approves the host's own
        /// connection during <c>StartHost</c>, and only <i>if a connection-approval callback is
        /// already installed</i>. Ours is installed two lines above, in <see cref="OnNetworkSpawn"/>
        /// — which cannot run until the lab scene has loaded, which the host does not do until
        /// <c>StartHost</c> has already returned. So the host takes the auto-approve path,
        /// <see cref="Approve"/> never sees them, and <see cref="SessionRegistry"/> ends up with an
        /// entry for every player except the one running the game. Nothing errors: it looks fine
        /// until the host drops something (nothing tracks their hands, so nothing puts it back) or
        /// reconnects (no session to restore).
        /// </para>
        /// <para>
        /// Fixing it by installing the callback earlier would mean taking it off this
        /// <c>NetworkBehaviour</c> entirely — its spawn hook is by definition after the scene, and the
        /// scene is by definition after <c>StartHost</c>. Seating the host explicitly keeps approval
        /// in one place doing one job (remote clients) and puts the special case where it can be read
        /// next to the reason for it.
        /// </para>
        /// The stable id is read from the same <c>NetworkConfig.ConnectionData</c> the connect flow
        /// filled in before <c>StartHost</c>, so the host is keyed exactly as they would have been had
        /// approval run — which is what makes their rejoin work.
        /// </summary>
        private void SeatTheHost(NetworkManager manager)
        {
            if (!manager.IsHost) return;   // a dedicated server has no player of its own to seat

            ulong hostId = manager.LocalClientId;
            if (Sessions.TryGet(hostId, out _)) return;

            var payload = manager.NetworkConfig.ConnectionData;
            string stableId = payload != null && payload.Length > 0
                ? Encoding.UTF8.GetString(payload)
                : null;

            if (string.IsNullOrWhiteSpace(stableId))
            {
                // Nothing better is available, and a host with no session at all is worse than a host
                // whose rejoin is keyed on something ephemeral.
                stableId = $"host-{hostId}";
                Debug.LogWarning(
                    "[LabNetwork] The host started with no identity in NetworkConfig.ConnectionData, " +
                    $"so their session is keyed on '{stableId}' and will not survive a rejoin. The " +
                    "connect flow sets this before StartHost; something started the host without it.",
                    this);
            }

            var join = Sessions.Join(stableId, hostId, Time.realtimeSinceStartupAsDouble);
            if (!join.Accepted)
            {
                Debug.LogError($"[LabNetwork] Could not seat the host: {join.RefusalReason}", this);
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

        /// <summary>
        /// Somebody joined a lab that is already open. <see cref="PlaceEveryone"/> only runs when this
        /// object spawns, which for a late joiner happened before they arrived — so they would stay
        /// frozen at wherever their player object was created, which is nowhere in particular.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;
            if (!manager.ConnectedClients.TryGetValue(clientId, out var client)) return;

            Place(client, (int)clientId);
        }

        private void OnClientDisconnect(ulong clientId)
        {
            // Any paper that connection was carrying becomes takeable again. Done before the session
            // is unbound so the client id still means something.
            //
            // The solvent bottle goes back the same way, and it has to go back here rather than
            // through Sessions.ItemReleased: HeldItem has no bottle kind, so SessionActor cannot
            // describe one, and a bottle left marked held by a dead connection is a bottle nobody can
            // ever pick up — half the lab's flushing capacity, gone for the rest of the run.
            if (Lab != null)
            {
                Lab.Slips.ReleaseAllHeldBy(clientId);
                Lab.Solvent.ReleaseAllHeldBy(clientId);
                PublishAll();
            }

            actors.Remove(clientId);

            // Before anything owned by that client despawns: the release handler needs the world
            // intact to put a carried vial back on the rack.
            Sessions.Disconnect(clientId, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>
        /// Put a dropped player's vial back where anyone can reach it.
        /// <para>
        /// Not a courtesy. <c>SampleLocation.Held</c> records <i>which</i> connection is holding a
        /// sample, and <see cref="LabCommandExecutor"/> refuses a vial somebody else is carrying — so
        /// a sample left marked held by a connection that no longer exists is a sample nobody can ever
        /// pick up again. <c>PlayerSession</c> argues at length for the rack over a reservation or a
        /// physics drop; this is that decision, executed.
        /// </para>
        /// The slot is left unset because the rack chooses its own, and the local prop is not moved:
        /// vials are local-only props (§3.2) and their positions do not replicate yet, so moving one
        /// here would only ever be right on the host.
        /// </summary>
        private void OnItemReleased(PlayerSession session, HeldItem item)
        {
            if (item.IsSample && Lab != null && Lab.Samples.TryGet(item.Sample, out var sample))
            {
                SampleLifecycle.TryMove(sample, SampleLocation.OnSurface(SampleRack.DefaultRackId, -1),
                                        out _);
                PublishAll();
            }

            ItemReleased?.Invoke(session, item);
        }

        // -- Requests --------------------------------------------------------------------------------

        /// <summary>Actors are per connection, so a reused client id can never inherit old hands.</summary>
        private readonly Dictionary<ulong, SessionActor> actors = new();

        /// <summary>Callbacks waiting on an answer, on a client. Empty on the host.</summary>
        private readonly Dictionary<ushort, Action<LabCommandResult>> awaiting = new();

        private ushort nextSequence = 1;

        /// <summary>
        /// The seam, wired up: <see cref="LabCommands.Send"/> ends here while a session is live.
        /// <para>
        /// The server branch is not an optimisation, it is the definition — the host <i>is</i> the
        /// authority, so its own player's request is answered by calling the executor, exactly as a
        /// client's is once it arrives. The only difference between the two paths is how long the
        /// answer takes.
        /// </para>
        /// </summary>
        private void Route(ILabActor actor, LabCommand command, Action<LabCommandResult> answered)
        {
            if (IsServer)
            {
                answered?.Invoke(Apply(actor, command));
                return;
            }

            // Zero means "no answer wanted", which keeps a fire-and-forget request from consuming a
            // sequence number the host then has to reply to.
            ushort sequence = 0;
            if (answered != null)
            {
                sequence = nextSequence++;
                if (nextSequence == 0) nextSequence = 1;
                awaiting[sequence] = answered;
            }

            SubmitCommandRpc(LabCommandMessage.From(command), sequence);
        }

        /// <summary>
        /// Run a request and republish if it changed anything.
        /// <para>
        /// The republish is deliberately unconditional on acceptance rather than left to the 4 Hz
        /// tick: a refusal changes nothing, and an acceptance is the one moment a client is watching
        /// for. Waiting up to a quarter of a second to see your own action land is what makes a
        /// co-op UI feel broken.
        /// </para>
        /// </summary>
        private LabCommandResult Apply(ILabActor actor, LabCommand command)
        {
            var result = LabCommands.ExecuteHere(actor, command);
            if (result.Accepted) PublishAll();
            return result;
        }

        /// <summary>
        /// The one door a client's actions come through.
        /// <para>
        /// Deliberately the <i>only</i> one. An RPC per action would be twenty entry points, each of
        /// them a place where the sender could be trusted by accident; with one, the actor is
        /// resolved from the connection every time and there is no signature that could carry
        /// anything else. The message is treated as hostile throughout — see
        /// <see cref="LabCommandMessage.ToCommand"/> — and a sender with no seat in the roster is
        /// refused rather than given a default one.
        /// </para>
        /// </summary>
        [Rpc(SendTo.Server)]
        private void SubmitCommandRpc(LabCommandMessage message, ushort sequence, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            var actor = ActorFor(sender);

            var result = actor != null
                ? Apply(actor, message.ToCommand())
                : LabCommandResult.No("You have no seat in this lab.");

            if (sequence == 0) return;

            // To the one client that asked, and to nobody else. A refusal is a fact about somebody's
            // hands and where they are standing; broadcasting it would put another player's failed
            // click on everyone's screen.
            AnswerCommandRpc(sequence, result.Accepted, Fixed512(result.Refusal), result.Sample.Value,
                             RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AnswerCommandRpc(ushort sequence, bool accepted, FixedString512Bytes refusal,
                                      int sample, RpcParams rpc = default)
        {
            if (!awaiting.TryGetValue(sequence, out var answered)) return;
            awaiting.Remove(sequence);

            answered(accepted
                ? LabCommandResult.Yes(new SampleId(sample))
                : LabCommandResult.No(refusal.ToString()));
        }

        /// <summary>
        /// The actor for a live connection, or null if that client has no session. Built on demand and
        /// cached, because the grip it holds is the host's record of that player's hands and has to
        /// survive between requests.
        /// </summary>
        private SessionActor ActorFor(ulong clientId)
        {
            if (actors.TryGetValue(clientId, out var actor) && actor.ClientId == clientId) return actor;
            if (!Sessions.TryGet(clientId, out var session)) return null;

            actor = new SessionActor(session);
            actors[clientId] = actor;
            return actor;
        }

        /// <summary>
        /// Truncating rather than throwing, for the same reason <c>ViewText</c> gives: a refusal is a
        /// sentence to read, and a clipped one still tells the player what went wrong. 512 bytes
        /// against a longest refusal of about 200 characters — the §5.3 "nothing left to check it
        /// with" one — with room for the multi-byte punctuation these are written in.
        /// </summary>
        private static FixedString512Bytes Fixed512(string value)
        {
            var packed = new FixedString512Bytes();
            if (!string.IsNullOrEmpty(value)) packed.CopyFromTruncated(value);
            return packed;
        }

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
            economy.Value = EconomyView.From(Lab.Economy, Lab.Tuning);

            Sync(samples);
            Sync(machines);
            Sync(vials);

            GatherBottles();
            Sync(solventBottles, bottleRows);

            GatherResults();
            Sync(results, resultRows);
            Sync(readings, readingRows);
        }

        // -- Solvent bottles -------------------------------------------------------------------------

        private readonly List<SolventBottleView> bottleRows = new();

        /// <summary>
        /// Rebuild the bottle rows. Fixed length for the whole run — a bottle is refilled rather than
        /// spent, so unlike <see cref="Sync(NetworkList{VialView})"/> there is no "stopped appearing"
        /// case to express and nothing is ever dropped.
        /// </summary>
        private void GatherBottles()
        {
            bottleRows.Clear();

            var store = Lab.Solvent;
            if (store == null) return;

            var all = store.All;
            for (int i = 0; i < all.Count; i++) bottleRows.Add(SolventBottleView.From(all[i]));
        }

        // -- Results ---------------------------------------------------------------------------------
        //
        // Two lists rebuilt in one pass. Every row is self-describing: a reading names the run it came
        // off by key, so the lists cannot be read against each other wrongly — a key that has not
        // arrived draws nothing, where an offset into a neighbouring list would draw the wrong
        // numbers under the right heading.

        private readonly List<ResultView> resultRows = new();
        private readonly List<ReadingView> readingRows = new();

        /// <summary>
        /// Where a run sits in <see cref="resultRows"/> this pass, so a result reached twice — filed
        /// against a record <i>and</i> still the last thing an instrument produced — is one row that
        /// knows both facts rather than two rows that each know half.
        /// </summary>
        private readonly Dictionary<TestResult, int> rowOf = new();

        /// <summary>
        /// Identity for a run, assigned once and never reused. Keyed on the object rather than
        /// derived from its position, because a position moves the day a sample is resolved and every
        /// reading behind it would move with it.
        /// </summary>
        private readonly Dictionary<TestResult, int> resultKeys = new();

        /// <summary>
        /// The same, for certificates. A <see cref="CalibrationCheck"/> outlives the readout it was
        /// built from — <see cref="MachineInstance.LastResult"/> is overwritten by the next run of any
        /// kind — so the certificate is what carries a standard's numbers to a client, and it needs an
        /// identity of its own.
        /// </summary>
        private readonly Dictionary<CalibrationCheck, int> checkKeys = new();

        private int nextResultKey;

        /// <summary>
        /// Rebuild the result rows and the readings under them.
        /// <para>
        /// Filed results first, in record order, because those are append-only and therefore stable at
        /// the tail; the instruments' own last readings follow, and those churn. A resolved record is
        /// closed and nothing on any screen draws its numbers, so its results stop travelling — which
        /// is what keeps a twenty-day contract from carrying every reading it ever took.
        /// </para>
        /// </summary>
        private void GatherResults()
        {
            resultRows.Clear();
            readingRows.Clear();
            rowOf.Clear();

            foreach (var state in Lab.Samples.All)
            {
                if (state.Stage == SampleStage.Resolved) continue;

                foreach (var result in state.Results) Emit(result, state.Id, filed: true, null);
            }

            foreach (var machine in Lab.Machines)
            {
                // A reference readout travels as its certificate instead: LastResult is transient and
                // LastCheck is what the terminal still has to be able to print tomorrow.
                if (machine.LastResult != null && !machine.LastResult.IsReference)
                    Emit(machine.LastResult, machine.LoadedSample, filed: false, machine.InstanceId);

                Emit(machine.LastBlank, SampleId.None, filed: false, machine.InstanceId);
                EmitCertificate(machine);
            }
        }

        /// <summary>
        /// Add a run and its numbers, or fold what is now known into the row it already has.
        /// <para>
        /// <paramref name="filed"/> is the §5.1 distinction and not bookkeeping: an instrument
        /// finishing a run puts nothing on a record, so an unfiled row is a slip somebody still has to
        /// carry to the desk. The terminal draws the filed ones; the instrument's screen draws its own.
        /// </para>
        /// </summary>
        private void Emit(TestResult result, SampleId sample, bool filed, string instanceId)
        {
            if (result == null) return;

            if (rowOf.TryGetValue(result, out int at))
            {
                var known = resultRows[at];
                if (filed) known.Filed = true;
                if (sample.IsValid) known.Sample = sample.Value;
                if (!string.IsNullOrEmpty(instanceId)) known.MachineInstanceId = ViewText.Fixed32(instanceId);
                resultRows[at] = known;
                return;
            }

            int key = KeyFor(result);
            rowOf[result] = resultRows.Count;
            resultRows.Add(ResultView.From(result, key, sample, filed, instanceId));

            foreach (var kv in result.Values) readingRows.Add(new ReadingView(key, kv.Key, kv.Value));
        }

        /// <summary>
        /// Publish the certificate on file as the reference run it was read from. The client rebuilds
        /// the per-element lines with <see cref="CalibrationCheck.From"/> against the standard its own
        /// content tables blend, so the certified column comes from the manual on both sides and the
        /// average error cannot come apart between two screens.
        /// </summary>
        private void EmitCertificate(MachineInstance machine)
        {
            var check = machine.LastCheck;
            if (check == null) return;

            int key = KeyFor(check);

            resultRows.Add(new ResultView
            {
                Key = key,
                Sample = 0,
                Filed = false,
                MachineDefId = ViewText.Fixed32(check.MachineId),
                MachineInstanceId = ViewText.Fixed32(machine.InstanceId),
                DayRun = check.Day,
                IsReference = true
            });

            foreach (var line in check.Lines)
            {
                if (line.Element == null) continue;
                readingRows.Add(new ReadingView(key, line.Element.Id, line.Measured));
            }
        }

        private int KeyFor(TestResult result)
        {
            if (resultKeys.TryGetValue(result, out int key)) return key;

            key = ++nextResultKey;
            resultKeys[result] = key;
            return key;
        }

        private int KeyFor(CalibrationCheck check)
        {
            if (checkKeys.TryGetValue(check, out int key)) return key;

            key = ++nextResultKey;
            checkKeys[check] = key;
            return key;
        }

        /// <summary>
        /// Publish the bottles. Consumed ones are dropped rather than sent with a tombstone: a client
        /// reconciles by matching ids, so a vial that stops appearing is a vial to destroy, and one
        /// list is easier to reason about than a list plus a rule about dead entries.
        /// </summary>
        private void Sync(NetworkList<VialView> list)
        {
            int i = 0;

            foreach (var state in Lab.Samples.All)
            {
                if (state.Location.Kind == SampleLocationKind.Consumed) continue;

                var view = VialView.From(state);
                if (i < list.Count)
                {
                    if (!list[i].Equals(view)) list[i] = view;
                }
                else list.Add(view);
                i++;
            }

            for (int extra = list.Count - 1; extra >= i; extra--) list.RemoveAt(extra);
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

        /// <summary>
        /// Push a rebuilt buffer into its list, in place. Same whole-snapshot rule as the projections
        /// above and the same reason: NGO suppresses a write that does not change a value, so the
        /// wire cost is the rows that actually moved, and there is no incremental invalidation to get
        /// wrong.
        /// </summary>
        private static void Sync<T>(NetworkList<T> list, List<T> rows)
            where T : unmanaged, IEquatable<T>
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < list.Count)
                {
                    if (!list[i].Equals(rows[i])) list[i] = rows[i];
                }
                else list.Add(rows[i]);
            }

            for (int extra = list.Count - 1; extra >= rows.Count; extra--) list.RemoveAt(extra);
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
