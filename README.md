# Champion's Shell

A Hollow Knight mod that adds a new charm, **Champion's Shell**. It is inspired by the Champion's Tunic in *Breath of the Wild*: while it is equipped, you can see your enemies' health.

> Status: early development. The charm, its enemy health bars, finding it in the world, its second ability and the Nailsmith upgrade are built. The upgrade works in game, including at the anvil.

## Features so far

- **The charm.** Champion's Shell costs 1 notch. It has a placeholder icon for now. You find it floating on the path from the Dirtmouth bench to the well. Touching it collects it and shows a short popup.
- **Enemy health bars.** While the charm is equipped, every enemy you can see shows its remaining HP in a small outlined bar above it. Enemies behind terrain get no bar. During a boss fight, one wide bar shows at the top of the screen instead, starting from your first hit.
- **Finishing Blow.** Once the charm is upgraded, your nail hits harder against enemies that are nearly dead. Each upgrade makes the bonus start earlier in the fight and adds more damage: below 25% enemy health for +20% damage, then below 32% for +28%, then below 40% for +35%. The charm's description shows its level, from Level 0 of 3 to Level 3 of 3.
- **Upgrades at the Nailsmith.** When you can afford an upgrade, the Nailsmith offers it before his nail offer: a Wanderer's Journal and 300 geo, then a Hallownest Seal and 800 geo, then a King's Idol and 2000 geo. Lemm is unchanged. Once the Nailsmith has left for good, you can still upgrade at his anvil.
- **Not tested in game yet.** The bars in Godhome, and the top boss bar on bosses other than False Knight.

## Requirements

- Hollow Knight 1.5.78 (the Modding API does not support the newer Unity 6 versions yet)
- [Hollow Knight Modding API](https://github.com/hk-modding/api)
- [SFCore](https://github.com/SFGrenade/SFCore)

## How it works

The mod changes the game while it runs, using Modding API hooks. The Nailsmith upgrade also edits a PlayMaker FSM. The mod itself does not replace any game files or scenes.

## License

Released under the [MIT License](LICENSE). This covers only this mod's own code and files, not Hollow Knight, which belongs to Team Cherry.

## How the code works

This section explains how Champion's Shell is put together. For line-by-line detail, read the comments in the code, since every line has one.

The mod is eight C# files in `ChampionsShell/`, plus one icon:

| File | Job |
|---|---|
| `ChampionsShell.cs` | Entry point. Sets everything up and handles saving. |
| `ChampionsShellCharm.cs` | The charm's name, description, cost and icon. |
| `EnemyTracker.cs` | Finds enemies and keeps track of their HP. |
| `EnemyHealthBars.cs` | Draws the bars on screen. |
| `FinishingBlow.cs` | Bonus nail damage to low-health enemies, by upgrade tier. |
| `NailsmithUpgrade.cs` | Charm upgrades at the Nailsmith and his anvil. |
| `WorldPickup.cs` | Places the charm in the world and collects it on touch. |
| `PickupPopup.cs` | The short popup shown on collection. |
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
   - creates the pickup popup and hooks room changes for the world pickup

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

`Refresh()` runs every frame. It removes dead or destroyed enemies (including those reported through `HealthManager.OnDeath`, which False Knight uses without setting `isDead`), raises `MaxHp` if a phase refills a boss, and sets `InFight` once an enemy has lost HP. HP is read directly each frame instead of hooking damage, because some boss FSMs change `hp` themselves.

### Drawing the bars

`EnemyHealthBars` is a `MonoBehaviour` on its own object that survives scene loads. It has a screen-space overlay canvas, so positions are in screen pixels.

Each frame, `Update()`:

1. Calls `tracker.Refresh()`.
2. Stops if the charm isn't equipped or the game is paused, hiding all bars.
3. For each living enemy, adds bosses to the docked bar's totals. For normal enemies, it finds the point above the head with `AboveOnScreen`, then checks `CanSee`.
4. Draws a bar for each enemy that passes both.
5. Hides the bars of enemies that didn't.
6. Shows the docked boss bar at the top of the screen once a boss has lost HP. That bar shows the combined HP of all bosses in the fight. False Knight is the exception: he's fought in rounds, so it shows only the part in play, body or head. When the game refills one part's HP, its round is over and the bar moves to the other part.

**Line of sight.** `CanSee` draws a line from the Knight to the enemy's middle and to its top. If terrain (layer mask `256`) blocks both, the enemy is out of sight. A bar stays for `SeenGraceTime` after that, so it doesn't flicker at corners. Bosses skip this check, since they never get a bar above them.

**Bar pieces.** Each bar (`BarWidget`) is a dark background with a pale fill inside it. An `Outline` effect marks the full length. The fill's width is set through `anchorMax.x`, which is current HP divided by max HP. The background is opaque because the outline is drawn behind it and would otherwise tint the whole bar.

**Reusing bars.** `enemyBars` maps each enemy to its bar, so a bar follows the same enemy from frame to frame. Finished bars go to `spareBars` for reuse, so the mod isn't constantly creating and destroying UI objects.

**Sliding.** `Place` moves each bar part of the way to its target every frame, so bars follow moving enemies smoothly.

### Things you can tune

All of these are constants at the top of their file, and sizes are in pixels at 1080p.

| Constant | File | Controls |
|---|---|---|
| `EnemyBarSize`, `DockedBarSize` | `EnemyHealthBars.cs` | Bar sizes |
| `GapAbove` | `EnemyHealthBars.cs` | Height of the bar above an enemy |
| `DockedFromTop` | `EnemyHealthBars.cs` | Docked bar's distance from the top of the screen |
| `OutlineWidth` | `EnemyHealthBars.cs` | Outline thickness |
| `BackColor`, `FillColor`, `OutlineColor` | `EnemyHealthBars.cs` | Bar colours |
| `SlideTime` | `EnemyHealthBars.cs` | How quickly bars slide |
| `SeenGraceTime` | `EnemyHealthBars.cs` | How long a bar stays after losing sight of an enemy |
| `ExtraBossNames` | `EnemyTracker.cs` | Bosses the automatic checks miss |
| `IconPixelsPerUnit` | `ChampionsShellCharm.cs` | Icon size in the charm menu |
| `LowHealthByTier`, `DamageByTier` | `FinishingBlow.cs` | When the bonus applies and how strong it is |
| `RelicByTier`, `GeoByTier` | `NailsmithUpgrade.cs` | What each upgrade costs |
| `TargetScene`, `TargetPosition` | `WorldPickup.cs` | Where the charm is found |
| `PickupRadius`, `BobHeight`, `BobSpeed` | `WorldPickup.cs` | Pickup distance and bobbing |
| `ShowTime`, `FadeTime` | `PickupPopup.cs` | How long the popup stays |

### Finishing Blow

`FinishingBlow` hooks `HealthManager.TakeDamage`. If the charm is equipped, its tier is above 0, the hit is `AttackTypes.Nail` and the enemy is at or below its tier's `LowHealthByTier` fraction of its tracked `MaxHp`, it raises `hitInstance.Multiplier`. The game applies that multiplier when it works out damage, so this stacks with other charms. The hook always calls the original method, or the hit would be lost. The tier is `ChampionsShellCharm.Tier`, saved as `LocalSettings.UpgradeTier`.

### The Nailsmith upgrade

`NailsmithUpgrade` hooks `PlayMakerFSM.OnEnable` and edits the Nailsmith's `Conversation Control` FSM. After his greeting, a new state checks `CanUpgrade()`: the save owns the charm, the tier is below 3, and it has the relic and the geo. If not, the game carries on as normal.

If it can, he makes the offer and the talk runs through the game's own yes/no states, with the `buying` flag sending them to the charm's steps instead of the nail's. The yes/no box takes the geo itself. On Yes, `Upgrade()` spends the relic, raises the tier and saves. On No, the talk carries on to his nail offer, so the charm never blocks nail upgrades.

The game's `Idle` state makes him leave when `PlayerData.nailsmithCliff` is set. A new state runs first: if an upgrade is affordable, his object stays with its sprite, hammer sound, fire and dream dialogue off and an Inspect prompt, and using it opens the same offer as the anvil, without his greetings. Once nothing is affordable, he leaves as normal.

### Finding the charm

`WorldPickup` watches Unity's `activeSceneChanged`. When the player enters `TargetScene` (`Town`, which is Dirtmouth) and the save doesn't own the charm, it adds a floating object using the charm's icon at `TargetPosition`. Unity removes that object when the player leaves the room, and it's only added back while the charm is unowned.

`CharmPickup` is the behaviour on that object. It bobs the icon and checks the Knight's distance every frame. It ignores room transitions, when the Knight's position is still from the old room. On touch, `WorldPickup.Collect` sets `hasCharm` (which unlocks the Charms tab), gives the charm and shows `PickupPopup`, a short icon and name that fades out.

**Watch out:** the game has its own global class named `SceneManager`, which hides Unity's, so Unity's is written out in full. Code in `activeSceneChanged` must never throw, for the same reason as `OnLoadLocal`.

### Building

From `ChampionsShell/`, run `dotnet build`. The project reads game files from a separate 1.5.78 copy of the game (`~/HKModding/HollowKnight-1.5.78/` by default), and that copy needs the Modding API and SFCore installed. After each build, the DLL is copied into the copy's `Managed/Mods/ChampionsShell/` folder. To use a different location, add `-p:HollowKnightCopy=/path/to/copy`.
