using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.Settings
{
    /// <summary>
    /// Persistence and enumeration for rebindable controls (#45). Everything the settings screen
    /// needs to draw a control list and keep a rebind; none of the interactive rebind loop itself,
    /// which belongs to the screen because it owns the modal, the prompt and escape-to-cancel.
    /// <para>
    /// <b>A rebind must never turn a hold into a tap.</b> Holds are a mechanic here, not a
    /// convenience: flushing a machine is a hold, agitating a vial is a hold, and §9 requires that
    /// time cost to be real hand-operated work rather than a menu click. The safety comes from what
    /// <c>PerformInteractiveRebinding</c> writes — <c>overridePath</c> and nothing else. Interactions
    /// and processors on the binding and on the action are left exactly as authored, so a rebound
    /// key is the same action with a different key on it. Anything added here that writes
    /// <c>overrideInteractions</c> would break that, and would break it silently: the control would
    /// still work, it would just cost nothing.
    /// </para>
    /// <para>
    /// The related trap, already paid for once: Unity's template shipped <c>Interact</c> with a
    /// <c>Hold</c> interaction, so <c>WasPressedThisFrame</c> never fired for a tap and nothing in
    /// the lab could be picked up. It presented as "left click does nothing". <c>PlayerInteractor</c>
    /// now logs an error if interactions reappear on that action, and this class must never be the
    /// thing that puts them back.
    /// </para>
    /// <para>
    /// Overrides persist as one JSON blob under a <b>versioned</b> key. If the bindable set ever
    /// changes shape the version moves and the old blob is ignored rather than half-applied — a
    /// stale override landing on a reused binding index is a control scheme nobody chose and nobody
    /// can explain.
    /// </para>
    /// </summary>
    public static class KeyBindings
    {
        /// <summary>Bump the suffix to discard every saved override rather than mis-apply it.</summary>
        public const string PrefsKey = "oiledup.bindings.v1";

        /// <summary>Gameplay controls only. The UI map is navigation and is not the player's to rebind.</summary>
        public const string PlayerMap = "Player";

        // -- Persistence -----------------------------------------------------------------------------

        /// <summary>
        /// Applies the saved overrides to <paramref name="asset"/>. Safe to call repeatedly: the
        /// underlying load replaces the override set rather than accumulating onto it, so a second
        /// player spawning in the same process does not double-apply anything.
        /// </summary>
        public static void Load(InputActionAsset asset)
        {
            if (asset == null) return;

            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                // A blob that will not parse is worse than none: it would leave the asset in
                // whatever state the loader reached before it threw. Drop it and take the defaults,
                // which are at least a control scheme that works.
                Debug.LogWarning(
                    $"[KeyBindings] Discarding unreadable saved bindings ({e.Message}). " +
                    "Controls are back to their defaults.");

                asset.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(PrefsKey);
                PlayerPrefs.Save();
            }
        }

        public static void Save(InputActionAsset asset)
        {
            if (asset == null) return;

            PlayerPrefs.SetString(PrefsKey, asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>Every row back to its authored key, and the saved blob deleted with it.</summary>
        public static void ResetAll(InputActionAsset asset)
        {
            if (asset != null) asset.RemoveAllBindingOverrides();

            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// One row back to its authored key. Persists immediately through the asset the action
        /// belongs to, so a per-row revert survives a restart the same way a rebind does.
        /// </summary>
        public static void Reset(BindableBinding binding)
        {
            if (!binding.IsValid) return;

            binding.Action.RemoveBindingOverride(binding.BindingIndex);

            var asset = binding.Action.actionMap != null ? binding.Action.actionMap.asset : null;
            if (asset != null) Save(asset);
        }

        // -- Enumeration -----------------------------------------------------------------------------

        /// <summary>
        /// The rows a control list should show: keyboard and mouse bindings on the <c>Player</c> map,
        /// composites expanded into their parts.
        /// <para>
        /// Gamepad, joystick and XR bindings are omitted rather than listed. There is no gamepad UI
        /// to rebind them from, and including them would let a keyboard rebind land on a binding
        /// index that a controller was using — breaking a device the player never touched in this
        /// menu. <c>Look</c> is omitted for the plainer reason that a mouse delta is not a key;
        /// its sensitivity and inversion are <see cref="GameSettings"/>' business.
        /// </para>
        /// Composite headers are skipped and their parts are not: "rebind Move" is meaningless, and
        /// the four (here eight) directions are what the player actually presses.
        /// </summary>
        public static IReadOnlyList<BindableBinding> Bindable(InputActionAsset asset)
        {
            var rows = new List<BindableBinding>();
            if (asset == null) return rows;

            var map = asset.FindActionMap(PlayerMap, throwIfNotFound: false);
            if (map == null) return rows;

            // The asset binds two keys to most things (W and Up Arrow, click and Enter). Both are
            // real rows, so the second gets an "(alt)" suffix instead of a duplicate label.
            var used = new Dictionary<string, int>();

            foreach (var action in map.actions)
            {
                if (IsExcludedAction(action.name)) continue;

                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];

                    if (binding.isComposite) continue;
                    if (!IsKeyboardOrMouse(binding.path)) continue;

                    rows.Add(new BindableBinding(action, i, Disambiguate(LabelFor(action, binding), used)));
                }
            }

            return rows;
        }

        /// <summary>What this row currently reads as, override included.</summary>
        public static string Display(BindableBinding binding)
        {
            if (!binding.IsValid) return "Unbound";

            var b = binding.Binding;
            string path = string.IsNullOrEmpty(b.effectivePath) ? b.path : b.effectivePath;
            if (string.IsNullOrEmpty(path)) return "Unbound";

            string text = InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);

            return string.IsNullOrEmpty(text) ? "Unbound" : text;
        }

        /// <summary>
        /// True when the Input System itself gates this action behind a <c>Hold</c>, so the screen
        /// can label the row and the player is told about the time cost before they meet it.
        /// <para>
        /// Expected to be false for every row today, and that is the correct answer rather than a
        /// stub: the lab's holds are timed by <c>PlayerInteractor</c> against each interactable's
        /// own <c>HoldSeconds</c> (0 s for picking a vial up, seconds for agitating, far more for a
        /// flush), because hold duration is a property of the thing being operated and not of the
        /// key. This reports the binding-level case only, which exists so that an action that ever
        /// legitimately carries a <c>Hold</c> cannot be presented to the player as a tap.
        /// </para>
        /// </summary>
        public static bool IsHeld(BindableBinding binding)
        {
            if (!binding.IsValid) return false;

            if (MentionsHold(binding.Action.interactions)) return true;

            var b = binding.Binding;
            return MentionsHold(b.effectiveInteractions) || MentionsHold(b.interactions);
        }

        /// <summary>
        /// Whether <paramref name="effectivePath"/> is already taken elsewhere on the <c>Player</c>
        /// map, and by what. Compares effective paths — the key as it currently is, override
        /// included — because comparing authored paths would happily report "free" for a key the
        /// player rebound something else onto five seconds ago.
        /// <para>
        /// Asked before the override is kept, so the screen can say "that key is already Sprint"
        /// instead of leaving two actions on one key for the player to discover in the lab.
        /// </para>
        /// </summary>
        /// <param name="candidate">The row being rebound. Never reported as conflicting with itself.</param>
        /// <param name="heldBy">The label of the row already using the path, or null.</param>
        public static bool Conflict(InputActionAsset asset, BindableBinding candidate,
            string effectivePath, out string heldBy)
        {
            heldBy = null;

            if (asset == null || string.IsNullOrEmpty(effectivePath)) return false;

            foreach (var row in Bindable(asset))
            {
                if (row.Action == candidate.Action && row.BindingIndex == candidate.BindingIndex)
                    continue;

                var b = row.Binding;
                string existing = string.IsNullOrEmpty(b.effectivePath) ? b.path : b.effectivePath;

                if (!string.Equals(existing, effectivePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                heldBy = row.Label;
                return true;
            }

            return false;
        }

        // -- Labelling -------------------------------------------------------------------------------

        private static bool IsExcludedAction(string actionName) =>
            string.Equals(actionName, "Look", StringComparison.OrdinalIgnoreCase);

        private static bool IsKeyboardOrMouse(string path) =>
            !string.IsNullOrEmpty(path) &&
            (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase));

        private static bool MentionsHold(string interactions) =>
            !string.IsNullOrEmpty(interactions) &&
            interactions.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Action names came from Unity's first-person template and describe a shooter, not a lab.
        /// The player is told what the key does here, not what the asset happens to call it.
        /// </summary>
        private static string LabelFor(InputAction action, InputBinding binding)
        {
            string label = action.name switch
            {
                "Move" => "Move",
                "Jump" => "Jump",
                "Crouch" => "Crouch",
                "Sprint" => "Sprint",
                "Interact" => "Interact",
                "Attack" => "Agitate / use item",
                "Previous" => "Previous item",
                "Next" => "Next item",
                _ => Prettify(action.name)
            };

            if (!binding.isPartOfComposite) return label;

            string part = binding.name switch
            {
                "up" => "forward",
                "down" => "backward",
                "left" => "left",
                "right" => "right",
                null => null,
                "" => null,
                _ => binding.name
            };

            return part == null ? label : $"{label} {part}";
        }

        private static string Disambiguate(string label, IDictionary<string, int> used)
        {
            if (!used.TryGetValue(label, out int seen))
            {
                used[label] = 1;
                return label;
            }

            used[label] = seen + 1;
            return seen == 1 ? $"{label} (alt)" : $"{label} (alt {seen})";
        }

        /// <summary>"ScrollWheel" -> "Scroll wheel". Only reached by an action nobody labelled.</summary>
        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length + 4);
            sb.Append(char.ToUpperInvariant(name[0]));

            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(name[i]));
                    continue;
                }

                sb.Append(name[i]);
            }

            return sb.ToString();
        }
    }
}
