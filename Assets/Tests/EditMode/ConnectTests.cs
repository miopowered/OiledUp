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
