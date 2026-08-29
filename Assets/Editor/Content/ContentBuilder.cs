using System;
using System.Collections.Generic;
using System.Linq;
using Residue.Data;
using UnityEditor;
using UnityEngine;

namespace Residue.Editor.Content
{
    /// <summary>Every definition built from <see cref="ContentTables"/>, indexed by id.</summary>
    public sealed class ContentSet
    {
        public readonly Dictionary<string, ElementDef> Elements = new();
        public readonly Dictionary<string, RootCauseDef> Causes = new();
        public readonly Dictionary<string, EquipmentProfileDef> Profiles = new();
        public readonly Dictionary<string, FaultDef> Faults = new();
        public readonly Dictionary<string, MachineDef> Machines = new();
        public readonly Dictionary<string, CustomerDef> Customers = new();

        public CustomerDef Customer(string id) => Customers.TryGetValue(id, out var v) ? v : null;
        public ElementDef Element(string id) => Elements.TryGetValue(id, out var v) ? v : null;
        public EquipmentProfileDef Profile(string id) => Profiles.TryGetValue(id, out var v) ? v : null;
        public FaultDef Fault(string id) => Faults.TryGetValue(id, out var v) ? v : null;
        public MachineDef Machine(string id) => Machines.TryGetValue(id, out var v) ? v : null;
        public RootCauseDef Cause(string id) => Causes.TryGetValue(id, out var v) ? v : null;

        public List<FaultDef> AllFaults => new(Faults.Values);
    }

    /// <summary>
    /// Projects <see cref="ContentTables"/> into ScriptableObject instances.
    /// <para>
    /// Definition fields are private and <c>[SerializeField]</c> so nothing can mutate balance data at
    /// runtime. Writing them therefore goes through <see cref="SerializedObject"/> rather than
    /// reflection — the same path the Inspector uses, so a field that no longer exists throws instead
    /// of silently doing nothing.
    /// </para>
    /// </summary>
    public static class ContentBuilder
    {
        /// <summary>
        /// Given a definition type and asset name, return the existing asset to write into, or null
        /// to allocate a fresh unsaved instance. ContentBootstrap uses this to populate assets
        /// <b>in place</b> so their GUIDs survive a rebuild — recreating them would break every
        /// scene and prefab reference in the project.
        /// </summary>
        public delegate ScriptableObject ExistingAssetResolver(Type type, string assetName);

        /// <summary>Unsaved instances for unit tests. Touches nothing on disk. Caller owns the objects.</summary>
        public static ContentSet BuildInMemory() => Build(null);

        public static ContentSet Build(ExistingAssetResolver resolver)
        {
            var set = new ContentSet();

            foreach (var row in ContentTables.Elements)
                set.Elements[row.Id] = PopulateElement(Instance<ElementDef>(resolver, $"Element_{row.Id}"), row);

            foreach (var row in ContentTables.Causes)
                set.Causes[row.Id] = PopulateCause(Instance<RootCauseDef>(resolver, $"Cause_{row.Id}"), row);

            foreach (var row in ContentTables.Profiles)
                set.Profiles[row.Id] = PopulateProfile(Instance<EquipmentProfileDef>(resolver, $"Profile_{row.Id}"), row, set);

            // Two passes: faults reference other faults through CanCause, so all must exist first.
            foreach (var row in ContentTables.Faults)
                set.Faults[row.Id] = PopulateFault(Instance<FaultDef>(resolver, $"Fault_{row.Id}"), row, set);

            foreach (var row in ContentTables.Faults)
                FillCascades(row, set);

            foreach (var row in ContentTables.Machines)
                set.Machines[row.Id] = PopulateMachine(Instance<MachineDef>(resolver, $"Machine_{row.Id}"), row, set);

            // After profiles, because a customer names the fluids it runs by profile id.
            foreach (var row in ContentTables.Customers)
                set.Customers[row.Id] = PopulateCustomer(Instance<CustomerDef>(resolver, $"Customer_{row.Id}"), row, set);

            return set;
        }

        /// <summary>
        /// An unsaved <see cref="ContentCatalog"/> pointing at the given set. Lets a test spin up a
        /// whole <c>LabState</c> without touching the AssetDatabase.
        /// </summary>
        public static ContentCatalog BuildCatalogInMemory(ContentSet set)
        {
            var catalog = ScriptableObject.CreateInstance<ContentCatalog>();
            catalog.name = "ContentCatalog";

            var so = new SerializedObject(catalog);
            FillList(so.FindProperty("elements"), ContentTables.Elements.Select(r => (UnityEngine.Object)set.Element(r.Id)));
            FillList(so.FindProperty("causes"), ContentTables.Causes.Select(r => (UnityEngine.Object)set.Cause(r.Id)));
            FillList(so.FindProperty("profiles"), ContentTables.Profiles.Select(r => (UnityEngine.Object)set.Profile(r.Id)));
            FillList(so.FindProperty("faults"), ContentTables.Faults.Select(r => (UnityEngine.Object)set.Fault(r.Id)));
            FillList(so.FindProperty("machines"), ContentTables.Machines.Select(r => (UnityEngine.Object)set.Machine(r.Id)));
            FillList(so.FindProperty("customers"), ContentTables.Customers.Select(r => (UnityEngine.Object)set.Customer(r.Id)));
            so.ApplyModifiedPropertiesWithoutUndo();

            return catalog;
        }

        private static void FillList(SerializedProperty list, IEnumerable<UnityEngine.Object> values)
        {
            var items = values.ToList();
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static T Instance<T>(ExistingAssetResolver resolver, string assetName) where T : ScriptableObject
        {
            var existing = resolver?.Invoke(typeof(T), assetName) as T;
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            created.name = assetName;
            return created;
        }

        // -- Individual definitions -----------------------------------------------------------------

        /// <summary>
        /// Project a customer. The oils are resolved to <see cref="EquipmentProfileDef"/> references
        /// rather than stored as ids, so a table naming a fluid that does not exist fails here — at
        /// content build time, where it is one line to fix — instead of arriving as a null on a
        /// delivery note halfway through a contract.
        /// </summary>
        private static CustomerDef PopulateCustomer(CustomerDef asset, in CustomerRow row, ContentSet set)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("industry").enumValueIndex = (int)row.Industry;
            so.FindProperty("orderPrefix").stringValue = row.OrderPrefix;
            so.FindProperty("reliability").enumValueIndex = (int)row.Reliability;
            so.FindProperty("paperworkSlipChance").floatValue = row.PaperworkSlip;
            so.FindProperty("sameDrumChance").floatValue = row.SameDrum;

            var sites = so.FindProperty("sites");
            sites.arraySize = row.Sites.Length;
            for (int i = 0; i < row.Sites.Length; i++)
                sites.GetArrayElementAtIndex(i).stringValue = row.Sites[i];

            var oils = so.FindProperty("oils");
            oils.arraySize = row.Oils.Length;
            for (int i = 0; i < row.Oils.Length; i++)
            {
                var profile = set.Profile(row.Oils[i]);
                if (profile == null)
                {
                    throw new ArgumentException(
                        $"Customer '{row.Id}' runs '{row.Oils[i]}', which is not a profile in " +
                        "ContentTables.Profiles.");
                }
                oils.GetArrayElementAtIndex(i).objectReferenceValue = profile;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static ElementDef PopulateElement(ElementDef asset, in ElementRow row)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("unit").stringValue = row.Unit;
            so.FindProperty("category").enumValueIndex = (int)row.Category;
            so.FindProperty("sourceHint").stringValue = row.Hint;
            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static RootCauseDef PopulateCause(RootCauseDef asset, in CauseRow row)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("explanation").stringValue = row.Explanation;
            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static EquipmentProfileDef PopulateProfile(EquipmentProfileDef asset, in ProfileRow row, ContentSet set)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("defaultOilChangeHours").floatValue = row.OilChangeHours;
            so.FindProperty("baseOilGrade").stringValue = row.OilGrade;

            var list = so.FindProperty("thresholds");
            list.arraySize = row.Thresholds.Length;
            for (int i = 0; i < row.Thresholds.Length; i++)
            {
                var t = row.Thresholds[i];
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("element").objectReferenceValue = Require(set.Element(t.ElementId), t.ElementId, row.Id);
                e.FindPropertyRelative("mode").enumValueIndex = (int)t.Mode;
                e.FindPropertyRelative("baseline").floatValue = t.Baseline;
                e.FindPropertyRelative("baselineVariance").floatValue = t.Variance;
                e.FindPropertyRelative("normalLimit").floatValue = t.NormalLimit;
                e.FindPropertyRelative("cautionLimit").floatValue = t.CautionLimit;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static FaultDef PopulateFault(FaultDef asset, in FaultRow row, ContentSet set)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("severity").enumValueIndex = (int)row.Severity;
            so.FindProperty("daysToFailure").intValue = row.DaysToFailure;
            so.FindProperty("repairCost").floatValue = row.RepairCost;
            so.FindProperty("teardownCostIfWrong").floatValue = row.TeardownCostIfWrong;
            so.FindProperty("rootCause").objectReferenceValue = set.Cause(row.RootCauseId);
            so.FindProperty("missedConsequence").stringValue = row.MissedConsequence ?? string.Empty;

            var sig = so.FindProperty("signature");
            sig.arraySize = row.Signature.Length;
            for (int i = 0; i < row.Signature.Length; i++)
            {
                var d = row.Signature[i];
                var e = sig.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("element").objectReferenceValue = Require(set.Element(d.ElementId), d.ElementId, row.Id);
                e.FindPropertyRelative("multiplier").floatValue = d.Multiplier;
                e.FindPropertyRelative("flatAdd").floatValue = d.FlatAdd;
                e.FindPropertyRelative("progressionOverSeverity").animationCurveValue =
                    AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            var valid = so.FindProperty("validOn");
            valid.arraySize = row.ValidOn.Length;
            for (int i = 0; i < row.ValidOn.Length; i++)
                valid.GetArrayElementAtIndex(i).objectReferenceValue = Require(set.Profile(row.ValidOn[i]), row.ValidOn[i], row.Id);

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static void FillCascades(in FaultRow row, ContentSet set)
        {
            var so = new SerializedObject(set.Fault(row.Id));
            var list = so.FindProperty("canCause");
            list.arraySize = row.CanCause.Length;
            for (int i = 0; i < row.CanCause.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = Require(set.Fault(row.CanCause[i]), row.CanCause[i], row.Id);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static MachineDef PopulateMachine(MachineDef asset, in MachineRow row, ContentSet set)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = row.Id;
            so.FindProperty("displayName").stringValue = row.Name;
            so.FindProperty("runTimeSeconds").floatValue = row.RunTimeSeconds;
            so.FindProperty("sampleVolumeMl").floatValue = row.SampleVolumeMl;
            so.FindProperty("costPerRun").floatValue = row.CostPerRun;
            so.FindProperty("baseNoisePercent").floatValue = row.Noise;
            so.FindProperty("calibrationDriftPerRun").floatValue = row.Drift;
            so.FindProperty("contaminationCarryoverPercent").floatValue = row.Carryover;
            so.FindProperty("requiresFumeHood").boolValue = row.FumeHood;
            so.FindProperty("requiresPreheat").boolValue = row.Preheat;
            so.FindProperty("preheatTargetC").floatValue = 100f;
            so.FindProperty("slots").intValue = 1;
            so.FindProperty("purchaseCost").intValue = row.PurchaseCost;
            so.FindProperty("footprint").vector2IntValue = new Vector2Int(1, 1);

            SetElementList(so.FindProperty("measures"), row.Measures, set, row.Id);
            SetElementList(so.FindProperty("cannotDetect"), row.CannotDetect, set, row.Id);

            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static void SetElementList(SerializedProperty list, string[] ids, ContentSet set, string owner)
        {
            list.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = Require(set.Element(ids[i]), ids[i], owner);
        }

        /// <summary>
        /// Fail loudly on a typo'd id. A silently-null reference here produces a fault whose signature
        /// quietly does nothing — exactly the kind of bug that hides for weeks.
        /// </summary>
        private static T Require<T>(T value, string id, string owner) where T : UnityEngine.Object
        {
            if (value == null)
                throw new KeyNotFoundException($"ContentTables: '{owner}' references unknown id '{id}'.");
            return value;
        }
    }
}
