# Changelog — TOR - Unknown Chaos (Chance Modifier)

## Unreleased

### Performance-Durchgang über die Per-Frame-Pfade (Audit 2026-09-01)
Kein Verhaltenswechsel; nur Arbeit gestrichen, die jeden Frame für ein unverändertes Ergebnis
wiederholt wurde.
- **Rollenanzeige** (`ChanceRoleInfoPatch`): pro Namensschild-Aufbau (jeden Frame für den eigenen
  Spieler, als Geist für alle) wurden ein neues `RoleInfo`, eine `List<string>` und bis zu neun
  formatierte Strings gebaut. Jetzt ein `RoleInfo` pro Chance-Spieler, neu nur wenn
  `Chance.statsVersion` sich bewegt (Roll, Aktivierung, Reset, Range-Reload, Spieler weg) oder der
  Impostor-Status des lokalen Spielers kippt.
- **Sabotage-Cooldown** (`ChanceSabotageCooldownPatch`): das Sabotage-System wird einmal pro
  `ShipStatus` aufgelöst statt per `TryCast`-Wrapper-Allokation jeden Frame.
- **Lobby-Handshake** (`ChanceVersionHandshake.cs`): der AppDomain-Snapshot wird nur noch
  veröffentlicht, wenn sich die Versionstabelle geändert hat (bisher jeden Lobby-Frame).
- **Collective-HUD-Zeile** (`UnknownsCollective.cs`, in allen fünf Mods gleich): Block gecacht.
- Null-Guard auf `AmongUsClient.Instance` im Aktivierungs-Tick.

### Chaos-Swap — Rollen-Interaktionen
Audit der Rollen, die der Chaos-Reroll (`erasePlayerRoles` + `setRole` im Exile-WrapUp)
beschädigen konnte. Details in `CHAOS_SWAP_INTERACTIONS.md`. Alle Fixes host-seitig in
`ChaosMode.cs`; TOR-Original unverändert.
- **A5/A4** — `isProtectedFromReroll`: Halter von Rollen, die nicht in den Chaos-Pools liegen
  (Godfather/Mafioso/Janitor, Deputy, Nice/Evil Guesser), werden nicht mehr in den Reroll
  gezogen. Verhindert (a) dass diese Rollen nach dem ersten Swap dauerhaft verschwinden und
  (b) das stille Mafioso-Kill-Unlock beim Wegswappen des Godfather.
- **A2** — Beide Pool-Filter in `RerollTeam` vergeben eine Rolle nur noch, wenn ihr lebender
  Halter selbst Reroll-Teilnehmer ist. Im Scope „nur Chance-Spieler" wurde sonst die Rolle
  eines lebenden Nicht-Teilnehmers per `setRole` überschrieben (ohne Erase, ohne Hinweis).
  Scope „alle" unverändert.
- **A3** — Solange ein lebender Deputy existiert, ist auch der Sheriff-Halter geschützt
  (über A2 damit ebenfalls aus dem Pool). Das Sheriff↔Deputy-Paar bleibt als Einheit
  unangetastet; kein Race zwischen Deputy-Promotion und Reroll mehr.
- **A6** — `[HarmonyPriority(Priority.Low)]` auf beiden Chaos-WrapUp-Patches: der Reroll läuft
  jetzt deterministisch nach TORs WrapUp-Postfix, sodass Seer-/Medium-Seelenlisten und der
  Deputy-Check vor `clearAndReload` konsumiert werden.

A1 (Misch-Lobby: Clients ohne den Mod behalten die alte Rolle) bleibt eine bewusste
„alle brauchen den Mod"-Grenze — TORs nativer Erase-RPC würde `Eraser.alreadyErased`
vergiften und ist daher kein gangbarer Weg; der Version-Handshake warnt bereits.

## 1.2.0

Minor bump: **P0.3** adds host-authoritative RPC validation, which changes cross-client
behaviour. Mixed lobbies (1.2.0 ↔ ≤1.0.15) are correctly flagged by the version handshake.

### Features
- **F1 — Consolidated lobby version handshake (presentation only).** Chance now publishes its
  handshake snapshot over the documented `TORMods.Handshake.*` AppDomain keys so UsefulTORStuff can
  render one combined per-player **Mod-Check** overview. When UsefulTORStuff is loaded, Chance
  suppresses its own standalone version-warning block (Useful owns the combined block); when Useful
  is absent, Chance renders its standalone block exactly as before. Wire format (RPC 251) unchanged.
- **F2 support** — added `GetReleaseNotes()` to the updater so the Mod Manager can show this mod's
  release notes; the existing `TriggerUpdateFromManager`/`GetUpdateState` hooks already support
  "Update All". `User-Agent` header added to the GitHub request (also P2.8).

### P0 — Crash / correctness
- **P0.1** — `CoShowAnnouncement`: replaced the fall-through `yield return null;` (which then
  called `Instantiate(null)` and threw) with `yield break;`, and guarded the `MainMenuManager`
  against null before `StartCoroutine(...)` in both the announcement coroutine and `OnSceneLoaded`.
- **P0.2** — `CoCheckForUpdate`: wrapped GitHub release deserialize + sort in try/catch/finally so
  a rate-limit/object/malformed-JSON response can no longer kill the coroutine and wedge `_busy`
  (and `_checkCompleted`) for the whole session. `Releases == null` is treated as "no update"
  everywhere. Every exit path now resets `_busy`/`_checkCompleted`.
- **P0.3** — `ChanceHandleRpcPatch.Prefix`: RPCs **200 (SetValues)**, **201 (ChaosReassign)** and
  **250 (Activation)** are now accepted only when the sender is the host (`OwnerId == HostId`).
  Non-host senders are logged and the RPC is consumed (`return false`). RPC **251** (version
  handshake) stays open to all clients by design.
- **P0.5** — Replaced the always-on `DebugVotes = true` field with a config-backed property reading
  `Config.Bind("Debug", "VoteLogging", false, …)`. Vote diagnostics default off, toggleable
  without a rebuild.

### P1 — Functional
- **P1.3** — `ChanceKillCooldownPatch`: the local HUD `KillButton.SetCoolDown(...)` is now gated on
  `__instance.AmOwner && HudManager.Instance != null`, so a non-local Chance player's
  `SetKillTimer` no longer drives (or NREs) the local kill button. The `killTimer` clamp still
  applies to every instance.
- **P1.4** — Vote-icon/tally consistency: for a Mayor with double-vote rolling multiplier ×1, the
  count patch now counts **2** (the multiplier never reduces below the Mayor baseline), matching
  the 2 icons the display renders. (Option (a).)
- **P1.5** — `playerVersions` is cleared on `AmongUsClient.OnGameJoined`, so the handshake cache
  reflects only the current lobby.
- **P1.6** — Re-randomisation moved off `MeetingHud.Close` onto `ExileController.WrapUp` /
  `AirshipExileController.WrapUpAndSpawn` (the same hooks Chaos Mode uses), so the exiled player is
  already dead when stats are re-rolled — no wasted RPC and meeting counting matches Chaos.

### P2 — QoL / performance
- **P2.1** — Added a `HashSet<byte> chanceIds` mirroring `chanceList`, maintained in
  `applyValues`/`CleanChancePlayers`/`clearAndReload`. `IsChancePlayer` is now O(1) instead of an
  O(n) `chanceList.Any(...)` on hot per-frame paths.
- **P2.2** — `ChanceVentButtonFrontPatch` reuses a static `List<Vector3>` (cleared per frame)
  instead of allocating one every frame.
- **P2.3** — PingTracker version line guarded by a `chanceCredits` marker check, preventing
  per-frame stacking if TOR ever stops rebuilding the text.
- **P2.8** — Updater sends a `User-Agent` header on the GitHub API request.
- **P2.10** — Already satisfied: `GetChanceShortDescription` already guards `Data?.Role?` null-safely.
