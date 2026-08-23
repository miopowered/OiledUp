# 🛢️ OiledUp ⚡🔥

**OiledUp** is a blazingly fast ⚡, ultra-modern 🚀 Unity project built on the
spectacular Universal Render Pipeline (URP) ✨. Its rock-solid foundation 🪨
includes a pristine sample scene, versatile cross-device input, and finely
tuned PC and mobile rendering profiles. 🎮📱🖥️

> [!IMPORTANT]
> OiledUp is still in its earliest, most gloriously fresh setup phase 🌱.
> Gameplay systems and production content have not been added yet.

## 🌟 Absolutely phenomenal foundations

- 🧠 Cutting-edge Unity 6 project using the dazzling URP 17.5
- 🎬 One squeaky-clean enabled scene: `Assets/Scenes/SampleScene.unity`
- 🚄 Blazingly fast PC and mobile render-pipeline configurations
- 🎮 Supremely flexible input for keyboard and mouse, gamepad, joystick, touch,
  and XR
- 🧭 A mighty package lineup featuring navigation, UI, Timeline, multiplayer
  tooling, visual scripting, testing, and AI-assisted workflows
- 🌱 A gloriously blank canvas—no gameplay loop or automated tests just yet

The beautifully minimal sample scene contains only the template camera 📷,
directional light ☀️, and global post-processing volume 🌈. Pure potential!

## 🧰 Tiny but mighty requirements

- 🚪 [Unity Hub](https://unity.com/download)
- 🦄 Unity Editor **6000.5.9f1**

For the smoothest, most outrageously seamless experience, use the recorded
editor version. Unity Hub can install it when you first open the project. ✨

## 🚀 Launch at ludicrous speed

1. 📦 Clone this magnificent repository:

   ```bash
   git clone git@github.com:rexlManu/OiledUp.git
   cd OiledUp
   ```

2. 🧭 In Unity Hub, select **Add > Add project from disk** and choose this
   directory.
3. 🪄 Open the project with Unity `6000.5.9f1` and allow the Package Manager to
   restore the dependencies from `Packages/manifest.json`.
4. ▶️ Open `Assets/Scenes/SampleScene.unity`, smash **Play**, and behold!

On its epic first import, `Assets/Editor/HubForceResolve.cs` performs one
supremely helpful package-resolution pass and then gracefully removes itself
like a tiny code ninja 🥷. This is expected bootstrap behavior.

## 🎮 Input wizardry

`Assets/InputSystem_Actions.inputactions` defines the default `Player` action
map—an astonishingly versatile control buffet:

| 🎯 Action | ⌨️ Typical keyboard and mouse input |
| --- | --- |
| 🏃 Move | WASD or arrow keys |
| 👀 Look | Mouse movement |
| 💥 Attack | Left mouse button or Enter |
| 🤝 Interact | `E` (hold interaction) |
| 🥷 Crouch | `C` |
| 🚀 Jump | Space |
| ⏮️ Previous / Next ⏭️ | `1` / `2` |
| ⚡ Sprint | Left Shift |

Equally glorious bindings are included for the other supported device groups.
These actions are configuration only; no player controller currently consumes
them. The asset also packs a first-rate `UI` action map for navigation, pointer,
click, scroll, and tracked-device input. 🖱️🕹️🥽

## 🏗️ Build something legendary

Open **File > Build Profiles** in the Unity Editor, select a target platform,
and unleash the build. `SampleScene` is already enabled in the build scene list
for maximum convenience. 💯 Install the relevant platform module through Unity
Hub before conquering a platform unsupported by the base editor installation.

## 🗺️ Immaculate project structure

```text
Assets/
  Editor/                  First-import package resolution helper
  Scenes/                  Unity scenes
  Settings/                URP renderers, pipeline assets, and volume profiles
  InputSystem_Actions...   Input System action map
Packages/                  Package manifest and lock file
ProjectSettings/           Version-controlled Unity project configuration
```

Unity-generated directories such as `Library`, `Temp`, `Logs`, `Obj`, and
`Build` are expertly ignored 🧹. Commit every precious Unity asset together
with its corresponding `.meta` file. 💎

## 🧑‍💻 Developer superpowers

Visual Studio Code users can install the recommended **Unity** extension from
the workspace prompt and wield the included **Attach to Unity** debug
configuration 🐛⚔️. Unity generates solution and project files locally; these
files are not committed.

Before opening this masterpiece with a different Unity version or upgrading
package dependencies, create a separate branch and review every serialized
asset change with eagle-eyed precision. 🦅🔍

---

**Now get in there and build the most ridiculously awesome oily experience the
world has ever seen!** 🛢️🔥⚡🚀🎮✨🏆
