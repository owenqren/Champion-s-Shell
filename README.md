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
