using Residue.Data;

namespace Residue.Editor.Content
{
    // ---------------------------------------------------------------------------------------------
    // Balance data lives here as CODE, not as hand-written .asset YAML.
    //
    // Why: a ScriptableObject .asset is a wall of GUIDs and fileIDs. You cannot review a balance
    // change in a pull request, you cannot diff "we doubled the coolant-leak sodium signature", and
    // an agent editing YAML by hand will eventually corrupt a reference. These tables are the source
    // of truth; ContentBootstrap projects them into .asset files, and the same tables build in-memory
    // fixtures for the tests. Change a number here, run Residue > Content > Rebuild Definitions.
    //
    // The .asset files ARE committed (Unity needs stable GUIDs for scene references) but they are
    // generated artifacts. Never edit them in the Inspector and expect it to survive a rebuild.
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
        public readonly string Id, Name, RootCauseId;
        public readonly FaultSeverity Severity;
        public readonly DeltaRow[] Signature;
        public readonly string[] CanCause, ValidOn;
        public readonly int DaysToFailure;
        public readonly float RepairCost, TeardownCostIfWrong;

        public FaultRow(string id, string name, FaultSeverity severity, DeltaRow[] signature,
                        int daysToFailure, float repairCost, float teardownCostIfWrong,
                        string rootCauseId, string[] validOn, string[] canCause = null)
        {
            Id = id; Name = name; Severity = severity; Signature = signature;
            DaysToFailure = daysToFailure; RepairCost = repairCost; TeardownCostIfWrong = teardownCostIfWrong;
            RootCauseId = rootCauseId; ValidOn = validOn; CanCause = canCause ?? new string[0];
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
        // -- Elements -----------------------------------------------------------------------------

        public static readonly ElementRow[] Elements =
        {
            new("Fe", "Iron",      "ppm", ElementCategory.WearMetal,  "gears, cylinder liners, shafts, rolling elements"),
            new("Cu", "Copper",    "ppm", ElementCategory.WearMetal,  "bushings, bearing cages, oil cooler cores, thrust washers"),
            new("Cr", "Chromium",  "ppm", ElementCategory.WearMetal,  "piston rings, plated liners, hardened shafts"),
            new("Pb", "Lead",      "ppm", ElementCategory.WearMetal,  "bearing overlay. Appears only once the overlay is breached"),
            new("Sn", "Tin",       "ppm", ElementCategory.WearMetal,  "bearing overlay, bushing plating"),
            new("Al", "Aluminium", "ppm", ElementCategory.WearMetal,  "pistons, housings, thrust bearings"),
            new("Ni", "Nickel",    "ppm", ElementCategory.WearMetal,  "alloy steels, valve train, turbine components"),

            new("FeLarge", "Large Ferrous Debris", "ppm", ElementCategory.WearMetal,
                "particles above 8 um. Spectrometers CANNOT see these - the plasma does not fully " +
                "atomise them. Only ferrography or particle counting will find this."),

            new("Si",     "Silicon",       "ppm", ElementCategory.Contaminant, "airborne dirt via a failed intake seal, OR silicone seal material"),
            new("Na",     "Sodium",        "ppm", ElementCategory.Contaminant, "coolant inhibitor, seawater, some detergent additives"),
            new("K",      "Potassium",     "ppm", ElementCategory.Contaminant, "coolant inhibitor. Sodium WITHOUT potassium is usually not coolant"),
            new("Water",  "Water",         "%",   ElementCategory.Contaminant, "condensation, coolant leak, washdown ingress"),
            new("Soot",   "Soot",          "%",   ElementCategory.Contaminant, "incomplete combustion, blowby, late injection"),
            new("Glycol", "Glycol",        "%",   ElementCategory.Contaminant, "coolant. Confirms a leak that sodium alone only suggests"),
            new("ISO",    "ISO 4406 Code", "code",ElementCategory.Contaminant, "particle count. The PRIMARY metric on hydraulic systems"),

            new("Zn", "Zinc",       "ppm", ElementCategory.Additive, "anti-wear additive (ZDDP). Falls as the oil is consumed"),
            new("P",  "Phosphorus", "ppm", ElementCategory.Additive, "anti-wear additive, tracks zinc"),
            new("Ca", "Calcium",    "ppm", ElementCategory.Additive, "detergent. A mismatch means the wrong oil went in"),
            new("Mo", "Molybdenum", "ppm", ElementCategory.Additive, "friction modifier. Strong fingerprint for oil brand"),

            new("TBN",    "Total Base Number", "mgKOH/g", ElementCategory.FluidProperty, "remaining acid-neutralising reserve. Falls over oil life"),
            new("TAN",    "Total Acid Number", "mgKOH/g", ElementCategory.FluidProperty, "acid build-up from oxidation"),
            new("Visc40", "Viscosity @40C",    "cSt",     ElementCategory.FluidProperty, "grade check. Rises with oxidation, falls with fuel dilution"),
            new("Visc100","Viscosity @100C",   "cSt",     ElementCategory.FluidProperty, "operating-temperature grade check"),
            new("Ox",     "Oxidation",         "Abs/cm",  ElementCategory.FluidProperty, "FTIR. Thermal degradation of the base oil"),
            new("Nit",    "Nitration",         "Abs/cm",  ElementCategory.FluidProperty, "FTIR. Blowby of combustion gases, gas-engine wear"),
            new("Flash",  "Flash Point",       "C",       ElementCategory.FluidProperty, "drops sharply with fuel dilution")
        };

        // -- Root causes --------------------------------------------------------------------------

        public static readonly CauseRow[] Causes =
        {
            new("normal_wear", "Normal Wear",
                "Readings consistent with hours in service. No action beyond the scheduled interval."),
            new("air_filter_failure", "Air Filter / Intake Seal Failure",
                "Silicon rising WITH aluminium and iron means abrasive dirt is entering through the intake. " +
                "The worn component is a symptom. Replace it without fixing the filter and the next sample " +
                "looks identical."),
            new("coolant_seal_failure", "Coolant Seal / Head Gasket Failure",
                "Sodium AND potassium together, with water and rising viscosity. Sodium alone is more often " +
                "an additive artefact or salt spray."),
            new("breather_condensation", "Breather / Condensation",
                "Water without sodium, potassium or glycol. Ambient moisture, not coolant."),
            new("bearing_failure", "Bearing Overlay Failure",
                "Lead and tin together mean the overlay is already breached and the substrate is exposed. " +
                "This is late-stage, not early warning."),
            new("bushing_wear", "Bushing Wear",
                "Copper without lead. Bronze bushings shed copper long before anything structural fails."),
            new("injector_fault", "Injector / Fuel System Fault",
                "Viscosity FALLS and flash point drops. Unburnt fuel is diluting the oil."),
            new("ring_liner_wear", "Ring / Liner Wear",
                "Chromium is the discriminator - it comes from the ring face, not the liner."),
            new("gear_tooth_spalling", "Gear Tooth Spalling",
                "Large ferrous flakes. A spectrometer reports a nearly clean sample because the particles " +
                "are too large to atomise. A clean ICP is not a clean sample.")
        };

        // -- Equipment profiles -------------------------------------------------------------------

        public static readonly ProfileRow[] Profiles =
        {
            new("diesel_engine_heavy", "Heavy Diesel Engine", "15W-40", 500f, new ThresholdRow[]
            {
                new("Fe",      ThresholdMode.UpperLimit,    20f,  0.30f, 50f,   100f),
                new("Cu",      ThresholdMode.UpperLimit,     8f,  0.30f, 20f,    40f),
                new("Cr",      ThresholdMode.UpperLimit,     3f,  0.35f, 10f,    20f),
                new("Pb",      ThresholdMode.UpperLimit,     5f,  0.35f, 15f,    30f),
                new("Sn",      ThresholdMode.UpperLimit,     2f,  0.35f,  8f,    15f),
                new("Al",      ThresholdMode.UpperLimit,     5f,  0.30f, 15f,    25f),
                new("Ni",      ThresholdMode.UpperLimit,     1f,  0.40f,  5f,    10f),
                new("Si",      ThresholdMode.UpperLimit,     6f,  0.30f, 15f,    25f),
                new("Na",      ThresholdMode.UpperLimit,     5f,  0.40f, 20f,    50f),
                new("K",       ThresholdMode.UpperLimit,     3f,  0.40f, 20f,    40f),
                new("Water",   ThresholdMode.UpperLimit,  0.01f,  0.50f, 0.05f,  0.15f),
                new("Soot",    ThresholdMode.UpperLimit,   0.4f,  0.45f, 1.5f,   3.0f),
                new("Glycol",  ThresholdMode.UpperLimit,    0f,   0f,    0.01f,  0.05f),
                new("FeLarge", ThresholdMode.UpperLimit,    1f,  0.50f, 10f,    25f),
                new("Ox",      ThresholdMode.UpperLimit,    8f,  0.25f, 20f,    30f),
                new("Nit",     ThresholdMode.UpperLimit,    6f,  0.25f, 20f,    30f),
                new("TAN",     ThresholdMode.UpperLimit,    1f,  0.30f,  2f,     4f),
                new("TBN",     ThresholdMode.LowerLimit,   10f,  0.12f,  6f,     3f),
                new("Zn",      ThresholdMode.LowerLimit, 1150f,  0.10f, 800f,  500f),
                new("P",       ThresholdMode.LowerLimit, 1050f,  0.10f, 750f,  480f),
                new("Ca",      ThresholdMode.LowerLimit, 2600f,  0.10f,1800f, 1200f),
                new("Mo",      ThresholdMode.LowerLimit,   60f,  0.20f,  30f,   15f),
                new("Flash",   ThresholdMode.LowerLimit,  220f,  0.05f, 200f,  180f),
                new("Visc100", ThresholdMode.DeviationBand,14.5f,0.04f, 0.05f,  0.12f),
                new("Visc40",  ThresholdMode.DeviationBand,110f, 0.04f, 0.05f,  0.12f)
            }),

            // Iron runs far higher in a gearbox and means far less. Water means far more.
            new("gearbox_industrial", "Industrial Gearbox", "ISO VG 320", 8000f, new ThresholdRow[]
            {
                new("Fe",      ThresholdMode.UpperLimit,    45f, 0.30f, 100f,  250f),
                new("Cu",      ThresholdMode.UpperLimit,     6f, 0.30f,  15f,   30f),
                new("Cr",      ThresholdMode.UpperLimit,     2f, 0.35f,   8f,   15f),
                new("Pb",      ThresholdMode.UpperLimit,     4f, 0.35f,  20f,   40f),
                new("Sn",      ThresholdMode.UpperLimit,     2f, 0.35f,  10f,   20f),
                new("Al",      ThresholdMode.UpperLimit,     4f, 0.30f,  15f,   25f),
                new("Ni",      ThresholdMode.UpperLimit,     1f, 0.40f,   5f,   10f),
                new("Si",      ThresholdMode.UpperLimit,     7f, 0.30f,  15f,   25f),
                new("Na",      ThresholdMode.UpperLimit,     4f, 0.40f,  25f,   50f),
                new("K",       ThresholdMode.UpperLimit,     3f, 0.40f,  25f,   50f),
                new("Water",   ThresholdMode.UpperLimit, 0.005f, 0.50f, 0.02f, 0.05f),
                new("Glycol",  ThresholdMode.UpperLimit,     0f, 0f,    0.01f, 0.05f),
                new("FeLarge", ThresholdMode.UpperLimit,     2f, 0.50f,  15f,   35f),
                new("Ox",      ThresholdMode.UpperLimit,     6f, 0.25f,  20f,   30f),
                new("TAN",     ThresholdMode.UpperLimit,   0.8f, 0.30f,   2f,    4f),
                new("Zn",      ThresholdMode.LowerLimit,  400f,  0.12f, 250f,  150f),
                new("P",       ThresholdMode.LowerLimit,  380f,  0.12f, 240f,  140f),
                new("Visc40",  ThresholdMode.DeviationBand,320f, 0.04f, 0.05f, 0.12f),
                new("Visc100", ThresholdMode.DeviationBand,24f,  0.04f, 0.05f, 0.12f)
            }),

            // Extremely tight, and particle count is the primary metric rather than spectroscopy.
            new("hydraulic_system", "Hydraulic System", "ISO VG 46", 4000f, new ThresholdRow[]
            {
                new("Fe",      ThresholdMode.UpperLimit,    5f,  0.30f, 15f,   30f),
                new("Cu",      ThresholdMode.UpperLimit,    3f,  0.30f, 10f,   20f),
                new("Cr",      ThresholdMode.UpperLimit,    1f,  0.35f,  5f,   10f),
                new("Pb",      ThresholdMode.UpperLimit,    1f,  0.35f,  5f,   10f),
                new("Sn",      ThresholdMode.UpperLimit,    1f,  0.35f,  5f,   10f),
                new("Al",      ThresholdMode.UpperLimit,    2f,  0.30f,  8f,   15f),
                new("Si",      ThresholdMode.UpperLimit,    4f,  0.30f, 10f,   20f),
                new("Na",      ThresholdMode.UpperLimit,    3f,  0.40f, 15f,   30f),
                new("K",       ThresholdMode.UpperLimit,    2f,  0.40f, 15f,   30f),
                new("Water",   ThresholdMode.UpperLimit, 0.004f, 0.50f,0.02f, 0.05f),
                new("ISO",     ThresholdMode.UpperLimit,   15f,  0.08f, 18f,   21f),
                new("FeLarge", ThresholdMode.UpperLimit,  0.5f,  0.50f,  6f,   15f),
                new("Ox",      ThresholdMode.UpperLimit,    5f,  0.25f, 18f,   28f),
                new("TAN",     ThresholdMode.UpperLimit,  0.5f,  0.30f,1.5f,    3f),
                new("Zn",      ThresholdMode.LowerLimit, 320f,   0.12f,200f,  120f),
                new("P",       ThresholdMode.LowerLimit, 300f,   0.12f,190f,  110f),
                new("Visc40",  ThresholdMode.DeviationBand,46f,  0.03f,0.04f, 0.10f)
            })
        };

        private static readonly string[] AllProfiles =
            { "diesel_engine_heavy", "gearbox_industrial", "hydraulic_system" };

        private static readonly string[] EngineOnly = { "diesel_engine_heavy" };
        private static readonly string[] GearboxOnly = { "gearbox_industrial" };

        // -- Faults -------------------------------------------------------------------------------

        public static readonly FaultRow[] Faults =
        {
            new("bearing_overlay_wear", "Bearing Overlay Wear", FaultSeverity.Imminent,
                // Lead must clear the CAUTION ceiling on every profile this runs on, and the gearbox
                // tolerates far more lead than an engine (critical at 40 vs 30). §4.3 is explicit that
                // Pb and Sn together mean the overlay is already breached, so this has to read
                // Critical everywhere or the fault is one the player can never correctly call.
                new DeltaRow[] { new("Pb", 5f, 38f), new("Sn", 5f, 11f), new("Cu", 2.5f, 14f) },
                daysToFailure: 4, repairCost: 8500f, teardownCostIfWrong: 4200f,
                rootCauseId: "bearing_failure", validOn: AllProfiles),

            // Reads like bearing wear to a novice, but lead stays flat. Usually only MONITOR.
            new("bushing_wear", "Bushing Wear", FaultSeverity.Developing,
                new DeltaRow[] { new("Cu", 5f, 28f), new("Pb", 1.15f) },
                daysToFailure: 14, repairCost: 1800f, teardownCostIfWrong: 3600f,
                rootCauseId: "bushing_wear", validOn: AllProfiles),

            new("ring_wear", "Piston Ring Wear", FaultSeverity.Developing,
                new DeltaRow[] { new("Cr", 5f, 16f), new("Fe", 2.2f, 28f), new("Soot", 1.8f, 0.5f) },
                daysToFailure: 11, repairCost: 5200f, teardownCostIfWrong: 4200f,
                rootCauseId: "ring_liner_wear", validOn: EngineOnly),

            // THE KEYSTONE TRAP. Root cause is the air filter, not the component showing the wear.
            new("dirt_ingress", "Dirt Ingress", FaultSeverity.Developing,
                new DeltaRow[] { new("Si", 4f, 20f), new("Fe", 2.4f, 32f), new("Al", 2.8f, 11f) },
                daysToFailure: 9, repairCost: 900f, teardownCostIfWrong: 5400f,
                rootCauseId: "air_filter_failure", validOn: AllProfiles,
                canCause: new[] { "ring_wear" }),

            new("coolant_leak", "Coolant Leak", FaultSeverity.Imminent,
                new DeltaRow[]
                {
                    new("Na", 6f, 46f), new("K", 5f, 20f), new("Water", 6f, 0.11f),
                    new("Glycol", 1f, 0.028f), new("Visc100", 1.14f)
                },
                daysToFailure: 5, repairCost: 6400f, teardownCostIfWrong: 4800f,
                rootCauseId: "coolant_seal_failure", validOn: AllProfiles),

            // Distinguished from coolant purely by the ABSENCE of sodium, potassium and glycol.
            new("water_ingress", "Water Ingress (Condensation)", FaultSeverity.Developing,
                new DeltaRow[] { new("Water", 6f, 0.16f), new("Fe", 1.5f, 12f) },
                daysToFailure: 15, repairCost: 700f, teardownCostIfWrong: 3200f,
                rootCauseId: "breather_condensation", validOn: AllProfiles),

            // The inverted reading: viscosity FALLS.
            new("fuel_dilution", "Fuel Dilution", FaultSeverity.Developing,
                new DeltaRow[] { new("Visc100", 0.80f), new("Visc40", 0.78f), new("Flash", 0.74f) },
                daysToFailure: 10, repairCost: 3100f, teardownCostIfWrong: 3800f,
                rootCauseId: "injector_fault", validOn: EngineOnly),

            // THE SECOND TRAP. Sits almost entirely in FeLarge, which the ICP cannot see.
            new("gear_spalling", "Gear Tooth Spalling", FaultSeverity.Imminent,
                // Fe barely moves ON PURPOSE. The debris is too large to dissolve or atomise, so the
                // spectrometer sees a near-normal iron figure while the metal is visibly failing.
                new DeltaRow[] { new("FeLarge", 1f, 48f), new("Fe", 1.15f, 6f) },
                daysToFailure: 6, repairCost: 12500f, teardownCostIfWrong: 5600f,
                rootCauseId: "gear_tooth_spalling", validOn: GearboxOnly)
        };

        // -- Machines -----------------------------------------------------------------------------

        public static readonly MachineRow[] Machines =
        {
            new("icp", "ICP Spectrometer", 180f, 5f, 12f,
                measures: new[] { "Fe", "Cu", "Cr", "Pb", "Sn", "Al", "Ni", "Si", "Na", "K", "Zn", "P", "Ca", "Mo" },
                cannotDetect: new[] { "FeLarge" },
                noise: 0.03f, drift: 0.004f, carryover: 0.06f,
                fumeHood: false, preheat: false, purchaseCost: 45000),

            new("ftir", "FTIR Spectrometer", 120f, 3f, 6f,
                measures: new[] { "Ox", "Nit", "Soot", "Water", "Glycol" },
                cannotDetect: null,
                noise: 0.06f, drift: 0.003f, carryover: 0.02f,
                fumeHood: false, preheat: false, purchaseCost: 28000),

            new("viscometer", "Viscometer", 300f, 10f, 4f,
                measures: new[] { "Visc40", "Visc100" },
                cannotDetect: null,
                noise: 0.02f, drift: 0.005f, carryover: 0.04f,
                fumeHood: false, preheat: true, purchaseCost: 15000),

            new("karl_fischer", "Karl Fischer Titrator", 240f, 5f, 18f,
                measures: new[] { "Water" },
                cannotDetect: null,
                noise: 0.01f, drift: 0.004f, carryover: 0.18f,
                fumeHood: false, preheat: false, purchaseCost: 22000),

            new("tan_tbn", "TAN/TBN Titrator", 480f, 8f, 22f,
                measures: new[] { "TAN", "TBN" },
                cannotDetect: null,
                noise: 0.03f, drift: 0.006f, carryover: 0.09f,
                fumeHood: true, preheat: false, purchaseCost: 19000),

            new("particle_counter", "Particle Counter", 120f, 15f, 9f,
                measures: new[] { "ISO", "FeLarge" },
                cannotDetect: null,
                noise: 0.05f, drift: 0.004f, carryover: 0.11f,
                fumeHood: false, preheat: false, purchaseCost: 26000),

            // Definitive but brutally slow. Answers the gear-spalling question nothing else can.
            new("ferrography", "Ferrography", 900f, 5f, 45f,
                measures: new[] { "Fe", "FeLarge" },
                cannotDetect: null,
                noise: 0.02f, drift: 0.002f, carryover: 0.03f,
                fumeHood: false, preheat: false, purchaseCost: 38000)
        };
    }
}
