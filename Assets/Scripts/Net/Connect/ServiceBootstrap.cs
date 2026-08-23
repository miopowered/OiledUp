using System;
using System.Threading.Tasks;
using Residue.Net.Session;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;

namespace Residue.Net.Connect
{
    /// <summary>
    /// Brings Unity Gaming Services up once, before anything asks who the player is.
    /// <para>
    /// <b>What happens in each case</b>, because this is the class that decides whether the game
    /// starts at all:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <i>Everything works.</i> UGS initialises against an explicit environment, anonymous sign-in
    /// succeeds, and the returned <see cref="ServiceStatus.Identity"/> is a
    /// <see cref="UgsPlayerIdentity"/> carrying the <c>PlayerId</c> that §M4 keys rejoin on.
    /// <see cref="ServiceStatus.Online"/> is true and host/join are available.
    /// </description></item>
    /// <item><description>
    /// <i>No network, no linked project, service outage, or a build run offline.</i>
    /// <see cref="UgsPlayerIdentity.ResolveOrLocalAsync"/> swallows the failure and hands back a
    /// <see cref="LocalPlayerIdentity"/>. <see cref="ServiceStatus.Online"/> is false, the connect
    /// screen refuses host and join with one sentence saying why, and <b>single player starts
    /// normally</b>. It never throws and it never leaves a caller awaiting forever.
    /// </description></item>
    /// <item><description>
    /// <i>UGS accepts the call and then never answers.</i> The real hang risk: a socket that is
    /// open but dead does not fail, it waits. <see cref="TimeoutSeconds"/> caps the wait and the
    /// result is the offline case above. The abandoned task is observed rather than left to
    /// resurface as an unobserved-exception log line minutes later.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Why the environment is set here rather than left to the project settings.</b>
    /// <c>ProjectSettings/Packages/com.unity.services.core/Settings.json</c> may carry an empty
    /// <c>EnvironmentName</c>, which reads as "unconfigured". Passing it explicitly means the
    /// environment the game talks to is a fact in source control rather than a fact about whoever
    /// last opened the Services window, and <c>-ugsEnvironment</c> lets a dev environment be
    /// selected without a code change.
    /// </para>
    /// </summary>
    public static class ServiceBootstrap
    {
        /// <summary>Every UGS project has this environment; nothing else can be assumed to exist.</summary>
        public const string DefaultEnvironment = "production";

        /// <summary>Command-line override, value follows the switch. Checked before the env var.</summary>
        public const string EnvironmentSwitch = "-ugsEnvironment";

        public const string EnvironmentVariable = "RESIDUE_UGS_ENVIRONMENT";

        /// <summary>
        /// How long to wait for sign-in before deciding the services are not there. Long enough for
        /// a slow connection, short enough that a player on a dead network is not looking at a
        /// spinner wondering whether the game has crashed.
        /// </summary>
        public static float TimeoutSeconds = 12f;

        private static Task<ServiceStatus> inFlight;
        private static ServiceStatus? resolved;

        /// <summary>True once a decision has been made. The connect screen uses it to skip a re-await.</summary>
        public static bool IsResolved => resolved.HasValue;

        /// <summary>The decision, or a default-constructed status if none has been made yet.</summary>
        public static ServiceStatus Status => resolved ?? default;

        /// <summary>
        /// The environment to sign in to: <c>-ugsEnvironment &lt;name&gt;</c>, then
        /// <c>RESIDUE_UGS_ENVIRONMENT</c>, then <see cref="DefaultEnvironment"/>. Never returns
        /// empty — <c>SetEnvironmentName</c> throws on an empty string, which would turn a blank
        /// override into a crash on the one path that is supposed to degrade quietly.
        /// </summary>
        public static string EnvironmentName()
        {
            string chosen = FromCommandLine() ?? FromEnvironmentVariable();
            return string.IsNullOrWhiteSpace(chosen) ? DefaultEnvironment : chosen.Trim();
        }

        /// <summary>
        /// Initialise and sign in, once. Concurrent callers share one attempt; later callers get the
        /// cached answer without a round trip.
        /// </summary>
        public static Task<ServiceStatus> EnsureAsync()
        {
            if (resolved.HasValue) return Task.FromResult(resolved.Value);
            return inFlight ??= RunAsync();
        }

        /// <summary>
        /// Throw the cached decision away so the next <see cref="EnsureAsync"/> tries again.
        /// <para>
        /// This is a real player action, not a test hook: someone who launched with the wi-fi off,
        /// turned it on and pressed HOST expects it to work. Without this they would have to
        /// restart the game to get a second attempt.
        /// </para>
        /// </summary>
        public static void Forget()
        {
            resolved = null;
            inFlight = null;
        }

        private static async Task<ServiceStatus> RunAsync()
        {
            var work = SignInAsync();
            var timeout = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1f, TimeoutSeconds)));

            var first = await Task.WhenAny(work, timeout);

            ServiceStatus status;
            if (first != work)
            {
                Observe(work);
                status = ServiceStatus.Offline(Local(),
                    "Unity Gaming Services did not answer. Playing offline.");
            }
            else
            {
                var identity = work.Result;
                status = identity is UgsPlayerIdentity
                    ? ServiceStatus.Ready(identity)
                    : ServiceStatus.Offline(identity ?? Local(),
                        "Could not sign in to Unity Gaming Services. Playing offline.");
            }

            if (!status.Online)
            {
                Debug.LogWarning($"[ServiceBootstrap] {status.Detail} Host and join are unavailable; " +
                                 "single player is not affected.");
            }

            resolved = status;
            return status;
        }

        private static async Task<IPlayerIdentity> SignInAsync()
        {
            // Initialise before UgsPlayerIdentity gets there. Its own InitializeAsync() takes no
            // options, so whichever call runs first fixes the environment for the process — and if
            // that is the options-less one, the environment comes from a settings file that may
            // well be blank.
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    options.SetEnvironmentName(EnvironmentName());
                    await UnityServices.InitializeAsync(options);
                }
            }
            catch (Exception e)
            {
                // Not fatal, and not final. ResolveOrLocalAsync below will try once more and fall
                // back to a local identity, which is the outcome we want anyway.
                Debug.LogWarning($"[ServiceBootstrap] Services init failed ({e.GetType().Name}: {e.Message}).");
            }

            return await UgsPlayerIdentity.ResolveOrLocalAsync();
        }

        private static IPlayerIdentity Local()
        {
            var local = new LocalPlayerIdentity();
            local.Resolve();
            return local;
        }

        /// <summary>
        /// Read a timed-out task's exception so the finalizer does not report it as unobserved.
        /// Nothing is done with it; by the time it lands the decision has already been made.
        /// </summary>
        private static void Observe(Task task) =>
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

        private static string FromCommandLine()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return null; }

            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], EnvironmentSwitch, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static string FromEnvironmentVariable()
        {
            try { return Environment.GetEnvironmentVariable(EnvironmentVariable); }
            catch { return null; }
        }
    }
}
