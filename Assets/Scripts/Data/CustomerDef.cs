using System.Collections.Generic;
using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// A firm that sends the lab work: who they are, which plants they draw from, which oils they
    /// run, and how much their paperwork can be trusted (§6.1).
    ///
    /// <para>
    /// <b>Customer identity is not flavour.</b> §6.1 wants "a client who cuts corners sends samples
    /// quietly all drawn from the same drum", and that only reads as a diagnosis rather than a
    /// coincidence if the sender has a history — a name that recurs across a contract, with tanks the
    /// player has seen before. <see cref="Reliability"/> and the two propensities beside it are what
    /// let a run's arrivals be shaped by <i>who</i> sent them.
    /// </para>
    ///
    /// <para>
    /// <b>A customer changes where samples come from and never what an instrument reads.</b> Nothing
    /// here touches a threshold, a fault signature or the measurement pipeline. Hard rule 1 is not
    /// negotiable: a sloppy customer is not a customer whose chemistry lies, it is a customer whose
    /// paperwork and drum discipline are worth checking. The tell for both is in the box — the note
    /// beside the vials, and readings that come back identical across supposedly different baths.
    /// </para>
    ///
    /// <para>
    /// The propensities are read by nobody yet. Reconciliation and the same-drum trap are #32; this
    /// type exists so that issue has a sender to act on rather than a table of anonymous vials.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Residue/Customer", fileName = "Customer_")]
    public sealed class CustomerDef : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private CustomerIndustry industry = CustomerIndustry.AutomotiveSupplier;

        [Tooltip("Prefix on this customer's job numbers, e.g. 'NW' -> NW-04127. Short enough to read " +
                 "off a delivery note at a glance.")]
        [SerializeField] private string orderPrefix;

        [Tooltip("Plant codes that appear at the front of this customer's tank tags. A carton comes " +
                 "from one plant, so a note's lines all share whichever of these was drawn.")]
        [SerializeField] private List<string> sites = new();

        [Tooltip("Fluids this customer actually runs. An arrival can only be one of these, which is " +
                 "what makes a mix distinctive rather than every customer sending everything.")]
        [SerializeField] private List<EquipmentProfileDef> oils = new();

        [Tooltip("How carefully this firm does its paperwork and its drum discipline. The label the " +
                 "player learns over a contract; the two chances below are what it means mechanically.")]
        [SerializeField] private CustomerReliability reliability = CustomerReliability.Routine;

        [Tooltip("Chance that a delivery's note does not match what is in the carton (#32). Read by " +
                 "nothing yet — #29 models the propensity, #32 acts on it.")]
        [SerializeField, Range(0f, 1f)] private float paperworkSlipChance;

        [Tooltip("§6.1's trap: chance that a delivery is drawn from one drum and labelled as several " +
                 "tanks. Also unread until #32. The tell is identical readings across the note's " +
                 "supposedly different baths, so it stays fair.")]
        [SerializeField, Range(0f, 1f)] private float sameDrumChance;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public CustomerIndustry Industry => industry;
        public string OrderPrefix => string.IsNullOrEmpty(orderPrefix) ? "JOB" : orderPrefix;
        public IReadOnlyList<string> Sites => sites;
        public IReadOnlyList<EquipmentProfileDef> Oils => oils;
        public CustomerReliability Reliability => reliability;
        public float PaperworkSlipChance => paperworkSlipChance;
        public float SameDrumChance => sameDrumChance;

        public bool Runs(EquipmentProfileDef profile) => profile != null && oils.Contains(profile);

        public bool Runs(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return false;
            foreach (var oil in oils)
            {
                if (oil != null && oil.Id == profileId) return true;
            }
            return false;
        }

        public override string ToString() => $"Customer:{Id}";
    }
}
