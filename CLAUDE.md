# RESIDUE — working notes for coding agents

Co-op first-person oil analysis simulator. Unity 6000.5.9f1, URP, Netcode for GameObjects.

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

The `unity` MCP server talks to the running Editor over a named pipe. It exposes
`Unity_RunCommand` (compiles *and executes* arbitrary C# in the Editor, returning compile status and
logs) and `Unity_GetConsoleLogs`. Between them you can trigger an `AssetDatabase.Refresh()`, run the
test suite, create assets, and read the resulting errors — without a GUI.

- The Editor must be **running** for these to work. Closed Editor = dead tools, not a broken setup.
- Launch Unity with `-automated` when agents will be driving it unattended. Without it, a modal
  dialog blocks the Editor and every MCP call hangs until a human clicks it.
- Unity holds a project lock, so `Unity.exe -batchmode` **cannot** run while the Editor is open.
  Do not reach for batchmode as a workaround.

### 3. Tests

`Assets/Tests/EditMode/ChemistryTests.cs` implements the §5.6 suite. Each test guards a specific
promise the game makes to the player; read the comment above one before changing it. Run them via
the Test Runner window, via `Unity_RunCommand`, or in CI.

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

---

## Conventions

- C# 9 (Unity 6). Target-typed `new()`, switch expressions, pattern matching are all available.
- One public type per file, filename matches. Enums may share a file.
- XML doc comments on public types explain *why*, and cite the spec section (`§5.2`) when the
  reasoning lives there. Do not restate what the signature already says.
- Conventional commits: `feat(scope):`, `fix(scope):`, `chore:`, `test:`, `docs:`.
- Commits are GPG-signed. If signing fails, fix `gpg.program` — never pass `--no-gpg-sign`.

See [`docs/WORKFLOW.md`](docs/WORKFLOW.md) for branching, issues and the definition of done.
