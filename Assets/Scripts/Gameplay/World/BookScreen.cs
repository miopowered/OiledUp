using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Serialization-only migration stub for player prefabs built before physical book inspection.
    /// New scene builds no longer add this component. It deliberately exposes no Open operation and
    /// keeps its old document hidden, so a stale prefab cannot revive the detached page UI.
    /// </summary>
    [Obsolete("Reference books are read on their 3D page surface through item inspection.")]
    public sealed class BookScreen : MonoBehaviour
    {
        private void Awake()
        {
            var document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.style.display = DisplayStyle.None;
            enabled = false;
        }
    }
}
