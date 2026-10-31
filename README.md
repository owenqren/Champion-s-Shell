# Champion's Shell

A Hollow Knight mod that adds a new charm, **Champion's Shell**. It is inspired by the Champion's Tunic in *Breath of the Wild*: while it is equipped, you can see your enemies' health.

> Status: early development. The charm and its enemy health bars work in a test build; the second ability, finding the charm in the world and the Nailsmith upgrade are not built yet.

## Features so far

- **The charm.** Champion's Shell costs 1 notch. It has a placeholder icon for now, and a test build hands it to you when you load a save.
- **Enemy health bars.** While the charm is equipped, every enemy you can see shows its remaining HP in a small outlined bar above it. Enemies behind terrain get no bar. Bosses get a bigger bar, which is built to move to the top of the screen when the boss is out of view. Godhome support is built too. Neither of those two has been tested in game yet.

## Planned features

- **A second ability.** Upgrading the charm adds a combat bonus. This is not decided yet; bonus damage against bosses and extra soul are the current ideas.
- **Easy to find.** The base charm costs 1 notch and is found early in the world.
- **Upgrades at the Nailsmith.** Pay geo and hand over a relic, such as a Wanderer's Journal, to upgrade the charm. Each upgrade makes the second ability stronger. Lemm is unchanged; you just keep the relic instead of selling it.

## Requirements

- Hollow Knight 1.5.78 (the Modding API does not support the newer Unity 6 versions yet)
- [Hollow Knight Modding API](https://github.com/hk-modding/api)
- [SFCore](https://github.com/SFGrenade/SFCore)

## How it works

The mod changes the game while it runs, using Modding API hooks and edits to PlayMaker FSMs. It does not replace any game files or scenes.

## License

Released under the [MIT License](LICENSE). This covers only this mod's own code and files, not Hollow Knight, which belongs to Team Cherry.

## How the code works

This section explains how Champion's Shell is put together. For line-by-line detail, read the comments in the code, since every line has one.

The mod is four C# files in `ChampionsShell/`, plus one icon:

| File | Job |
|---|---|
| `ChampionsShell.cs` | Entry point. Sets everything up and handles saving. |
| `ChampionsShellCharm.cs` | The charm's name, description, cost and icon. |
| `EnemyTracker.cs` | Finds enemies and keeps track of their HP. |
| `EnemyHealthBars.cs` | Draws the bars on screen. |
| `Resources/ChampionsShell.png` | Placeholder charm icon, built into the DLL. |

### What it builds on

- **Modding API.** It's patched into the game's `Assembly-CSharp.dll`. It loads the mod and provides `Mod`, `ModHooks` and per-save settings. It only supports Hollow Knight 1.5.78.
- **MonoMod hooks** (`On.*`). They ship with the Modding API and let the mod run its own code around any game method.
- **SFCore.** A library that adds custom charms to the inventory. It gives the charm an ID past the game's 40.
- **Unity.** Used for the on-screen bars (`Canvas`, `Image`) and the line-of-sight check (`Physics2D`).

### Startup

1. The Modding API finds the `ChampionsShell` class in the DLL and creates it.
2. It calls `Initialize()`, which:
   - creates the charm and applies any saved state to it
   - creates the `EnemyTracker` and hooks enemy spawning
   - creates the `EnemyHealthBars` object
   - adds the temporary test giver

Everything else then runs from hooks and from Unity's once-per-frame `Update`.

### The charm

`ChampionsShellCharm` extends SFCore's `EasyCharm` and only supplies data: name, description, 1-notch cost and icon.

The game has fixed save fields for charms 1 to 40, so a charm with ID 41 has nowhere to be stored. Instead, the game looks these values up by name (`gotCharm_41`, `equippedCharm_41`, `charmCost_41`), and `EasyCharm` answers through `ModHooks`.

The charm has no effect code of its own. The health bars simply check whether it's equipped.

### Saving

`ChampionsShell` implements `ILocalSettings<LocalSettings>`, so the Modding API stores `LocalSettings` next to each save file.

- `OnSaveLocal` copies the charm's owned, equipped and new flags into the settings.
- `OnLoadLocal` puts them back.

**Watch out:** the Modding API also calls `OnLoadLocal` on scene changes, including before `Initialize()` has run. If it throws, the game's own scene setup is skipped and the game freezes on the loading screen. That's why `charm` can be null and is always checked in `OnLoadLocal`, and why the settings are kept in a field and applied again in `Initialize()`.

### Tracking enemies

`EnemyTracker` hooks `HealthManager.Start`, which runs for every enemy. After the game's own `Start` finishes, `hp` holds the enemy's real maximum, because Godhome scaling is applied there. Each enemy becomes a `TrackedEnemy`.

An enemy counts as a boss if any of these is true:

1. It's in `BossSceneController.Instance.bosses` (Godhome).
2. Its private `unlockBossScene` field is set, meaning it unlocks a Hall of Gods statue.
3. Its object name is in `ExtraBossNames`, a manual list that's empty for now.

`Refresh()` runs every frame. It removes dead or destroyed enemies, raises `MaxHp` if a phase refills a boss, and sets `InFight` once an enemy has lost HP. HP is read directly each frame instead of hooking damage, because some boss FSMs change `hp` themselves.

### Drawing the bars

`EnemyHealthBars` is a `MonoBehaviour` on its own object that survives scene loads. It has a screen-space overlay canvas, so positions are in screen pixels.

Each frame, `Update()`:

1. Calls `tracker.Refresh()`.
2. Stops if the charm isn't equipped or the game is paused, hiding all bars.
3. For each living enemy, finds the point above its head with `AboveOnScreen`, then checks `CanSee`.
4. Draws a bar for each enemy that passes both.
5. Hides the bars of enemies that didn't.
6. Shows the docked boss bar at the top of the screen if a boss being fought is out of view. That bar shows the combined HP of all bosses in the fight.

**Line of sight.** `CanSee` draws a line from the Knight to the enemy's middle and to its top. If terrain (layer mask `256`) blocks both, the enemy is out of sight. A bar stays for `SeenGraceTime` after that, so it doesn't flicker at corners. A boss being fought skips this check, so going behind a wall docks its bar instead of hiding it.

**Bar pieces.** Each bar (`BarWidget`) is a dark background with a pale fill inside it. An `Outline` effect marks the full length. The fill's width is set through `anchorMax.x`, which is current HP divided by max HP. The background is opaque because the outline is drawn behind it and would otherwise tint the whole bar.

**Reusing bars.** `enemyBars` maps each enemy to its bar, so a bar follows the same enemy from frame to frame. Finished bars go to `spareBars` for reuse, so the mod isn't constantly creating and destroying UI objects.

**Sliding.** `Place` moves each bar part of the way to its target every frame, so boss bars slide between the above-boss and docked positions instead of snapping.

### Things you can tune

All of these are constants at the top of their file, and sizes are in pixels at 1080p.

| Constant | File | Controls |
|---|---|---|
| `EnemyBarSize`, `BossBarSize`, `DockedBarSize` | `EnemyHealthBars.cs` | Bar sizes |
| `GapAbove` | `EnemyHealthBars.cs` | Height of the bar above an enemy |
| `DockedFromTop` | `EnemyHealthBars.cs` | Docked bar's distance from the top of the screen |
| `OutlineWidth` | `EnemyHealthBars.cs` | Outline thickness |
| `BackColor`, `FillColor`, `OutlineColor` | `EnemyHealthBars.cs` | Bar colours |
| `SlideTime` | `EnemyHealthBars.cs` | How quickly bars slide |
| `SeenGraceTime` | `EnemyHealthBars.cs` | How long a bar stays after losing sight of an enemy |
| `ExtraBossNames` | `EnemyTracker.cs` | Bosses the automatic checks miss |
| `IconPixelsPerUnit` | `ChampionsShellCharm.cs` | Icon size in the charm menu |

### Temporary code

`GiveCharmForTesting` in `ChampionsShell.cs` gives you the charm on a new game or when a save loads. It also sets `hasCharm`, which unlocks the Charms tab. It gets removed when the charm can be found in the world, along with the two hook lines in `Initialize()` that call it.

### Building

From `ChampionsShell/`, run `dotnet build`. The project reads game files from a separate 1.5.78 copy of the game (`~/HKModding/HollowKnight-1.5.78/` by default), and that copy needs the Modding API and SFCore installed. After each build, the DLL is copied into the copy's `Managed/Mods/ChampionsShell/` folder. To use a different location, add `-p:HollowKnightCopy=/path/to/copy`.
