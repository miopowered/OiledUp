using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using UnityEngine;

namespace Residue.Net.Connect
{
    /// <summary>
    /// Keeps the host's lobby from expiring while the game runs.
    /// <para>
    /// Unity's Lobby service reaps a lobby that has been silent for thirty seconds. That is a
    /// feature — it is what stops a crashed host leaving a lobby a friend can still join — but it
    /// means a healthy hundred-minute session has to say so roughly every fifteen seconds or it
    /// disappears mid-run and nobody can join.
    /// </para>
    /// The ping is fired and forgotten rather than awaited. A heartbeat is a best-effort keepalive:
    /// a dropped one is corrected by the next, and blocking the game loop on it would trade an
    /// invisible problem for a visible stall. Overlapping pings are suppressed instead, because a
    /// slow service is exactly when a naive timer would stack requests.
    /// <para>
    /// The ping is injectable so the interval logic can be tested without a live project — the
    /// timing is the part that has a bug in it, and it is the part UGS cannot help us check.
    /// </para>
    /// </summary>
    public sealed class LobbyHeartbeat
    {
        /// <summary>
        /// Comfortably under half the service's thirty-second timeout, so one lost ping is
        /// survivable with room to spare.
        /// <para>
        /// Exactly half would put the second ping on the timeout itself — no margin for a slow round
        /// trip, and a lobby reaped mid-session looks like the host vanished. Twelve leaves six
        /// seconds of slack and costs nothing: a heartbeat is a keepalive, not traffic worth saving.
        /// </para>
        /// </summary>
        public const double DefaultIntervalSeconds = 12.0;

        private readonly Func<string, Task> ping;

        private string lobbyId;
        private double nextDue;
        private bool inFlight;

        public double IntervalSeconds { get; set; } = DefaultIntervalSeconds;

        /// <summary>The lobby being kept alive, or null.</summary>
        public string LobbyId => lobbyId;

        public bool IsBeating => lobbyId != null;

        /// <summary>Pings sent since construction. Diagnostic, and what the timing test asserts on.</summary>
        public int Beats { get; private set; }

        /// <summary>
        /// The last ping failure, or null. Kept rather than thrown: a lobby that has already been
        /// reaped will fail every ping from here on, and the useful response is to show it once on
        /// the connect screen, not to tear the game down.
        /// </summary>
        public string LastError { get; private set; }

        /// <param name="ping">
        /// How to ping. Defaults to the Lobby service. Substitute in a test — anything touching
        /// <c>LobbyService.Instance</c> needs a signed-in UGS project to even construct.
        /// </param>
        public LobbyHeartbeat(Func<string, Task> ping = null)
        {
            this.ping = ping ?? (id => LobbyService.Instance.SendHeartbeatPingAsync(id));
        }

        /// <summary>
        /// Start beating for a lobby. The first ping is a full interval away: the lobby was created
        /// moments ago and is therefore as fresh as a ping would make it.
        /// </summary>
        public void Bind(string id, double now)
        {
            lobbyId = id;
            nextDue = now + IntervalSeconds;
            LastError = null;
        }

        /// <summary>Stop. Safe to call when not beating, which is most of the time.</summary>
        public void Release()
        {
            lobbyId = null;
            inFlight = false;
        }

        /// <summary>
        /// Drive from <c>Update</c>. Returns immediately unless a ping is due, and never blocks.
        /// </summary>
        public void Tick(double now)
        {
            if (lobbyId == null || inFlight || now < nextDue) return;

            nextDue = now + IntervalSeconds;
            inFlight = true;
            _ = SendAsync(lobbyId);
        }

        private async Task SendAsync(string id)
        {
            try
            {
                await ping(id);

                // Released while the ping was in flight: the answer is about a lobby we have left.
                if (lobbyId != id) return;

                Beats++;
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning($"[LobbyHeartbeat] Ping for '{id}' failed: {e.Message}. " +
                                 "If this repeats, the lobby has expired and nobody new can join.");
            }
            finally
            {
                inFlight = false;
            }
        }
    }
}
