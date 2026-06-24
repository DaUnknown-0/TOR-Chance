# TOR Chance Modifier

A modifier plugin for [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) that adds the **Chance** modifier: affected players get randomized speed, kill cooldown, vision, task count, vote weight, kill distance and sabotage cooldown — plus per-kill, auto-report and vent-access probabilities. Everything about them is random. It also ships an independent **Chaos Mode** that re-rolls roles after every meeting.

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

## Features

- **Chance modifier** with configurable assignment chance and quantity.
- **Per-effect toggles** — every randomization has its own enable switch. **Default: off**, so an effect stays vanilla until you enable it.
- Per-player randomization of:
  - **Movement speed** (configurable min/max range)
  - **Kill cooldown** (configurable min/max range)
  - **Vision radius** (configurable min/max range)
  - **Task count** (configurable min/max range; immediate activation only)
  - **Vote multiplier** (configurable min/max range, e.g. ×0–×3 vote weight at meetings)
  - **Kill distance** (configurable min/max range)
  - **Sabotage cooldown** (impostors only; configurable min/max range)
- **Kill Success Chance** — when a Chance player kills, the kill only goes through with the configured probability (otherwise it is a BlankKill).
- **Auto-Report Chance** — each second, a configurable probability to auto-report the nearest body.
- **Vent Access Chance** — a Chance player has the configured probability of being able to use vents, independent of role.
- **Activation delay** — immediate, or after N meetings / N seconds. (Task reduction requires immediate activation, since tasks are assigned at game start.)
- Re-randomizes speed / cooldown / vision after every meeting (on the exile wrap-up); task counts stay fixed for the whole game.
- The local Chance player sees their own randomized stats under their role description; other players just see "You are CHAOS!".
- **Chaos Mode** — optionally re-roll roles for living players after every meeting (configurable role pool and scope), with protected roles and a full role-history line on the end screen.
- **Host-authoritative RPCs** — the gameplay RPCs that re-roll stats, reassign roles and activate the modifier (200 / 201 / 250) are accepted only from the lobby host; non-host senders are ignored and logged. The version handshake (251) stays open to all clients. A lobby mod-check flags players missing the mod or on a different build.

## Chaos Mode

Configured under the **Modifier** tab, independent of the Chance modifier.

| Option | Values | What it does |
|---|---|---|
| Chaos Mode | Off / On | Re-rolls the roles of all living players after every meeting |
| Chaos: Role Pool | All enabled roles / Only roles already in play | "All": new roles can appear. "In play": only roles already present are re-distributed |
| Chaos: Affected Players | All players / Only Chance players | Re-roll everyone, or only carriers of the Chance modifier |

**Always protected** (never re-rolled): Godfather, Mafioso, Janitor; Deputy (while a Sheriff is alive); Nice/Evil Guesser; Spy; Snitch. The end-screen role summary shows each player's full role path (`Sheriff → Medic → Mayor`, trimmed from the left if too long).

## Download & Install

1. Install The Other Roles into your Among Us BepInEx setup.
2. Download the latest `TOR-ChanceModifier.dll` from the [Releases page](https://github.com/DaUnknown-0/TOR-Chance/releases/latest).
3. Copy `TOR-ChanceModifier.dll` into `<Among Us>/BepInEx/plugins/`.
4. Start the game. The host can enable the modifier under the **Modifier** settings tab (look for `Chance`), and Chaos Mode in the same tab.

After the first install, the in-game auto-updater checks this repo's GitHub releases on the main menu and offers an update button — manual downloads are only needed for the initial setup.

## Requirements

- [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) **4.8.0** (hard dependency)

When [Useful TOR Stuff](https://github.com/DaUnknown-0/Useful-TOR-stuff) is also installed, Chance suppresses its own standalone lobby version warning and lets Useful render a single combined per-player **Mod-Check** overview instead.

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.
