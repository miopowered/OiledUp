# Workflow

How work gets picked up, done and landed on this project. Optimised for one human plus a rotating
cast of coding agents, where the agents usually cannot see the Unity Editor.

---

## The core problem this solves

Most of this game is buildable headlessly — the chemistry model, content tables, consequence
resolution, save/load, tooling. A meaningful minority is not: scene authoring, ProBuilder geometry,
lighting bakes, prefab wiring, anything you have to *look* at.

So every unit of work is classified up front by whether an agent can finish and verify it alone.
Mixing the two inside one issue is what produces half-done work nobody can review.

---

## GitHub is the to-do list

Nothing lives in a chat log or a scratch file. If it should happen, it is an issue.

### Milestones

`M0`–`M8`, mirroring [`docs/DESIGN.md`](DESIGN.md) §8. Every issue belongs to exactly one. The
milestone description carries that milestone's **acceptance criterion** verbatim from the spec —
that is the bar, not "the code is written".

Milestones are gates, not buckets. §9 is explicit: do not start M4 until M2's acceptance holds.

### Labels

| Label | Meaning |
|---|---|
| `area:chemistry` `area:gameplay` `area:netcode` `area:ui` `area:art` `area:content` `area:tooling` | Which part of the system |
| `type:feat` `type:bug` `type:chore` `type:test` `type:docs` | Change kind; matches the commit prefix |
| **`agent:ready`** | Fully specified, headlessly verifiable. An agent can take this unattended. |
| **`agent:assisted`** | An agent does the code, a human does one GUI step (wire a prefab, bake, eyeball it). |
| **`agent:blocked-on-gui`** | Needs the Editor. Batch these into a single sitting. |
| `needs-decision` | Blocked on a design call. Not startable. |
| `priority:now` `priority:next` `priority:later` | Ordering within a milestone |
| `keystone` | Guards a design pillar. Extra review; never cut to save time. |

An issue is only `agent:ready` when its acceptance criteria can be checked by running a command.
"Looks right" is not acceptance criteria. If you cannot write the check, the issue is
`agent:assisted` and you say which step needs eyes.

### Working an issue

```powershell
gh issue list --label agent:ready --milestone M1
gh issue develop <N> --checkout          # creates feat/<N>-slug and switches to it
```

One issue per branch, one branch per PR. Reference the issue in the PR body with `Closes #N`.

---

## Definition of done

A PR is not ready until all of these hold. Agents: check them yourself, do not delegate to review.

1. **Both compile checks pass.**
   ```powershell
   dotnet build "tools\compilecheck\Residue.CompileCheck.csproj" -v q --nologo
   dotnet build "tools\compilecheck\Residue.CompileCheck.Editor.csproj" -v q --nologo
   ```
2. **`ChemistryTests` green** if anything under `Residue.Chemistry`, `Residue.Data` or
   `ContentTables.cs` changed.
3. **`.meta` files committed** alongside every added asset. See CLAUDE.md — a missing `.meta` is a
   GUID-churn bug that only shows up on someone else's machine.
4. **No new Editor console errors or warnings.** Check via `Unity_GetConsoleLogs`.
5. **CLAUDE.md updated** if a convention, deviation or hard rule changed.
6. **The issue's acceptance criteria are quoted in the PR** with evidence — command output, a log
   line, a screenshot for visual work.

If part of the issue turned out to be blocked, finish everything else and say explicitly in the PR
what you left and why. Do not silently narrow scope.

---

## Branching

- `main` always compiles and has green tests. It is the only long-lived branch.
- `feat/<issue>-<slug>`, `fix/<issue>-<slug>`, `chore/<slug>`.
- Rebase onto `main` before opening the PR. Merge commits on feature branches make Unity YAML
  conflicts much harder to resolve.
- Squash-merge into `main` so history is one commit per issue.

### Unity-specific merge hazards

Scenes and prefabs do not merge like code. Two people editing `SampleScene.unity` will conflict in
ways git cannot resolve alone, which is why `.gitattributes` routes them through UnityYAMLMerge.

Practical rule: **only one open PR at a time may touch a given scene or prefab.** Say so in the
issue. For anything larger, prefer additive prefabs over editing a shared scene.

---

## Continuous integration

`.github/workflows/tests.yml` runs EditMode tests on every PR via
[game-ci](https://game.ci/). It needs Unity licence secrets in the repo:

| Secret | What |
|---|---|
| `UNITY_LICENSE` | Contents of the `.ulf` activation file |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

Getting the `.ulf` for a Personal licence is a one-time manual step — see
[`docs/CI_SETUP.md`](CI_SETUP.md). Until the secrets exist the workflow is a no-op, so the local
compile check plus `Unity_RunCommand` remain the real gate.

---

## When you are blocked

- **A design call is missing** → open an issue labelled `needs-decision`, state the options and your
  recommendation, then work on something else. Do not guess and do not stall the whole task.
- **You need the Editor** → open an `agent:blocked-on-gui` issue with the exact steps a human should
  perform, and finish everything around it.
- **A test fails and you think the test is wrong** → it usually is not; these tests encode design
  pillars. Say why in the issue before changing the assertion.

---

## Cadence

Commit often and in coherent units — one commit per logical change, not one per file and not one per
day. Push branches early; a draft PR is a fine place to think out loud.
