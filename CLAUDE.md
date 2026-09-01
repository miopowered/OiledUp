# OILED UP — working notes for coding agents

Co-op first-person heat-treatment oil analysis simulator. Unity 6000.5.9f1, URP, Netcode for
GameObjects.

The lab analyses **oil-based heat-treatment process fluids** for industrial customers: quench oils
(cold, martempering, vacuum, accelerated), general hardening oils and corrosion-protection oils.
Water-based polymer quenchants and aqueous cleaners are out of scope.

> Assemblies and namespaces are `Residue.*`, from the working title, and they are **staying that
> way** — the owner decided to keep it (#34, closed as not planned). It is not pending debt and
> nobody should go looking for the rename: `Residue.Data`, `Residue.Chemistry`, `Residue.Gameplay`,
> `Residue.Net` and `Residue.Editor` are simply what the assemblies are called.

**Read [`docs/DESIGN.md`](docs/DESIGN.md) before doing design-adjacent work.** It is the source of
truth for what the game is. This file covers only what the code and the spec do not already say.

---

## Hard rules

These are load-bearing. Breaking one silently ruins the game rather than breaking a build.

1. **The chemistry never lies.** Difficulty comes from volume, machine occupancy, contamination and
   ambiguity — never from randomising the science. A player who understands cause must beat a player
   who memorised a table. If a change would make a correct diagnosis unreliable, it is wrong even if
   it makes the game harder.
2. **Ground truth never reaches a client.** `SampleGroundTruth` is server-only and lives in a
   separate type from `SampleState` for exactly this reason. `Residue.Chemistry` must never
   reference `Residue.Net`. `MeasurementPipeline` must only ever be called on the host.
3. **Never punish something the player could not have checked.** Contamination and drift are only
   fair because a blank run and a reference sample reveal them. Any new hidden-state mechanic needs
   a detectable tell shipped in the same change.
4. **Signal colours mean verdict state and nothing else.** Palette row 4 is reserved. Do not use red,
   amber or green for decoration anywhere.
5. **Balance data is edited in `ContentTables.cs`, never in the Inspector.** The `.asset` files under
   `Assets/Data/` are generated. Inspector edits are silently overwritten on the next rebuild.

---

## Verifying your work

You will usually not have the Editor GUI. Use these in order.

### 1. Headless compile check (~5 s) — run this constantly

```powershell
dotnet build "tools\compilecheck\Residue.CompileCheck.csproj" -v q --nologo         # runtime asmdefs
dotnet build "tools\compilecheck\Residue.CompileCheck.Editor.csproj" -v q --nologo  # editor + tests
```

These compile the real source against Unity's own managed DLLs, so overload resolution, namespaces
and signatures are checked for real. They do **not** run tests and do **not** enforce asmdef
boundaries — Unity has the final word. See the comments in each `.csproj`.

If a new package's types fail to resolve, its DLL is missing from `Library/ScriptAssemblies`, which
means Unity has not imported it yet. Add a `<Reference>` with an `Exists(...)` condition rather than
an unconditional one.

### 2. Unity MCP — the real loop

The `unity-mcp` server talks to the running Editor over a named pipe. It exposes
`Unity_RunCommand` (compiles *and executes* arbitrary C# in the Editor, returning compile status and
logs) and `Unity_GetConsoleLogs`. Between them you can trigger an `AssetDatabase.Refresh()`, run the
test suite, create assets, and read the resulting errors — without a GUI.

- The Editor must be **running** for these to work. Closed Editor = dead tools, not a broken setup.
- Launch Unity with `-automated` when agents will be driving it unattended. Without it, a modal
  dialog blocks the Editor and every MCP call hangs until a human clicks it.
- Unity holds a project lock, so `Unity.exe -batchmode` **cannot** run while the Editor is open.
  Do not reach for batchmode as a workaround.

The server is not part of the repo — it ships with the `com.unity.ai.assistant` package, which
installs a relay binary to `~/.unity/relay/` and needs registering with Claude Code once per machine:

```powershell
claude mcp add unity-mcp --scope local -- "$env:USERPROFILE\.unity\relay\relay_win.exe" `
  --mcp --project-path "C:\path\to\OiledUp"
```

`--project-path` is not optional in practice. The relay otherwise binds to the first Editor it
discovers, and `~/.unity/mcp/connections/` accumulates stale entries from other checkouts — so
without it you can end up driving a different project. On macOS/Linux the relay is
`relay_mac_{arm64,x64}.app/Contents/MacOS/…` or `relay_linux`; the Editor's own
**Edit > Project Settings > AI > Unity MCP Server > Integrations** can write the config instead.

Registering only writes config — the tools appear in the **next** Claude Code session, not the
current one. A first-time direct connection also has to be approved once from that same settings
page, under **Pending Connections**.

### 3. Tests

`Assets/Tests/EditMode/ChemistryTests.cs` implements the §5.6 suite. Each test guards a specific
promise the game makes to the player; read the comment above one before changing it. Run them via
the Test Runner window, via `Unity_RunCommand`, or in CI.

**Do not drive `TestRunnerApi` from `Unity_RunCommand` with a callback object.** The obvious
approach — implement `ICallbacks`, `RegisterCallbacks`, `Execute` — hangs the Editor **after every
test has already passed**. `Unity_RunCommand` compiles your snippet into a temporary dynamic
assembly, so the callback instance lives in an assembly the post-run domain reload is in the middle
of tearing down. Every test reports, `RunFinished` never arrives, and the Editor is left
unresponsive at a flat memory figure (a deadlock, not a spin) and has to be killed. It reads exactly
like a test that loops forever, which is the wrong thing to go looking for.

**Run them through `TestRunReport` instead.** `Residue/Build/Run EditMode Tests` — or
`Residue.Editor.Build.TestRunReport.Run()` from `Unity_RunCommand`, which is one line and safe,
because the reporter lives in a real assembly that survives the reload. It appends a line per test to
`Temp/oiledup-editmode.txt` as the run goes, so poll that file rather than waiting on a return value:

```powershell
Get-Content Temp\oiledup-editmode.txt | Where-Object { $_ -match '^(FAIL|DONE)' }
```

Per-test lines are written as they happen, so even a run that dies at the very end still tells you
what passed and which test was last. `RunFinished` may still not arrive — the `DONE` summary line is
a bonus, not the result. Expect the Editor to be unresponsive for a while after the run and to
possibly need killing; the results are already on disk by then.

Do this before merging anything that touches tests. A failing assertion sat on `main` unnoticed
precisely because the suite was hard to run.

### 4. In-game debug keys

- **F3** — interaction overlay. Wireframes every nearby collider, the ray, and the ordered list of
  what it passes through. This is how you tell a collider/mesh mismatch from a blocker in front of
  the thing you wanted.
- **F4** — third person. Pulls the camera back and un-hides `CharacterBody`, which is otherwise
  culled from its owner's eye camera. The body exists for other players and there are none until M4,
  so this is the only way to see the walk cycle.

Driving the game from `Unity_RunCommand` works, with one trap: queue input with
`InputSystem.QueueStateEvent` and **do not** call `InputSystem.Update()`. Forcing an update consumes
the press in a synthetic frame, so edge-triggered actions (`WasPressedThisFrame` — jump, interact)
have already gone false by the time the next game frame runs. Level-triggered ones (`ReadValue` —
move, sprint) survive either way, which makes this look like "jump is broken" rather than a test
artefact. Note also that the Editor throttles frames while unfocused, so `Time.timeScale` and
wall-clock delays are only loosely related.

---

## Unity discipline

- **Every asset needs its `.meta` file committed.** Commit `Foo.cs` and `Foo.cs.meta` together. A
  missing `.meta` means Unity regenerates it with a *different GUID* on the next machine, silently
  breaking every reference to it. If you add files without the Editor running, the `.meta` files
  appear only after Unity next imports — wait for that before committing.
- **Never hand-edit `.asset`, `.prefab` or `.unity` YAML.** Use the generators or the Editor.
- `.gitattributes` routes Unity YAML through UnityYAMLMerge and puts binaries in Git LFS. If a merge
  driver error appears, the Unity version in `merge.unityyamlmerge.driver` (repo git config) no
  longer matches the installed Editor.
- Serialization is **Force Text**; do not change it.

---

## Architecture

```
Residue.Data       definitions (ScriptableObjects) + PaletteUv.  No dependencies.
Residue.Chemistry  sample generation, measurement, contamination, drift.  -> Data
Residue.Gameplay   interaction, machines, day cycle.                      -> Data, Chemistry, InputSystem
Residue.Net        NGO layer.                                             -> Data, Chemistry, Gameplay, Netcode
Residue.Editor     content generation, style enforcement.  Editor-only.
Residue.Tests.EditMode
```

The dependency direction is the security boundary. `Residue.Chemistry` cannot see `Residue.Net`, so
ground truth has no path to a serializer. `ChemistryTests.NetworkLayerNeverMentionsGroundTruth`
enforces this by reflection once `Residue.Net` has code in it.

**Definitions are immutable at runtime.** Fields are `[SerializeField] private` with read-only
accessors so no system can mutate shared balance data mid-run.

**Determinism matters.** Sample generation uses `Residue.Chemistry.Rng` (xorshift128), not
`UnityEngine.Random` (global mutable state) and not `System.Random` (algorithm differs between .NET
runtimes, so a seed does not pin a sequence). A run seed must reproduce a whole contract exactly.

---

## Changing content

1. Edit the tables in `Assets/Editor/Content/ContentTables.cs`.
2. In the Editor: **Residue > Content > Rebuild Definitions**.
3. **Residue > Content > Validate** to catch faults with no signature, signatures that move an
   element the profile does not score, and machines measuring elements nobody cares about.
4. Run `ChemistryTests`. A new fault that never trips Critical at full severity will fail
   `EveryFault_AtMaxSeverity_ProducesACriticalReading` — that is the test doing its job, not a
   flaky test. Strengthen the signature.

Rebuilds update assets **in place** so GUIDs survive. Rows deleted from the tables are reported as
orphans but never auto-deleted.

Watch the tails when tuning: healthy baselines are clamped into the Normal band by construction
(`SampleGenerator.ClampToNormalBand`), so a signature must be decisive at the *low* end of the
baseline range, not just at the typical value.

---

## Art

The style contract (§2.1) is enforced at import, not by discipline. Anything dropped into
`Assets/Art/Imported/{Props,Machines,Characters}/` gets its materials stripped, normals hardened,
scale forced to metres, and a palette material applied.

- Palette and materials are generated: **Residue > Art > Rebuild Palette**.
- Geometry addresses colour through `Residue.Data.PaletteUv`, never raw UVs.
- Sourcing external assets is fine and expected — the enforcer is what makes mixed sources cohere.
  Prefer CC0/permissive sources and record the licence in `Assets/Art/Imported/CREDITS.md`.
- For simple props, authoring the mesh in C# is often faster than sourcing one, because the style is
  untextured flat-shaded geometry. A dial is a cylinder, not a texture.

---

## Deviations from the spec

Deliberate. Do not "correct" these back without reading the reasoning.

| Spec | Implementation | Why |
|---|---|---|
| `Threshold.normalMax/cautionMax/inverted` | `ThresholdMode {UpperLimit, LowerLimit, DeviationBand}` | Viscosity is scored as a ± band around the grade nominal. Neither a max nor an inverted max can express "both directions are bad". |
| No healthy baseline on `Threshold` | Added `Baseline` + `BaselineVariance` | `ElementDelta.multiplier` is "applied to baseline", but §4.2 defined no baseline to apply it to. |
| `trueValues` on `SampleState` behind a comment | Separate `SampleGroundTruth` type | A comment is not a boundary. The split makes the leak structurally impossible instead of test-detectable. |
| `Severity` | `FaultSeverity` | Avoids collision with `ReadingSeverity`. |
| Balance authored as `.asset` files | Authored in `ContentTables.cs`, projected to `.asset` | A ScriptableObject `.asset` is a wall of GUIDs. Balance changes must be reviewable in a diff. |
| M0 before M1 | M1 built first | M1 is explicitly "no rendering work" and is verifiable headlessly; M0 needs scene authoring in the GUI. |
| §5.1's `log` step, and mis-logging as a failure mode | Booking-in removed; a sample carries its label's tag from the moment it exists (#73) | The loop stopped dead at a keyboard: nothing could be prepped or run until it had been typed into the terminal. The cost was paid knowingly — §5.1's named failure mode is gone, `SampleStage.Logged` and the `Unpacked → Logged → Prepped` gate with it, and so is the hard-rule-3 argument built on "walk back and read the bottle". `RecordTag` now equals `EquipmentTag`, so `VialView` and `SampleView` stay separate lists on their remaining argument (world vs. screens) rather than on the label boundary. Do not reintroduce a typed tag without reintroducing the tell. |
| The above, narrowed | Registration returned for ambiguous vials only (#32) | A vial that cannot speak for itself — an unreadable label, or two bottles claiming one tank — does get a recorded decision. This is not booking-in returning: it is refused outright on a legible vial ("There is nothing to decide"), it gates nothing, and an unregistered vial can still be moved, agitated, run and filed. The tell #73 removed is reintroduced with it, which is the condition the row above sets: the note in the box is the evidence, and every ambiguity is answerable from it — an unreadable label always leaves exactly one line no legible bottle claims, verified over 600 runs. If that refusal on a legible vial ever softens, booking-in is back for every bottle. |

---

## Conventions

- C# 9 (Unity 6). Target-typed `new()`, switch expressions, pattern matching are all available.
- One public type per file, filename matches. Enums may share a file.
- XML doc comments on public types explain *why*, and cite the spec section (`§5.2`) when the
  reasoning lives there. Do not restate what the signature already says.
- Conventional commits: `feat(scope):`, `fix(scope):`, `chore:`, `test:`, `docs:`.
- Commits are GPG-signed. If signing fails, fix `gpg.program` — never pass `--no-gpg-sign`.

See [`docs/WORKFLOW.md`](docs/WORKFLOW.md) for branching, issues and the definition of done.
