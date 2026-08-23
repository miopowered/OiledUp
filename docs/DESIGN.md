# OILED UP — Technical & Design Specification

**Co-op first-person heat-treatment oil analysis simulator. Unity 6 / URP.**

> **Domain:** the lab analyses **oil-based heat-treatment process fluids** for industrial
> customers — quench oils (cold, martempering, vacuum, accelerated), general hardening oils and
> corrosion-protection oils. Water-based polymer quenchants and aqueous cleaners are out of scope.
>
> Where the implementation deviates from this document, the deviation and its reason are recorded
> in [`CLAUDE.md`](../CLAUDE.md) under *Deviations from the spec*. Do not "correct" the code to
> match this document without reading that section — several differences are deliberate.
>
> **Section numbers are load-bearing.** Code comments cite `§5.2`, `§4.3` and so on throughout.
> Renumbering silently invalidates them.

---

## 1. Concept

You and 1–3 friends staff the laboratory of a heat-treatment fluids supplier. Customers — forges,
fastener works, spring makers, automotive suppliers — run your quench oils in their hardening
lines. Every week they ship you samples drawn from their tanks, and your job is to tell them
whether the oil is still fit to quench with.

You run the panel, interpret the results, and file a verdict on each sample: **NORMAL**,
**MONITOR**, or **CRITICAL — TANK OUT OF SERVICE**.

Verdicts land days later. Miss water in a bath running at 180 °C and a batch of parts comes back
cracked — or the tank catches fire. Condemn a tank that was fine and the customer dumps eight
thousand litres of serviceable oil and stops a line to do it. Both directions cost real money and
both have your name on the report.

### 1.1 Design pillars

1. **Real chemistry is the content.** Quench oil degradation is genuinely learnable. Players who
   understand *cause* — that a falling flash point means something volatile got in, and that
   whether it is water or hydraulic oil changes what you tell the customer — beat players who
   memorised one row of a table. Never randomise the chemistry.
2. **The bottleneck moves, the knowledge doesn't.** Difficulty scales via volume, instrument
   occupancy, contamination risk and sample ambiguity — never by making the science lie.
3. **Two-sided failure.** A missed fault scraps a heat-treat batch or starts a fire. A false
   positive dumps a tank of good oil and halts production. There is no safe default answer.
4. **Layout is the skill ceiling.** The between-shift lab rebuild is where mastery lives. At least
   one shared bottleneck must exist at all times.
5. **Co-op by information asymmetry.** Receiving, prep, instrument operation and interpretation are
   genuinely different jobs. Players must talk.

### 1.2 Session shape

Fixed-length contracts, not an endless drift. A contract is 10–20 in-game days. Between days: spend
budget, rebuild the lab, review pending consequences. A run ends on contract completion or on
financial failure. Meta-progression persists across runs.

---

## 2. Art Direction & Asset Pipeline

Low-poly, heavy use of generated and purchased assets, but the result must not look like a
mismatched asset flip. **The solution is a style contract enforced at import time** — assets from
different sources clash because of their materials and textures, not their geometry, so strip every
imported material and re-apply one of our own.

### 2.1 The style contract

| Property | Rule |
|---|---|
| Shading | Flat-shaded. Hard normals everywhere. |
| Textures | **None**, with exactly one exception: instrument screens, where the texture *is* the readout. |
| Colour | A single 16×16 palette atlas sampled by UV, addressed through `PaletteUv`. |
| Materials | Four palette materials plus `M_Screen`. |
| Poly budget | Props 100–800 tris. Machines 800–3000 tris. Characters 1500 tris. |
| Scale | 1 unit = 1 metre. Player eye height 1.7 m. Bench height 0.9 m. |
| Pivot | Props pivot at base centre. Handheld items pivot at grip point. |
| Detail | Geometry detail only. A dial is a cylinder, not a texture. |

### 2.2 Palette

A single 16×16 PNG, point-filtered. Rows are hue families, columns value steps.

- **Neutrals (rows 0–1):** cold greys, off-white, concrete, steel. The lab body.
- **Warms (row 2):** oxidised orange, rust, aged brass, amber. Oil and corrosion.
- **Cools (row 3):** teal, deep blue, cyan. Screens, coolant, solvent.
- **Signal colours (row 4):** pure red, amber, green. **Reserved exclusively for verdict and alarm
  state.** Never decorate with these. If red only ever means critical, the player reads the room
  instantly — and instrument screens therefore use the coolant family, never row 4.

### 2.3 Automated import enforcement

`StyleEnforcer` (an `AssetPostprocessor`) conforms anything under `Assets/Art/Imported/`: materials
stripped, normals hardened, scale forced to metres, palette material applied, poly budget and
palette-contract violations logged. This is what makes mixed-source assets cohere.

### 2.4 Lighting

URP Forward+, baked lightmaps for the static lab, realtime points for interior strip lighting and
instrument indicators. Mild bloom with a high threshold so only emissives bloom; slight vignette;
linear cold fog. No motion blur, depth of field or chromatic aberration — they fight the crisp
flat-shaded read.

### 2.5 Authoring workflow

Geometry is **generated in C#** (`ProcMesh`, `LabSceneBuilder`) rather than authored by hand. For an
untextured flat-shaded style a prop *is* boxes and cylinders pinned to palette texels, and a script
is reviewable in a diff, reproducible on any machine, and editable by an agent that cannot see the
Editor. Externally sourced assets remain welcome; the import enforcer conforms them.

### 2.6 First-person presentation

No visible body initially. Interaction is a raycast from camera centre at 2.5 m, with a crosshair
that changes **shape** on a valid target and a subtle emissive pulse on the target — outlines read
as a rendering fault on untextured hard-normal geometry. Held items render on a separate camera
with its own near clip plane.

**You have one pair of hands.** A vial, a carton, a results slip and a manual all compete for them.
That scarcity is what makes §5.5 a layout problem rather than decoration.

---

## 3. Technical Stack

| Concern | Choice | Reason |
|---|---|---|
| Engine | Unity 6, URP | Stated requirement |
| Netcode | Netcode for GameObjects | No physics-critical interactions; first-party is fine |
| Transport | Unity Transport + Relay | No port forwarding |
| Lobby | Unity Lobby | Join-code flow |
| Voice | Vivox, 3D positional | Proximity chat is a design feature |
| Input | Input System | Rebinding, gamepad later |
| Data | ScriptableObjects for definitions, JSON for save | Designer-editable, portable |
| Testing | Unity Test Framework | The chemistry model must have unit tests — see §5.6 |

### 3.1 Authority model

**Host-authoritative, fully.** The host owns all sample state, all instrument state, the economy,
the day timer, contract state and consequence resolution. Clients own **only** their own transform
and look direction. Every interaction is a request the host validates.

There is no fast-twitch action here, so latency is irrelevant and simplicity is worth far more than
responsiveness. **Never let a client compute a test result.**

### 3.2 Networked object strategy

Do **not** spawn a NetworkObject per vial — a busy shift has 200+ and it will drown you. Samples
live in a server-side registry; physical vials are local-only pooled props carrying a `SampleId`,
with location represented as an enum plus reference (`Held`, `InMachine`, `OnSurface`, `InCrate`,
`Archived`). Only cartons get real NetworkObjects.

---

## 4. Data Model

Definitions are ScriptableObjects under `Assets/Data/`, generated from `ContentTables.cs`. Runtime
state is plain C# classes.

### 4.1 Element definitions

An "element" is any measurable quantity — a chemical species, a physical property, or a cooling
curve metric. `ElementDef` carries id, display name, unit, category and a `sourceHint` explaining
where it comes from, which the reference book renders verbatim.

### 4.2 Fluid profiles

Thresholds are **per fluid type**. This is the single most important source of legitimate
difficulty: 500 ppm of water is unremarkable in a cold bath and a fire risk in a martempering bath
running at 180 °C, because above 100 °C the water flashes to steam inside the oil.

Author at least six profiles:

| Profile | Character |
|---|---|
| `hardening_oil_general` | Forgiving general purpose. The tutorial fluid. |
| `quench_oil_cold` | Fast oil, ambient bath, widest water tolerance |
| `quench_oil_martempering` | Hot bath, 120–200 °C. Water tolerance drops by an order of magnitude |
| `quench_oil_vacuum` | Low vapour pressure, very tight volatiles and viscosity spec |
| `quench_oil_accelerated` | Speed-improved, additive-dependent. Where depletion bites |
| `corrosion_protection_oil` | Film and water-separation driven rather than quench-speed driven |

`ThresholdMode` covers three shapes: `UpperLimit` (water, TAN, insolubles), `LowerLimit` (flash
point, cooling rate, saponification) and `DeviationBand` (viscosity, temperature at maximum cooling
rate). Every quantity here fits one of them.

### 4.3 Fault archetypes

A sample is generated by selecting a fault (or none), applying its signature to the profile
baselines, then adding noise.

| Fault | Signature | The trap |
|---|---|---|
| Water ingress (heat exchanger leak) | Water↑↑, flash↓, demulsibility↓ | On a hot bath this is the fire case, not merely a quality case |
| Thermal degradation / oxidation | Visc↑, TAN↑, insolubles↑, cooling rate↓ | The slow, expected one |
| Hydraulic oil carryover | Visc↓, flash↓ | Looks like water on flash point alone. **Water content discriminates** |
| **Cleaner carryover from the washer line** | **Water↑, demulsibility↓↓, cooling curve disturbed** | **Keystone. The fault is upstream in the washer, not in the tank. Changing the oil does not fix it and the next sample looks identical.** |
| **Additive depletion (accelerated oil)** | **Cooling rate at 300 °C↓, everything else in spec** | **Keystone. The entire conventional panel reads normal. Only a cooling curve sees it. A clean result is not a clean sample.** |
| Sludge / carbon loading | Insolubles↑↑, cooling curve slowed | |
| Salt drag-in from a salt-bath line | Na↑↑, otherwise clean | Corrosion risk that no condition test flags |
| Wrong oil topped up | Ca/Zn/P fingerprint mismatch, visc off-grade | Detected by additive elements, not by condition |
| Localised overheating / varnish | TAN↑, insolubles↑, visc at top of band | Confusable with general oxidation |

The two bolded entries are what make expert play meaningful. Implement them.

### 4.4 Sample state

Ground truth — what is actually wrong and the real concentrations — lives in a **separate type**
from the replicated sample state, held by the host in a map no RPC signature can reach. A comment is
not a boundary.

Player-facing state carries the equipment tag, the customer and job it arrived under, the profile,
hours in service, the field note, remaining volume, temperature, settledness, filed results, and the
verdict.

### 4.5 Instrument definitions

`MachineDef` carries run time, sample volume consumed, cost per run, what it measures, **what it
cannot detect**, noise, calibration drift, carryover, placement requirements and footprint.

| Instrument | Time | Vol | Measures | Notes |
|---|---|---|---|---|
| Cooling curve tester (ISO 9950) | 900 s | 5 ml | Max cooling rate, rate at 300 °C, time to 400 °C | **Definitive but brutally slow.** The only instrument that sees quench performance |
| Karl Fischer titrator | 240 s | 5 ml | Water (precise ppm) | High carryover |
| Viscometer | 300 s | 10 ml | Viscosity @40 °C | Requires preheat |
| Flash point tester | 300 s | 15 ml | Flash point | Safety-critical, high volume cost |
| TAN titrator | 480 s | 8 ml | Acid number | Slow, fume hood |
| Centrifuge / insolubles | 180 s | 10 ml | Sludge, precipitation number | Shared bottleneck |
| Elemental analyser | 180 s | 5 ml | Na, Ca, Zn, P, Fe | **Blind to cooling performance** |

**Every instrument except the cooling curve tester is blind to quench performance.** That is the
mechanism behind the additive-depletion trap, and it is expressed through `cannotDetect` rather than
special-cased anywhere.

The volume economy matters: a 100 ml sample cannot take the full panel twice. Test ordering is a
real decision.

---

## 5. Core Systems

### 5.1 Sample lifecycle

```
Truck arrives → unload cartons → unbox → reconcile against delivery note → log
  → [fridge | bench] → prep (agitate, preheat) → instrument (load, run, unload, clean)
  → read display → carry printout to terminal → file results
  → cross-reference history → file verdict → archive
  → [days later] consequence resolves
```

Each arrow is a player action. **Results do not file themselves**: an instrument produces a reading,
shows it on its own display, and prints a slip. The reading joins the record only when someone
carries that slip to the terminal. A slip left on a bench is a test you paid for and cannot use.

**Logging:** samples arrive against a delivery note. The player registers each vial's tank
identifier at the terminal. Mis-logging is a real failure mode; a discrepancy between note and
contents is only fair because the note is in the box.

### 5.2 The contamination system

**The most important mechanic in the design. Build it early.**

Every instrument accumulates residue from what ran through it, and that residue transfers into the
next sample's *measured* values. Cleaning is a 20–40 s player action at the wash station consuming
purchased solvent — so cleaning costs money and players will be tempted to skip it.

Rushing does not merely cost time, it poisons your information. A skipped flush after a
water-contaminated sample makes the next tank read wet. The player either catches it (re-run: more
time, more volume) or condemns a good tank.

The tell is a **blank run**: solvent pushed through the instrument, reading residue directly. It
costs instrument time. Good players blank after every critical sample.

### 5.3 Calibration drift

Instruments accumulate drift each run, with a sign re-rolled per instrument per day, silently
scaling all readings. A **certified reference sample** measures it; recalibration costs time and
money.

The dread: discovering an instrument drifted 18 % high means **every verdict filed since it started
drifting is suspect**. Show the affected archived samples and let them be re-opened — if they still
have volume left.

### 5.4 Verdict & consequence resolution

| Filed | Reality | Outcome |
|---|---|---|
| CRITICAL | Fault present | Full payout + accuracy bonus |
| CRITICAL | No fault | Tank dumped and refilled, line downtime, reputation hit |
| MONITOR | Developing fault | Partial payout, tank resampled next cycle with worse numbers |
| MONITOR | Imminent fault | Batch scrapped or tank fire. Major loss |
| NORMAL | Fault present | **Catastrophic.** Cracked or soft parts, customer claim, named in the incident file |
| NORMAL | No fault | Standard payout |

A **tank fire** — missed water in a hot bath — is the single most expensive outcome in the game.

**Root-cause bonus:** filing the correct root cause (e.g. "washer line carryover" rather than "oil
degraded") pays a significant bonus. This is what rewards understanding over table-lookup.

### 5.5 Layout / build mode

Between days the lab is editable on a 0.5 m grid. Instruments occupy footprint cells and need bench
or floor space plus power.

**Mandatory shared bottleneck: the fume hood.** Solvent work and TAN titration both require it and
it has finite adjacent slots. Secondary bottlenecks: a single wash station, a single centrifuge, the
delivery bay's distance from the lab, and aisle width — two players carrying cartons cannot pass in
a one-cell aisle. Make that literal collision.

### 5.6 Chemistry model tests

Write these before the UI. Each guards a specific promise:

- A sample with no fault, zero contamination and zero drift reads Normal on every tracked quantity.
- Each fault archetype at maximum severity produces at least one Critical reading, on every profile
  it is valid for.
- The confusable pair — water ingress vs hydraulic carryover — is separable by water content in
  >95 % of 1000 generated samples.
- Additive depletion produces a **clean conventional panel** and a **critical cooling curve**.
- Contamination carryover from a critical sample into a clean one can push it above the critical
  limit, and cleaning restores a trustworthy reading.
- Ground truth cannot be reached from the replicated sample type, and the netcode assembly never
  mentions it.
- Every blanket verdict strategy loses money, and reading the results correctly beats all of them.

---

## 6. Progression

### 6.1 Within a run

**Days 1–3 — one customer, clean panel.** General hardening oil only. 4–6 samples/day. Generous
time. Tutorial faults: water ingress, oxidation, sludge loading. Teach the panel.

**Days 4–8 — more customers.** Cold and martempering baths added, so water limits now differ by an
order of magnitude between samples and the player must check the profile. 10–14 samples/day. The
queue begins to outpace your hands. Repeat tanks from earlier days introduce history.

**Days 9–14 — complications.**
- Samples arriving cold must be warmed before viscosity means anything.
- Accelerated oils arrive, where the conventional panel stops being sufficient.
- A customer who cuts corners sends samples quietly all drawn from the same tank — identical results
  across supposedly different baths. Catching this is a large bonus; missing it means filing several
  wrong verdicts.
- Rush samples that jump the queue with a hard deadline.

**Days 15–20 — the assumption break.** A new oil formulation arrives whose additive baseline is
completely different, and the player's instincts actively mislead them. Old thresholds produce false
positives. A new reference volume is issued mid-contract; reading it is optional and expensive in
time.

### 6.2 Between runs

Unlocks persist. Most upgrades should buy you out of a **specific failure mode**, not just out of
slowness:

| Upgrade | Removes |
|---|---|
| Barcode scanner + printer | Mis-logging errors entirely |
| Autosampler | Standing at the instrument; queue slots |
| Second cooling curve tester | The primary occupancy bottleneck |
| Closed-loop solvent recycler | Solvent cost; makes cleaning free |
| Inline blank automation | Auto-detects residue above threshold |
| Larger sample intake | The volume economy, partially |
| Field kit dispatch | Lets you request a re-draw instead of guessing |
| Reference library expansion | Faster history cross-referencing |

### 6.3 Guarding against solved-game decay

Diagnosis games get solved once the community writes the table down. Accept this. The answer is that
**knowledge stays stable but the bottleneck moves**.

The canonical hard decision, which stays hard even for a player who knows the table cold:

> Water is borderline. Viscosity is at the top of the band. You have 8 ml left. A cooling curve
> would settle whether the oil still quenches, but it takes fifteen minutes and you have four
> samples queued behind this one. The Karl Fischer is due for calibration. What do you file?

Generate these deliberately: an ambiguity budget per day forcing N samples into the borderline band
where a single test cannot resolve them.

---

## 7. The Optional Weird Layer

Restrained, opt-in at contract selection, and **not a monster.**

Some samples return readings that are not physically possible — a cooling curve with a plateau at a
temperature nothing transforms at, a viscosity that changes between two runs of the same vial, an
elemental trace matching no known additive package.

There is no chase and no entity. The escalation is **procedural**: a new field appears on the report
form; a sealed courier begins collecting certain vials; a rule is issued that flagged samples are
not to be re-run; the rule is amended, then amended again; certain customer tanks stop appearing in
the database, including ones you filed on last week.

The dread comes from the paperwork changing. Ambiguity is the point — never confirm anything.

---

## 8. Milestones

### M0 — Grey box, single player ✅
First-person controller, one room, pick up and put down a vial, one instrument that runs and
reports. **Delivered.**

### M1 — Chemistry model + tests ✅
All definitions, generator, the §5.6 suite. **Delivered** (against the wear-metal domain; D1
re-points it at heat-treatment oils).

### M2 — The verdict loop
Terminal, reference books, consequence resolution, money and reputation. **Largely delivered.**
Remaining: results grouped by category (#11), sample lifecycle transition table (#9), and the two
§2.6 presentation elements that were specified but never built (#36).

**This is the gate.** If reading results and deciding is not interesting alone, stop and re-examine
before adding co-op. Nothing in M3–M5 or D2 should start before this milestone closes and the gate
has actually been played — the point of a gate is that it can fail, and it cannot fail if the work
behind it has already shipped.

### M3 — Contamination + calibration
Residue and blanks are implemented in the model. Remaining: the wash station as a physical action,
solvent as a purchasable consumable, reference samples and the retroactive suspicion UI.

### M4 — Co-op
NGO + Relay + Lobby, host-authoritative state, local-prop split, Vivox proximity voice, 4 players.
Acceptance: four clients, 60 samples, no desync, **no client ever receives ground truth**.

### M5 — Day cycle + layout
Crate arrival, day timer, end-of-day summary, grid rebuild with the fume-hood bottleneck, shop,
save/load. **Save/load in co-op is the hard part, not the gameplay.** Decide client rejoin *now*.

### M6 — Art pass
Style enforcer, palette, lab architecture, instrument models, props, baked lighting.

Hands and the character body landed early, during M2 — a first-person game with no hands reads as
unfinished long before the art pass, and the body had to exist before M4 rather than after it.

### M7 — Content & balance
Six profiles, the full fault set, ambiguity tuning, meta-progression, audio.

The 20-day arc landed early, at M2. It was not a content nicety: consequences settle
`DaysToFailure` days after a verdict and those run 4–14 days, so on the old 3-day contract no
verdict on a faulty unit could ever resolve and the loop never paid out at all.

### M8 — Weird layer, optional
Contract modifier, anomalous value injection, versioned form schema.

### D1 — Heat-treatment oil domain
Retire wear-metal content; re-point the model at quench oils. Content and consequence wording only —
no architecture change.

### D2 — Deliveries and customers
Truck, delivery bay, cartons, unboxing, delivery notes and reconciliation.

Gated behind M2. D2 adds volume and ceremony to the front of the loop; it cannot make the middle of
the loop interesting, and building it first would only make a weak verdict loop take longer to reach.

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| **Game is boring solo, so co-op just adds people to a boring game** | M2 gate. Do not proceed to M4 until the solo verdict loop is engaging. |
| Community solves the chemistry in a week | Expected. §6.3 — move the bottleneck, don't randomise the science. |
| Contamination feels like arbitrary punishment | Always provide a detectable tell. **Never punish something the player could not have checked.** |
| Too much reading, not enough doing | Prep, carrying, unboxing and cleaning must be real hand-operated tasks with real time cost — not menu clicks. |
| Networked save/load breaks late | M5, not M8. |
| Mixed assets look incoherent | §2.3 automated enforcement, solved at import. |
| Scope | The weird layer and half the fault archetypes are cuttable. **The contamination system is not.** |

**No NPCs are required anywhere in this design.** No NavMesh, no pathfinding, no crowd sync. This
removes the largest technical risk category from the genre. The delivery truck is a prop and a
timer, not a character.

---

## 10. The Prototype That Answers Everything

> One sample. Three instruments. A contamination flag. A verdict button. 8 ml of oil remaining and a
> cooling curve run that costs 5 ml and fifteen minutes.

If two people arguing over whether to spend the last of the sample on a cooling curve is tense, the
whole game works and everything after is content. If it isn't, no amount of instruments, upgrades or
art will save it.

Build the ugliest possible version first.
