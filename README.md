# TOR Chance Modifier

A modifier plugin for [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) that adds the **Chance** modifier: affected players get randomized speed, kill cooldown, vision, task count, and a per-kill success chance. Everything about them is random.

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

## Features

- **Chance modifier** with configurable assignment rate and quantity
- Per-player randomization of:
  - Movement speed (configurable min/max range)
  - Kill cooldown (configurable min/max range)
  - Vision radius (configurable min/max range)
  - Task count (configurable min/max range)
  - Extra votes (0–3 additional votes added on top of the normal vote; configurable min/max range)
  - Kill distance (configurable min/max range)
  - Sabotage cooldown (impostors only; configurable min/max range)
- **Kill Success Chance**: when a Chance player kills, the kill only goes through with the configured probability
- **Vent Access Chance**: a Chance player has the configured probability of being able to use vents
- **Activation delay**: immediate, or after N meetings / N seconds
- Re-randomizes speed/cooldown/vision after every meeting; task counts stay fixed
- Local player sees their current stats under their role description
- **Chaos Mode**: optionally reroll roles for players each meeting (configurable role pool and scope)
- **Host-authoritative RPCs**: the gameplay RPCs that re-roll stats, reassign roles, and activate the modifier are accepted only from the lobby host; non-host senders are ignored and logged. A lobby mod-check flags players missing the mod or on a different build.

## Download & Install

1. Install The Other Roles into your Among Us BepInEx setup.
2. Download the latest `TOR-ChanceModifier.dll` from the [Releases page](https://github.com/DaUnknown-0/TOR-Chance/releases/latest).
3. Copy `TOR-ChanceModifier.dll` into `<Among Us>/BepInEx/plugins/`.
4. Start the game. The host can enable the modifier under the **Modifier** settings tab (look for `Chance`).

After the first install, the in-game auto-updater checks this repo's GitHub releases on the main menu and offers an update button — manual downloads are only needed for the initial setup.

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.
