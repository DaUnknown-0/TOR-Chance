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

## Host authority & multiplayer (since 1.2.0)

The gameplay RPCs that re-roll stats / reassign roles / activate the modifier
(**200 SetValues**, **201 ChaosReassign**, **250 Activation**) are **host-authoritative**: clients
accept them only when the sender is the lobby host. RPCs from a non-host sender are ignored and
logged. The version-handshake RPC (**251**) stays open to all clients.

Because of this behaviour change, **all players should run the same version**. The lobby mod-check
(shown to the host) flags players who are missing the mod or on a different/modified build — a
mixed 1.2.0 ↔ ≤1.0.15 lobby is reported there.

If **Useful TOR Stuff** is also installed, the two mods' lobby version warnings are merged into one
combined per-player **Mod-Check** overview (rendered by Useful TOR Stuff); Chance shows its own
standalone list only when Useful TOR Stuff is absent. This is a presentation merge only — the RPC
wire format (251) is unchanged.

## Options (Modifier tab)

| Option | Default | Notes |
|---|---|---|
| Chance | off | Assignment rate (per 10% slot) |
| Chance Quantity | 1 | How many players can roll the modifier |
| Min / Max Speed | 0.5× / 2.5× | |
| Min / Max Kill Cooldown | 5 s / 60 s | |
| Min / Max Tasks | 1 / 10 | Only with *Immediate* activation |
| Kill Success Chance % | 30 | Probability a Chance player's kill lands |
| Auto-Report Chance % | 10 | Per second near a body |
| Min / Max Vision | 0.25× / 5× | |
| Vent Access Chance % | 0 | Chance to gain vent use |
| Min / Max Vote Multiplier | 1 / 1 | 0 removes the vote; ≥2 adds extra votes |
| Min / Max Kill Distance | 1 / 1.75 | |
| Min / Max Sabotage Cooldown | 5 s / 30 s | Impostors only |
| Activation Delay Mode | Delayed | Immediate or Delayed |
| Activation Delay Unit | Meetings | Meetings or Seconds |
| Activate After Meetings / Seconds | 1 / 30 | |
| Chaos Mode | Off | Reroll roles each meeting |
| Chaos: Role Pool | All enabled roles | Or "only roles already in play" |
| Chaos: Affected Players | All players | Or "only Chance players" |

### Configuration (BepInEx config)

`com.tormod.chancemodifier.cfg`:
- `[General] Enabled` — load the mod (default `true`).
- `[Debug] VoteLogging` — per-voter vote-multiplier diagnostics in the log (default `false`,
  toggleable without a rebuild).

## Compatibility

| Chance Modifier | The Other Roles | Among Us |
|---|---|---|
| 1.2.0 | 4.8.0 | Steam build matching TOR 4.8.0 |

## Requirements

- [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) (hard dependency)
- BepInEx IL2CPP 6.0.0-be.697
- Among Us (Steam build matching the TOR version you're using)

## Building

The project references `TheOtherRoles.dll` at:

```
..\TheOtherRoles-main\TheOtherRoles-main\TheOtherRoles\bin\Debug\net6.0\TheOtherRoles.dll
```

Build TOR locally first (or adjust the `HintPath` in `ChanceMod.csproj`) so this DLL exists, then:

```
dotnet build -c Release
```

The output `TOR-ChanceModifier.dll` lands in `bin/Release/net6.0/`.

To auto-copy to your Among Us install, set the `AmongUsLatest` environment variable to your Among Us folder (the one containing `Among Us.exe` and `BepInEx/`).

## Installation

1. Install The Other Roles into your Among Us BepInEx setup.
2. Copy `TOR-ChanceModifier.dll` into `<Among Us>/BepInEx/plugins/`.
3. Start the game. The host can enable the modifier under the **Modifier** settings tab (look for `Chance`).

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.
