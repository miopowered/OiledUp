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
- [x] **Results slips on a client.** The paper exists in every process. Only the record travels —
      §3.2 keeps a slip a local prop for the same reason it keeps a vial one — and `SlipReconciler`
      rebuilds the trays from it each frame. Filing a result was host-only until this landed, which
      is a hole in the middle of the loop: two people run instruments in parallel and only one could
      do the paperwork.
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
- Take the results slip out of an instrument's tray, read it in hand, rack it, and file it at the
  desk. See other players carrying paper around, and lose the race for a slip two of you reached for.

- Read the end-of-day report, and the closing screen when the run ends. Everybody worked the shift,
  so everybody sees the reckoning. It shows between shifts at every desk at once and comes off all of
  them when somebody starts the next day.

Not yet, and the reason:

| Blocked | Why |
|---|---|
| — | Nothing at the desk. |

### Why the report is allowed to name a fault

`ConsequenceReport` is the one screen in the game that says what was actually wrong (§4.3), so
replicating it looks like a hard-rule-2 problem. It is not, quite: the fault is named only *after*
the verdict has been scored and the money has moved, which is the same argument that lets the host's
own screen print it. Content is not the risk — timing is.

Two things put a sample back in play, and a report naming the fault on one of them is the answer to a
question the game has not finished asking:

- **MONITOR on a developing fault requeues the unit** (§5.4), and the re-draw carries the *same*
  fault further along. So a report that puts its own unit back in play crosses **without the
  diagnosis** — `ReportView.From` withholds any headline naming the fault or its root cause.
- **A record re-opened after a recalibration** (§5.3) goes back to `Measured` and can be re-filed.
  This one needs no rule: a report exists only for a sample `TryResolve` accepted, and `Resolved` has
  no outgoing edge in the lifecycle table, so a reported record can never come back. That dependency
  is pinned by `NetworkViewTests.AReportedRecord_CanNeverComeBackIntoPlay`.

And the rows are on the wire **only between shifts**. `LabState.LastReports` outlives the day it
describes; `BeginDay` raises `DayInProgress` before it generates the re-draws, so publishing nothing
while a day is open closes the window strictly before the sample walks back in.

The headline names the tank the vial was *filed under*, which since #73 is the tag on its label — so
a report can be matched to a bottle and to a terminal row without a lookup.

### How vial props work

1. A `VialView` — sample id, the **paper label**, volume, and `SampleLocation` — replicated in its
   own list, separate from `SampleView`. The two answer different questions and change at different
   rates: this one is what the world reads, that one is what screens draw.

   > Before #73 the split carried a second, sharper job: keeping the label away from any screen that
   > could diff it against a player-typed tag and hand over a mis-log for free. Booking-in is gone,
   > so `RecordTag` *is* the label and there is no diff to make. The split now stands on the first
   > argument alone.
2. `IVialSlots` is the surface abstraction. `CartonProp`, `SampleRack` and `MachineStation` each
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

### How results slips work

1. **A `SlipView` names its reading rather than carrying one.** The row is a ticket, a
   `ResultView.Key`, the sample, the blank flag, what is printed across the top, and a
   `SampleLocation`. The numbers travel once, as the `ReadingView` rows already under that key. A
   copy on the slip as well would be a second wire path to the same figures, and the day the two
   disagreed the paper in a player's hand and the panel at the desk would quote different results for
   one run.
2. **`ResultSlips` gained a location.** It was already the host's record of which slips exist and who
   is holding one; it now also holds *where*, because "not in anyone's hands" is not enough to draw
   with. `LabCommandExecutor` records the rack hole a put-down names, and a dropped player's paper
   goes back to the tray that printed it — a carry socket is destroyed with its avatar, taking the
   prop parented to it.
3. **`InMachine` means two different sockets.** An instrument holds a vial in its sample path, where
   the station mediates access (§5.4), and paper in an output tray, which is exactly the thing you
   walk up and take. `PropSockets.ForSlip` resolves the tray through `LabRuntime.RegisterTray` and
   delegates everything else — a rack hole, a pair of hands — to the shared lookup, because a slip on
   a rack is competing for the same shelf space a bottle is (§5.5).
4. **A slip is consumed by filing**, which neither of the other local props is: a vial is spent and a
   bottle is refilled, but only paper stops existing because somebody used it correctly. The host
   discards the ticket, the row stops appearing, and `SlipReconciler` destroys it. That is also what
   makes the reconcile-by-set-difference rule load-bearing rather than tidy: a stale slip is a second
   chance to file the same numbers.
5. **Two players reaching for the same tray** is settled by `ResultSlips.TryClaim`, so the loser gets
   a refusal and their `Carried` stays empty. Their *room* agrees a publish later: there is one prop
   per ticket, and it is re-parented into the winner's hands with its colliders off, so the loser
   cannot keep aiming at a pick-up the host is bound to refuse.
6. **`SlipFeed` is the seam** — the fifth of its kind, after `LabCommands.Router`,
   `LabView.Replicated`, `VialFeed.Source` and `RecordFeed.Source`. `Residue.Net.ReplicatedSlips`
   fills in both halves at startup: the snapshot, and the by-key lookup a `PrintoutProp` uses to
   resolve its numbers the one time somebody glances at it.
7. **An outstanding slip keeps its run on the wire.** `LabNetwork` publishes every unfiled slip's
   result alongside each instrument's own. Without that, carrying a slip away and running the machine
   again took its numbers off the wire — the run is not filed and is no longer `LastResult` — and the
   client's copy of the paper would go blank while the host's still read.

---

## Two instances on one machine

The supported way to test co-op locally. Launch two builds with **different** `-playerId` values:

```powershell
cd F:\Unity\OiledUp\Output
Start-Process ".\My project.exe" -ArgumentList '-playerId','tester-one','-screen-fullscreen','0','-screen-width','1280','-screen-height','720'
Start-Process ".\My project.exe" -ArgumentList '-playerId','tester-two','-screen-fullscreen','0','-screen-width','1280','-screen-height','720'
```

The connect menu prints the resolved identity in small grey text at the bottom, so you can see at a
glance which window is which. `RESIDUE_PLAYER_ID` does the same job for a process you cannot pass
arguments to — the Editor, most usefully, since it must be set before the Editor launches.

### Why the flag exists, and what it has to steer

Two processes on one machine share `Application.persistentDataPath`. **Two separate things are
cached there**, and the flag has to cover both or it only half works:

1. **The local id file.** Without an override both read the same GUID, and the host treats the second
   window as the first player *reconnecting* — handing it the first player's hands. That is the
   stable-id bug from #17, reintroduced by the test harness.
2. **The UGS Authentication session.** Anonymous sign-in caches its token in the same place, so both
   instances sign in as the *same cloud player*. The symptom is the second window failing to join
   with **"player is already a member of the lobby"** — the Lobby service being entirely correct
   about something nobody meant.

`-playerId` therefore also names an **Authentication profile** (`InitializationOptions.SetProfile`),
which is what gives anonymous sign-in a session of its own. The name is sanitised to the characters
profiles allow, because a rejected profile throws out of `InitializeAsync` and would cost the connect
rather than the testing convenience it was asked for.

The profile is set **only when overridden**. A shipped build on somebody's own machine keeps the
default profile and the identity it has been using all along — rejoin depends on that id surviving a
restart (§M4), and quietly profiling every install would break it.

> The Editor shares `persistentDataPath` with a build of the same project, so an Editor client and a
> build host collide in exactly the same way. Two builds is the cleaner test.

None of this applies when playing with someone else: separate machines, separate data paths.
