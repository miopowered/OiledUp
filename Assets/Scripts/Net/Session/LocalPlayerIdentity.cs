using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Residue.Net.Session
{
    /// <summary>
    /// A stable id that needs no cloud project: a GUID minted once and written next to the save
    /// data, re-read on every subsequent launch.
    /// <para>
    /// <b>This is the implementation that runs today.</b> There is no UGS project linked yet, so
    /// <see cref="UgsPlayerIdentity"/> cannot resolve and every rejoin test, every two-instance
    /// playtest and every edit-mode test goes through this class. It is deliberately standalone —
    /// no services, no network, no <c>async</c> that actually awaits anything — so co-op can be
    /// built and driven long before anyone signs into anything.
    /// </para>
    /// Resolution order, and the reason for it:
    /// <list type="number">
    /// <item><description>
    /// <c>-playerId &lt;value&gt;</c> on the command line, then the <c>RESIDUE_PLAYER_ID</c>
    /// environment variable. Both exist for the case this class is most used in: two instances on
    /// one machine. They would otherwise share a persistent data path, therefore share the file,
    /// therefore share an id — and the registry would treat the second window as the first player
    /// reconnecting and hand it the first player's hands. That is the exact bug the stable id was
    /// introduced to prevent, reintroduced by the test harness.
    /// </description></item>
    /// <item><description>
    /// The persisted file. Plain text, one GUID, so it can be deleted to simulate a brand-new
    /// player without clearing anything else.
    /// </description></item>
    /// <item><description>
    /// A fresh GUID, written back immediately. If the write fails the id is still returned and used
    /// for this run: an unwritable disk should cost you rejoin across restarts, not the session.
    /// </description></item>
    /// </list>
    /// </summary>
    public sealed class LocalPlayerIdentity : IPlayerIdentity
    {
        /// <summary>Command-line switch, checked before anything else. Value follows the switch.</summary>
        public const string CommandLineSwitch = "-playerId";

        public const string EnvironmentVariable = "RESIDUE_PLAYER_ID";

        private const string DefaultFileName = "player-id.txt";

        private readonly string filePath;

        public string StableId { get; private set; }

        public bool IsReady => !string.IsNullOrEmpty(StableId);

        /// <summary>
        /// Last six characters of the id, so the roster can tell two local test instances apart
        /// without showing a whole GUID.
        /// </summary>
        public string DisplayName =>
            IsReady ? $"Player {StableId.Substring(Math.Max(0, StableId.Length - 6))}" : "Player";

        /// <param name="path">
        /// Where the GUID lives. Defaults to <see cref="Application.persistentDataPath"/>. Injectable
        /// so a test can point it at a temp directory instead of writing into the real profile —
        /// a test that persists to the same file the Editor uses would give the machine's developer
        /// the test's identity.
        /// </param>
        public LocalPlayerIdentity(string path = null)
        {
            filePath = string.IsNullOrEmpty(path)
                ? Path.Combine(Application.persistentDataPath, DefaultFileName)
                : path;
        }

        /// <summary>
        /// Synchronous because nothing here is remote. <see cref="ResolveAsync"/> exists only to
        /// satisfy the interface, which has to accommodate a service that really does await.
        /// </summary>
        public string Resolve()
        {
            if (IsReady) return StableId;

            StableId = FromCommandLine() ?? FromEnvironment() ?? FromFile() ?? Mint();
            return StableId;
        }

        public Task<string> ResolveAsync() => Task.FromResult(Resolve());

        /// <summary>
        /// Throw this id away and mint another on the next resolve. Only for the "pretend I am
        /// someone else" case in testing; a shipped build has no reason to change identity.
        /// </summary>
        public void Forget()
        {
            StableId = null;
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalPlayerIdentity] Could not delete '{filePath}': {e.Message}");
            }
        }

        // -- Sources ----------------------------------------------------------------------------------

        private static string FromCommandLine()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return null; }

            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], CommandLineSwitch, StringComparison.OrdinalIgnoreCase))
                    return Clean(args[i + 1]);
            }
            return null;
        }

        private static string FromEnvironment()
        {
            try { return Clean(Environment.GetEnvironmentVariable(EnvironmentVariable)); }
            catch { return null; }
        }

        private string FromFile()
        {
            try
            {
                return File.Exists(filePath) ? Clean(File.ReadAllText(filePath)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalPlayerIdentity] Could not read '{filePath}': {e.Message}");
                return null;
            }
        }

        private string Mint()
        {
            string id = Guid.NewGuid().ToString("N");

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, id);
            }
            catch (Exception e)
            {
                // Not fatal. The id works for this run; only rejoin across a restart is lost, and
                // saying so once beats a silently different identity every launch.
                Debug.LogWarning(
                    $"[LocalPlayerIdentity] Minted {id} but could not persist it to '{filePath}': " +
                    $"{e.Message}. Rejoin will not survive a restart.");
            }

            return id;
        }

        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string trimmed = raw.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
