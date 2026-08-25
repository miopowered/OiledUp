using UnityEngine.InputSystem;

namespace Residue.Gameplay.Settings
{
    /// <summary>
    /// One rebindable row: an action plus the index of the single binding on it that the row owns.
    /// <para>
    /// The index is load-bearing rather than incidental. A composite like Move is one action with
    /// four (here eight) separate binding indices under it, and "rebind Move" is meaningless —
    /// the player rebinds <i>forward</i>. Carrying the index is what lets the screen call
    /// <c>action.PerformInteractiveRebinding(binding.BindingIndex)</c> and touch exactly the one
    /// key it showed, instead of the first binding on the action, which for Move is the gamepad
    /// stick.
    /// </para>
    /// <see cref="Label"/> is authored in <see cref="KeyBindings"/> rather than derived from the
    /// action name, because the action names came from Unity's template and no longer describe what
    /// the lab does with them — "Attack" is how you agitate a vial.
    /// </summary>
    public readonly struct BindableBinding
    {
        public readonly InputAction Action;
        public readonly int BindingIndex;
        public readonly string Label;

        public BindableBinding(InputAction action, int bindingIndex, string label)
        {
            Action = action;
            BindingIndex = bindingIndex;
            Label = label;
        }

        public bool IsValid =>
            Action != null && BindingIndex >= 0 && BindingIndex < Action.bindings.Count;

        /// <summary>The live binding, including any override. Default-valued when <see cref="IsValid"/> is false.</summary>
        public InputBinding Binding => IsValid ? Action.bindings[BindingIndex] : default;

        /// <summary>
        /// True while the player has moved this row off its authored key. The screen uses it to
        /// decide whether a per-row revert affordance is worth showing at all.
        /// </summary>
        public bool IsOverridden => IsValid && Binding.overridePath != null;

        public override string ToString() => Label;
    }
}
