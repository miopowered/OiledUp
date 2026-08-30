using System;
using System.Collections.Generic;
using Residue.Editor.Content;
using UnityEditor;
using UnityEngine;

namespace Residue.Editor.Chemistry
{
    /// <summary>
    /// The inputs for <see cref="SampleDump"/>, and a copy-pasteable view of what it produced.
    /// <para>
    /// Deliberately thin. Everything that decides what the report says lives in
    /// <see cref="SampleDump"/>, which returns a string; this window owns the controls, the scroll
    /// bar and the clipboard and nothing else. That split is what lets the EditMode suite assert the
    /// determinism criterion without a GUI — and it is why
    /// <c>SampleDump.Build(SampleDumpRequest.Default())</c> is a one-liner from
    /// <c>Unity_RunCommand</c> when there is no Editor to click in.
    /// </para>
    /// <para>
    /// No signal colours anywhere (hard rule 4). Severity is spelled out in words, which also
    /// survives being pasted into an issue comment.
    /// </para>
    /// </summary>
    public sealed class SampleDumpWindow : EditorWindow
    {
        [SerializeField] private SampleDumpRequest request = SampleDumpRequest.Default();
        [SerializeField] private string report = string.Empty;
        [SerializeField] private Vector2 scroll;

        private string[] profileIds;
        private string[] faultLabels;
        private string[] faultIds;

        private Font mono;
        private GUIStyle monoStyle;

        /// <summary>Tried in order for the report pane. Columns only line up in a fixed-width face.</summary>
        private static readonly string[] MonospaceCandidates =
        {
            "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "Monaco"
        };

        [MenuItem("Residue/Chemistry/Sample Dump", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<SampleDumpWindow>("Sample Dump");
            window.minSize = new Vector2(680f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            BuildIdLists();
        }

        private void OnDisable()
        {
            if (mono != null) DestroyImmediate(mono);
            mono = null;
            monoStyle = null;
        }

        private void OnGUI()
        {
            // First paint rather than OnEnable: building content projects the whole of ContentTables
            // through SerializedObject, which is not something to do while the Editor is still coming
            // back from a domain reload.
            if (string.IsNullOrEmpty(report)) Generate();

            DrawInputs();
            EditorGUILayout.Space(4f);
            DrawButtons();
            EditorGUILayout.Space(4f);
            DrawReport();
        }

        // -- Controls -------------------------------------------------------------------------------

        private void DrawInputs()
        {
            EditorGUILayout.LabelField("Sample", EditorStyles.boldLabel);

            request.Seed = EditorGUILayout.IntField(
                new GUIContent("Seed", "Seeds Residue.Chemistry.Rng. The same seed must reproduce this report exactly."),
                request.Seed);

            request.ProfileId = PopupById("Profile", profileIds, request.ProfileId);
            request.EquipmentTag = EditorGUILayout.TextField("Equipment tag", request.EquipmentTag);
            request.Day = EditorGUILayout.IntField("Collected day", request.Day);
            request.HoursSinceOilChange = EditorGUILayout.FloatField(
                new GUIContent("Hours on oil", "Drives depletion of LowerLimit elements and accumulation of wear metals."),
                request.HoursSinceOilChange);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Fault", EditorStyles.boldLabel);

            int faultIndex = Mathf.Max(0, Array.IndexOf(faultIds, request.FaultId ?? string.Empty));
            faultIndex = EditorGUILayout.Popup(
                new GUIContent("Forced fault", "Empty rolls from the pool, subject to Healthy chance."),
                faultIndex, faultLabels);
            request.FaultId = faultIds[faultIndex];

            using (new EditorGUILayout.HorizontalScope())
            {
                request.ForceSeverity = EditorGUILayout.ToggleLeft(
                    new GUIContent("Force severity", "Otherwise rolled from the fault's severity band."),
                    request.ForceSeverity, GUILayout.Width(140f));

                using (new EditorGUI.DisabledScope(!request.ForceSeverity))
                    request.Severity01 = EditorGUILayout.Slider(request.Severity01, 0f, 1f);
            }

            request.ForceHealthy = EditorGUILayout.ToggleLeft(
                new GUIContent("Force healthy", "No fault at all, whatever is selected above."),
                request.ForceHealthy);

            request.ForceBorderline = EditorGUILayout.ToggleLeft(
                new GUIContent("Force borderline", "§6.3's ambiguity budget: land the sample in the Caution band."),
                request.ForceBorderline);

            request.HealthyChance = EditorGUILayout.Slider("Healthy chance", request.HealthyChance, 0f, 1f);
            request.CascadeChance = EditorGUILayout.Slider("Cascade chance", request.CascadeChance, 0f, 1f);
        }

        private void DrawButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(24f))) Generate();

                if (GUILayout.Button(new GUIContent("Next seed", "Seed + 1, then regenerate. Never a random draw."),
                        GUILayout.Height(24f), GUILayout.Width(90f)))
                {
                    request.Seed = unchecked(request.Seed + 1);
                    Generate();
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(report)))
                {
                    if (GUILayout.Button("Copy", GUILayout.Height(24f), GUILayout.Width(70f)))
                        EditorGUIUtility.systemCopyBuffer = report;

                    if (GUILayout.Button("Log", GUILayout.Height(24f), GUILayout.Width(70f)))
                        Debug.Log(report);
                }
            }
        }

        private void DrawReport()
        {
            EditorGUILayout.LabelField(
                "Report — selectable, and Copy puts it on the clipboard ready for an issue comment.",
                EditorStyles.miniLabel);

            var style = ReportStyle();
            var content = new GUIContent(report);
            var size = style.CalcSize(content);

            using var scope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scope.scrollPosition;
            EditorGUILayout.SelectableLabel(
                report, style,
                GUILayout.Width(Mathf.Max(size.x + 20f, 400f)),
                GUILayout.Height(Mathf.Max(size.y + 8f, 200f)));
        }

        // -- Plumbing -------------------------------------------------------------------------------

        private void Generate()
        {
            report = SampleDump.Build(request);
            Repaint();
        }

        /// <summary>
        /// Built from <c>ContentTables</c> rather than from definitions, so the lists are plain strings
        /// that survive a domain reload — an unsaved ScriptableObject would not, and the window would
        /// come back from a recompile with empty popups.
        /// </summary>
        private void BuildIdLists()
        {
            var profiles = new List<string>(ContentTables.Profiles.Length);
            foreach (var row in ContentTables.Profiles) profiles.Add(row.Id);
            profileIds = profiles.ToArray();

            var ids = new List<string> { string.Empty };
            var labels = new List<string> { "(roll from the pool)" };
            foreach (var row in ContentTables.Faults)
            {
                ids.Add(row.Id);
                labels.Add($"{row.Id}  -  {row.Name}");
            }
            faultIds = ids.ToArray();
            faultLabels = labels.ToArray();
        }

        private static string PopupById(string label, string[] ids, string current)
        {
            if (ids == null || ids.Length == 0) return current;
            int index = Mathf.Max(0, Array.IndexOf(ids, current));
            return ids[EditorGUILayout.Popup(label, index, ids)];
        }

        private GUIStyle ReportStyle()
        {
            if (monoStyle != null) return monoStyle;

            monoStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false,
                richText = false,
                alignment = TextAnchor.UpperLeft
            };

            var font = MonospaceFont();
            if (font != null)
            {
                monoStyle.font = font;
                monoStyle.fontSize = 12;
            }
            return monoStyle;
        }

        /// <summary>
        /// The report is column-aligned, so it needs a fixed-width face to be readable in the window.
        /// Null if the machine has none of the usual ones — the text is still correct, and Copy still
        /// pastes into a code block where it lines up regardless.
        /// </summary>
        private Font MonospaceFont()
        {
            if (mono != null) return mono;

            var installed = Font.GetOSInstalledFontNames();
            if (installed == null) return null;

            foreach (string wanted in MonospaceCandidates)
            {
                foreach (string available in installed)
                {
                    if (!string.Equals(available, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    mono = Font.CreateDynamicFontFromOSFont(available, 12);
                    return mono;
                }
            }
            return null;
        }
    }
}
