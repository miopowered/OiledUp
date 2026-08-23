using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// A selectable root cause on the report form. Exists so the root-cause bonus (§5.4) is a
    /// pickable list rather than free text — the keystone case being that dirt ingress's root
    /// cause is a failed AIR FILTER, so replacing the worn component does not fix anything.
    /// </summary>
    [CreateAssetMenu(menuName = "Residue/Root Cause", fileName = "Cause_")]
    public sealed class RootCauseDef : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Shown in the reference book to teach the distinction, e.g. why Si+Al means intake, not gears.")]
        [SerializeField, TextArea(2, 5)] private string explanation;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string Explanation => explanation;
    }
}
