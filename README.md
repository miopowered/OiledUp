# 🛢️ RESIDUE

> **Working title:** RESIDUE · **Repository:** OiledUp

**RESIDUE** is a gloriously hands-on, co-op first-person oil-analysis simulator
built with Unity 6 and URP. You and up to three friends run a remote industrial
laboratory: receive used-oil samples, choose which tests are worth the remaining
volume and machine time, interpret the chemistry, and file a verdict before a
very expensive piece of equipment fails. 🔬⚙️🔥

The science never lies. The pressure comes from dwindling sample volume,
contaminated instruments, calibration drift, crowded machines, and the terrible
knowledge that both a missed failure and an unnecessary teardown cost money.

> [!IMPORTANT]
> The project is in **early pre-alpha**. The chemistry foundation is implemented
> and tested, but the first-person laboratory and playable verdict loop are not
> built yet. The current scene is still a minimal Unity template scene. 🌱

## 🎯 The ridiculously compelling pitch

Every sample is a tiny industrial mystery. An elevated iron reading might mean
ordinary wear, abrasive dirt ingress, or a gearbox shedding particles too large
for an ICP spectrometer to see. Running every test is impossible, so the real
game is deciding what evidence is worth its cost:

```text
Crate arrives → log → prep → test → interpret → file verdict
      → days pass → consequences land → rebuild and upgrade the lab
```

- 🧪 **Real chemistry is the content.** Understanding cause beats memorising a
  lookup table.
- ⚖️ **Failure cuts both ways.** False negatives destroy machinery; false
  positives waste fortunes on healthy equipment.
- ⏱️ **The bottleneck moves.** Difficulty grows through workload, contamination,
  machine occupancy, and ambiguity—never fake science.
- 🏗️ **Layout is the skill ceiling.** Between shifts, teams redesign the lab
  around shared bottlenecks such as the fume hood.
- 🗣️ **Co-op creates information asymmetry.** Preparation, machine operation,
  and diagnosis are different jobs, so communication genuinely matters.

Read the complete [technical and design specification](docs/DESIGN.md) for the
full game loop, art direction, progression, networking model, and milestones.

## ⚡ What is implemented now

The current codebase delivers a deterministic, server-oriented chemistry model
with a surprisingly mighty amount of oily detail:

- 🎲 Seeded sample generation using a project-owned xorshift128 RNG
- 🧬 26 measured properties across wear metals, contaminants, additives, and
  fluid properties
- 🚜 Three equipment profiles: heavy diesel engines, industrial gearboxes, and
  hydraulic systems
- 💥 Eight fault archetypes and nine diagnosable root causes
- 🔬 Seven laboratory machines with distinct noise, drift, cost, runtime,
  sample-volume use, carryover, and detection limits
- 🧼 Machine residue, skipped-flush contamination, and revealing blank runs
- 📉 Calibration drift with retroactive suspicion marking
- 🕵️ Deliberately tricky but fair cases, including bearing-versus-bushing wear
  and gear spalling that looks clean on ICP
- 🔒 A structural boundary between player-visible sample state and server-only
  ground truth
- ✅ Eight EditMode tests guarding the promises above
- 🎨 A 16×16 industrial palette generator and an importer that enforces the
  low-poly, texture-free art style

`Residue.Gameplay` and `Residue.Net` currently establish assembly boundaries;
their first-person and multiplayer systems are still to come. Netcode for Game
Objects, Relay, Lobby, Authentication, and Vivox packages are already present
for the planned host-authoritative 1–4 player architecture. 🚀

## 🧰 Requirements

- [Git LFS](https://git-lfs.com/)
- [Unity Hub](https://unity.com/download)
- Unity Editor **6000.5.9f1**

Use the recorded editor version for the smoothest, most outrageously seamless
experience. Platform-specific builds also require their matching Unity module.

## 🚀 Get running at ludicrous speed

1. Clone the repository and fetch its LFS assets:

   ```bash
   git lfs install
   git clone git@github.com:rexlManu/OiledUp.git
   cd OiledUp
   git lfs pull
   ```

2. In Unity Hub, choose **Add > Add project from disk** and select the cloned
   directory.
3. Open it with Unity `6000.5.9f1` and let Package Manager restore dependencies.
4. Generate and validate the code-authored project content:

   - **Residue > Content > Rebuild Definitions**
   - **Residue > Content > Validate**
   - **Residue > Art > Rebuild Palette**

5. Open `Assets/Scenes/SampleScene.unity`.

Pressing Play currently shows the template scene, not a playable game. The
chemistry model is exercised through EditMode tests while gameplay milestones
are under construction. 🏗️

## 🧪 Tests and validation

Open **Window > General > Test Runner**, select **EditMode**, and run the
`Residue.Tests.EditMode` suite. The tests cover healthy baselines, maximum fault
severity, diagnostic separation, instrument blind spots, contamination, blank
runs, and the ground-truth security boundary.

Pull requests also run the EditMode suite through GameCI when the repository's
Unity licence secrets are configured. See [CI setup](docs/CI_SETUP.md) for the
one-time activation process and known editor-image caveat.

When changing balance data:

1. Edit `Assets/Editor/Content/ContentTables.cs`—never generated `.asset` files.
2. Rebuild definitions from the **Residue** menu.
3. Run **Residue > Content > Validate**.
4. Run the complete EditMode suite.

Definitions are updated in place so their GUIDs remain stable. Deleted table
rows are reported as orphaned assets and are never silently removed. 💎

## 🧱 Architecture

```text
Residue.Data        Immutable ScriptableObject definitions
       ↓
Residue.Chemistry   Deterministic generation and measurement
       ↓
Residue.Gameplay    Interaction, machines, and day cycle (scaffolded)
       ↓
Residue.Net         Host-authoritative NGO layer (scaffolded)

Residue.Editor      Content generation and art-style enforcement
Residue.Tests       EditMode chemistry and boundary tests
```

The dependency direction is also a security boundary: chemistry cannot depend
on networking, and server-only `SampleGroundTruth` has no path into a client
payload. Clients will request interactions; only the host may generate samples,
run measurements, own machine state, or resolve consequences.

## 🗺️ Project map

```text
Assets/
  Editor/Art/           Palette generation and import-time style enforcement
  Editor/Content/       Reviewable balance tables and asset generation
  Scripts/Data/         Elements, profiles, faults, machines, and thresholds
  Scripts/Chemistry/    Samples, deterministic RNG, residue, drift, measurement
  Scripts/Gameplay/     Gameplay assembly boundary
  Scripts/Net/          Multiplayer assembly boundary
  Tests/EditMode/       Chemistry and architecture tests
  Scenes/               Unity scenes
docs/
  DESIGN.md             Source of truth for the game
  WORKFLOW.md           Branching, issues, CI, and definition of done
  CI_SETUP.md           Unity licence setup for GitHub Actions
```

## 🎨 Industrial art superpowers

The art direction is low-poly, flat-shaded, and texture-free. Models placed
under `Assets/Art/Imported/{Props,Machines,Characters}` automatically have
materials stripped, normals hardened, scale normalised to metres, and the
project palette applied. Poly-budget warnings keep even wildly different asset
sources looking like one magnificently cohesive industrial world. 🏭✨

Signal red, amber, and green are reserved exclusively for verdict and alarm
states. They are information, never decoration.

## 🤝 Contributing

Start with [the workflow](docs/WORKFLOW.md) and read [CLAUDE.md](CLAUDE.md)
before design-adjacent or chemistry work. The load-bearing rules are simple:

- One issue per branch and one branch per pull request
- Use conventional commits such as `feat(chemistry):`, `fix(content):`, and
  `docs:`
- Commit every Unity asset with its `.meta` file
- Never hand-edit Unity `.asset`, `.prefab`, or `.unity` YAML
- Keep balance changes in `ContentTables.cs`
- Never expose `SampleGroundTruth` to clients
- Never make correct chemistry unreliable just to create difficulty

---

**Build the ugliest possible prototype first—then make it the most blazingly
brilliant oil laboratory the world has ever seen.** 🛢️🔬⚡🚀🏆
