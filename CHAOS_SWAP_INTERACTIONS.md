# Chaos-Swap: Gefährdete Rollen-Interaktionen

Der Chaos-Mode swappt nach jedem Meeting (Hook: `ExileController.WrapUp` /
`AirshipExileController.WrapUpAndSpawn`) die Rollen lebender Spieler per
`RPCProcedure.erasePlayerRoles(id)` + `RPCProcedure.setRole(roleId, id)`.
Erhalten bleiben: Vanilla-Team (Imp/Crew), alle Modifier (Lovers etc., `ignoreModifier=true`)
und die Chance-Stats (hängen an der PlayerId, nicht an der Rolle).

Diese Doku listet, was dabei kaputtgehen kann. Stand: TOR-Source im Repo, ChanceMod aktuell.

---

> **Status:** A2–A6 sind in `ChaosMode.cs` behoben (host-seitig, host-verifizierbar).
> A1 bleibt eine bewusste „alle brauchen den Mod"-Grenze (siehe Begründung dort).
> A7 sowie die Abschnitte B–D sind inhärente Swap-Folgen bzw. liegen im TOR-Original
> und werden nicht angefasst.

## A. Echte Bugs / Desyncs

### A1. Misch-Lobby (Client hat TOR, aber nicht ChanceMod) — NICHT behebbar (akzeptiert)
`SendChaosReassign` schickt zwei RPCs: den eigenen Chaos-RPC 201 (erase + set + History)
und TORs natives `SetRole` (104, damit Ghosts überall die neue Rolle sehen).
Clients **ohne** ChanceMod verarbeiten nur 104:
- Die **alte Rolle wird dort nie erased** → der Spieler hält aus Sicht dieses Clients alte
  und neue Rolle gleichzeitig (Rollenanzeige, lokal laufende Rollenlogik wie Medic-Shield-Anzeige).
- Bei Zuweisung `NoneRoleId` (vanilla) wird **gar nichts** gesendet → der Client sieht dauerhaft
  die alte Rolle.
Der Version-Handshake (RPC 251) **warnt nur** in der Lobby, blockt den Start nicht.
→ Chaos-Mode ist faktisch nur sauber, wenn alle den ChanceMod haben.

**Warum nicht über TORs nativen Erase-RPC behebbar:** TORs `CustomRPC.ErasePlayerRoles`
(139) ruft im Handler `Eraser.alreadyErased.Add(target)` (RPC.cs:1518). Würde der Swap ihn
mitsenden (wie das native `SetRole` 104), landete jeder geswappte Spieler in der Erased-Liste
und wäre von allen Folge-Rerolls ausgeschlossen. Daher bleibt der Erase mod-intern (RPC 201);
Nicht-Mod-Clients behalten zwangsläufig die alte Rolle. Konsistent mit dem Snitch-Fix-Gating.

### A2. Scope „nur Chance-Spieler": Rollen-Diebstahl von Nicht-Scope-Haltern — BEHOBEN
Der Pool-Filter schließt eine Rolle nur aus, wenn ihr Halter **tot** ist
(`holder.Data.IsDead`). Ein **lebender** Halter außerhalb des Scopes (kein Chance-Spieler)
bleibt im Pool bzw. in der „in play"-Liste:
- Ein Chance-Spieler kann z. B. Sheriff rollen → `setRole` überschreibt `Sheriff.sheriff`,
  der bisherige (nicht gescopte) Sheriff verliert die Rolle **kommentarlos** — kein Erase,
  keine Meldung, Buttons verschwinden einfach.
Im Scope „alle" ist das sicher: `erasePlayerRoles` prüft identitätsbasiert
(`player == Medic.medic`), der Alt-Halter löscht die schon weitergegebene Rolle also nicht.

**Fix:** Beide Pool-Filter in `RerollTeam` vergeben eine Rolle nur noch, wenn ihr lebender
Halter selbst Reroll-Teilnehmer ist (`players.Any(... == holder.PlayerId)`). Im Scope „alle"
unverändert (jeder lebende Halter ist Teilnehmer).

### A3. Deputy-Promotion feuert nach Sheriff-Swap — BEHOBEN
`deputyCheckPromotion` (läuft in TORs `FixedUpdate` **und** im selben WrapUp-Postfix)
promotet den Deputy, sobald `Sheriff.sheriff == null` — auch wenn der Ex-Sheriff lebt.
- Scope „alle": unkritisch, der Deputy wird im selben synchronen Durchlauf mit-erased.
- Scope „nur Chance-Spieler" (Sheriff im Scope, Deputy nicht): Sheriff-Swap → `sheriff = null`
  → Deputy promotet sofort zum Sheriff. Vergibt der Reroll gleichzeitig Sheriff neu,
  konkurrieren Promotion und `setRole` um `Sheriff.sheriff`.

**Fix:** Solange ein lebender Deputy existiert, ist der Sheriff-Halter via
`isProtectedFromReroll` vom Reroll ausgenommen; A2 hält Sheriff damit auch aus dem Pool.
Das Sheriff↔Deputy-Paar bleibt als Einheit unangetastet, kein Promotion-Race mehr.

### A4. Mafioso wird durch Godfather-Swap freigeschaltet — BEHOBEN
Die Kill-/Sabotage-Sperre des Mafioso prüft `Godfather.godfather != null && !IsDead`.
Wird der Godfather geswappt (`godfather = null`), während der Mafioso nicht im Scope ist,
ist der Mafioso sofort freigeschaltet, obwohl der Ex-Godfather lebt.

**Fix:** Godfather/Mafioso/Janitor sind via `isProtectedFromReroll` vom Reroll ausgenommen
(siehe A5), der Godfather wird also nie weggeswappt → kein spontanes Mafioso-Unlock.

### A5. Pool-fremde Rollen werden vernichtet, nie neu vergeben — BEHOBEN
Die Spielerlisten schützen nur Neutrale, Spy und Snitch (Crew) — Imps gar nicht.
Halter dieser Rollen werden also erased, die Rollen sind aber **nicht im Pool**:
- **Deputy**, **Godfather/Mafioso/Janitor**, **Shifter**: nach dem ersten Reroll für den Rest
  des Spiels verschwunden.
- **Guesser**: `erasePlayerRoles` ruft immer `Guesser.clear(playerId)` — ein gerollter
  Nice/Evil Guesser verliert die Guess-Fähigkeit dauerhaft.
Die „Stability exclusions" schützen nur die Rollen-Neuvergabe, **nicht die Halter**.

**Fix:** `isProtectedFromReroll` nimmt die Halter dieser Rollen (Godfather, Mafioso, Janitor,
Deputy, Nice/Evil Guesser) zusätzlich aus den Reroll-Teilnehmerlisten heraus — gleiches Muster
wie die bestehende Spy/Snitch-Ausnahme. Sie behalten ihre Rolle, statt sie zu verlieren.

### A6. Hook-Reihenfolge im WrapUp (implizite Annahme) — BEHOBEN
TORs eigener WrapUp-Postfix erledigt am selben Hook: Seer-Seelen spawnen
(aus `Seer.deadBodyPositions`), Medium-Seelen (aus `Medium.futureDeadBodies`),
Deputy-Check, BountyHunter-Reset. Liefe der Chaos-Swap **vor** TORs Postfix, wären diese
Listen durch `clearAndReload` bereits geleert → keine Seelen für den (Ex-)Seer/Medium.
Aktuell läuft ChanceMod nach TOR (Patch-Reihenfolge bei gleicher Priority = Registrierungs-
reihenfolge, BepInEx lädt ChanceMod nach TOR) — das ist aber nirgends garantiert/erzwungen.

**Fix:** Die beiden Chaos-WrapUp-Patches haben jetzt `[HarmonyPriority(Priority.Low)]` und laufen
damit deterministisch **nach** TORs Normal-Priority-Postfix — die Seelen-/Deputy-Listen sind
beim Reroll bereits konsumiert.

### A7. Tracker: `localArrows` wird zerstört, aber nicht geleert (TOR-Bug, durch Swap aktiviert) — NICHT angefasst (TOR-Original)
`Tracker.clearAndReload` zerstört die Arrow-GameObjects in `localArrows`, **leert die Liste
aber nicht**. Normal egal (läuft nur beim Spielstart), beim Mid-Game-Swap iteriert das
Corpse-Tracking danach über zerstörte Unity-Objekte → Risiko unsichtbarer Pfeile/Exceptions.

---

## B. Stiller Zustandsverlust beim Swap des Halters

Alles per `clearAndReload` weg, ohne Hinweis für Betroffene:

| Rolle | Verlorener Zustand |
|---|---|
| Medic | Aktives Schild (`shielded = null`) verschwindet sofort; neuer Medic darf erneut schilden (`usedShield = false`) |
| Warlock | Aktiver Fluch (`curseVictim`) |
| Ninja | Markierung (`ninjaMarked`) |
| Yoyo | `markedLocation` — auch mit Option „mark stays over meeting". Silhouetten werden bei dieser Option **nicht** gecleart → verwaiste Silhouette ohne Yoyo |
| BountyHunter | Bounty + Pfeil (neuer BH rollt sofort neu — verschmerzbar) |
| Tracker | Ziel + Pfeil (plus A7) |
| Seer / Medium | Gesammelte Leichen-Positionen → neuer Halter kann frühere Tode nicht abfragen |
| Trickster | Laufendes Lights-Out endet sofort (`lightsOutTimer = 0`) |
| Trapper | `playersOnMap` (gesammelte Reveal-Infos) |

---

## C. Verwaiste / vererbte Weltobjekte

Statische Objekt-Listen hängen **nicht** an der Rolle und überleben den Swap:

- **Trapper-Traps** (`Trap.traps`): bleiben aktiv und melden künftig an den **neuen** Trapper
  (Anzeige läuft über `Trapper.trapper`). Charges des neuen Trappers starten frisch.
- **Trickster-Boxen** (`JackInTheBox.AllJackInTheBoxes`): bleiben; der neue Trickster sieht
  die geerbten Boxen, sie zählen ins Limit, und bei Erreichen konvertieren **alle** (auch
  geerbte) zu Vents. Ohne neuen Trickster bleiben unsichtbare Boxen einfach liegen.
- **SecurityGuard**: versiegelte Vents und platzierte Kameras sind Map-Änderungen und bleiben
  dauerhaft; gleichzeitig resettet `clearAndReload` Schrauben/`placedCameras` auf 0 → neuer
  Guard hat volles Budget, obwohl die Map schon modifiziert ist.

---

## D. Ressourcen-Reset = Balance-Drift über viele Meetings

Jede Neuzuweisung füllt verbrauchbare Limits wieder auf — über ein langes Spiel ergibt das
faktisch unbegrenzte Nutzungen rotierender Rollen:

- Mayor: `remoteMeetingsLeft`, `voteTwice`
- Swapper: `charges`
- Engineer: `remainingFixes`
- Hacker / SecurityGuard / Trapper: Charges bzw. Schrauben
- Medic: `usedShield`

---

## E. Bereits behandelt / geprüft unkritisch

- **Eraser-Historie**: wird vor dem Reroll gesnapshottet und danach restauriert (ChaosMode.RerollRoles).
- **Spy/Snitch-Halter**: explizit von der Crew-Spielerliste ausgenommen.
- **Neutrale + Jackal/Sidekick**: via `isNeutral` ausgeschlossen; zusätzlich macht
  `canBeErased` Erase auf Jackal/Sidekick/Ex-Jackals zum No-Op.
- **Witch**: `futureSpelled` wird in `ExileController.BeginForGameplay` (vor WrapUp) aufgelöst
  und geleert → der Swap trifft keine offenen Spells.
- **Vampire**: Bisse werden beim Meeting-Start aufgelöst; zum Swap-Zeitpunkt ist `bitten` leer.
  Knoblauch bleibt funktionslos-harmlos liegen.
- **Bomber**: TOR cleart Bomben ohnehin beim Meeting-Start; zum WrapUp existiert keine.
- **Morphling/Camouflager**: `resetMorph`/`resetCamouflage` setzen die Optik korrekt zurück.
- **Lovers & Modifier**: bleiben erhalten (`ignoreModifier = true`).
- **Chance-Stats**: hängen an der PlayerId und überleben den Rollenwechsel (gewollt) —
  z. B. gilt der Kill-CD-Clamp danach auch für einen frisch gerollten Sheriff.
