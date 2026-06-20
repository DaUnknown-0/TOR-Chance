# In-game verification checklist — Chance Modifier 1.2.0

These changes build cleanly but could not be verified statically; confirm them in a live lobby.

## P0.3 — Host validation on gameplay RPCs (needs a second client)
- [ ] Host + ≥1 client with the mod. Normal game: stats/Chaos/activation still apply on all clients
      (host-sent RPCs 200/201/250 accepted).
- [ ] From a **non-host** client, send/replay an RPC 200/201/250 (or run a hacked client). Confirm
      it is **rejected** on receivers with a log line `Rejected host-only RPC … from non-host
      sender …` and no stats/role change occurs.
- [ ] RPC 251 handshake still works from every client (lobby mod-check list populates for all).

## P0.5 — Vote logging config
- [ ] Default install: no per-voter vote spam in the log during meetings.
- [ ] Set `[Debug] VoteLogging = true` in the config; confirm the diagnostics return without a rebuild.

## P1.4 — Mayor + multiplier ×1 (needs Mayor + Chance on same player)
- [ ] A Mayor with double-vote who rolls Chance vote multiplier **×1**: the displayed icon count
      and the resolved tally now agree (both **2**).
- [ ] Spot-check ×0 (no icon, vote removed), ×2, ×3 still match between icons and tally.

## P1.6 — Re-randomisation vs. exile ordering (needs a meeting)
- [ ] Vote someone out: the exiled player is dead before stats re-roll; no RPC is sent re-rolling
      the just-exiled player.
- [ ] **Tie / skipped vote**: confirm `ExileController.WrapUp` (or Airship `WrapUpAndSpawn`) still
      fires so re-randomisation happens (verify against TOR 4.8.0 exile flow — ties route through the
      exile controller with no victim).
- [ ] Airship map: confirm `AirshipExileController.WrapUpAndSpawn` path triggers re-randomisation.

## P1.5 — Handshake cache reset
- [ ] Leave a lobby and join another; the host mod-check list shows only current-lobby players.

## F1 — Combined lobby Mod-Check (needs TOR - Forgotten Fixes installed)
- [ ] With both mods installed: Chance does NOT draw its own version-warning list; the combined
      "Mod-Check:" block (rendered by TOR - Forgotten Fixes) shows the Chance column.
- [ ] With TOR - Forgotten Fixes NOT installed: Chance shows its own standalone mismatch list as before.

## P1.3 — Kill cooldown HUD
- [ ] Local Chance impostor/role with kill button: button cooldown reflects only the local player,
      never a remote Chance player's timer; no NRE during HUD spin-up.
