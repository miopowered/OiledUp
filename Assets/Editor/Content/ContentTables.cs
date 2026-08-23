using Residue.Data;

namespace Residue.Editor.Content
{
    // ---------------------------------------------------------------------------------------------
    // Balance data lives here as CODE, not as hand-written .asset YAML.
    //
    // Why: a ScriptableObject .asset is a wall of GUIDs and fileIDs. You cannot review a balance
    // change in a pull request, you cannot diff "we doubled the water signature on hot baths", and
    // an agent editing YAML by hand will eventually corrupt a reference. These tables are the source
    // of truth; ContentBootstrap projects them into .asset files, and the same tables build in-memory
    // fixtures for the tests. Change a number here, run Residue > Content > Rebuild Definitions.
    //
    // DOMAIN: oil-based heat-treatment process fluids (§4.2). Quench oils, hardening oils and
    // corrosion-protection oils. Water-based polymer quenchants and aqueous cleaners are out of
    // scope; a cleaner appears here only as a CONTAMINANT of an oil, never as a fluid we analyse.
    // ---------------------------------------------------------------------------------------------

    internal readonly struct ElementRow
    {
        public readonly string Id, Name, Unit, Hint;
        public readonly ElementCategory Category;

        public ElementRow(string id, string name, string unit, ElementCategory category, string hint)
        {
            Id = id; Name = name; Unit = unit; Category = category; Hint = hint;
        }
    }

    internal readonly struct ThresholdRow
    {
        public readonly string ElementId;
        public readonly ThresholdMode Mode;
        public readonly float Baseline, Variance, NormalLimit, CautionLimit;

        public ThresholdRow(string elementId, ThresholdMode mode, float baseline, float variance,
                            float normalLimit, float cautionLimit)
        {
            ElementId = elementId; Mode = mode; Baseline = baseline; Variance = variance;
            NormalLimit = normalLimit; CautionLimit = cautionLimit;
        }
    }

    internal readonly struct ProfileRow
    {
        public readonly string Id, Name, OilGrade;
        public readonly float OilChangeHours;
        public readonly ThresholdRow[] Thresholds;

        public ProfileRow(string id, string name, string oilGrade, float oilChangeHours, ThresholdRow[] thresholds)
        {
            Id = id; Name = name; OilGrade = oilGrade; OilChangeHours = oilChangeHours; Thresholds = thresholds;
        }
    }

    internal readonly struct DeltaRow
    {
        public readonly string ElementId;
        public readonly float Multiplier, FlatAdd;

        public DeltaRow(string elementId, float multiplier, float flatAdd = 0f)
        {
            ElementId = elementId; Multiplier = multiplier; FlatAdd = flatAdd;
        }
    }

    internal readonly struct FaultRow
    {
        public readonly string Id, Name, RootCauseId, MissedConsequence;
        public readonly FaultSeverity Severity;
        public readonly DeltaRow[] Signature;
        public readonly string[] CanCause, ValidOn;
        public readonly int DaysToFailure;
        public readonly float RepairCost, TeardownCostIfWrong;

        public FaultRow(string id, string name, FaultSeverity severity, DeltaRow[] signature,
                        int daysToFailure, float repairCost, float teardownCostIfWrong,
                        string rootCauseId, string[] validOn, string missedConsequence,
                        string[] canCause = null)
        {
            Id = id; Name = name; Severity = severity; Signature = signature;
            DaysToFailure = daysToFailure; RepairCost = repairCost; TeardownCostIfWrong = teardownCostIfWrong;
            RootCauseId = rootCauseId; ValidOn = validOn; MissedConsequence = missedConsequence;
            CanCause = canCause ?? new string[0];
        }
    }

    internal readonly struct MachineRow
    {
        public readonly string Id, Name;
        public readonly float RunTimeSeconds, SampleVolumeMl, CostPerRun;
        public readonly string[] Measures, CannotDetect;
        public readonly float Noise, Drift, Carryover;
        public readonly bool FumeHood, Preheat;
        public readonly int PurchaseCost;

        public MachineRow(string id, string name, float runTime, float volumeMl, float costPerRun,
                          string[] measures, string[] cannotDetect, float noise, float drift,
                          float carryover, bool fumeHood, bool preheat, int purchaseCost)
        {
            Id = id; Name = name; RunTimeSeconds = runTime; SampleVolumeMl = volumeMl; CostPerRun = costPerRun;
            Measures = measures; CannotDetect = cannotDetect ?? new string[0];
            Noise = noise; Drift = drift; Carryover = carryover;
            FumeHood = fumeHood; Preheat = preheat; PurchaseCost = purchaseCost;
        }
    }

    internal readonly struct CauseRow
    {
        public readonly string Id, Name, Explanation;
        public CauseRow(string id, string name, string explanation)
        {
            Id = id; Name = name; Explanation = explanation;
        }
    }

    internal static class ContentTables
    {
        /// <summary>Quantities only the cooling curve tester can see. The §4.3 keystone trap.</summary>
        public static readonly string[] CoolingCurveOnly = { "CRmax", "TCRmax", "CR300", "T400" };

        // -- Elements -----------------------------------------------------------------------------

        public static readonly ElementRow[] Elements =
        {
            // --- Condition ---
            new("Visc40", "Viscosity @40C", "cSt", ElementCategory.FluidProperty,
                "the workhorse figure. Rises as the oil oxidises and polymerises; falls when " +
                "something thinner gets in, such as hydraulic oil. Both directions are bad, so it " +
                "is scored as a band around the grade nominal rather than a maximum."),

            new("Water", "Water", "ppm", ElementCategory.Contaminant,
                "leaking heat exchangers, washer carryover, condensation in a cold tank. Above " +
                "100 C water flashes to steam INSIDE the oil, so the same figure that is a quality " +
                "problem in a cold bath is a fire and explosion risk in a hot one."),

            new("Flash", "Flash Point", "C", ElementCategory.FluidProperty,
                "the temperature at which vapour above the oil will ignite. Falls when anything " +
                "volatile gets in. A falling flash point is a safety finding first and a quality " +
                "finding second."),

            new("TAN", "Total Acid Number", "mgKOH/g", ElementCategory.FluidProperty,
                "acid built up by oxidation. Climbs steadily with thermal ageing and sharply with " +
                "localised overheating."),

            new("Insol", "Insolubles", "%", ElementCategory.Contaminant,
                "sludge, carbon and scale suspended in the oil. Blankets the part surface during " +
                "the quench and slows heat extraction."),

            new("Demul", "Demulsibility", "min", ElementCategory.FluidProperty,
                "minutes for the oil to shed water it has picked up. Fresh oil separates quickly. " +
                "Surfactant contamination from a washer line destroys this, and the oil then holds " +
                "water instead of releasing it. Longer is worse."),

            new("Sap", "Saponification No.", "mgKOH/g", ElementCategory.Additive,
                "a proxy for how much of the ester and additive package is still present. Falls as " +
                "additives are consumed; a mismatch against the declared grade means the wrong " +
                "product went into the tank."),

            // --- Quench performance, ISO 9950 cooling curve ---
            new("CRmax", "Max Cooling Rate", "C/s", ElementCategory.FluidProperty,
                "the fastest the oil pulls heat out of the probe. The headline quench-speed figure. " +
                "Only a cooling curve tester measures this."),

            new("TCRmax", "Temp at Max Rate", "C", ElementCategory.FluidProperty,
                "where in the cooling process the oil is working hardest. Drifts when the vapour " +
                "blanket stage changes character. Scored as a band."),

            new("CR300", "Cooling Rate @300C", "C/s", ElementCategory.FluidProperty,
                "the single most important number in the panel. 300 C is where martensite forms, " +
                "so this is what decides whether the customer's parts come out hard. An oil can " +
                "pass every conventional test and still have lost this."),

            new("T400", "Time to 400C", "s", ElementCategory.FluidProperty,
                "how long the part spends in the danger band on the way down. Longer means more " +
                "chance of soft spots. Rises with sludge and with additive loss."),

            // --- Contamination and additive fingerprint ---
            new("Na", "Sodium", "ppm", ElementCategory.Contaminant,
                "salt dragged in from a neighbouring salt-bath line on parts or fixtures. Causes " +
                "corrosion and staining and shows up in nothing but an elemental scan."),

            new("Fe", "Iron", "ppm", ElementCategory.Contaminant,
                "scale and fines carried in on the parts themselves. Some is normal in any working " +
                "tank; a climb means filtration is not keeping up."),

            new("Ca", "Calcium", "ppm", ElementCategory.Additive,
                "detergent additive. Part of the fingerprint that identifies which product is " +
                "actually in the tank."),

            new("Zn", "Zinc", "ppm", ElementCategory.Additive,
                "anti-wear and antioxidant additive. A sharp change means the tank was topped up " +
                "with something that is not what the paperwork says."),

            new("P", "Phosphorus", "ppm", ElementCategory.Additive,
                "tracks zinc in most packages. Zinc and phosphorus moving together points at a " +
                "product change; one moving alone points at depletion.")
        };

        // -- Root causes --------------------------------------------------------------------------

        public static readonly CauseRow[] Causes =
        {
            new("normal_service", "Normal Service",
                "Readings consistent with hours in the tank. No action beyond the scheduled top-up."),

            new("heat_exchanger_leak", "Heat Exchanger / Cooling Coil Leak",
                "Water rising with a falling flash point and a collapsing demulsibility means the " +
                "cooling circuit is leaking into the bath. On a bath running above 100 C this is a " +
                "fire risk, not a quality issue: the water flashes to steam inside the oil and the " +
                "tank can erupt. Pull the bath, do not schedule it."),

            new("thermal_ageing", "Thermal Ageing",
                "Viscosity, acid number and insolubles all climbing together, cooling rate easing " +
                "off. This is the oil doing what oil does over time. Expected; the question is only " +
                "whether it has gone far enough to matter."),

            new("hydraulic_leak", "Hydraulic System Leak",
                "Viscosity DOWN and flash point down together. Something thinner has got into the " +
                "tank. Water does the same thing to flash point but raises water content; if the " +
                "water figure is clean, look at the hydraulics on the quench press."),

            new("washer_line_carryover", "Washer Line Carryover",
                "Cleaner surfactant entering the quench tank from the wash stage. Demulsibility " +
                "collapses, water climbs, and the quench curve is disturbed. THE OIL IS NOT THE " +
                "FAULT. Replace the charge without fixing the washer and the next sample looks " +
                "exactly the same."),

            new("additive_exhaustion", "Quench Additive Exhaustion",
                "The speed-improver package in an accelerated oil has been consumed. Viscosity, " +
                "water, acid number and insolubles all read normal, because none of them measure " +
                "what the oil is for. Only a cooling curve shows that the oil has stopped " +
                "quenching. A clean conventional panel is not a clean oil."),

            new("filtration_failure", "Filtration Failure",
                "Insolubles climbing with a slowing curve. Sludge and scale are blanketing the " +
                "parts. The oil itself may be perfectly serviceable once cleaned up."),

            new("salt_bath_dragin", "Salt Bath Drag-in",
                "Sodium in a quench oil comes from a salt line, carried over on parts or fixtures. " +
                "Nothing in the condition panel flags it, and the customer will see it as staining " +
                "and corrosion weeks later."),

            new("wrong_product", "Wrong Product Topped Up",
                "The additive fingerprint does not match the declared grade. Somebody topped the " +
                "tank up from the wrong drum. The oil in the tank may be fine on its own terms and " +
                "still be wrong for the job."),

            new("localised_overheating", "Localised Overheating",
                "Acid number and insolubles up with viscosity high in the band, in an oil that is " +
                "not otherwise old. Points at a hot spot: an overloaded charge, a failed agitator, " +
                "or parts entering far above the intended temperature.")
        };

        // -- Profiles -----------------------------------------------------------------------------
        //
        // Water is the number to compare across these. A cold bath tolerates roughly ten times what
        // a martempering bath does, for the physical reason in the Water source hint. That spread is
        // the §1.1.2 pillar: the same figure means different things on different fluids.

        private static ThresholdRow[] CoolingCurve(float crMax, float crMaxNormal, float crMaxCaution,
                                                   float cr300, float cr300Normal, float cr300Caution,
                                                   float t400, float t400Normal, float t400Caution)
            => new[]
            {
                new ThresholdRow("CRmax", ThresholdMode.LowerLimit, crMax, 0.07f, crMaxNormal, crMaxCaution),
                new ThresholdRow("TCRmax", ThresholdMode.DeviationBand, 580f, 0.04f, 0.05f, 0.12f),
                new ThresholdRow("CR300", ThresholdMode.LowerLimit, cr300, 0.08f, cr300Normal, cr300Caution),
                new ThresholdRow("T400", ThresholdMode.UpperLimit, t400, 0.12f, t400Normal, t400Caution)
            };

        private static ThresholdRow[] Fingerprint(float ca, float zn, float p)
            => new[]
            {
                new ThresholdRow("Ca", ThresholdMode.LowerLimit, ca, 0.09f, ca * 0.68f, ca * 0.40f),
                new ThresholdRow("Zn", ThresholdMode.LowerLimit, zn, 0.09f, zn * 0.68f, zn * 0.40f),
                new ThresholdRow("P", ThresholdMode.LowerLimit, p, 0.09f, p * 0.68f, p * 0.40f),
                new ThresholdRow("Na", ThresholdMode.UpperLimit, 8f, 0.35f, 40f, 90f),
                new ThresholdRow("Fe", ThresholdMode.UpperLimit, 22f, 0.35f, 80f, 180f)
            };

        private static ThresholdRow[] Join(params ThresholdRow[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;

            var all = new ThresholdRow[n];
            int i = 0;
            foreach (var p in parts)
            {
                foreach (var row in p) all[i++] = row;
            }
            return all;
        }

        public static readonly ProfileRow[] Profiles =
        {
            // The tutorial fluid. Wide bands everywhere, so a first-day player can be wrong by a
            // margin and still read the panel correctly.
            new("hardening_oil_general", "General Hardening Oil", "ISO VG 22", 6000f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 22f, 0.04f, 0.07f, 0.16f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 90f, 0.35f, 500f, 1200f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 205f, 0.05f, 170f, 150f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.35f, 0.30f, 1.6f, 2.6f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.04f, 0.35f, 0.18f, 0.42f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 12f, 0.25f, 26f, 42f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 3.2f, 0.10f, 2.1f, 1.3f)
                },
                CoolingCurve(112f, 90f, 74f, 16f, 12.5f, 9.0f, 5.0f, 7.2f, 9.8f),
                Fingerprint(1400f, 300f, 280f))),

            new("quench_oil_cold", "Cold Quench Oil", "ISO VG 22", 5000f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 21f, 0.04f, 0.06f, 0.15f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 85f, 0.35f, 400f, 1000f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 200f, 0.05f, 170f, 150f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.35f, 0.30f, 1.5f, 2.5f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.04f, 0.35f, 0.15f, 0.40f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 12f, 0.25f, 25f, 40f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 3.4f, 0.10f, 2.2f, 1.4f)
                },
                CoolingCurve(118f, 95f, 78f, 18f, 14f, 10f, 4.6f, 6.5f, 9.0f),
                Fingerprint(1500f, 320f, 300f))),

            // Runs at 120-200 C. Water tolerance collapses, because above 100 C it flashes to steam
            // inside the oil. Viscosity is also far higher, so a hydraulic leak shows up hard.
            new("quench_oil_martempering", "Martempering Oil (hot bath)", "ISO VG 100", 7000f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 105f, 0.04f, 0.06f, 0.14f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 55f, 0.35f, 120f, 300f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 250f, 0.04f, 220f, 200f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.40f, 0.30f, 1.8f, 2.8f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.05f, 0.35f, 0.20f, 0.45f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 14f, 0.25f, 28f, 44f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 3.0f, 0.10f, 2.0f, 1.2f)
                },
                CoolingCurve(78f, 62f, 50f, 11f, 8.5f, 6.0f, 7.5f, 10.5f, 14f),
                Fingerprint(1300f, 280f, 260f))),

            // Vacuum furnaces pull hard vacuum over the bath, so anything volatile is unacceptable
            // and the viscosity spec is very tight.
            new("quench_oil_vacuum", "Vacuum Quench Oil", "ISO VG 15", 8000f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 15f, 0.03f, 0.04f, 0.10f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 40f, 0.35f, 100f, 250f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 230f, 0.04f, 205f, 190f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.25f, 0.30f, 1.2f, 2.0f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.03f, 0.35f, 0.12f, 0.30f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 10f, 0.25f, 22f, 36f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 2.6f, 0.10f, 1.7f, 1.0f)
                },
                CoolingCurve(96f, 78f, 64f, 14f, 11f, 8.0f, 5.4f, 7.6f, 10.4f),
                Fingerprint(900f, 200f, 190f))),

            // Speed-improved. The additive package IS the product, so exhaustion is invisible to
            // every conventional test and shows only on the curve.
            new("quench_oil_accelerated", "Accelerated Quench Oil", "ISO VG 22", 4500f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 23f, 0.04f, 0.06f, 0.15f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 85f, 0.35f, 350f, 900f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 195f, 0.05f, 168f, 148f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.40f, 0.30f, 1.5f, 2.5f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.04f, 0.35f, 0.15f, 0.40f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 13f, 0.25f, 25f, 40f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 5.2f, 0.10f, 3.4f, 2.1f)
                },
                CoolingCurve(142f, 118f, 98f, 30f, 24f, 17f, 3.4f, 4.8f, 6.6f),
                Fingerprint(1800f, 520f, 480f))),

            // Not a quenchant. Judged on water separation and film, not on quench speed, so it has
            // no cooling curve thresholds at all.
            new("corrosion_protection_oil", "Corrosion Protection Oil", "ISO VG 32", 9000f, Join(
                new[]
                {
                    new ThresholdRow("Visc40", ThresholdMode.DeviationBand, 32f, 0.05f, 0.08f, 0.18f),
                    new ThresholdRow("Water", ThresholdMode.UpperLimit, 70f, 0.35f, 450f, 1100f),
                    new ThresholdRow("Flash", ThresholdMode.LowerLimit, 210f, 0.05f, 175f, 155f),
                    new ThresholdRow("TAN", ThresholdMode.UpperLimit, 0.30f, 0.30f, 1.4f, 2.4f),
                    new ThresholdRow("Insol", ThresholdMode.UpperLimit, 0.03f, 0.35f, 0.14f, 0.34f),
                    new ThresholdRow("Demul", ThresholdMode.UpperLimit, 9f, 0.25f, 20f, 34f),
                    new ThresholdRow("Sap", ThresholdMode.LowerLimit, 4.0f, 0.10f, 2.6f, 1.6f)
                },
                Fingerprint(1100f, 380f, 350f)))
        };

        private static readonly string[] AllFluids =
        {
            "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering",
            "quench_oil_vacuum", "quench_oil_accelerated", "corrosion_protection_oil"
        };

        private static readonly string[] Quenchants =
        {
            "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering",
            "quench_oil_vacuum", "quench_oil_accelerated"
        };

        private static readonly string[] HotBaths = { "quench_oil_martempering", "quench_oil_vacuum" };
        private static readonly string[] Accelerated = { "quench_oil_accelerated" };

        // -- Faults -------------------------------------------------------------------------------
        //
        // A signature must be decisive across the WHOLE healthy range, not just at the typical
        // value, and which end is dangerous depends on the threshold mode:
        //
        //   UpperLimit  the LOW end. Healthy is floored at 35% of nominal, so a mostly-multiplier
        //               signature barely moves an already-low reading.
        //   LowerLimit  the HIGH end. Healthy has no upper clamp, so a +3 sigma sample times a
        //               gentle multiplier can still land in Caution instead of Critical.
        //
        // Tuning to the typical value passes most of the time and fails the max-severity test on an
        // unlucky roll, which is the worst possible way to find out.

        public static readonly FaultRow[] Faults =
        {
            new("water_ingress", "Water Ingress", FaultSeverity.Imminent,
                new DeltaRow[]
                {
                    new("Water", 8f, 1200f), new("Flash", 0.72f), new("Demul", 2.2f, 12f)
                },
                daysToFailure: 4, repairCost: 11500f, teardownCostIfWrong: 6200f,
                rootCauseId: "heat_exchanger_leak", validOn: AllFluids,
                missedConsequence: "Left in service. Water flashed to steam as the charge went in and the bath erupted. Fire crews attended, the line is down and the incident is with the insurer. This is the one that hurts people, not just parts."),

            new("thermal_ageing", "Thermal Ageing", FaultSeverity.Developing,
                new DeltaRow[]
                {
                    new("Visc40", 1.26f), new("TAN", 4f, 2.6f),
                    new("Insol", 3f, 0.25f), new("CRmax", 0.82f)
                },
                daysToFailure: 13, repairCost: 4200f, teardownCostIfWrong: 5400f,
                rootCauseId: "thermal_ageing", validOn: AllFluids,
                missedConsequence: "Run past its life. Hardness drifted out of spec across a fortnight of production before anyone connected it to the oil."),

            // Confusable with water on flash point alone. Water content is the discriminator.
            new("hydraulic_carryover", "Hydraulic Oil Carryover", FaultSeverity.Developing,
                new DeltaRow[] { new("Visc40", 0.78f), new("Flash", 0.75f) },
                daysToFailure: 10, repairCost: 3600f, teardownCostIfWrong: 5400f,
                rootCauseId: "hydraulic_leak", validOn: Quenchants,
                missedConsequence: "The leak kept diluting the bath. Quench speed drifted and a run of parts came back soft."),

            // KEYSTONE. The fault is upstream in the washer, not in the tank.
            new("cleaner_carryover", "Cleaner Carryover", FaultSeverity.Developing,
                new DeltaRow[]
                {
                    new("Demul", 4f, 38f), new("Water", 4f, 500f), new("CR300", 0.80f)
                },
                daysToFailure: 9, repairCost: 1400f, teardownCostIfWrong: 6400f,
                rootCauseId: "washer_line_carryover", validOn: Quenchants,
                missedConsequence: "Signed off while the washer kept feeding surfactant into the tank. The oil held water instead of shedding it and the parts stained."),

            // KEYSTONE. Moves nothing but cooling-curve quantities, so the whole conventional panel
            // reads clean. Do not add a condition-panel element to this signature.
            new("additive_exhaustion", "Quench Additive Exhaustion", FaultSeverity.Developing,
                new DeltaRow[]
                {
                    new("CR300", 0.42f), new("CRmax", 0.86f), new("T400", 1.6f, 2.4f)
                },
                daysToFailure: 11, repairCost: 5800f, teardownCostIfWrong: 5200f,
                rootCauseId: "additive_exhaustion", validOn: Accelerated,
                missedConsequence: "Passed on a clean conventional panel. The oil had stopped quenching weeks earlier, and a month of parts failed hardness testing at the customer."),

            new("sludge_loading", "Sludge and Carbon Loading", FaultSeverity.Developing,
                new DeltaRow[]
                {
                    new("Insol", 6f, 0.45f), new("CRmax", 0.72f), new("T400", 2.2f, 6.0f)
                },
                daysToFailure: 13, repairCost: 2600f, teardownCostIfWrong: 4800f,
                rootCauseId: "filtration_failure", validOn: Quenchants,
                missedConsequence: "Sludge kept building. Parts came out with soft spots where the blanket clung to the surface.",
                canCause: new[] { "thermal_ageing" }),

            // Nothing in the condition panel flags this. Only an elemental scan sees it.
            new("salt_dragin", "Salt Bath Drag-in", FaultSeverity.Developing,
                new DeltaRow[] { new("Na", 6f, 95f), new("Fe", 1.8f, 30f) },
                daysToFailure: 14, repairCost: 2100f, teardownCostIfWrong: 4600f,
                rootCauseId: "salt_bath_dragin", validOn: Quenchants,
                missedConsequence: "Salt kept accumulating. The customer's finished parts corroded in storage and came back as a claim."),

            new("wrong_product", "Wrong Product Topped Up", FaultSeverity.Developing,
                new DeltaRow[]
                {
                    new("Zn", 0.28f), new("P", 0.30f), new("Sap", 0.5f), new("Visc40", 1.18f)
                },
                daysToFailure: 8, repairCost: 3100f, teardownCostIfWrong: 5000f,
                rootCauseId: "wrong_product", validOn: AllFluids,
                missedConsequence: "The tank stayed on the wrong product. Parts were quenched in an oil that was never qualified for them."),

            new("localised_overheating", "Localised Overheating", FaultSeverity.Imminent,
                new DeltaRow[]
                {
                    new("TAN", 3.2f, 2.6f), new("Insol", 4f, 0.42f), new("Visc40", 1.26f)
                },
                daysToFailure: 6, repairCost: 7400f, teardownCostIfWrong: 5600f,
                rootCauseId: "localised_overheating", validOn: HotBaths,
                missedConsequence: "The hot spot was never found. The oil coked, the bath had to be dumped and the furnace stripped.")
        };

        // -- Instruments --------------------------------------------------------------------------
        //
        // Every instrument except the cooling curve tester lists the cooling-curve quantities in
        // cannotDetect. That is what the operator manual's CANNOT DETECT page renders, and it is the
        // fair warning that makes the additive-exhaustion trap legitimate rather than a gotcha.

        public static readonly MachineRow[] Machines =
        {
            // The new ferrography: definitive, brutally slow, and the only thing that answers the
            // question that actually matters.
            new("cooling_curve", "Cooling Curve Tester", 900f, 5f, 60f,
                measures: new[] { "CRmax", "TCRmax", "CR300", "T400" },
                cannotDetect: null,
                noise: 0.03f, drift: 0.004f, carryover: 0.04f,
                fumeHood: false, preheat: false, purchaseCost: 52000),

            new("karl_fischer", "Karl Fischer Titrator", 240f, 5f, 18f,
                measures: new[] { "Water" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.01f, drift: 0.004f, carryover: 0.18f,
                fumeHood: false, preheat: false, purchaseCost: 22000),

            new("viscometer", "Viscometer", 300f, 10f, 6f,
                measures: new[] { "Visc40" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.02f, drift: 0.005f, carryover: 0.05f,
                fumeHood: false, preheat: true, purchaseCost: 15000),

            new("flash_point", "Flash Point Tester", 300f, 15f, 12f,
                measures: new[] { "Flash" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.02f, drift: 0.003f, carryover: 0.02f,
                fumeHood: true, preheat: false, purchaseCost: 17000),

            new("tan_titrator", "TAN Titrator", 480f, 8f, 22f,
                measures: new[] { "TAN", "Sap" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.03f, drift: 0.006f, carryover: 0.09f,
                fumeHood: true, preheat: false, purchaseCost: 19000),

            new("centrifuge", "Centrifuge / Insolubles", 180f, 10f, 8f,
                measures: new[] { "Insol", "Demul" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.05f, drift: 0.004f, carryover: 0.11f,
                fumeHood: false, preheat: false, purchaseCost: 13000),

            new("elemental", "Elemental Analyser", 180f, 5f, 14f,
                measures: new[] { "Na", "Fe", "Ca", "Zn", "P" },
                cannotDetect: CoolingCurveOnly,
                noise: 0.03f, drift: 0.004f, carryover: 0.06f,
                fumeHood: false, preheat: false, purchaseCost: 45000)
        };
    }
}
