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
- [ ] **Vial props on a client.** See below — the largest thing left.
- [ ] **Results on a client.** See below.
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

Not yet, and the reason in each case:

| Blocked | Why |
|---|---|
| Taking a vial from the crate, racking one, loading one, taking one back | **Vial props do not exist on a client.** §3.2 makes a vial a local prop rather than a `NetworkObject` — 200+ per shift would drown the connection — and nothing replicates `SampleLocation`, so a client's crate and racks are genuinely empty. The world layer says so out loud (`ILabView.HasVialProps`) instead of drawing a bare shelf, and `MachineStation` refuses a take-back rather than letting the host accept one: the host *would* accept, and the sample would then be recorded as held by a player with nothing in their hands and be unreachable by anyone until that connection dropped. |
| Reading a result off an instrument display, or using the terminal at all | **`TestResult` does not replicate.** No view can express measured values. The terminal's sample list, instruments panel and calibration certificates could all be built from what already crosses today — but the results table cannot, and a terminal that offered FILE NORMAL / MONITOR / CRITICAL with no evidence on the screen would be asking for a call the player could not check, which hard rule 3 forbids. So it says what it is instead. |

### Unblocking vial props

1. A `VialView` — sample id, the **paper label**, volume, and `SampleLocation` — replicated in its
   own list. The label has to travel: reading it off the bottle is the only tell for a mis-log
   (§5.1). It must **not** join `SampleView`, which feeds screens; a screen that could diff the label
   against `RecordTag` would correct the player's mistake for them. Two lists, one rule each.
2. A surface abstraction so a prop can be parented client-side: `IntakeCrate` and `SampleRack` build
   their own slot transforms and only `LabRuntime.RegisterFixture` knows where fixtures are, which
   gives a fixture root but not a slot.
3. A client-side prop manager on `LabRuntime` that reconciles props against that list each publish —
   spawn on arrival, re-parent on move, destroy on `Consumed`.
4. Held-by-another-player needs a client id to carry-socket lookup, which today only `PlayerAvatar`
   could answer.

### Unblocking results

A `ResultView` carrying machine def id, day, suspect and blank/reference flags, plus the element
readings as a `FixedList` of (element id, value) pairs. The one design question is the budget: a
`FixedList512Bytes` holds about fourteen readings and an elemental panel can exceed that, so either
the panel is split across rows or the readings ride in their own list keyed by result. Everything
downstream — the terminal's results table, run log, suspect archive and the instrument display —
falls out of it.

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
