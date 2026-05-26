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
  - Vote multiplier (0–3 votes; configurable min/max range)
  - Kill distance (configurable min/max range)
  - Sabotage cooldown (impostors only; configurable min/max range)
- **Kill Success Chance**: when a Chance player kills, the kill only goes through with the configured probability
- **Vent Access Chance**: a Chance player has the configured probability of being able to use vents
- **Activation delay**: immediate, or after N meetings / N seconds
- Re-randomizes speed/cooldown/vision after every meeting; task counts stay fixed
- Local player sees their current stats under their role description

### Test mode

Verify the configured stats without a full lobby:

- **F4** — toggle a read-only preview panel that rolls a sample stat set from the current option
  ranges (`R` rerolls). In a running game as host the panel also offers *Apply to me (live)*, which
  force-assigns the Chance modifier to your own player so you can feel the effects.

To test with dummies, use the companion **TOR Debug Unlock** plugin. Dummies only work **offline** —
on an online server the round freezes after the countdown because the server waits for the (non
existent) dummy clients. Recommended workflow:

1. Enable the Chance modifier (rate > 0) once in any lobby (the setting persists).
2. Open **Freeplay** (main menu → *How To Play*). There is no server and no start handshake, so
   nothing freezes. (A round on a server cannot use these dummies — the server waits for the non
   existent dummy clients and the round freezes after the countdown.)
3. Press `F` to spawn dummies (they appear as killable crewmates).
4. Press `I` to toggle yourself to Impostor → the kill button appears, so you can kill the dummies
   and test kill distance / kill cooldown.
5. Open the F4 panel and click *Apply to me (live)* to feel the Chance stats (speed, vision, vent, …).

> Note: *Kill distance* and *sabotage cooldown* are applied client-side per player; sabotage cooldown
> only affects impostors (vanilla has no per-player sabotage cooldown, so it is a local approximation).

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
