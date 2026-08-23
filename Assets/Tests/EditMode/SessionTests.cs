using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Gameplay.World;
using Residue.Net.Session;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the §M4 rejoin decision (#17). Every test here protects a promise about <i>who a player
    /// is</i> rather than about the chemistry: that a dropped connection does not cost you your run,
    /// that it does not hand your hands to a stranger, and that the vial you were carrying when the
    /// router died does not take the lab down with it.
    /// <para>
    /// <see cref="SessionRegistry"/> is a plain class on purpose, so all of this runs with no
    /// transport, no frame loop and no clock but the one the test passes in.
    /// </para>
    /// </summary>
    public sealed class SessionTests
    {
        private const string Alice = "auth-alice-0001";
        private const string Bob = "auth-bob-0002";
        private const string Carol = "auth-carol-0003";
        private const string Dave = "auth-dave-0004";
        private const string Erin = "auth-erin-0005";

        private static readonly SampleId Vial = new(41);

        private SessionRegistry registry;

        [SetUp]
        public void SetUp() => registry = new SessionRegistry();

        // -----------------------------------------------------------------------------------------
        // Rejoin. The whole reason the type exists.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The promise §M4 makes: drop out at minute 80 of a 100-minute run and you come back to the
        /// same player, not a new one. Pose and part-finished work survive the gap.
        /// </summary>
        [Test]
        public void Rejoin_RestoresTheSameSession()
        {
            var first = registry.Join(Alice, clientId: 3, nowSeconds: 0d);
            Assert.AreEqual(JoinOutcome.Created, first.Outcome);

            first.Session.Pose = PlayerPose.At(new Vector3(4f, 0f, -2f), yaw: 90f, pitch: -12f);
            first.Session.PendingAction = new HeldAction("flush", "ftir-0", 12f, 20f);

            registry.Disconnect(3, nowSeconds: 10d);

            // NGO hands the reconnecting client a completely different number. That must not matter.
            var again = registry.Join(Alice, clientId: 7, nowSeconds: 30d);

            Assert.AreEqual(JoinOutcome.Restored, again.Outcome, "a known identity is a rejoin");
            Assert.IsTrue(again.IsRejoin);
            Assert.AreSame(first.Session, again.Session, "rejoin must restore the same record");
            Assert.AreEqual(7ul, again.Session.ClientId);
            Assert.AreEqual(2, again.Session.JoinCount);

            Assert.IsTrue(again.Session.Pose.HasValue);
            Assert.AreEqual(new Vector3(4f, 0f, -2f), again.Session.Pose.Position);
            Assert.AreEqual(90f, again.Session.Pose.Yaw, 0.001f);

            Assert.AreEqual(12f, again.Session.PendingAction.ElapsedSeconds, 0.001f,
                "twelve seconds into a twenty-second flush is most of the cost already paid");
        }

        /// <summary>
        /// <b>The bug the stable id exists to prevent.</b> NGO allocates client ids per connection and
        /// reuses them, so the number that meant Alice five minutes ago can mean Bob now. If the
        /// registry keyed on it, Bob would connect straight into Alice's session — her pose, her
        /// hands, her half-finished flush — and Alice would never get her run back.
        /// </summary>
        [Test]
        public void AReusedClientId_DoesNotResurrectTheWrongPlayersSession()
        {
            var alice = registry.Join(Alice, clientId: 2, nowSeconds: 0d).Session;
            alice.Pose = PlayerPose.At(new Vector3(9f, 0f, 9f), 45f, 0f);
            alice.Held = HeldItem.Vial(Vial);

            registry.Disconnect(2, nowSeconds: 5d);

            // Bob connects and the transport reissues the number Alice had.
            var bob = registry.Join(Bob, clientId: 2, nowSeconds: 6d);

            Assert.AreEqual(JoinOutcome.Created, bob.Outcome, "Bob has never been here before");
            Assert.AreNotSame(alice, bob.Session);
            Assert.IsFalse(bob.Session.Pose.HasValue, "Bob must not inherit Alice's position");
            Assert.IsTrue(bob.Session.Held.IsEmpty, "Bob must not inherit Alice's hands");

            Assert.IsTrue(registry.TryGet(2, out var onTwo));
            Assert.AreSame(bob.Session, onTwo, "client 2 now means Bob and only Bob");

            // Alice's record is untouched and still waiting for her.
            Assert.IsTrue(registry.TryGetByStableId(Alice, out var stillAlice));
            Assert.AreSame(alice, stillAlice);
            Assert.IsFalse(stillAlice.IsConnected);
            Assert.AreEqual(new Vector3(9f, 0f, 9f), stillAlice.Pose.Position);

            // And she still gets it back when she reconnects on a third number.
            var back = registry.Join(Alice, clientId: 5, nowSeconds: 40d);
            Assert.AreEqual(JoinOutcome.Restored, back.Outcome);
            Assert.AreSame(alice, back.Session);

            Assert.IsTrue(registry.TryGet(2, out var stillBob));
            Assert.AreSame(bob.Session, stillBob, "Alice's return must not disturb client 2");
        }

        /// <summary>
        /// Two identities are two players, always. A collision here would give two people one pair of
        /// hands and one position, which reads in game as teleporting and vials vanishing.
        /// </summary>
        [Test]
        public void TwoIdentities_NeverShareASession()
        {
            var a = registry.Join(Alice, 0, 0d).Session;
            var b = registry.Join(Bob, 1, 0d).Session;

            Assert.AreNotSame(a, b);
            Assert.AreEqual(2, registry.SeatsTaken);

            a.Held = HeldItem.Vial(Vial);
            Assert.IsTrue(b.Held.IsEmpty, "one player's hands are not the other's");

            var seen = new HashSet<PlayerSession>();
            foreach (var s in registry.All) Assert.IsTrue(seen.Add(s), "a session was listed twice");
            Assert.AreEqual(2, seen.Count);
        }

        /// <summary>
        /// A player who never drops must not be able to tell that anyone else did. Rejoin machinery
        /// that quietly rebinds or re-poses the bystanders is worse than no rejoin at all.
        /// </summary>
        [Test]
        public void APlayerWhoNeverDisconnects_IsUnaffectedByEveryoneElsesChurn()
        {
            var steady = registry.Join(Alice, clientId: 1, nowSeconds: 0d).Session;
            steady.Pose = PlayerPose.At(new Vector3(1f, 0f, 1f), 10f, 5f);
            steady.Held = HeldItem.Printout(Vial, "ftir-0");

            registry.Join(Bob, clientId: 2, nowSeconds: 1d);
            registry.Disconnect(2, nowSeconds: 2d);
            registry.Join(Carol, clientId: 2, nowSeconds: 3d);   // reused number
            registry.Join(Bob, clientId: 4, nowSeconds: 4d);     // Bob returns elsewhere
            registry.Disconnect(2, nowSeconds: 5d);              // Carol drops

            Assert.IsTrue(steady.IsConnected);
            Assert.AreEqual(1ul, steady.ClientId);
            Assert.AreEqual(1, steady.JoinCount, "a bystander is never re-joined");
            Assert.AreEqual(HeldItem.Printout(Vial, "ftir-0"), steady.Held);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), steady.Pose.Position);
            Assert.IsTrue(steady.ReleasedOnDisconnect.IsEmpty, "nothing left this player's hands");

            Assert.IsTrue(registry.TryGet(1, out var same));
            Assert.AreSame(steady, same);
        }

        // -----------------------------------------------------------------------------------------
        // The carried vial. A sample nobody may touch is a softlock; see PlayerSession's type doc.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The rule: dropping empties the hands immediately and announces the item so the host can
        /// put it back in the rack. Nothing stays reserved to an absent player, because with one pair
        /// of hands each and instruments that block, a reserved vial is work the remaining players
        /// can neither do nor clear.
        /// </summary>
        [Test]
        public void DisconnectingWithAVial_ReleasesItInsteadOfReservingIt()
        {
            var session = registry.Join(Alice, 1, 0d).Session;
            session.Held = HeldItem.Vial(Vial);

            HeldItem announced = HeldItem.None;
            PlayerSession announcedFor = null;
            registry.ItemReleased += (s, item) => { announcedFor = s; announced = item; };

            registry.Disconnect(1, nowSeconds: 12d);

            Assert.AreSame(session, announcedFor);
            Assert.AreEqual(HeldItem.Vial(Vial), announced, "the host is told exactly what to put back");
            Assert.IsTrue(announced.IsSample);
            Assert.IsTrue(session.Held.IsEmpty, "an absent player holds nothing");
            Assert.AreEqual(HeldItem.Vial(Vial), session.ReleasedOnDisconnect);
        }

        /// <summary>
        /// On return the player is <i>told</i> where their vial went, never handed it. In the gap
        /// another player may have taken it, run it or filed on it, and re-materialising it would
        /// either duplicate the sample or snatch it out of a colleague's hand mid-task.
        /// </summary>
        [Test]
        public void RejoiningPlayerIsToldWhereTheirVialWent_NotHandedItBack()
        {
            var session = registry.Join(Alice, 1, 0d).Session;
            session.Held = HeldItem.Vial(Vial);
            registry.Disconnect(1, 5d);

            var back = registry.Join(Alice, 6, 60d);

            Assert.AreEqual(JoinOutcome.Restored, back.Outcome);
            Assert.IsTrue(back.Session.Held.IsEmpty, "hands come back empty");
            Assert.AreEqual(HeldItem.Vial(Vial), back.Session.ReleasedOnDisconnect,
                "but the note survives, so the player can be told where to look");

            back.Session.AcknowledgeRelease();
            Assert.IsTrue(back.Session.ReleasedOnDisconnect.IsEmpty,
                "the note is shown once, not on every subsequent reconnect");
        }

        /// <summary>
        /// The same rule for paper. One sentence to remember rather than three — and a slip left in a
        /// tray is exactly the loss §5.1 already tolerates, so nothing new is being punished.
        /// </summary>
        [Test]
        public void PrintoutsAndManuals_AreReleasedByTheSameRule()
        {
            var withSlip = registry.Join(Alice, 1, 0d).Session;
            withSlip.Held = HeldItem.Printout(Vial, "ftir-0");

            var withBook = registry.Join(Bob, 2, 0d).Session;
            withBook.Held = HeldItem.ReferenceBook(BookKind.DiagnosticGuide);

            var released = new List<HeldItem>();
            registry.ItemReleased += (_, item) => released.Add(item);

            registry.Disconnect(1, 1d);
            registry.Disconnect(2, 1d);

            CollectionAssert.AreEqual(
                new[] { HeldItem.Printout(Vial, "ftir-0"), HeldItem.ReferenceBook(BookKind.DiagnosticGuide) },
                released);

            Assert.IsFalse(released[0].IsSample, "a slip is not oil");
            Assert.IsFalse(released[1].IsSample);
        }

        /// <summary>
        /// Empty hands release nothing. Otherwise the host would be asked to put a nonexistent vial
        /// back on the rack every time anyone quit, and the rack would fill with ghosts.
        /// </summary>
        [Test]
        public void DisconnectingEmptyHanded_AnnouncesNothing()
        {
            registry.Join(Alice, 1, 0d);

            int announcements = 0;
            registry.ItemReleased += (_, __) => announcements++;

            var session = registry.Disconnect(1, 1d);

            Assert.IsNotNull(session);
            Assert.AreEqual(0, announcements);
            Assert.IsTrue(session.ReleasedOnDisconnect.IsEmpty);
        }

        /// <summary>
        /// Leaving for good must not strand a vial either. Quitting deliberately is the same physical
        /// situation as dropping out; only the seat differs.
        /// </summary>
        [Test]
        public void ForgettingAPlayer_StillReleasesWhatTheyWereHolding()
        {
            var session = registry.Join(Alice, 1, 0d).Session;
            session.Held = HeldItem.Vial(Vial);

            HeldItem announced = HeldItem.None;
            registry.ItemReleased += (_, item) => announced = item;

            Assert.IsTrue(registry.Forget(Alice));

            Assert.AreEqual(HeldItem.Vial(Vial), announced);
            Assert.AreEqual(0, registry.SeatsTaken);
            Assert.IsFalse(registry.TryGetByStableId(Alice, out _));
            Assert.IsFalse(registry.TryGet(1, out _));
        }

        // -----------------------------------------------------------------------------------------
        // Seats. An absent player's seat is theirs until it is given up.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Rejoin is worthless if a stranger can take the seat while you reconnect. A dropped player
        /// keeps theirs; the fifth identity is refused whether or not anyone is currently absent.
        /// </summary>
        [Test]
        public void AnAbsentPlayerKeepsTheirSeat_AndAStrangerIsRefused()
        {
            registry.Join(Alice, 1, 0d);
            registry.Join(Bob, 2, 0d);
            registry.Join(Carol, 3, 0d);
            registry.Join(Dave, 4, 0d);

            registry.Disconnect(4, nowSeconds: 10d);
            Assert.AreEqual(3, registry.ConnectedCount);
            Assert.AreEqual(4, registry.SeatsTaken, "an absent player still occupies a seat");

            var stranger = registry.Join(Erin, 4, nowSeconds: 11d);
            Assert.AreEqual(JoinOutcome.RejectedLabFull, stranger.Outcome);
            Assert.IsFalse(stranger.Accepted);
            Assert.IsNotNull(stranger.RefusalReason);

            var dave = registry.Join(Dave, clientId: 9, nowSeconds: 20d);
            Assert.AreEqual(JoinOutcome.Restored, dave.Outcome, "the seat was being held for Dave");

            // The seat only opens when it is deliberately given up.
            Assert.IsTrue(registry.Forget(Dave));
            Assert.AreEqual(JoinOutcome.Created, registry.Join(Erin, 4, 30d).Outcome);
        }

        /// <summary>
        /// One identity, one body. A duplicate sign-in — a second window, or a drop the transport has
        /// not noticed — must move the session to the live connection and name the stale one for the
        /// host to kick, or the lab ends up with two Alices.
        /// </summary>
        [Test]
        public void ASecondConnectionForTheSameIdentity_DisplacesTheFirst()
        {
            var first = registry.Join(Alice, clientId: 1, nowSeconds: 0d).Session;
            first.Held = HeldItem.Vial(Vial);

            var second = registry.Join(Alice, clientId: 8, nowSeconds: 1d);

            Assert.AreEqual(JoinOutcome.Displaced, second.Outcome);
            Assert.AreSame(first, second.Session, "same player, so the same record moves across");
            Assert.AreEqual(1ul, second.DisplacedClientId, "the host must disconnect the stale client");
            Assert.AreEqual(8ul, first.ClientId);

            Assert.IsFalse(registry.TryGet(1, out _), "the stale client id no longer resolves");
            Assert.IsTrue(registry.TryGet(8, out var live));
            Assert.AreSame(first, live);

            Assert.AreEqual(HeldItem.Vial(Vial), first.Held,
                "same hands, so nothing is dropped on the floor for a duplicate sign-in");
            Assert.AreEqual(1, registry.SeatsTaken);
            Assert.AreEqual(1, registry.ConnectedCount);
        }

        /// <summary>
        /// A blank identity is refused rather than defaulted. Keying on "" would put every client
        /// whose sign-in failed into a single shared session — the exact collision the stable id is
        /// there to avoid, arrived at from the other direction.
        /// </summary>
        [Test]
        public void AnEmptyIdentity_IsRefused()
        {
            foreach (string bad in new[] { null, "", "   " })
            {
                var result = registry.Join(bad, 1, 0d);
                Assert.AreEqual(JoinOutcome.RejectedNoIdentity, result.Outcome, $"'{bad}'");
                Assert.IsFalse(result.Accepted);
                Assert.IsNull(result.Session);
            }

            Assert.AreEqual(0, registry.SeatsTaken);
        }

        /// <summary>
        /// NGO fires a disconnect for connections that were never approved. That must be a no-op
        /// rather than an exception on the host's callback thread.
        /// </summary>
        [Test]
        public void DisconnectingAnUnknownClient_IsANoOp()
        {
            var session = registry.Join(Alice, 1, 0d).Session;

            Assert.IsNull(registry.Disconnect(99, 1d), "a client id that was never bound");
            Assert.AreEqual(1, registry.ConnectedCount, "and nobody else is disturbed by it");

            Assert.AreSame(session, registry.Disconnect(1, 1d), "the real disconnect still works");
            Assert.IsNull(registry.Disconnect(1, 2d), "and does not fire a second time");
        }

        /// <summary>
        /// <see cref="SessionRegistry.Connected"/> is what anything that sends iterates. An absent
        /// session has no client id to send to, so listing one there is a null reference waiting for
        /// the first RPC.
        /// </summary>
        [Test]
        public void ConnectedListsOnlyLiveConnections()
        {
            var a = registry.Join(Alice, 1, 0d).Session;
            var b = registry.Join(Bob, 2, 0d).Session;
            registry.Disconnect(2, 3d);

            CollectionAssert.AreEquivalent(new[] { a }, new List<PlayerSession>(registry.Connected));
            CollectionAssert.AreEquivalent(new[] { a, b }, new List<PlayerSession>(registry.All));
            CollectionAssert.AreEquivalent(new[] { b }, registry.Absent());

            Assert.AreEqual(0d, a.AbsentSeconds(10d), "a connected player is never absent");
            Assert.AreEqual(7d, b.AbsentSeconds(10d), 0.0001d);
        }

        // -----------------------------------------------------------------------------------------
        // Part-finished holds.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The seconds survive the drop, but they only count for the same job on the same instrument.
        /// Handing twelve seconds of flush back to someone who has walked to a different machine
        /// would give away a flush that never happened — hard rule 3's unfairness in reverse.
        /// </summary>
        [Test]
        public void APartFinishedHold_SurvivesADropButOnlyMatchesItsOwnTarget()
        {
            var session = registry.Join(Alice, 1, 0d).Session;
            session.PendingAction = new HeldAction("flush", "ftir-0", 12f, 20f);

            registry.Disconnect(1, 5d);
            var back = registry.Join(Alice, 2, 30d).Session;

            Assert.AreEqual(0.6f, back.PendingAction.Progress01, 0.001f);
            Assert.IsTrue(back.PendingAction.Matches("flush", "ftir-0"));
            Assert.IsFalse(back.PendingAction.Matches("flush", "ftir-1"), "different instrument");
            Assert.IsFalse(back.PendingAction.Matches("calibrate", "ftir-0"), "different job");
            Assert.IsFalse(HeldAction.None.Matches("flush", "ftir-0"), "nothing matches nothing");
        }

        // -----------------------------------------------------------------------------------------
        // Identity. The local fallback is what actually runs until a cloud project is linked.
        // -----------------------------------------------------------------------------------------

        private readonly List<string> tempFiles = new();

        [TearDown]
        public void TearDown()
        {
            foreach (string path in tempFiles)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
            }
            tempFiles.Clear();
        }

        private string TempIdPath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"residue-playerid-{Guid.NewGuid():N}.txt");
            tempFiles.Add(path);
            return path;
        }

        /// <summary>
        /// The override sources exist for running two instances on one machine, and they would make
        /// the persistence tests below lie about which source answered.
        /// </summary>
        private static void SkipIfIdentityIsOverridden()
        {
            string env = null;
            try { env = Environment.GetEnvironmentVariable(LocalPlayerIdentity.EnvironmentVariable); }
            catch { /* sandboxed; treat as unset */ }

            if (!string.IsNullOrEmpty(env))
                Assert.Ignore($"{LocalPlayerIdentity.EnvironmentVariable} is set, which overrides the file.");
        }

        /// <summary>
        /// Rejoin across a restart is only possible if the fallback id is the same id next launch.
        /// A GUID minted per process would make every reconnect look like a brand-new player and quietly
        /// eat a seat each time.
        /// </summary>
        [Test]
        public void LocalIdentity_PersistsTheSameIdAcrossInstances()
        {
            SkipIfIdentityIsOverridden();

            string path = TempIdPath();

            string first = new LocalPlayerIdentity(path).Resolve();
            Assert.IsFalse(string.IsNullOrEmpty(first));
            Assert.IsTrue(File.Exists(path), "the id has to reach disk to survive a restart");

            string second = new LocalPlayerIdentity(path).Resolve();
            Assert.AreEqual(first, second, "the same install is the same player");
        }

        /// <summary>
        /// Two separate stores are two separate players. This is the property that makes the fallback
        /// usable for the case it exists for — two clients, one machine — and without it the second
        /// window would be treated as the first one reconnecting.
        /// </summary>
        [Test]
        public void LocalIdentity_MintsADistinctIdPerStore()
        {
            SkipIfIdentityIsOverridden();

            string a = new LocalPlayerIdentity(TempIdPath()).Resolve();
            string b = new LocalPlayerIdentity(TempIdPath()).Resolve();

            Assert.AreNotEqual(a, b);

            var registry2 = new SessionRegistry();
            Assert.AreEqual(JoinOutcome.Created, registry2.Join(a, 0, 0d).Outcome);
            Assert.AreEqual(JoinOutcome.Created, registry2.Join(b, 1, 0d).Outcome,
                "two local instances must be two players, not one displacing the other");
        }

        /// <summary>
        /// <see cref="IPlayerIdentity.IsReady"/> is what the connect flow gates on; a resolver that
        /// reports ready before it has an id would let a blank session key through.
        /// </summary>
        [Test]
        public void LocalIdentity_IsNotReadyUntilResolved()
        {
            SkipIfIdentityIsOverridden();

            var identity = new LocalPlayerIdentity(TempIdPath());
            Assert.IsFalse(identity.IsReady);
            Assert.IsNull(identity.StableId);

            identity.Resolve();

            Assert.IsTrue(identity.IsReady);
            Assert.IsFalse(string.IsNullOrEmpty(identity.StableId));
            string once = identity.StableId;
            Assert.AreEqual(once, identity.Resolve(), "resolving twice does not re-mint");
        }
    }
}
