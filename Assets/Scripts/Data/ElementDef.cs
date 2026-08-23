using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// One measurable quantity: a wear metal, a contaminant, an additive, or a fluid property.
    /// Definitions are immutable at runtime — everything is exposed read-only so a mis-written
    /// system cannot mutate shared balance data and silently poison a run.
    /// </summary>
    [CreateAssetMenu(menuName = "Residue/Element", fileName = "Element_")]
    public sealed class ElementDef : ScriptableObject
    {
        [Tooltip("Stable key used in trueValues/measured dictionaries and save files. Never rename after content exists.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Tooltip("ppm, %, mgKOH/g, cSt, ...")]
        [SerializeField] private string unit;

        [SerializeField] private ElementCategory category;

        [Tooltip("Shown in the in-game reference book. e.g. 'gears, cylinder liners, shafts'")]
        [SerializeField, TextArea(2, 4)] private string sourceHint;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string Unit => unit;
        public ElementCategory Category => category;
        public string SourceHint => sourceHint;

        public override string ToString() => $"{Id} ({Unit})";
    }
}
