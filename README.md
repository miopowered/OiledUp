# Oiled Up

## Where the project is

| Milestone | State |
|---|---|
| M0 Grey box · M2 Verdict loop · M3 Contamination · M5 Day cycle | Complete |
| M1 Chemistry + tests | Complete bar debug tooling and CI ([#3](https://github.com/miopowered/OiledUp/issues/3), [#18](https://github.com/miopowered/OiledUp/issues/18)) |
| M4 Co-op | Complete host-side; **a joined client cannot see delivery cartons** ([#80](https://github.com/miopowered/OiledUp/issues/80)) |
| D1 Heat-treatment domain · D2 Deliveries and customers | Complete |
| F1 Frontend, options, accessibility | Menu, lobby, settings, rebinds and colourblind support done; polish outstanding |
| M6 Art · M7 Content and balance · M8 Weird layer | Not started |

A single player can play a full contract end to end: samples arrive by truck, are
unboxed, reconciled against a delivery note, prepped, run, filed, and resolved —
with the run surviving a quit. It remains pre-alpha.

## Project

Oiled Up is a 1–4 player first-person co-op game about running a heat-treatment
oil laboratory. Receive customer samples, choose and operate tests, interpret
the results, and file a verdict before a bad quench bath ruins parts or starts a
fire. Unnecessary shutdowns are costly too, so there is no safe default answer.

The project is built with Unity 6 and URP. It currently includes the playable
lab workflow, deterministic chemistry, contamination and calibration systems,
customer deliveries and note reconciliation, host-authoritative multiplayer,
proximity voice, run save and continue, and a full front end.

See [docs/DESIGN.md](docs/DESIGN.md) for the full design and technical rules.

## Still missing

### Blocking co-op

- **[#80](https://github.com/miopowered/OiledUp/issues/80) Cartons are not replicated.** A joined
  client sees the bay and the truck but no boxes, so it cannot start the day. Single player and the
  host are complete; this is the one thing standing between the current build and a co-op session.

### Verification debt

- **The EditMode suite is not routinely run.** [#76](https://github.com/miopowered/OiledUp/issues/76)
  fixed the deadlock that made it unrunnable, but that fix is itself unvalidated, and roughly sixty
  tests added since have never been executed. Run `Residue > Build > Run EditMode Tests` and read
  `Temp/oiledup-editmode.txt`.
- **[#18](https://github.com/miopowered/OiledUp/issues/18) CI is deliberately off** while this is a
  single-developer project — the local path works, it is just operated by hand. Revisit if a second
  person commits, or if `main` goes red unnoticed again.
- **[#3](https://github.com/miopowered/OiledUp/issues/3)** No debug command to dump a generated
  sample's ground truth, which makes a chemistry bug harder to pin than it should be.

### Polish and accessibility ([F1](https://github.com/miopowered/OiledUp/milestone/12))

- [#46](https://github.com/miopowered/OiledUp/issues/46) The lab is completely silent — no audio at
  all. The settings screen already has volume sliders wired to nothing.
- [#47](https://github.com/miopowered/OiledUp/issues/47) No first-run onboarding; nothing tells a new
  player the lab has rules.
- [#51](https://github.com/miopowered/OiledUp/issues/51) Scene transitions hard-cut.
- [#53](https://github.com/miopowered/OiledUp/issues/53) No credits screen, which some art licences
  require.
- [#54](https://github.com/miopowered/OiledUp/issues/54) Motion comfort is partly done — FOV and
  head-bob-off ship; bob as a scale and any control over the landing impulse do not.
- [#55](https://github.com/miopowered/OiledUp/issues/55) Every player-facing string is a literal at
  its use site, so localisation is not yet possible.

### Not started

- Art pass (M6), content and balance pass (M7), and the optional weird layer (M8).
- [#34](https://github.com/miopowered/OiledUp/issues/34) Assemblies and namespaces are still
  `Residue.*` from the working title.

Track the details in [GitHub Issues](https://github.com/miopowered/OiledUp/issues).

## Requirements

- [Git LFS](https://git-lfs.com/)
- [Unity Hub](https://unity.com/download)
- Unity Editor `6000.5.9f1`

## Setup

```bash
git lfs install
git clone git@github.com:miopowered/OiledUp.git
cd OiledUp
git lfs pull
```

Add the project in Unity Hub and open it with Unity `6000.5.9f1`. After package
restore, use these menu commands:

1. `Residue > Content > Rebuild Definitions`
2. `Residue > Content > Validate`
3. `Residue > Art > Rebuild Palette`
4. `Residue > Build > Rebuild Greybox Lab`

Open `Assets/Scenes/Boot.unity` and press Play. That is the scene a build starts
in, and the menu, the save slot and the lobby all wake up there — opening
`Lab.unity` directly skips them and starts a run with no way back to a menu.

## Tests

`Residue > Build > Run EditMode Tests`, then read `Temp/oiledup-editmode.txt`. It
appends a line per test as the run goes, so a run that dies partway still says how
far it got. The Test Runner window works too.

Do not drive `TestRunnerApi` from the Unity MCP tool — see the trap documented in
[CLAUDE.md](CLAUDE.md), which deadlocks the Editor after every test has passed.

When changing balance data, edit
`Assets/Editor/Content/ContentTables.cs`, rebuild and validate the definitions,
then run the full EditMode suite. Do not edit generated `.asset` files by hand.

## Structure

```text
Assets/Scripts/Data/         Definitions
Assets/Scripts/Chemistry/    Generation and measurement
Assets/Scripts/Gameplay/     Lab interactions and simulation
Assets/Scripts/Net/          Multiplayer and replication
Assets/Editor/               Content and art tooling
Assets/Tests/EditMode/       Automated tests
docs/                        Design, workflow, and CI notes
```

The host owns samples, measurements, machines, economy, and consequences.
Clients request interactions and never receive sample ground truth.

## Contributing

Read [docs/WORKFLOW.md](docs/WORKFLOW.md) and [CLAUDE.md](CLAUDE.md).

- Use one issue per branch and one branch per pull request.
- Commit Unity assets with their `.meta` files.
- Keep generated content and scene files out of manual YAML edits.
- Preserve the host-only `SampleGroundTruth` boundary.
- Keep chemistry deterministic and scientifically consistent.
