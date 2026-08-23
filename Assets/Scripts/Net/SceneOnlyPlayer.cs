using Unity.Netcode;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Removes the lab scene's built-in player when a networked session is running.
    /// <para>
    /// The scene ships with a player so single player is exactly the M0 experience: open
    /// <c>Lab.unity</c>, press Play, walk around. Netcode spawns its own player object per client
    /// from a prefab, which would leave two bodies for one person — two cameras, two audio
    /// listeners, and an interaction ray belonging to a body nobody is driving.
    /// </para>
    /// The scene player and the prefab are built by the same code, so this is not a duplicate to
    /// keep in step; it is the same character, present twice for a moment, and this decides which
    /// copy survives.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneOnlyPlayer : MonoBehaviour
    {
        private void Awake()
        {
            var manager = NetworkManager.Singleton;

            // IsListening rather than merely existing: the Boot scene keeps a NetworkManager alive
            // across a single-player load, and a manager that was never started has no players to
            // spawn, so tearing this one out would leave nobody to play.
            if (manager == null || !manager.IsListening) return;

            Destroy(gameObject);
        }
    }
}
