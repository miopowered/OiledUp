using System;
using System.Collections.Generic;
using System.Linq;
using Residue.Data;
using UnityEditor;
using UnityEngine;

namespace Residue.Editor.Content
{
    /// <summary>
    /// Projects <see cref="ContentTables"/> onto the .asset files under Assets/Data.
    /// <para>
    /// Assets are written <b>in place</b>. Existing files keep their GUIDs, so rebuilding never breaks
    /// a scene, prefab or serialized reference. Rows deleted from the tables are reported but never
    /// auto-deleted — removing an asset that something still points at is not a decision a menu item
    /// should make silently.
    /// </para>
    /// </summary>
    public static class ContentBootstrap
    {
        private const string Root = "Assets/Data";

        private static readonly Dictionary<Type, string> FolderForType = new()
        {
            { typeof(ElementDef), "Elements" },
            { typeof(RootCauseDef), "Causes" },
            { typeof(EquipmentProfileDef), "Profiles" },
            { typeof(FaultDef), "Faults" },
            { typeof(MachineDef), "Machines" },
            { typeof(CustomerDef), "Customers" }
        };

        [MenuItem("Residue/Content/Rebuild Definitions", priority = 0)]
        public static void Rebuild()
        {
            var created = new List<string>();
            var touched = new HashSet<string>();

            try
            {
                AssetDatabase.StartAssetEditing();
                EnsureFolders();

                var set = ContentBuilder.Build((type, name) => Resolve(type, name, created, touched));
                MarkDirty(set);
                WriteCatalog(set, created);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var orphans = FindOrphans(touched);

            Debug.Log(
                $"[Residue] Content rebuilt. " +
                $"{ContentTables.Elements.Length} elements, {ContentTables.Causes.Length} causes, " +
                $"{ContentTables.Profiles.Length} profiles, {ContentTables.Faults.Length} faults, " +
                $"{ContentTables.Machines.Length} machines, " +
                $"{ContentTables.Customers.Length} customers. " +
                $"{created.Count} created, {touched.Count - created.Count} updated in place.");

            if (created.Count > 0)
                Debug.Log("[Residue] Created:\n  " + string.Join("\n  ", created));

            if (orphans.Count > 0)
            {
                Debug.LogWarning(
                    "[Residue] These assets no longer have a row in ContentTables. Delete them by hand " +
                    "once you have confirmed nothing references them:\n  " + string.Join("\n  ", orphans));
            }
        }

        [MenuItem("Residue/Content/Validate", priority = 1)]
        public static void Validate()
        {
            var problems = new List<string>();

            try
            {
                var set = ContentBuilder.BuildInMemory();

                foreach (var fault in set.Faults.Values)
                {
                    if (fault.Signature.Count == 0)
                        problems.Add($"Fault '{fault.Id}' has an empty signature — it would be undetectable.");

                    if (fault.RootCause == null)
                        problems.Add($"Fault '{fault.Id}' has no root cause, so it cannot pay the §5.4 bonus.");

                    // A signature may legitimately move something a given profile does not score:
                    // thermal ageing slows the cooling curve, and a corrosion-protection oil has no
                    // cooling curve because it is not a quenchant. That is fine as long as SOMETHING
                    // in the signature is scored. The real defect is a fault that, on some profile it
                    // claims to be valid on, moves nothing that profile measures — an undetectable
                    // fault the player is nonetheless punished for missing.
                    foreach (var profile in fault.ValidOn)
                    {
                        bool anyScored = false;
                        foreach (var d in fault.Signature)
                        {
                            if (d?.Element == null) continue;
                            if (!profile.TryGetThreshold(d.Element.Id, out _)) continue;
                            anyScored = true;
                            break;
                        }

                        if (!anyScored)
                        {
                            problems.Add(
                                $"Fault '{fault.Id}' is valid on '{profile.Id}' but moves nothing that " +
                                $"profile scores — it would be undetectable and still punished.");
                        }
                    }
                }

                // Every element a machine claims to measure should matter to at least one profile.
                foreach (var machine in set.Machines.Values)
                {
                    foreach (var e in machine.Measures)
                    {
                        if (e == null) continue;
                        if (!set.Profiles.Values.Any(p => p.TryGetThreshold(e.Id, out _)))
                            problems.Add($"Machine '{machine.Id}' measures '{e.Id}', which no profile scores.");
                    }
                }
            }
            catch (Exception ex)
            {
                problems.Add($"Content failed to build: {ex.Message}");
            }

            if (problems.Count == 0)
                Debug.Log("[Residue] Content validation passed.");
            else
                Debug.LogWarning($"[Residue] Content validation found {problems.Count} problem(s):\n  " +
                                 string.Join("\n  ", problems));
        }

        // -- Asset plumbing --------------------------------------------------------------------------

        private static ScriptableObject Resolve(Type type, string assetName, List<string> created, HashSet<string> touched)
        {
            string path = PathFor(type, assetName);
            touched.Add(path);

            var existing = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
            if (existing != null) return existing;

            var instance = ScriptableObject.CreateInstance(type);
            instance.name = assetName;
            AssetDatabase.CreateAsset(instance, path);
            created.Add(path);
            return instance;
        }

        private static string PathFor(Type type, string assetName)
        {
            if (!FolderForType.TryGetValue(type, out string folder))
                throw new ArgumentException($"No output folder configured for definition type {type.Name}.");
            return $"{Root}/{folder}/{assetName}.asset";
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                AssetDatabase.CreateFolder("Assets", "Data");

            foreach (string folder in FolderForType.Values)
            {
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}"))
                    AssetDatabase.CreateFolder(Root, folder);
            }
        }

        /// <summary>
        /// Roll every definition up into the single asset runtime code references. Written in place
        /// like the definitions, so the scene reference to it survives a rebuild.
        /// </summary>
        private static void WriteCatalog(ContentSet set, List<string> created)
        {
            const string path = Root + "/ContentCatalog.asset";

            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ContentCatalog>();
                catalog.name = "ContentCatalog";
                AssetDatabase.CreateAsset(catalog, path);
                created.Add(path);
            }

            var so = new SerializedObject(catalog);
            Fill(so.FindProperty("elements"), ContentTables.Elements.Select(r => (UnityEngine.Object)set.Element(r.Id)));
            Fill(so.FindProperty("causes"), ContentTables.Causes.Select(r => (UnityEngine.Object)set.Cause(r.Id)));
            Fill(so.FindProperty("profiles"), ContentTables.Profiles.Select(r => (UnityEngine.Object)set.Profile(r.Id)));
            Fill(so.FindProperty("faults"), ContentTables.Faults.Select(r => (UnityEngine.Object)set.Fault(r.Id)));
            Fill(so.FindProperty("machines"), ContentTables.Machines.Select(r => (UnityEngine.Object)set.Machine(r.Id)));
            Fill(so.FindProperty("customers"), ContentTables.Customers.Select(r => (UnityEngine.Object)set.Customer(r.Id)));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(catalog);
        }

        private static void Fill(SerializedProperty list, IEnumerable<UnityEngine.Object> values)
        {
            var items = values.ToList();
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static void MarkDirty(ContentSet set)
        {
            foreach (var o in set.Elements.Values) EditorUtility.SetDirty(o);
            foreach (var o in set.Causes.Values) EditorUtility.SetDirty(o);
            foreach (var o in set.Profiles.Values) EditorUtility.SetDirty(o);
            foreach (var o in set.Faults.Values) EditorUtility.SetDirty(o);
            foreach (var o in set.Machines.Values) EditorUtility.SetDirty(o);
        }

        private static List<string> FindOrphans(HashSet<string> touched)
        {
            var orphans = new List<string>();
            foreach (string folder in FolderForType.Values)
            {
                string dir = $"{Root}/{folder}";
                if (!AssetDatabase.IsValidFolder(dir)) continue;

                foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!touched.Contains(path)) orphans.Add(path);
                }
            }
            return orphans;
        }
    }
}
