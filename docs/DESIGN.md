# RESIDUE — Technical & Design Specification

> **Status:** source of truth for what the game is. Kept verbatim as authored.
>
> Where the implementation deviates from this document, the deviation and its reason are recorded
> in [`CLAUDE.md`](../CLAUDE.md) under *Deviations from the spec*, and in the commit that made it.
> Do not silently "fix" the implementation to match this document without reading that section
> first — several differences are deliberate.

**Working title. Co-op first-person oil analysis simulator. Unity 6 / URP.**

This document is written to be handed to a coding agent. It is ordered so that each milestone is independently buildable and testable. Do not skip ahead — later systems assume the data model from Section 4 exists.

---

## 1. Concept

You and 1–3 friends staff a remote laboratory outpost (*Außenstelle*) for a large oil company. Crates of used-oil samples arrive from rigs, haul trucks, gearboxes and compressors in the field. Your job is to run laboratory tests, interpret the results, and file a verdict on each sample: **NORMAL**, **MONITOR**, or **CRITICAL — PULL EQUIPMENT**.

Verdicts have consequences that land days later. Miss a failing bearing and a compressor seizes — a six-figure loss with your name on the report. Flag everything critical to be safe and you burn the company's money on unnecessary teardowns. Both directions punish you.

### 1.1 Design pillars

1. **Real chemistry is the content.** The wear-metal diagnostic tree is genuinely learnable. Players who understand *cause* (silicon ingress causing iron wear) beat players who memorised one row of a table. Never randomise the chemistry — that destroys the satisfaction of learning it.
2. **The bottleneck moves, the knowledge doesn't.** Difficulty scales via volume, machine occupancy, contamination risk and sample ambiguity — never by making the science lie to you.
3. **Two-sided failure.** False negatives and false positives both cost. There is no safe default answer.
4. **Layout is the skill ceiling.** Following PlateUp: the between-shift lab-rebuild layer is where mastery lives. At least one shared bottleneck must exist at all times.
5. **Co-op by information asymmetry.** Prep, machine operation and interpretation are genuinely different jobs. Players must talk.

### 1.2 Session shape

Fixed-length contracts (PlateUp-style runs), not an endless drift. A contract is 10–20 in-game days. Between days: spend budget, rebuild the lab, review pending consequences. Run ends on contract completion or on financial failure. Meta-progression unlocks persist across runs.

---

## 2. Art Direction & Asset Pipeline

The stated requirement: low-poly, heavy use of purchased/generated assets, but the result must not look like a mismatched asset flip, and the developer (or an AI tool) must be able to author new props easily.

**The solution is a style contract enforced at import time.** Assets from different sources clash because of their *materials and textures*, not their geometry. So: strip every imported material and re-apply one of our own. This is non-negotiable and should be automated.

### 2.1 The style contract

| Property | Rule |
|---|---|
| Shading | Flat-shaded. Hard normals everywhere, no smoothing groups except on cylinders >12 sides. |
| Textures | **None.** No albedo maps, no normal maps, no roughness maps. |
| Colour | Vertex colours OR a single 16×16 palette atlas sampled by UV. Pick one project-wide (recommend palette atlas — easier for AI-generated meshes). |
| Materials | Exactly 4 project materials: `M_Palette_Opaque`, `M_Palette_Emissive`, `M_Palette_Transparent` (glassware, fluids), `M_Palette_Cutout` (decals, labels). |
| Poly budget | Props 100–800 tris. Machines 800–3000 tris. Characters 1500 tris. |
| Scale | 1 unit = 1 metre. Player eye height 1.7 m. Bench height 0.9 m. Enforce on import. |
| Pivot | Props pivot at base centre. Handheld items pivot at grip point. |
| Detail | Geometry detail only. A dial is a cylinder, not a texture. |

### 2.2 Palette

A single 16×16 PNG, point-filtered, no mipmaps below level 2. Rows are hue families, columns are value steps. All meshes UV-map to texel centres.

Suggested palette identity for this game — desaturated industrial with high-chroma accents reserved for *information*:

- **Neutrals (rows 0–1):** cold greys, off-white, concrete, steel. The lab body.
- **Warms (row 2):** oxidised orange, rust, aged brass, amber. Oil and corrosion.
- **Cools (row 3):** teal, deep blue, cyan. Coolant, water, screens.
- **Signal colours (row 4):** pure red, amber, green. **Reserved exclusively for verdict/alarm state.** Never use these for decoration — if red only ever means "critical", the player reads the room instantly.

### 2.3 Automated import enforcement

Write an `AssetPostprocessor` at `Assets/Editor/StyleEnforcer.cs`:

```
OnPreprocessModel:
  - importer.globalScale set so 1 unit = 1 m
  - importer.importMaterials = false
  - importer.importCameras = false, importCameras = false, importLights = false
  - importer.normals = ImportNormals.Calculate
  - importer.normalSmoothingAngle = 0   // hard normals -> flat shading
  - importer.optimizeMeshVertices = true

OnPostprocessModel:
  - Assign M_Palette_Opaque to every renderer
  - Log warning if tri count exceeds budget for its folder category
  - Log warning if any mesh has >1 UV channel or vertex colours when in atlas mode
```

Anything dropped into `Assets/Art/Imported/` is therefore automatically conformed. This is what makes mixed-source assets cohere — the developer can buy any low-poly pack and it arrives already wearing the project's skin.

### 2.4 Lighting

- **URP Forward+**, baked lightmaps for the static lab, one realtime directional for outside, realtime point/spot only on player flashlight and machine indicator lights.
- Bake at low resolution (10–20 texels/unit). Flat-shaded geometry plus soft baked GI is the entire look.
- Use a **global volume** with mild bloom (threshold high, so only emissives bloom), slight vignette, and colour grading via LUT. No motion blur, no depth of field, no chromatic aberration — they fight the crisp flat-shaded read.
- Fog: linear, tinted cold. Sells the outpost isolation and hides the level boundary cheaply.

### 2.5 Authoring workflow for the developer

- **Blender** with a saved startup file containing the palette material and a 1 m reference cube. Model → assign palette colours by face → export FBX to `Assets/Art/Imported/`. No UV unwrapping needed beyond snapping to palette texels; use a "Set Palette Colour" script that assigns UVs to a chosen texel centre.
- **AI-generated meshes** (Meshy, Tripo, etc.) will arrive too dense and textured. Pipeline: import → Blender decimate to budget → shade flat → re-assign palette UVs → export. Budget ~10 minutes per prop. The import enforcer handles the rest.
- **ProBuilder** in-editor for the lab architecture itself — walls, benches, doorways. Faster than round-tripping to Blender and stays on-grid.

### 2.6 First-person presentation

- No visible body initially (saves animation work). Hands are two simple flat-shaded meshes with a 3-state pose: empty, holding-vial, holding-crate.
- Held items render on a **separate camera with its own near clip plane** to prevent wall clipping. Standard FPS setup.
- Interaction is a raycast from camera centre, 2.5 m range, with a small crosshair that changes shape on a valid target. No highlight outlines — they don't suit flat shading. Use a subtle emissive pulse on the target instead.

---

## 3. Technical Stack

| Concern | Choice | Reason |
|---|---|---|
| Engine | Unity 6 LTS, URP | Stated requirement |
| Netcode | Netcode for GameObjects (NGO) | No physics-critical interactions; first-party is fine and better documented in Unity 6 |
| Transport | Unity Transport + Relay | No port forwarding, no Steam dependency for early testing |
| Lobby | Unity Lobby service | Join-code flow, easy to swap for Steam lobbies later |
| Voice | Vivox with 3D positional falloff | Proximity chat is a *design feature*, not a nicety |
| Input | Input System package | Rebinding, gamepad support later |
| Data | ScriptableObjects for definitions, JSON for save | Designer-editable content, portable saves |
| Testing | Unity Test Framework | The chemistry model must have unit tests — see §5.6 |

### 3.1 Authority model

**Host-authoritative, fully.** The host owns:

- All sample state (composition, contamination, remaining volume)
- All machine state (occupancy, calibration drift, queue)
- Economy, day timer, contract state
- Consequence resolution

Clients own **only** their own player transform and look direction. Every interaction is a `ServerRpc` request; the host validates and replies with a `ClientRpc` or NetworkVariable change.

This is deliberately conservative. There is no fast-twitch action in this game, so latency is irrelevant and the simplicity is worth far more than responsiveness. **Never let a client compute a test result.**

### 3.2 Networked object strategy

Do **not** spawn a NetworkObject per vial. A busy shift could have 200+ vials and that will drown you.

Instead:
- Samples live in a server-side `Dictionary<SampleId, SampleState>`.
- Physical vial props are **local-only pooled GameObjects** carrying a `SampleId`.
- Their location is represented as a server-side enum + reference: `Held(clientId)`, `InMachine(machineId, slot)`, `OnSurface(surfaceId, slotIndex)`, `InCrate(crateId)`, `InFridge(slotIndex)`.
- When location changes, the server broadcasts and each client re-parents its local prop. Snap-to-slot, no interpolation needed.

Only **crates** (large, physically carried, few at a time) get real NetworkObjects with NetworkTransform.

---

## 4. Data Model

All definitions are ScriptableObjects in `Assets/Data/`. All runtime state is plain C# classes, serialisable to JSON.

### 4.1 Element definitions

```csharp
[CreateAssetMenu] public class ElementDef : ScriptableObject {
    public string id;              // "Fe", "Cu", "Si", "H2O", "TBN", "Visc40", "Soot"
    public string displayName;
    public string unit;            // "ppm", "%", "mgKOH/g", "cSt"
    public ElementCategory category; // WearMetal, Contaminant, Additive, FluidProperty
    public string sourceHint;      // "gears, cylinder liners, shafts" — shown in the reference book
}
```

### 4.2 Equipment profiles

Thresholds are **per equipment type**. 60 ppm iron is routine in a haul truck engine and alarming in a hydraulic system. This is the single most important source of legitimate difficulty.

```csharp
[CreateAssetMenu] public class EquipmentProfileDef : ScriptableObject {
    public string id;               // "diesel_engine_heavy", "gearbox_industrial", ...
    public string displayName;
    public List<Threshold> thresholds;
    public float defaultOilChangeHours;
    public string baseOilGrade;     // "15W-40"
}

[Serializable] public class Threshold {
    public ElementDef element;
    public float normalMax;
    public float cautionMax;      // above this = critical
    public float newOilBaseline;  // for additives/TBN which start high and fall
    public bool inverted;         // true for TBN, viscosity-loss cases
}
```

**Starting threshold table** (game-tuned, based on typical industry ranges — treat as balance values, not real-world spec):

*Heavy diesel engine:*

| Element | Normal < | Caution < | Critical ≥ |
|---|---|---|---|
| Fe | 50 | 100 | 100 |
| Cu | 20 | 40 | 40 |
| Cr | 10 | 20 | 20 |
| Pb | 15 | 30 | 30 |
| Sn | 8 | 15 | 15 |
| Al | 15 | 25 | 25 |
| Si | 15 | 25 | 25 |
| Na | 20 | 50 | 50 |
| K | 20 | 40 | 40 |
| Water % | 0.05 | 0.15 | 0.15 |
| Soot % | 1.5 | 3.0 | 3.0 |
| TBN (inverted) | >6 | >3 | ≤3 |
| Visc@100°C | ±5% | ±12% | ±12% |

*Industrial gearbox* — same elements, but Fe normal <100 / critical ≥250, Cu normal <15, and water is far more punishing (critical ≥0.05%).

*Hydraulic system* — extremely tight: Fe normal <15, and **particle count is the primary metric** (ISO 4406 code), not spectroscopy.

Author at least 6 equipment profiles before shipping.

### 4.3 Fault archetypes

This is your content core. A sample is generated by selecting a fault (or none), applying its signature, then adding noise.

```csharp
[CreateAssetMenu] public class FaultDef : ScriptableObject {
    public string id;
    public string displayName;         // shown only after resolution
    public Severity severity;          // Benign, Developing, Imminent
    public List<ElementDelta> signature;
    public List<FaultDef> canCause;    // cascade: Si ingress -> abrasive wear
    public List<EquipmentProfileDef> validOn;
    public int daysToFailure;          // if unaddressed
    public float repairCost;
    public float teardownCostIfWrong;  // cost of a false positive on this
}

[Serializable] public class ElementDelta {
    public ElementDef element;
    public float multiplier;   // applied to baseline
    public float flatAdd;
    public AnimationCurve progressionOverSeverity;
}
```

**Ship with at least these 14 faults:**

| Fault | Signature | The trap |
|---|---|---|
| Bearing overlay wear | Pb↑↑, Sn↑, Cu↑ | Pb+Sn together = already late. Must be CRITICAL. |
| Bushing wear | Cu↑↑, Pb normal | Looks like bearings to a novice. Usually MONITOR. |
| Ring wear | Cr↑, Fe↑, Soot↑ | Cr is the discriminator |
| Liner scuffing | Fe↑↑, Cr↑, Soot↑↑ | |
| **Dirt ingress** | **Si↑↑, Fe↑, Al↑** | **The keystone trap. Root cause is a failed air filter, NOT the gearbox. Replacing the component doesn't fix it.** |
| Coolant leak | Na↑↑, K↑, Water↑, Visc↑ | Na without K may be a seawater/additive false flag |
| Water ingress (condensation) | Water↑, Fe↑ mild, no Na/K | Distinguish from coolant by absence of Na/K |
| Fuel dilution | Visc↓↓, Flashpoint↓ | Viscosity *drops* — inverted reading |
| Oxidation / overheating | FTIR oxidation↑, Visc↑, TAN↑ | |
| Soot loading | Soot↑↑, Visc↑ | Benign in isolation on a diesel |
| Wrong oil added | Additive pack mismatch, Visc off-grade | Detected by additive elements (Zn, P, Ca, Mo) not matching the declared grade |
| Additive depletion | Zn↓, P↓, TBN↓ | End of oil life, not equipment failure. Verdict: MONITOR + change oil |
| Gear tooth spalling | Fe↑↑, large ferrous particles on ferrography only | **ICP misses it — particles too large for the plasma.** Only ferrography or particle count catches it. This teaches players that a clean ICP is not a clean sample. |
| Seal failure | Si↑ (silicone seal material), Water↑ | Si from seal ≠ Si from dirt; the Al ratio discriminates |

The last two entries are the ones that make expert play meaningful. Implement them.

### 4.4 Sample state

```csharp
public class SampleState {
    public SampleId id;
    public string equipmentTag;          // "RIG-7 COMPRESSOR B"
    public EquipmentProfileDef profile;
    public float hoursSinceOilChange;
    public string fieldTechNote;         // may be wrong, vague, or absent
    public DateTime collectedDay;

    // GROUND TRUTH — server only, never sent to clients
    public List<FaultDef> actualFaults;
    public Dictionary<string,float> trueValues;

    // Physical
    public float volumeMl;               // starts 100ml, tests consume it
    public float temperatureC;
    public bool isSettled;               // needs agitation if not
    public Dictionary<string,float> contamination;  // carried-over elements

    // Player-facing
    public List<TestResult> results;
    public Verdict? filedVerdict;
    public string filedRootCause;        // optional; bonus payout if correct
}
```

**`trueValues` must never be serialised into a ClientRpc.** Add an editor test that asserts this.

### 4.5 Machine definitions

```csharp
[CreateAssetMenu] public class MachineDef : ScriptableObject {
    public string id;
    public string displayName;
    public float runTimeSeconds;
    public float sampleVolumeMl;         // consumed per run
    public float costPerRun;             // consumables
    public List<ElementDef> measures;
    public List<ElementDef> cannotDetect; // e.g. ICP cannot see large particles
    public float baseNoisePercent;
    public float calibrationDriftPerRun;
    public float contaminationCarryoverPercent;
    public bool requiresFumeHood;
    public bool requiresPreheat;
    public float preheatTargetC;
    public int slots;                    // autosampler upgrade increases this
    public int purchaseCost;
    public Vector2Int footprint;         // bench grid cells
}
```

**Starting machine roster:**

| Machine | Time | Vol | Measures | Notes |
|---|---|---|---|---|
| ICP Spectrometer | 180 s | 5 ml | All wear metals, additives, Si, Na, K | **Blind to particles >8 µm** |
| FTIR Spectrometer | 120 s | 3 ml | Oxidation, nitration, soot, water (coarse), glycol | Fast broad screen |
| Viscometer | 300 s | 10 ml | Visc@40, Visc@100 | Requires preheat to bath temp |
| Karl Fischer Titrator | 240 s | 5 ml | Water (precise ppm) | High contamination carryover |
| TAN/TBN Titrator | 480 s | 8 ml | Acid/base number | Slow, fume hood required |
| Particle Counter | 120 s | 15 ml | ISO 4406 code | High volume cost |
| Ferrography | 900 s | 5 ml | Particle morphology, wear mode | **Definitive but very slow and expensive** |
| Centrifuge (prep) | 180 s | 0 ml | — | Shared bottleneck, single unit at start |

Note the volume economy: a 100 ml sample cannot take the full panel. **Test ordering is a real decision.** Ferrography answers everything but costs 15 minutes of machine time you rarely have.

---

## 5. Core Systems

### 5.1 Sample lifecycle

```
CrateArrives → Unload → Log(barcode/manual) → [Fridge | Bench]
  → Prep(agitate, decant, preheat, dilute) → Machine(load, run, unload, clean)
  → Results → Cross-reference history → File verdict → Archive
  → [days later] Consequence resolves
```

Each arrow is a player action. Implement as a state machine on `SampleState` with server-side validation of legal transitions.

**Logging:** samples arrive with a paper label. Player must enter the equipment tag into the terminal to associate physical vial with database record. Mis-logging is a real failure mode (and the barcode-scanner upgrade removes it — see §6).

### 5.2 The contamination system

**This is the most important mechanic in the design. Build it early.**

Every machine has a `residue` dictionary. On completing a run:

```
foreach element in sample.trueValues:
    machine.residue[element] += sample.trueValues[element] * machine.contaminationCarryoverPercent
```

On loading a new sample, the residue transfers *into that sample's measured values*:

```
measured[element] = trueValue[element] 
                  + machine.residue[element] * TRANSFER_RATE
                  + sample.contamination[element]
                  + noise
```

Cleaning a machine (a 20–40 s player action at the wash station, consuming solvent) zeroes `residue`. Solvent is a purchasable consumable — so cleaning has a monetary cost and players *will* be tempted to skip it.

**Why this is the best mechanic here:** rushing doesn't just cost time, it poisons your information. A skipped flush after a bearing-failure sample makes the next sample show elevated copper. The player either catches it (re-run: more time, more volume) or files a false positive and a healthy compressor gets torn down. It also forces the "did you flush the line?" conversation that co-op games live on.

Give the player a detectable tell: a **blank run** (distilled solvent through the machine) shows residue directly. It costs machine time. Good players blank after every critical sample; rushed players find out the hard way.

### 5.3 Calibration drift

Each machine accumulates `driftPercent += calibrationDriftPerRun` each run, with a random sign per machine per day. Drift silently scales all readings.

A **Certified Reference Sample** (known values, purchased consumable) can be run to measure drift, then the machine can be recalibrated. This costs machine time and money.

The dread: if you discover a machine drifted 18% high, **every verdict you filed since it started drifting is suspect.** Show the player a list of affected archived samples and let them re-open them for re-testing — if they still have volume left. Beautiful escalating pressure for almost no implementation cost.

### 5.4 Verdict & consequence resolution

On filing a verdict, server records it. On day `N + faultDef.daysToFailure`, resolve:

| Filed | Reality | Outcome |
|---|---|---|
| CRITICAL | Fault present | Full payout + accuracy bonus |
| CRITICAL | No fault | Unnecessary teardown cost, reputation hit |
| MONITOR | Developing fault | Partial payout, sample re-sent next cycle with worse numbers |
| MONITOR | Imminent fault | Equipment fails. Major loss. |
| NORMAL | Fault present | **Catastrophic.** Cost scales with fault severity. Named in the incident report. |
| NORMAL | No fault | Standard payout |

**Root-cause bonus:** if the player also files the correct root cause (e.g. "air filter failure" rather than "gearbox wear" for the silicon case), pay a significant bonus. This is what rewards understanding over table-lookup.

### 5.5 Layout / build mode

Between days, the lab is editable. Grid-based (0.5 m cells) placement on a floorplan with fixed walls. Machines occupy `footprint` cells and require a bench or floor space plus a power connection.

**Mandatory shared bottleneck: the fume hood.** Both solvent work (cleaning) and TAN/TBN + sample prep require it, and it has finite adjacent bench slots. Place it near intake and prep flows while machines starve; place it centrally and everyone collides in the aisle. Without a genuine tradeoff like this, players hit the ceiling in an hour and it becomes the grind we're avoiding.

Secondary bottlenecks: single wash station (limited taps), single centrifuge, the sample fridge (distance from door determines whether crates pile up), and aisle width (two players carrying crates cannot pass in a 1-cell aisle — make this literal collision, it generates comedy).

Implement as: `LayoutGrid` with `PlacedObject` records, validated server-side, saved with the run. NavMesh is not needed if there are no NPCs — see §7 note.

### 5.6 Chemistry model tests

Write unit tests before writing the UI. `Assets/Tests/EditMode/ChemistryTests.cs`:

- A sample with no fault, zero contamination, zero drift produces values inside `normalMax` for every element in its profile.
- Each fault archetype, applied at maximum severity, produces at least one element above `cautionMax`.
- Bearing-overlay and bushing-wear signatures are distinguishable by the Pb/Cu ratio in >95% of 1000 generated samples.
- Gear spalling produces a *clean* ICP result and a *dirty* ferrography result. (Guards the §4.3 trap.)
- Contamination carryover from a critical sample into a clean sample can push it above `cautionMax`. (Proves the mechanic bites.)
- `SampleState.trueValues` does not appear in any serialised client payload.

---

## 6. Progression

### 6.1 Within a run

**Days 1–3 — one client, clean panel.** Diesel engines only. 4–6 samples/day. Generous time. Tutorial faults: dirt ingress, coolant leak, bearing wear. Teach the panel.

**Days 4–8 — second contract.** Gearboxes and hydraulics added, so thresholds now differ per sample and the player must check the profile. 10–14 samples/day. Queue begins to outpace hands. Introduce sample history (repeat equipment from earlier days).

**Days 9–14 — complications.**
- Arctic samples arrive cold and must be warmed before viscosity means anything (skip it and you get a false high).
- Blended oils confound the spectrometer's additive readings.
- A client who cuts corners sends samples that are quietly all drawn from the same drum — identical results across supposedly different equipment. Catching this is a large bonus; missing it means filing 8 wrong verdicts.
- Rush samples that jump the queue with a hard deadline.

**Days 15–20 — the assumption break.** A new synthetic formulation arrives where the additive baseline is completely different and the player's instincts actively mislead them. Old thresholds produce false positives. A new reference document is issued mid-contract; reading it is optional and expensive in time.

### 6.2 Between runs (meta)

Unlocks that persist. Critically, **most upgrades should buy you out of a specific failure mode, not just out of slowness** — this makes the shop feel like a response to how your run is going wrong.

| Upgrade | Removes |
|---|---|
| Barcode scanner + printer | Mis-logging errors entirely |
| Autosampler (ICP) | Standing at the machine; +8 queue slots |
| Automatic dilutor | Manual prep step + prep-error contamination |
| Second ICP | The primary occupancy bottleneck |
| Closed-loop solvent recycler | Solvent cost; makes cleaning free |
| Inline blank automation | Auto-detects residue above threshold |
| Larger sample intake (250 ml) | The volume economy, partially |
| Field kit dispatch | Lets you request a re-draw instead of guessing |
| Reference library expansion | Faster history cross-referencing |

### 6.3 Guarding against solved-game decay

Diagnosis games get solved once the community writes the table down. Accept this. The design answer is that **knowledge stays stable but the bottleneck moves** — pressure comes from volume, machine occupancy, contamination risk and *ambiguity in the sample*, never from the science lying.

The canonical hard decision, which stays hard even for a player who knows the table cold:

> Iron is elevated. Silicon is borderline. You have 8 ml left. Ferrography would settle it but takes 15 minutes and you have four samples queued behind this one. The ICP is due for calibration. What do you file?

Generate these deliberately: a `SampleAmbiguityBudget` per day that forces N samples into the borderline band where a single test cannot resolve them.

---

## 7. The Optional Weird Layer

Keep this restrained, keep it opt-in at contract selection, and **do not build a monster.**

Some samples return readings that are not chemically possible — a spectral peak at a wavelength matching no element, a viscosity that changes between two runs of the same vial, a ferrography image showing particle morphology that isn't wear debris.

There is no chase and no entity. The escalation is **procedural**:

1. A new field appears on the report form: *ANOMALOUS — DESCRIBE*.
2. A sealed courier begins collecting certain vials. You do not see who.
3. A rule is issued: flagged samples are not to be re-run.
4. The rule is amended. Then amended again.
5. Certain equipment tags stop appearing in the client database, including ones you filed on last week.

The dread comes from the paperwork changing. This is far cheaper to build and much harder to get tired of than anything with a model and an AI. Ambiguity is the point — never confirm anything.

Implementation: a `ContractModifier` that injects anomalous values, plus a `FormSchema` that the server can version mid-run. Roughly a week of work, not a system.

---

## 8. Milestones

Each milestone must be playable and testable before proceeding.

### M0 — Grey box, single player (target: 1 week)
- First-person controller, 1.7 m eye height, no visible body
- One room, ProBuilder walls, one bench
- Pick up / put down a vial via raycast interaction
- One machine: press button, wait 3 s, print a number to a UI panel

**Acceptance:** you can carry a vial to a machine, run it, and see a number.

### M1 — Chemistry model + tests (1 week)
- All ScriptableObject definitions from §4
- 3 equipment profiles, 8 fault archetypes, 4 machines
- Sample generator: profile + fault → trueValues + noise
- All unit tests from §5.6 passing
- Debug console command to dump a generated sample's ground truth

**Acceptance:** tests green. No rendering work in this milestone.

### M2 — The verdict loop (1 week)
- Terminal UI: sample list, results per sample, verdict buttons
- Reference book UI: element sources, threshold tables per profile
- Consequence resolution with a day-advance debug button
- Money and reputation counters

**Acceptance:** single player can run a full day solo and see win/loss feedback. **This is the first point where you can tell whether the game is fun.** If reading results and deciding isn't interesting alone, stop and re-examine before adding co-op.

### M3 — Contamination + calibration (1 week)
- Machine residue, transfer, cleaning action, solvent consumable
- Blank runs
- Calibration drift, reference samples, retroactive suspicion UI

**Acceptance:** a deliberately skipped flush after a critical sample causes a demonstrable false positive.

### M4 — Co-op (2 weeks)
- NGO + Relay + Lobby with join codes
- Host-authoritative sample and machine state per §3.1
- Local-prop / server-state split per §3.2
- Vivox proximity voice with distance falloff
- 4-player capacity

**Acceptance:** four clients, one host, 60 samples in a session, no desync, no client ever receives `trueValues`.

### M5 — Day cycle + layout mode (2 weeks)
- Crate arrival, day timer, end-of-day summary
- Grid-based lab rebuild with the fume-hood bottleneck
- Shop with machines and upgrades
- Save/load of full run state

**Save/load in co-op is the hard part, not the gameplay.** Host-authoritative full world snapshot to JSON. Decide *now* whether a client can rejoin mid-run (recommend: yes, rejoin by client ID restores their held items). Retrofitting this later is miserable.

**Acceptance:** a run can be saved, quit, reloaded and continued with all four players.

### M6 — Art pass (3 weeks)
- StyleEnforcer AssetPostprocessor
- Palette atlas + 4 materials
- Lab architecture, 8 machine models, ~40 props
- Baked lighting, volume post-processing, fog
- Hand meshes + 3-pose held-item system

**Acceptance:** an asset from a random purchased low-poly pack, dropped into `Assets/Art/Imported/`, appears in-scene already conforming to the style contract with no manual work.

### M7 — Content & balance (4 weeks)
- 6 equipment profiles, 14 faults, full 20-day contract arc
- Ambiguity budget tuning
- Meta-progression unlocks
- Audio: machine loops, alarms, ambient outpost, footsteps

### M8 — Weird layer, optional (1 week)
- Contract modifier, anomalous value injection, versioned form schema

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| **Game is boring solo, so co-op just adds people to a boring game** | M2 gate. Do not proceed to M4 until the solo verdict loop is engaging. |
| Community solves the chemistry in a week | Expected. §6.3 — move the bottleneck, don't randomise the science. |
| Contamination feels like arbitrary punishment | Always provide a detectable tell (blank runs). Never punish something the player couldn't have checked. |
| Too much reading, not enough doing | Physical prep actions (agitate, decant, preheat, clean) must be real, hand-operated tasks with real time cost — not menu clicks. |
| Networked save/load breaks late | M5, not M8. Build it before there's much state to save. |
| Mixed assets look incoherent | §2.3 automated enforcement. This is solved at import, not by discipline. |
| Scope | The weird layer (M8) and half the fault archetypes are cuttable. The contamination system is not. |

**No NPCs are required anywhere in this design.** No NavMesh, no pathfinding, no crowd sync. This removes the largest technical risk category from the genre. Do not add walking clients or delivery drivers without a very good reason.

---

## 10. The Prototype That Answers Everything

Before M3, build this in isolation and play it with one friend:

> One sample. Three machines. A contamination flag. A verdict button. 8 ml of oil remaining and a ferrography run that costs 5 ml and 15 minutes.

If two people arguing over whether to spend the last of the sample on ferrography is tense, the whole game works and everything after is content. If it isn't, no amount of machines, upgrades or art will save it.

Build the ugliest possible version first.
