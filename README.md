# Oiled Up

## Current milestone: M2 — Verdict loop

Goal: a single player can complete a full lab day and receive clear win/loss feedback.

- [x] Sample lifecycle and validation
- [x] Verdict filing and delayed consequences
- [x] Terminal, reference book, economy, and reputation
- [x] Contract timing that allows consequences to resolve
- [ ] [Target feedback and held-item camera](https://github.com/miopowered/OiledUp/issues/36)
- [ ] [Three-slot inventory, unified HUD, and item inspection](https://github.com/miopowered/OiledUp/issues/63)

## Project

Oiled Up is a 1–4 player first-person co-op game about running a heat-treatment
oil laboratory. Receive customer samples, choose and operate tests, interpret
the results, and file a verdict before a bad quench bath ruins parts or starts a
fire. Unnecessary shutdowns are costly too, so there is no safe default answer.

The project is built with Unity 6 and URP. It currently includes the playable
lab workflow, deterministic chemistry, contamination and calibration systems,
host-authoritative multiplayer foundations, proximity voice, and tested save
and lab-layout foundations. It remains pre-alpha.

See [docs/DESIGN.md](docs/DESIGN.md) for the full design and technical rules.

## Still missing

- Final M2 interaction polish: inventory, inspection, target feedback, and held-item presentation
- Customer deliveries: trucks, cartons, delivery notes, and discrepancy checks
- Complete co-op validation and consistent host/client machine displays
- Full run save/restore and player-facing lab rebuild mode
- Main, pause, settings, loading, onboarding, and credits screens
- Rebindable controls, motion comfort, colourblind support, and localisation readiness
- Final art, audio, content, balance, and meta-progression passes
- Chemistry debug tooling and working Unity CI licence setup

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

Open `Assets/Scenes/Lab.unity`.

## Tests

In Unity, open **Window > General > Test Runner** and run the
`Residue.Tests.EditMode` suite.

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
