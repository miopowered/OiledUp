# Multiplayer setup

What a human has to do by hand before co-op can run, and what is still being built around it.

Everything here is clicking in the Unity Editor and a browser. None of it is scriptable, and none of
it is secret — a Unity Gaming Services project id ships inside the game build anyway.

---

## What you do — about five minutes

### 1. Link the project to Unity Cloud

In the **Unity Editor**:

1. **Edit → Project Settings → Services**
2. Sign in with your Unity account if prompted.
3. Choose your organisation.
4. **Create** a project called `Oiled Up`, or **Link** an existing one.

That writes `cloudProjectId` into `ProjectSettings/ProjectSettings.asset`. Everything else reads it
from there.

> If the Services window shows an empty organisation list, the Editor is signed into a different
> account than the dashboard. Sign out and back in from the account menu, top right.

### 2. Turn on four services

At **https://cloud.unity.com**, open the project you just linked. Enable:

| Service | Why it is needed |
|---|---|
| **Authentication** | Issues each player a `PlayerId` that survives a disconnect. This is what rejoin is keyed on — without it, a returning player cannot be recognised. Make sure **Anonymous** sign-in is enabled; nobody should need an account to play. |
| **Relay** | Carries traffic between host and clients. Neither of you has to port-forward. |
| **Lobby** | Turns a join code into a Relay connection, so your friend has something short to type. |
| **Vivox** | Proximity voice. Last thing wired up, so it is fine if this one lags behind. |

Each is a "Get started" / "Enable" button on its own page in the project.

### 3. Commit the link

`ProjectSettings.asset` now names the cloud project. Commit it — your friend's checkout has to point
at the same project or you are not in the same game.

### 4. Tell me when it is done

Say so and I will read `cloudProjectId` back off disk and confirm the link actually landed. Unity
occasionally needs an Editor restart before it writes that value, and it is worth catching before it
looks like a netcode bug.

---

## What your friend needs

A build, and nothing else. No Unity account, no dashboard access, no port forwarding — anonymous
sign-in plus Relay means the whole thing is a join code.

---

## What is still being built

Setup alone will not make co-op work. Remaining, in order:

- [x] **Host authority.** A client no longer builds its own lab. `LabRuntime.SimulatesLocally` — the
      one that mattered, because a client running the simulation would hold ground truth locally
      whether or not anything crossed the wire.
- [x] **Client-safe views.** What a client is allowed to know, as types that cannot express anything
      more.
- [x] **Sessions and rejoin.** Identity, the roster, and the rule for what happens to a carried vial
      when its holder drops.
- [x] **Action RPCs.** Every interaction is a request the host validates through the lifecycle
      gateways that already existed. `LabCommands` is the seam; `LabCommandExecutor` is the one place
      an action becomes a change.
- [x] **Connect flow.** Host / Join screen, Lobby join codes, Relay transport.
- [x] **Player bodies.** Spawning and replicating the `CharacterBody` built at M2.
- [x] **A client that can read the room.** Stations, buttons, screens and the HUD read `ILabView` /
      `IMachineView` through `LabView.Current`, satisfied by the host's `LabState` on one side and by
      the replicated views on the other. Before this, every one of them found no `LabState` on a
      client and switched itself off.
- [x] **Vial props on a client.** The bottles exist in every process. Only the record travels — §3.2
      keeps a vial a local prop, because 200+ `NetworkObject`s a shift would drown the connection —
      and `VialReconciler` rebuilds the room from it each frame.
- [x] **Results on a client.** Measured values replicate, so the terminal is a real terminal on a
      client and an instrument's own screen shows the numbers it just produced. See below for the
      shape and for what is still host-only.
- [ ] **Vivox proximity voice.**

---

## What a joined client can and cannot do

Working today, identically to the host:

- Walk the room, see other players, read every instrument's name, occupancy, run clock and progress
  bar, and watch its status light.
- Flush an instrument, run a solvent blank, run a certified standard, recalibrate. None of these
  needs a vial, which is why they are complete.
- Start a run on an instrument somebody else has loaded, and see when a result is waiting in it.
- Read the day clock, the balance, reputation, solvent and ampoule stock, and the open-sample count.
- Pick up, read and put down the reference manual.
- Take a vial out of the delivery crate and read the label off it, rack it, load an instrument with
  it and take it back out. See other players carrying bottles around.

- Book a vial in, read its panel grouped by category, read the run log, the instruments' blanks and
  their calibration certificates, re-open a record in doubt, order solvent and ampoules, and file a
  verdict — all from the same terminal the host uses, drawn by the same code.
- Read the numbers off an instrument's own screen when a run finishes.

Not yet, and the reason:

| Blocked | Why |
|---|---|
| Reading the end-of-day report | **`ConsequenceReport` does not replicate.** It is the one screen in the game that names a fault (§4.3), and it does so after the consequence has landed. Putting that on the wire is a deliberate decision about ground truth and deserves its own change rather than riding along with the results table. Ending the day works from either desk; a joined one says where the summary is drawn. |

### How vial props work

1. A `VialView` — sample id, the **paper label**, volume, and `SampleLocation` — replicated in its
   own list. The label has to travel: reading it off the bottle is the only tell for a mis-log
   (§5.1). It deliberately does **not** join `SampleView`, which feeds screens; a screen that could
   diff the label against `RecordTag` would correct the player's mistake for them.
2. `IVialSlots` is the surface abstraction. `IntakeCrate`, `SampleRack` and `MachineStation` each
   hand out the transform for slot *N*, and register it alongside their position with
   `LabRuntime.RegisterFixture`, so a client can turn `rack#3` back into a place in the room.
   Occupancy is read off the slot transforms — a slot's child *is* its occupant — so props parented
   by the host's own code and props parented by the reconciler are counted by the same rule.
3. `VialReconciler`, driven from `LabRuntime.Update`, walks the list each frame: spawn what appeared,
   re-parent what moved, destroy what stopped appearing. It never touches the local player's hands,
   which belong to the callbacks in `LabCommands.Attempt`.
4. `VialFeed` is the seam — the third of its kind, after `LabCommands.Router` and
   `LabView.Replicated`. `Residue.Net.ReplicatedVials` fills it in at startup and answers
   "where are client *N*'s hands" through a small accessor on `PlayerAvatar`.

### How results work

1. **Two lists, not a nested one.** `ResultView` is one finished run — a key, the sample it belongs
   to, whether it has been filed, the machine definition and the placed instrument, the day, volume
   and cost, and the blank / reference / suspect flags. `ReadingView` is one (element id, value)
   pair naming the run's key.
2. **The budget question, answered by refusing a budget.** Readings inside the result — a
   `FixedList512Bytes` of pairs — holds about fourteen, and the panel it has to hold is content the
   tables are free to grow. Every cap needs an overflow rule, and every overflow rule means a
   terminal showing fewer numbers than the host scored the verdict against: a call the player could
   not check, which hard rule 3 forbids. Silent truncation would be worse again. A flat keyed list
   has no cap to exceed; the cost is four bytes of key per reading, and the key is what makes a row
   self-describing rather than positional — readings for a key that has not arrived draw nothing,
   where an offset into a neighbouring list would draw the wrong numbers under the right heading.
3. **What the host publishes.** Every result filed against a record that has not been resolved, plus
   each instrument's last reading, its last solvent blank, and the certificate on file. A resolved
   record is closed and nothing draws its numbers, so they stop travelling. Filed and unfiled are
   different rows on purpose: an instrument finishing a run puts nothing on a record until somebody
   carries the slip to the desk (§5.1), so the terminal draws the filed ones and the machine's own
   screen draws whatever is on it.
4. **`RecordFeed` is the seam** — the fourth of its kind, after `LabCommands.Router`,
   `LabView.Replicated` and `VialFeed.Source`. `Residue.Net.ReplicatedRecords` fills it in on spawn
   and rebuilds `SampleState`, `TestResult` and `CalibrationCheck` objects from the rows, so the
   terminal has one set of drawing code rather than a host version and a client version that can
   quietly disagree. Nothing there computes a reading: §3.1 keeps `MeasurementPipeline` host-only.
5. **The certificate is rebuilt, not sent.** A client runs `CalibrationCheck.From` against a
   `ReferenceStandard` blended from its own content tables, which is where the host's came from too
   — §5.3 turns on every certified figure being one the player can look up in the manual, so
   deriving it is what stops the certificate and the limits disagreeing across two screens.
6. **The instrument display pulls.** A client has no run-completed event, so `MachineDisplay` asks
   the feed four times a second and redraws when what is on the glass is not the reading the host
   published. It captions a sample run with the sample id: the paper label reaches a client through
   `VialView` and must never reach a screen (§5.1), and the typed tag would caption a client's screen
   differently from the host's.

---

## Testing before the cloud project exists

Development does not block on any of the above. `LocalPlayerIdentity` mints and persists a GUID, so
the session and rejoin logic runs without Authentication.

One trap it exists to avoid: two instances on one machine share `Application.persistentDataPath`, so
they would read the *same* id file and the host would treat the second window as the first player
reconnecting — handing it the first player's hands. Pass an override so each instance is a distinct
person:

```
"OiledUp.exe" -playerId tester-two
```

or set `RESIDUE_PLAYER_ID` in the environment.
