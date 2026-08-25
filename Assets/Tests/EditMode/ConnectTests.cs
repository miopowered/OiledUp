using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Residue.Net.Connect;
using Residue.Net.Session;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The parts of the M4 connect flow that can be checked without a live Unity Gaming Services
    /// project.
    /// <para>
    /// <b>What is deliberately not here.</b> A Relay allocation, a Lobby create/join, an NGO
    /// handshake and the <c>SimulatesLocally</c> ordering around a real scene load all need a
    /// signed-in cloud project and two running processes. Faking them would test the fake. Those are
    /// verified by hand, two instances on one machine with distinct <c>-playerId</c> values — see
    /// docs/MULTIPLAYER.md.
    /// </para>
    /// What is left is exactly the part that fails silently and would never be noticed: a join code
    /// that was typed correctly and rejected anyway, a heartbeat that stops beating, and a button
    /// that stays live during an in-flight allocation.
    /// </summary>
    public sealed class ConnectTests
    {
        // -----------------------------------------------------------------------------------------
        // Join codes. The whole co-op UX is six characters read out loud.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// A code read out over voice gets typed lowercase by roughly half of everyone. Rejecting it
        /// would be the game blaming the player for the game's own formatting.
        /// </summary>
        [Test]
        public void JoinCode_AcceptsWhatSomeoneTypedAfterHearingIt()
        {
            Assert.AreEqual("ABC123", JoinCode.Normalise("abc123"));
            Assert.AreEqual("ABC123", JoinCode.Normalise("AbC123"));
            Assert.IsTrue(JoinCode.IsWellFormed(JoinCode.Normalise("abc123")));
        }

        /// <summary>
        /// Pasted codes arrive wrapped in whatever the chat window did to them: a trailing newline,
        /// a leading space, or hyphens the sender added to make it readable.
        /// </summary>
        [Test]
        public void JoinCode_SurvivesBeingPasted()
        {
            Assert.AreEqual("ABC123", JoinCode.Normalise("  ABC123\r\n"));
            Assert.AreEqual("ABC123", JoinCode.Normalise("ABC-123"));
            Assert.AreEqual("ABC123", JoinCode.Normalise("ABC 123"));
            Assert.AreEqual("ABC123", JoinCode.Normalise("\tabc\n123 "));
        }

        /// <summary>
        /// Round trip: what the host is shown must be what the client can type. If
        /// <see cref="JoinCode.ForReading"/> and <see cref="JoinCode.Normalise"/> ever disagree, the
        /// host reads out a code that does not work and both players blame the network.
        /// </summary>
        [Test]
        public void JoinCode_WhatTheHostReadsOut_NormalisesBackToTheCode()
        {
            const string code = "K7QM2Z";
            Assert.AreEqual(code, JoinCode.Normalise(JoinCode.ForReading(code)));
            Assert.AreEqual("K7Q M2Z", JoinCode.ForReading(code));
        }

        /// <summary>
        /// A typo has to be caught locally. Sending it costs a round trip and comes back as a
        /// service error the player cannot act on.
        /// </summary>
        [Test]
        public void JoinCode_RejectsAnythingThatIsNotSixCharacters()
        {
            Assert.IsFalse(JoinCode.IsWellFormed(null));
            Assert.IsFalse(JoinCode.IsWellFormed(""));
            Assert.IsFalse(JoinCode.IsWellFormed("ABC12"));
            Assert.IsFalse(JoinCode.IsWellFormed("ABC1234"));
            Assert.IsFalse(JoinCode.IsWellFormed("abc123"), "Lowercase has not been normalised yet.");
            Assert.IsFalse(JoinCode.IsWellFormed("ABC-12"));
        }

        /// <summary>
        /// Prose is not mined for a code. "join code: ABC123" would have to guess which six
        /// characters were meant, and a guess that is believed is worse than a refusal.
        /// </summary>
        [Test]
        public void JoinCode_DoesNotTryToFindACodeInsideASentence()
        {
            Assert.IsFalse(JoinCode.IsWellFormed(JoinCode.Normalise("join code: ABC123")));
        }

        /// <summary>
        /// No character is remapped. Turning O into 0 would rescue a misheard code and silently
        /// corrupt a correctly typed one, with no way for the player to tell which happened.
        /// </summary>
        [Test]
        public void JoinCode_NeverRewritesAConfusableCharacter()
        {
            Assert.AreEqual("O0IL1S", JoinCode.Normalise("o0il1s"));
        }

        // -----------------------------------------------------------------------------------------
        // State machine. A live button during an in-flight allocation books two relays.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// HOST, JOIN and SINGLE PLAYER must all be refused while something is in flight. Each of
        /// the busy states is holding a Relay allocation, a Lobby membership or a running transport
        /// that a second command would abandon rather than unwind.
        /// </summary>
        [Test]
        public void ConnectStates_RefuseASecondCommandWhileSomethingIsInFlight()
        {
            foreach (var busy in new[]
                     {
                         ConnectState.Preparing, ConnectState.Allocating,
                         ConnectState.Resolving, ConnectState.Connecting
                     })
            {
                Assert.IsTrue(ConnectStates.IsBusy(busy), $"{busy} should be busy.");
                Assert.IsFalse(ConnectStates.AcceptsCommands(busy),
                    $"{busy} accepted a command. That is two Relay allocations and one lobby.");
            }
        }

        /// <summary>
        /// A failure is a prompt to try again, not a dead end — everything it held has already been
        /// released by the time the state is set.
        /// </summary>
        [Test]
        public void ConnectStates_AFailureCanBeRetried()
        {
            Assert.IsTrue(ConnectStates.AcceptsCommands(ConnectState.Failed));
            Assert.IsTrue(ConnectStates.AcceptsCommands(ConnectState.Idle));
        }

        /// <summary>
        /// A live session refuses new commands, and so does single player: both have already loaded
        /// the lab, and starting a second one on top of it is not a thing the menu should offer.
        /// </summary>
        [Test]
        public void ConnectStates_ALiveSessionTakesNoMoreCommands()
        {
            Assert.IsTrue(ConnectStates.IsLive(ConnectState.Hosting));
            Assert.IsTrue(ConnectStates.IsLive(ConnectState.Joined));
            Assert.IsFalse(ConnectStates.AcceptsCommands(ConnectState.Hosting));
            Assert.IsFalse(ConnectStates.AcceptsCommands(ConnectState.Joined));
            Assert.IsFalse(ConnectStates.AcceptsCommands(ConnectState.SinglePlayer));
        }

        /// <summary>
        /// Every state says something. A blank status line on a connect screen reads as a frozen
        /// game, which is the one impression this screen must never give.
        /// </summary>
        [Test]
        public void ConnectStates_EveryStateHasSomethingToSay()
        {
            foreach (ConnectState state in Enum.GetValues(typeof(ConnectState)))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(ConnectStates.Label(state)),
                    $"{state} has no status line.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // The lobby. Everything below runs with no NetworkManager at all, which is what a
        // LobbyRoom degrades to: a roster of one and a working countdown. The messaging half needs
        // two processes and a Relay allocation and is verified by hand; the clock is the half that
        // has a bug in it, and it is the half UGS cannot help us check.
        // -----------------------------------------------------------------------------------------

        private static LobbyRoom SoloRoom()
        {
            var room = new LobbyRoom();
            room.OpenAsHost("Tester", "stable-1");
            return room;
        }

        /// <summary>
        /// The countdown has to actually end, and end once. Firing twice loads the lab twice; never
        /// firing is a lobby with a number frozen on it that nobody can leave.
        /// </summary>
        [Test]
        public void Lobby_CountdownEndsExactlyOnce()
        {
            var room = SoloRoom();
            int started = 0;
            room.Starting += () => started++;

            room.Tick(0f);
            room.StartCountdown();

            Assert.IsTrue(room.IsCountingDown);
            Assert.AreEqual(LobbyRoom.CountdownSeconds, room.CountdownRemaining, 0.001f);

            room.Tick(LobbyRoom.CountdownSeconds - 0.5f);
            Assert.AreEqual(0, started, "The countdown ended half a second early.");
            Assert.AreEqual(0.5f, room.CountdownRemaining, 0.001f);

            room.Tick(LobbyRoom.CountdownSeconds);
            room.Tick(LobbyRoom.CountdownSeconds + 1f);
            room.Tick(LobbyRoom.CountdownSeconds + 2f);

            Assert.AreEqual(1, started, "Starting fired more than once; that is two scene loads.");
            Assert.IsFalse(room.IsCountingDown);
            Assert.AreEqual(0f, room.CountdownRemaining);
        }

        /// <summary>
        /// Cancelling has to be final. A countdown that resumes after a cancel starts a shift the
        /// host explicitly stopped.
        /// </summary>
        [Test]
        public void Lobby_CancellingTheCountdownStopsItForGood()
        {
            var room = SoloRoom();
            int started = 0;
            room.Starting += () => started++;

            room.Tick(0f);
            room.StartCountdown();
            room.Tick(1f);
            room.CancelCountdown();

            Assert.IsFalse(room.IsCountingDown);

            room.Tick(LobbyRoom.CountdownSeconds * 3f);

            Assert.AreEqual(0, started);
            Assert.IsFalse(room.IsCountingDown);
        }

        /// <summary>
        /// A second press while it is already running must not push the deadline out. Otherwise a
        /// host tapping the button holds everyone at four seconds indefinitely.
        /// </summary>
        [Test]
        public void Lobby_StartingACountdownTwiceDoesNotExtendIt()
        {
            var room = SoloRoom();

            room.Tick(0f);
            room.StartCountdown();
            room.Tick(2f);
            room.StartCountdown();

            Assert.AreEqual(LobbyRoom.CountdownSeconds - 2f, room.CountdownRemaining, 0.001f);
        }

        /// <summary>
        /// The host is not blocked by an unready room — see the type doc on why a lobby you cannot
        /// leave because somebody walked away from their keyboard is the worse failure. The counts
        /// exist so the button can say how many are ready instead.
        /// </summary>
        [Test]
        public void Lobby_TheHostCanStartWithNobodyReady()
        {
            var room = SoloRoom();

            Assert.IsFalse(room.LocalReady);
            Assert.IsFalse(room.EveryoneReady);
            Assert.AreEqual(0, room.ReadyCount);

            room.Tick(0f);
            room.StartCountdown();

            Assert.IsTrue(room.IsCountingDown, "An unready room refused to start.");
        }

        [Test]
        public void Lobby_ReadyTogglesAndIsCounted()
        {
            var room = SoloRoom();

            Assert.AreEqual(1, room.Seats.Count);
            Assert.IsTrue(room.Seats[0].IsHost);

            room.ToggleReady();

            Assert.IsTrue(room.LocalReady);
            Assert.AreEqual(1, room.ReadyCount);
            Assert.IsTrue(room.EveryoneReady);
            Assert.IsTrue(room.Seats[0].Ready, "The roster row did not follow the ready flag.");

            room.ToggleReady();

            Assert.IsFalse(room.LocalReady);
            Assert.AreEqual(0, room.ReadyCount);
            Assert.IsFalse(room.EveryoneReady);
        }

        /// <summary>
        /// Readying up during the countdown does not cancel it, and neither does readying down. Only
        /// somebody arriving does — they are the one person who has not seen the roster.
        /// </summary>
        [Test]
        public void Lobby_ReadyingDuringTheCountdownDoesNotStopIt()
        {
            var room = SoloRoom();

            room.Tick(0f);
            room.StartCountdown();
            room.SetReady(true);
            room.SetReady(false);

            Assert.IsTrue(room.IsCountingDown);
        }

        /// <summary>
        /// A client pressing START is ignored, not refused loudly. The same screen runs on both sides
        /// and a shared path that quietly does nothing where it has no authority is easier to keep
        /// correct than one that throws into a UI callback.
        /// </summary>
        [Test]
        public void Lobby_AClientCannotStartTheCountdown()
        {
            var room = new LobbyRoom();
            room.OpenAsClient("Guest");

            Assert.IsFalse(room.IsHost);
            Assert.IsFalse(room.IsOpen,
                "A client's room must stay shut until the host answers, or somebody joining a shift " +
                "already in progress gets a lobby flashed at them.");

            Assert.DoesNotThrow(() => room.StartCountdown());
            Assert.DoesNotThrow(() => room.CancelCountdown());
            Assert.DoesNotThrow(() => room.Tick(10f));

            Assert.IsFalse(room.IsCountingDown);
        }

        /// <summary>
        /// The stable-id map is the handover to <c>LabNetwork</c>: it seats everyone already in the
        /// room from this, and a null there is a player with no seat who is refused every action they
        /// take. It has to survive <c>Seal</c>, because that runs before <c>LabNetwork</c> spawns.
        /// </summary>
        [Test]
        public void Lobby_TheStableIdOutlivesTheLobbyItWasCollectedIn()
        {
            var room = SoloRoom();

            Assert.AreEqual("stable-1", room.StableIdOf(0));
            Assert.IsNull(room.StableIdOf(7), "An unknown client must answer null, not a placeholder.");

            room.Seal();

            Assert.IsFalse(room.IsOpen);
            Assert.AreEqual("stable-1", room.StableIdOf(0),
                "Sealing dropped the map LabNetwork reads to seat the room.");

            room.Close();

            Assert.IsNull(room.StableIdOf(0));
        }

        /// <summary>
        /// Teardown runs on every failure path there is, including ones where nothing was ever
        /// started. A close that can fail is a close that leaves half a session behind.
        /// </summary>
        [Test]
        public void Lobby_ClosingIsSafeAndIdempotent()
        {
            var room = new LobbyRoom();

            Assert.DoesNotThrow(() => room.Close());
            Assert.DoesNotThrow(() => room.Close());
            Assert.DoesNotThrow(() => room.Tick(1f));

            room.OpenAsHost("Tester", "stable-1");
            room.Close();
            room.Close();

            Assert.IsFalse(room.IsOpen);
            Assert.IsFalse(room.IsHost);
            Assert.AreEqual(0, room.Seats.Count);
        }

        /// <summary>
        /// Capacity is the lab's, not a second number that can drift from it. The lobby refuses the
        /// fifth player before the lab ever gets the chance to.
        /// </summary>
        [Test]
        public void Lobby_SeatsTheSameNumberOfPlayersTheLabDoes()
        {
            Assert.AreEqual(SessionRegistry.DefaultCapacity, SoloRoom().Capacity);
        }

        // -----------------------------------------------------------------------------------------
        // Voice. Vivox may leave an SDK task pending when the network dies mid-handshake.
        // -----------------------------------------------------------------------------------------

        [Test]
        public async Task VoiceConnect_ACompletedSdkCallDoesNotWaitForTheDeadline()
        {
            await VoiceChat.AwaitWithTimeoutAsync(Task.CompletedTask, 0.02f);
        }

        [Test]
        public void VoiceConnect_APendingSdkCallHasADeadline()
        {
            var never = new TaskCompletionSource<bool>();

            Assert.ThrowsAsync<TimeoutException>(async () =>
                await VoiceChat.AwaitWithTimeoutAsync(never.Task, 0.02f));
        }

        [Test]
        public void VoiceVolume_StaysWithinTheAudioRange()
        {
            var voice = new VoiceChat();

            voice.SetOutputVolume(-1f);
            Assert.AreEqual(0f, voice.OutputVolume);

            voice.SetOutputVolume(2f);
            Assert.AreEqual(1f, voice.OutputVolume);
        }

        // -----------------------------------------------------------------------------------------
        // Heartbeat. A lobby that stops saying it is alive is reaped in thirty seconds.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The interval must be comfortably under the service's thirty-second timeout, so that one
        /// lost ping does not cost the lobby.
        /// </summary>
        [Test]
        public void Heartbeat_BeatsFastEnoughToSurviveALostPing()
        {
            Assert.Less(LobbyHeartbeat.DefaultIntervalSeconds * 2.0, 30.0,
                "Two intervals must fit inside the Lobby service's 30 s timeout, or a single " +
                "dropped ping reaps the lobby mid-session.");
        }

        [Test]
        public void Heartbeat_DoesNotPingBeforeItIsDue()
        {
            var pinged = new List<string>();
            var beat = new LobbyHeartbeat(id => { pinged.Add(id); return Task.CompletedTask; });

            beat.Bind("lobby-1", 0.0);
            beat.Tick(1.0);
            beat.Tick(LobbyHeartbeat.DefaultIntervalSeconds - 0.01);

            Assert.AreEqual(0, pinged.Count,
                "The lobby was created moments ago; pinging it immediately spends a request on nothing.");
        }

        [Test]
        public void Heartbeat_PingsOncePerInterval()
        {
            var pinged = new List<string>();
            var beat = new LobbyHeartbeat(id => { pinged.Add(id); return Task.CompletedTask; });

            beat.Bind("lobby-1", 0.0);

            double interval = LobbyHeartbeat.DefaultIntervalSeconds;
            for (int i = 1; i <= 4; i++) beat.Tick(interval * i);

            Assert.AreEqual(4, pinged.Count);
            Assert.AreEqual(4, beat.Beats);
            CollectionAssert.AreEqual(new[] { "lobby-1", "lobby-1", "lobby-1", "lobby-1" }, pinged);
        }

        /// <summary>
        /// Releasing has to actually stop it. A heartbeat that keeps pinging a deleted lobby logs a
        /// warning every fifteen seconds for the rest of the process.
        /// </summary>
        [Test]
        public void Heartbeat_StopsWhenReleased()
        {
            var pinged = new List<string>();
            var beat = new LobbyHeartbeat(id => { pinged.Add(id); return Task.CompletedTask; });

            beat.Bind("lobby-1", 0.0);
            beat.Tick(LobbyHeartbeat.DefaultIntervalSeconds);
            beat.Release();
            beat.Tick(LobbyHeartbeat.DefaultIntervalSeconds * 5);

            Assert.AreEqual(1, pinged.Count);
            Assert.IsFalse(beat.IsBeating);
        }

        /// <summary>
        /// A failed ping is recorded and survived, never thrown. A transient service error must not
        /// take down a session that is otherwise fine.
        /// </summary>
        [Test]
        public void Heartbeat_SurvivesAFailedPing()
        {
            int calls = 0;
            var beat = new LobbyHeartbeat(_ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException(new InvalidOperationException("service hiccup"))
                    : Task.CompletedTask;
            });

            beat.Bind("lobby-1", 0.0);
            beat.Tick(LobbyHeartbeat.DefaultIntervalSeconds);

            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, beat.Beats);
            Assert.IsNotNull(beat.LastError, "A failure the host can see beats one it cannot.");

            beat.Tick(LobbyHeartbeat.DefaultIntervalSeconds * 2);

            Assert.AreEqual(2, calls, "One failure must not stop the heartbeat.");
            Assert.AreEqual(1, beat.Beats);
            Assert.IsNull(beat.LastError);
        }

        // -----------------------------------------------------------------------------------------
        // The offline decision. Hard requirement: no cloud project must never mean no game.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// An offline status still carries a usable identity. This is the whole reason
        /// <see cref="ServiceStatus"/> is not a bool: the caller has to be able to start single
        /// player from the same value that told it co-op is unavailable.
        /// </summary>
        [Test]
        public void OfflineStatus_StillCarriesAnIdentity_SoSinglePlayerCanStart()
        {
            string path = Path.Combine(Path.GetTempPath(), $"residue-connect-{Guid.NewGuid():N}.txt");
            var local = new LocalPlayerIdentity(path);
            local.Resolve();

            try
            {
                var status = ServiceStatus.Offline(local, "Playing offline.");

                Assert.IsFalse(status.Online);
                Assert.IsTrue(status.HasIdentity,
                    "An offline status with no identity leaves single player with nothing to key a " +
                    "session on, which is the failure the local identity exists to prevent.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(status.Detail),
                    "A refusal with no reason is indistinguishable from a hang.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// The environment is never blank. <c>SetEnvironmentName</c> throws on an empty string, so a
        /// blank override would turn the one path that is supposed to degrade quietly into a crash.
        /// </summary>
        [Test]
        public void ServiceBootstrap_AlwaysNamesAnEnvironment()
        {
            string name = ServiceBootstrap.EnvironmentName();

            Assert.IsFalse(string.IsNullOrWhiteSpace(name));
            Assert.AreEqual(name.Trim(), name);
        }
    }
}
