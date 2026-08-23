# CI setup

`.github/workflows/tests.yml` runs the EditMode suite on every PR through
[game-ci](https://game.ci/). It skips itself with a warning until the licence secrets exist, so an
unconfigured repo does not fail every PR with an activation error.

## Why this needs a manual step

A Unity Personal licence cannot be generated headlessly. Activation requires uploading a machine
-specific request file to Unity's licensing site and downloading the result. Unity 6 also no longer
writes the old `C:\ProgramData\Unity\Unity_lic.ulf` on activation, so there is usually no existing
file to copy.

This is a one-time, ~3 minute task.

## Steps

**1. Generate the activation request.** Close the Unity Editor first — it holds a project lock.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" `
  -batchmode -nographics -logFile - -quit -createManualActivationFile
```

This writes `Unity_v6000.5.9f1.alf` into the working directory.

**2. Exchange it for a licence.** Go to <https://license.unity3d.com/manual>, upload the `.alf`,
choose *Unity Personal*, and download the resulting `.ulf` file.

**3. Add the secrets.**

```powershell
gh secret set UNITY_LICENSE  --repo rexlManu/OiledUp < "path\to\Unity_v6000.x.ulf"
gh secret set UNITY_EMAIL    --repo rexlManu/OiledUp
gh secret set UNITY_PASSWORD --repo rexlManu/OiledUp
```

`gh secret set` without a value prompts for it, so the password never lands in shell history.

**4. Verify.**

```powershell
gh secret list --repo rexlManu/OiledUp
gh workflow run Tests --repo rexlManu/OiledUp
gh run watch --repo rexlManu/OiledUp
```

## Known risk: editor image availability

game-ci publishes a Docker image per Unity version, and new versions lag by days to weeks. If the
run fails with an image-not-found error for `6000.5.9f1`, check
<https://game.ci/docs/docker/versions> and either wait or temporarily pin `unityVersion` in the
workflow to the nearest published 6000.5.x image.

This is why the local compile check plus `Unity_RunCommand` stay the real gate — CI is a safety net,
not the primary loop.

## Cost

The repo is private, so Actions minutes are metered. A cached EditMode run is a few minutes; a cold
one with full package resolution can be 15+. The `concurrency` block cancels superseded runs on the
same branch, and the `Library` cache does the heavy lifting. If minutes become a problem, drop the
`push` trigger and keep `pull_request` only.
