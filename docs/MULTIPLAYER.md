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
- [ ] **Action RPCs.** Every interaction currently calls `LabState` directly, which only works on the
      host. Each becomes a server call validated through the lifecycle gateways that already exist.
      This is the bulk of what is left.
- [ ] **Connect flow.** Host / Join screen, Lobby join codes, Relay transport.
- [ ] **Player bodies.** Spawning and replicating the `CharacterBody` built at M2.
- [ ] **Vivox proximity voice.**

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
