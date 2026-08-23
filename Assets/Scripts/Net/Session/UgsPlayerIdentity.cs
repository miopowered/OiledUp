using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Residue.Net.Session
{
    /// <summary>
    /// The shipping identity: UGS Authentication's anonymous <c>PlayerId</c>, which §M4 names as the
    /// key rejoin is built on.
    /// <para>
    /// Anonymous sign-in is the right level of ceremony here. It survives a restart on the same
    /// device without asking anyone for an account, and it is the same id Relay, Lobby and Vivox
    /// already speak, so the roster, the voice channel and the session record all agree about who a
    /// player is without a second mapping table to keep in sync.
    /// </para>
    /// <b>This cannot resolve until a cloud project is linked</b>, and none is. Until then
    /// <see cref="ResolveAsync"/> returns null and the caller is expected to fall back to
    /// <see cref="LocalPlayerIdentity"/> — see <see cref="ResolveOrLocalAsync"/>, which is the call
    /// worth making from the connect flow.
    /// </summary>
    public sealed class UgsPlayerIdentity : IPlayerIdentity
    {
        public string StableId { get; private set; }

        public bool IsReady => !string.IsNullOrEmpty(StableId);

        public string DisplayName { get; private set; } = "Player";

        public async Task<string> ResolveAsync()
        {
            if (IsReady) return StableId;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                StableId = AuthenticationService.Instance.PlayerId;

                string name = AuthenticationService.Instance.PlayerName;
                if (!string.IsNullOrEmpty(name)) DisplayName = name;

                return StableId;
            }
            catch (Exception e)
            {
                // Deliberately swallowed. No linked project is the expected state right now, and an
                // exception out of here would abort a connect that the local fallback can complete.
                Debug.LogWarning(
                    $"[UgsPlayerIdentity] Anonymous sign-in unavailable ({e.GetType().Name}: " +
                    $"{e.Message}). Falling back to a local identity.");
                return null;
            }
        }

        /// <summary>
        /// UGS if it answers, the persisted local GUID if it does not.
        /// <para>
        /// The fallback is not a nicety — it is the path that runs today. Keeping it inside one call
        /// means the connect flow has a single line and no branch that only ever gets exercised once
        /// someone remembers to link a project.
        /// </para>
        /// </summary>
        public static async Task<IPlayerIdentity> ResolveOrLocalAsync()
        {
            var ugs = new UgsPlayerIdentity();
            if (await ugs.ResolveAsync() != null) return ugs;

            var local = new LocalPlayerIdentity();
            local.Resolve();
            return local;
        }
    }
}
