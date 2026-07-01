// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TheOtherRoles;
using static TheOtherRoles.TheOtherRoles;

namespace TOR_ChanceModifier {

    // Chaos Mode: after every meeting, re-roll the role of each living Crew/Impostor player
    // within their own team, weighted by the existing TOR spawn chances.
    public static class ChaosMode {
        private const byte NoneRoleId = 255; // marker: clear role, assign no new TOR role (vanilla)

        // TOR's CustomRPC.SetRole value. The enum is internal to TOR so it can't be referenced by
        // name; the value is stable. Used to push the new role through TOR's own RPC handler so it
        // reaches EVERY TOR client (the path the Shifter uses), not just clients with this mod.
        private const byte TorSetRoleRpcId = 104;

        private static bool processedThisMeeting;

        // Per-player role progression for the end-of-game summary (Bug 4).
        // Built inside ApplyChaosReassign, which runs on every client via the chaos RPC.
        private static bool historyInitialized;
        private static readonly Dictionary<byte, List<string>> roleHistory = new Dictionary<byte, List<string>>();

        private sealed class ChaosRole {
            public readonly RoleId Id;
            public readonly Func<CustomOption> Rate;
            public readonly Func<PlayerControl> Holder;
            public ChaosRole(RoleId id, Func<CustomOption> rate, Func<PlayerControl> holder) {
                Id = id; Rate = rate; Holder = holder;
            }
        }

        // Stability exclusions are simply absent from these lists:
        // Godfather/Mafioso/Janitor (Mafia trio), Deputy, Evil/Nice Guesser, Spy, Snitch.
        private static List<ChaosRole> ImpostorRoles() => new List<ChaosRole> {
            new ChaosRole(RoleId.Morphling,    () => CustomOptionHolder.morphlingSpawnRate,    () => Morphling.morphling),
            new ChaosRole(RoleId.Camouflager,  () => CustomOptionHolder.camouflagerSpawnRate,  () => Camouflager.camouflager),
            new ChaosRole(RoleId.Vampire,      () => CustomOptionHolder.vampireSpawnRate,      () => Vampire.vampire),
            new ChaosRole(RoleId.Eraser,       () => CustomOptionHolder.eraserSpawnRate,       () => Eraser.eraser),
            new ChaosRole(RoleId.Trickster,    () => CustomOptionHolder.tricksterSpawnRate,    () => Trickster.trickster),
            new ChaosRole(RoleId.Cleaner,      () => CustomOptionHolder.cleanerSpawnRate,      () => Cleaner.cleaner),
            new ChaosRole(RoleId.Warlock,      () => CustomOptionHolder.warlockSpawnRate,      () => Warlock.warlock),
            new ChaosRole(RoleId.BountyHunter, () => CustomOptionHolder.bountyHunterSpawnRate, () => BountyHunter.bountyHunter),
            new ChaosRole(RoleId.Witch,        () => CustomOptionHolder.witchSpawnRate,        () => Witch.witch),
            new ChaosRole(RoleId.Ninja,        () => CustomOptionHolder.ninjaSpawnRate,        () => Ninja.ninja),
            new ChaosRole(RoleId.Bomber,       () => CustomOptionHolder.bomberSpawnRate,       () => Bomber.bomber),
            new ChaosRole(RoleId.Yoyo,         () => CustomOptionHolder.yoyoSpawnRate,         () => Yoyo.yoyo),
        };

        private static List<ChaosRole> CrewRoles() => new List<ChaosRole> {
            new ChaosRole(RoleId.Mayor,         () => CustomOptionHolder.mayorSpawnRate,         () => Mayor.mayor),
            new ChaosRole(RoleId.Portalmaker,   () => CustomOptionHolder.portalmakerSpawnRate,   () => Portalmaker.portalmaker),
            new ChaosRole(RoleId.Engineer,      () => CustomOptionHolder.engineerSpawnRate,      () => Engineer.engineer),
            new ChaosRole(RoleId.Sheriff,       () => CustomOptionHolder.sheriffSpawnRate,       () => Sheriff.sheriff),
            new ChaosRole(RoleId.Lighter,       () => CustomOptionHolder.lighterSpawnRate,       () => Lighter.lighter),
            new ChaosRole(RoleId.Detective,     () => CustomOptionHolder.detectiveSpawnRate,     () => Detective.detective),
            new ChaosRole(RoleId.TimeMaster,    () => CustomOptionHolder.timeMasterSpawnRate,    () => TimeMaster.timeMaster),
            new ChaosRole(RoleId.Medic,         () => CustomOptionHolder.medicSpawnRate,         () => Medic.medic),
            new ChaosRole(RoleId.Swapper,       () => CustomOptionHolder.swapperSpawnRate,       () => Swapper.swapper),
            new ChaosRole(RoleId.Seer,          () => CustomOptionHolder.seerSpawnRate,          () => Seer.seer),
            new ChaosRole(RoleId.Hacker,        () => CustomOptionHolder.hackerSpawnRate,        () => Hacker.hacker),
            new ChaosRole(RoleId.Tracker,       () => CustomOptionHolder.trackerSpawnRate,       () => Tracker.tracker),
            new ChaosRole(RoleId.SecurityGuard, () => CustomOptionHolder.securityGuardSpawnRate, () => SecurityGuard.securityGuard),
            new ChaosRole(RoleId.Medium,        () => CustomOptionHolder.mediumSpawnRate,        () => Medium.medium),
            new ChaosRole(RoleId.Trapper,       () => CustomOptionHolder.trapperSpawnRate,       () => Trapper.trapper),
        };

        public static void Reset() {
            processedThisMeeting = false;
            historyInitialized = false;
            roleHistory.Clear();
        }

        public static void OnMeetingStarted() {
            processedThisMeeting = false;
        }

        public static void OnMeetingEnded() {
            if (!(AmongUsClient.Instance?.AmHost ?? false)) return;
            if (processedThisMeeting) return;
            processedThisMeeting = true;
            RerollRoles();
        }

        private static bool IsEnabled() {
            return ChanceOptions.chaosMode != null && ChanceOptions.chaosMode.getSelection() == 1;
        }

        // Selection 1 == "Only roles already in play": redistribute existing roles instead of
        // re-rolling new ones from the full enabled pool. Read host-side only (RerollRoles is host).
        private static bool OnlyInPlayRoles() {
            return ChanceOptions.chaosRolePool != null && ChanceOptions.chaosRolePool.getSelection() == 1;
        }

        private static bool IsErased(PlayerControl p) {
            return Eraser.alreadyErased != null && Eraser.alreadyErased.Contains(p.PlayerId);
        }

        // Holders that must NEVER be pulled into the reroll. Their roles are deliberately absent
        // from the chaos pools (see ImpostorRoles/CrewRoles), so rerolling such a player would
        // erasePlayerRoles their role with no pool entry able to hand it back — the role would
        // vanish for the rest of the game. Two of them also break a linked-role mechanic if touched:
        //   • Godfather/Mafioso/Janitor: erasing the Godfather instantly unlocks the Mafioso's kill,
        //     because TOR gates that on "Godfather alive" (Helpers/UpdatePatch/UsablesPatch) — the
        //     Mafioso is never promoted, just silently freed.
        //   • Deputy + (conditionally) Sheriff: the Deputy promotes to Sheriff the moment
        //     Sheriff.sheriff is null (deputyCheckPromotion fires in the same WrapUp). Keeping the
        //     Sheriff while a living Deputy can still promote avoids the reroll racing that promotion.
        // Guesser (Nice/Evil) is excluded because erasePlayerRoles always clears Guesser charges,
        // permanently removing the guess ability even though the role isn't in any pool.
        private static bool isProtectedFromReroll(PlayerControl p) {
            if (p == null) return false;
            if (p == Godfather.godfather) return true;
            if (p == Mafioso.mafioso) return true;
            if (p == Janitor.janitor) return true;
            if (p == Deputy.deputy) return true;
            if (Guesser.isGuesser(p.PlayerId)) return true;
            if (p == Sheriff.sheriff && Deputy.deputy != null && Deputy.deputy.Data != null
                && !Deputy.deputy.Data.IsDead) return true;
            return false;
        }

        public static void RerollRoles() {
            if (!(AmongUsClient.Instance?.AmHost ?? false)) return;
            if (!IsEnabled()) return;

            // Reassigning a role runs RPCProcedure.erasePlayerRoles, which calls Eraser.clearAndReload()
            // when the reassigned player IS the eraser — and that wipes Eraser.alreadyErased. Since the
            // eraser (an impostor) gets rerolled like everyone else, the record of who was erased would
            // be lost, so previously erased players would be re-rolled into a new role next meeting.
            // Snapshot the erased set now and restore it after the reroll.
            var erasedSnapshot = Eraser.alreadyErased != null
                ? new List<byte>(Eraser.alreadyErased)
                : new List<byte>();

            var alive = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && p.Data.Role != null)
                .Where(p => !IsErased(p)) // players the Eraser caught keep no role
                .ToList();

            // "Only Chance players" scope: restrict the reroll to players carrying the Chance
            // modifier. With the Chance modifier off/inactive nobody qualifies, so nothing rerolls.
            if (ChanceOptions.chaosScope != null && ChanceOptions.chaosScope.getSelection() == 1)
                alive = alive.Where(p => Chance.IsChancePlayer(p.PlayerId)).ToList();

            var impPlayers = alive.Where(p => p.Data.Role.IsImpostor
                                               && !isProtectedFromReroll(p)).ToList();
            var crewPlayers = alive.Where(p => !p.Data.Role.IsImpostor
                                               && !Helpers.isNeutral(p)
                                               && p != Spy.spy && p != Snitch.snitch
                                               && !isProtectedFromReroll(p)).ToList();

            ChancePlugin.Logger?.LogInfo($"[Chaos] Reroll: {impPlayers.Count} impostors, {crewPlayers.Count} crew " +
                $"(alive={alive.Count}, spy={(Spy.spy != null ? Spy.spy.PlayerId.ToString() : "-")}, snitch={(Snitch.snitch != null ? Snitch.snitch.PlayerId.ToString() : "-")})");

            RerollTeam(impPlayers, ImpostorRoles());
            RerollTeam(crewPlayers, CrewRoles());

            // Restore the erased record in case a reassigned eraser cleared it (see snapshot above),
            // so erased players stay excluded from every future reroll.
            if (Eraser.alreadyErased == null) Eraser.alreadyErased = new List<byte>();
            foreach (var id in erasedSnapshot)
                if (!Eraser.alreadyErased.Contains(id)) Eraser.alreadyErased.Add(id);

            // Optional second step: reroll the players' primary TOR modifiers (see RerollModifiers).
            RerollModifiers();
        }

        private static void RerollTeam(List<PlayerControl> players, List<ChaosRole> rolePool) {
            if (players.Count == 0) return;

            var shuffledPlayers = players.OrderBy(_ => rnd.Next()).ToList();
            var result = new List<KeyValuePair<byte, byte>>();
            int idx = 0;

            if (OnlyInPlayRoles()) {
                // "In play only" mode: redistribute just the roles currently held by a living
                // player (already present this round) among the players. No new roles spawn — the
                // set of roles stays the same, only their holders change (multi-way Shifter swap).
                // Only roles whose holder is itself a reroll participant qualify: redistributing a
                // role held by a living non-participant (possible in the "only Chance players" scope)
                // would reassign it via setRole and silently strip the original, un-rerolled holder.
                var inPlay = rolePool
                    .Where(r => { var h = r.Holder(); return h != null && h.Data != null && !h.Data.IsDead
                                                              && players.Any(p => p.PlayerId == h.PlayerId); })
                    .Select(r => r.Id)
                    .OrderBy(_ => rnd.Next())
                    .ToList();
                foreach (var roleId in inPlay) {
                    if (idx >= shuffledPlayers.Count) break;
                    result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, (byte)roleId));
                    idx++;
                }
            } else {
                // "All roles" mode: weighted re-roll from every enabled role (new roles can appear,
                // others vanish), drawn by spawn chance and kept unique while distinct roles last.
                var available = rolePool.Where(r => {
                    var opt = r.Rate();
                    if (opt == null || opt.getSelection() <= 0) return false;
                    var holder = r.Holder();
                    if (holder != null && holder.Data != null && holder.Data.IsDead) return false; // locked to dead holder
                    // Held by a living non-participant (e.g. a non-Chance player in the "only Chance
                    // players" scope): assigning it elsewhere would setRole over them and silently
                    // strip the role without an erase. Leave it where it is.
                    if (holder != null && holder.Data != null && !holder.Data.IsDead
                        && !players.Any(p => p.PlayerId == holder.PlayerId)) return false;
                    return true;
                }).ToList();

                var ensured = available.Where(r => r.Rate().getSelection() >= 10).Select(r => r.Id)
                                       .OrderBy(_ => rnd.Next()).ToList();
                var tickets = new List<RoleId>();
                foreach (var r in available) {
                    int sel = r.Rate().getSelection();
                    if (sel >= 1 && sel < 10) for (int i = 0; i < sel; i++) tickets.Add(r.Id);
                }

                var assignedRoles = new HashSet<RoleId>();

                // Guaranteed (100%) roles first.
                foreach (var roleId in ensured) {
                    if (idx >= shuffledPlayers.Count) break;
                    if (!assignedRoles.Add(roleId)) continue;
                    result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, (byte)roleId));
                    idx++;
                }

                // Weighted ticket pool for 1-9 chance roles, kept unique while distinct roles last.
                while (idx < shuffledPlayers.Count && tickets.Count > 0) {
                    RoleId roleId = tickets[rnd.Next(tickets.Count)];
                    tickets.RemoveAll(x => x == roleId); // enforce uniqueness
                    if (!assignedRoles.Add(roleId)) continue;
                    result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, (byte)roleId));
                    idx++;
                }
            }

            // TOR roles are singletons (one static holder field each), so a role can only be held
            // by ONE player. Any remaining players stay vanilla — duplicating a role would just
            // blank out whoever was assigned it first.
            for (; idx < shuffledPlayers.Count; idx++) {
                result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, NoneRoleId));
            }

            foreach (var entry in result) {
                // Skip reassigning a player to the role they already hold. The reroll otherwise runs
                // erasePlayerRoles → <Role>.clearAndReload → setRole, which silently wipes that role's
                // live state — e.g. the Medic shield (Medic.shielded), even though the player stays
                // Medic. Leaving it untouched preserves the shield (and is cross-client correct: no
                // RPC is sent for this player, so no client wipes anything). NoneRoleId stays as-is.
                if (entry.Value != NoneRoleId
                    && rolePool.Any(r => (byte)r.Id == entry.Value
                                         && r.Holder() != null && r.Holder().PlayerId == entry.Key))
                    continue;
                SendChaosReassign(entry.Key, entry.Value);
            }
        }

        private static void SendChaosReassign(byte playerId, byte roleId) {
            ChancePlugin.Logger?.LogInfo($"[Chaos] Assign: player {playerId} -> role {(roleId == NoneRoleId ? "none/vanilla" : ((RoleId)roleId).ToString())}");

            // Custom RPC (Chance-mod clients): clears the player's previous role + tracks history.
            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, Chance.ChaosRpcId, Hazel.SendOption.Reliable, -1);
            w.Write(playerId);
            w.Write(roleId);
            AmongUsClient.Instance.FinishRpcImmediately(w);

            // Native TOR SetRole RPC: pushes the new role through TOR's own handler so EVERY TOR
            // client applies it (the path the Shifter uses) — this is what makes ghosts on any
            // client see the swap. Sent after the erase RPC, so the per-client order is erase→set.
            if (roleId != NoneRoleId) {
                MessageWriter sr = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId, TorSetRoleRpcId, Hazel.SendOption.Reliable, -1);
                sr.Write(roleId);
                sr.Write(playerId);
                AmongUsClient.Instance.FinishRpcImmediately(sr);
            }

            ApplyChaosReassign(playerId, roleId); // host applies locally (erase + set + history)
        }

        public static void ApplyChaosReassign(byte playerId, byte roleId) {
            // The actual role change must always run on every client (this is what ghosts read
            // live for the role label), so it gets its own try-catch and runs FIRST. The
            // end-of-game history is best-effort and must never be able to block the reassignment.
            try {
                RPCProcedure.erasePlayerRoles(playerId); // keeps vanilla team + modifiers (ignoreModifier=true)
                if (roleId != NoneRoleId) RPCProcedure.setRole(roleId, playerId);
                // Scrambled-arpeggio cue only for the player whose role just changed.
                if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == playerId)
                    ChanceAssets.PlayChaos();
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chaos] ApplyChaosReassign failed for player {playerId}, role {roleId}: {e}");
            }

            try {
                EnsureHistoryInitialized();
                RecordCurrentRole(playerId);
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chaos] role-history update failed for player {playerId}: {e}");
            }
        }

        // Snapshot every player's starting role the first time chaos touches anyone this game.
        private static void EnsureHistoryInitialized() {
            if (historyInitialized) return;
            historyInitialized = true;
            foreach (var p in PlayerControl.AllPlayerControls) {
                if (p == null || p.Data == null || p.Data.Role == null) continue;
                roleHistory[p.PlayerId] = new List<string> { RoleInfo.GetRolesString(p, true, false) };
            }
        }

        private static void RecordCurrentRole(byte playerId) {
            var p = Helpers.playerById(playerId);
            if (p == null) return;
            string name = RoleInfo.GetRolesString(p, true, false);
            if (!roleHistory.TryGetValue(playerId, out var list)) {
                list = new List<string>();
                roleHistory[playerId] = list;
            }
            if (list.Count == 0 || list[list.Count - 1] != name) list.Add(name);
        }

        public static List<string> GetHistory(byte playerId) {
            return roleHistory.TryGetValue(playerId, out var list) ? list : null;
        }

        // Read-only Zugriff für den Summary-Trim-Patch (Zeilen am Bildschirmrand kürzen).
        internal static Dictionary<byte, List<string>> AllHistories => roleHistory;

        // ============================================================================================
        // Chaos: Modifier reroll (optional, default off). After the role reroll, redistribute the
        // players' PRIMARY TOR modifier (Tiebreaker/Mini/Bait/Bloody/AntiTeleport/Sunglasses/Vip/
        // Invert/Chameleon/Armored/Shifter). The Chance modifier itself and the Lover pair are left
        // untouched. Spawn chances + per-modifier quantity limits are respected and a player can end
        // up with no modifier at all. Host drives the assignment; the strip is broadcast via a Chance
        // RPC to every mod client, the new modifier through TOR's native SetModifier RPC (so all TOR
        // clients add it). A player carries at most one primary modifier (TOR's own assignment rule).
        // ============================================================================================
        private const byte TorSetModifierRpcId = 105; // CustomRPC.SetModifier (enum is internal to TOR)

        private sealed class ModSpec {
            public readonly RoleId Id;
            public readonly Func<int> Rate;            // spawn rate selection 0..10 (10 == guaranteed)
            public readonly Func<int> Limit;           // global max holders (quantity, or 1 for singletons)
            public readonly Func<IEnumerable<byte>> Holders; // current holder player ids
            public readonly bool CrewOnly;             // not impostor, not neutral
            public readonly bool ExcludeSpy;           // additionally not the Spy
            public ModSpec(RoleId id, Func<int> rate, Func<int> limit, Func<IEnumerable<byte>> holders,
                           bool crewOnly = false, bool excludeSpy = false) {
                Id = id; Rate = rate; Limit = limit; Holders = holders; CrewOnly = crewOnly; ExcludeSpy = excludeSpy;
            }
        }

        private static IEnumerable<byte> single(PlayerControl p) {
            if (p != null) yield return p.PlayerId;
        }
        private static IEnumerable<byte> many(List<PlayerControl> list) {
            if (list == null) yield break;
            foreach (var p in list) if (p != null) yield return p.PlayerId;
        }

        private static List<ModSpec> ModifierSpecs() => new List<ModSpec> {
            new ModSpec(RoleId.Tiebreaker,   () => CustomOptionHolder.modifierTieBreaker.getSelection(),   () => 1, () => single(Tiebreaker.tiebreaker)),
            new ModSpec(RoleId.Mini,         () => CustomOptionHolder.modifierMini.getSelection(),         () => 1, () => single(Mini.mini)),
            new ModSpec(RoleId.Armored,      () => CustomOptionHolder.modifierArmored.getSelection(),      () => 1, () => single(Armored.armored)),
            new ModSpec(RoleId.Shifter,      () => CustomOptionHolder.modifierShifter.getSelection(),      () => 1, () => single(Shifter.shifter), crewOnly: true, excludeSpy: true),
            new ModSpec(RoleId.Bait,         () => CustomOptionHolder.modifierBait.getSelection(),         () => CustomOptionHolder.modifierBaitQuantity.getQuantity(),         () => many(Bait.bait)),
            new ModSpec(RoleId.Bloody,       () => CustomOptionHolder.modifierBloody.getSelection(),       () => CustomOptionHolder.modifierBloodyQuantity.getQuantity(),       () => many(Bloody.bloody)),
            new ModSpec(RoleId.AntiTeleport, () => CustomOptionHolder.modifierAntiTeleport.getSelection(), () => CustomOptionHolder.modifierAntiTeleportQuantity.getQuantity(), () => many(AntiTeleport.antiTeleport)),
            new ModSpec(RoleId.Sunglasses,   () => CustomOptionHolder.modifierSunglasses.getSelection(),   () => CustomOptionHolder.modifierSunglassesQuantity.getQuantity(),   () => many(Sunglasses.sunglasses), crewOnly: true),
            new ModSpec(RoleId.Vip,          () => CustomOptionHolder.modifierVip.getSelection(),          () => CustomOptionHolder.modifierVipQuantity.getQuantity(),          () => many(Vip.vip)),
            new ModSpec(RoleId.Invert,       () => CustomOptionHolder.modifierInvert.getSelection(),       () => CustomOptionHolder.modifierInvertQuantity.getQuantity(),       () => many(Invert.invert)),
            new ModSpec(RoleId.Chameleon,    () => CustomOptionHolder.modifierChameleon.getSelection(),    () => CustomOptionHolder.modifierChameleonQuantity.getQuantity(),    () => many(Chameleon.chameleon)),
        };

        private static bool ModifierRerollEnabled() {
            return ChanceOptions.chaosModifierReroll != null && ChanceOptions.chaosModifierReroll.getSelection() == 1;
        }
        private static bool OnlyChanceModifierScope() {
            return ChanceOptions.chaosModifierScope != null && ChanceOptions.chaosModifierScope.getSelection() == 1;
        }

        private static bool ModifierAllowed(ModSpec spec, PlayerControl p) {
            if (spec.CrewOnly && (p.Data.Role.IsImpostor || Helpers.isNeutral(p))) return false;
            if (spec.ExcludeSpy && p == Spy.spy) return false;
            return true;
        }

        public static void RerollModifiers() {
            if (!(AmongUsClient.Instance?.AmHost ?? false)) return;
            if (!IsEnabled() || !ModifierRerollEnabled()) return;

            var participants = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && p.Data.Role != null)
                .ToList();
            if (OnlyChanceModifierScope())
                participants = participants.Where(p => Chance.IsChancePlayer(p.PlayerId)).ToList();
            if (participants.Count == 0) return;

            var participantIds = new HashSet<byte>(participants.Select(p => p.PlayerId));

            // Strip the participants' current primary modifiers first, so their slots free up before we
            // count remaining capacity and reassign.
            foreach (var p in participants) SendChaosModifierClear(p.PlayerId);

            var specs = ModifierSpecs();

            // Remaining capacity per modifier = global limit minus holders OUTSIDE the participant set
            // (participants were just cleared). Disabled modifiers (rate 0) contribute nothing.
            var avail = new Dictionary<RoleId, int>();
            foreach (var s in specs) {
                if (s.Rate() <= 0) { avail[s.Id] = 0; continue; }
                int outside = s.Holders().Count(id => !participantIds.Contains(id));
                avail[s.Id] = Math.Max(0, s.Limit() - outside);
            }

            // Total modifiers to hand out among the participants, bounded by the global modifier count
            // setting and the participant count (the rest stay modifier-less).
            int countMin = CustomOptionHolder.modifiersCountMin.getSelection();
            int countMax = CustomOptionHolder.modifiersCountMax.getSelection();
            if (countMin > countMax) countMin = countMax;
            int targetCount = Math.Min(rnd.Next(countMin, countMax + 1), participants.Count);

            var shuffled = participants.OrderBy(_ => rnd.Next()).ToList();
            var assignedTo = new HashSet<byte>();
            int assigned = 0;

            // 1) Guaranteed (rate == 10) modifiers first, up to their capacity.
            var ensured = new List<RoleId>();
            foreach (var s in specs)
                if (s.Rate() == 10) for (int i = 0; i < avail[s.Id]; i++) ensured.Add(s.Id);
            ensured = ensured.OrderBy(_ => rnd.Next()).ToList();
            foreach (var id in ensured) {
                if (assigned >= targetCount) break;
                if (avail[id] <= 0) continue;
                var spec = specs.First(x => x.Id == id);
                var target = shuffled.FirstOrDefault(p => !assignedTo.Contains(p.PlayerId) && ModifierAllowed(spec, p));
                if (target == null) continue;
                AssignModifier(spec, target);
                assignedTo.Add(target.PlayerId); avail[id]--; assigned++;
            }

            // 2) Weighted chance modifiers (rate 1..9) for the remaining slots.
            var tickets = new List<RoleId>();
            foreach (var s in specs) {
                int sel = s.Rate();
                if (sel >= 1 && sel < 10 && avail[s.Id] > 0) for (int i = 0; i < sel; i++) tickets.Add(s.Id);
            }
            while (assigned < targetCount && tickets.Count > 0) {
                RoleId id = tickets[rnd.Next(tickets.Count)];
                if (avail[id] <= 0) { tickets.RemoveAll(x => x == id); continue; }
                var spec = specs.First(x => x.Id == id);
                var target = shuffled.FirstOrDefault(p => !assignedTo.Contains(p.PlayerId) && ModifierAllowed(spec, p));
                if (target == null) { tickets.RemoveAll(x => x == id); continue; } // nobody eligible for this one
                AssignModifier(spec, target);
                assignedTo.Add(target.PlayerId); avail[id]--; assigned++;
                if (avail[id] <= 0) tickets.RemoveAll(x => x == id);
            }

            ChancePlugin.Logger?.LogInfo($"[Chaos] Modifier reroll: {assigned} modifier(s) handed to {participants.Count} participant(s).");
        }

        private static void AssignModifier(ModSpec spec, PlayerControl target) {
            SendChaosModifierSet(target.PlayerId, (byte)spec.Id, 0);
        }

        // Host: clear a player's primary modifiers everywhere. The custom Chance RPC reaches mod
        // clients; the host clears locally. (TOR has no native "remove modifier" RPC, so non-mod
        // clients can't be told to clear — accepted; the host is authoritative for win conditions.)
        private static void SendChaosModifierClear(byte playerId) {
            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, Chance.ChaosModifierClearRpcId, Hazel.SendOption.Reliable, -1);
            w.Write(playerId);
            AmongUsClient.Instance.FinishRpcImmediately(w);
            ApplyChaosModifierClear(playerId); // host applies locally (no self-RPC)
        }

        // Host: assign a modifier through TOR's native SetModifier RPC (reaches every TOR client, mod
        // or not). The host doesn't receive its own RPC, so it applies locally too.
        private static void SendChaosModifierSet(byte playerId, byte modifierId, byte flag) {
            ChancePlugin.Logger?.LogInfo($"[Chaos] Modifier assign: player {playerId} -> {(RoleId)modifierId}");
            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, TorSetModifierRpcId, Hazel.SendOption.Reliable, -1);
            w.Write(modifierId);
            w.Write(playerId);
            w.Write(flag);
            AmongUsClient.Instance.FinishRpcImmediately(w);
            try { RPCProcedure.setModifier(modifierId, playerId, flag); } catch { }
        }

        // Runs on host + every Chance-mod client: strip the player from all primary modifier holders.
        public static void ApplyChaosModifierClear(byte playerId) {
            try {
                Bait.bait?.RemoveAll(x => x != null && x.PlayerId == playerId);
                Bloody.bloody?.RemoveAll(x => x != null && x.PlayerId == playerId);
                AntiTeleport.antiTeleport?.RemoveAll(x => x != null && x.PlayerId == playerId);
                Sunglasses.sunglasses?.RemoveAll(x => x != null && x.PlayerId == playerId);
                Vip.vip?.RemoveAll(x => x != null && x.PlayerId == playerId);
                Invert.invert?.RemoveAll(x => x != null && x.PlayerId == playerId);
                Chameleon.chameleon?.RemoveAll(x => x != null && x.PlayerId == playerId);
                if (Tiebreaker.tiebreaker != null && Tiebreaker.tiebreaker.PlayerId == playerId) Tiebreaker.tiebreaker = null;
                if (Mini.mini != null && Mini.mini.PlayerId == playerId) Mini.mini = null;
                if (Armored.armored != null && Armored.armored.PlayerId == playerId) Armored.armored = null;
                if (Shifter.shifter != null && Shifter.shifter.PlayerId == playerId) Shifter.shifter = null;
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chaos] ApplyChaosModifierClear failed for player {playerId}: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class ChaosMeetingStartPatch {
        public static void Postfix() => ChaosMode.OnMeetingStarted();
    }

    // Triggered after the exile cutscene wraps up. This runs AFTER ExileController.Begin,
    // where the Eraser performs its erasures, so Eraser.alreadyErased is populated in time.
    // Priority.Low so the reroll runs AFTER TOR's own WrapUp postfix: that postfix consumes
    // Seer.deadBodyPositions and Medium.futureDeadBodies (spawning their souls) and runs the
    // Deputy promotion check. The reroll's erasePlayerRoles → clearAndReload would otherwise wipe
    // those lists first, so the (ex-)Seer/Medium would lose the souls for this meeting's deaths.
    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    [HarmonyPriority(Priority.Low)]
    static class ChaosExileWrapUpPatch {
        public static void Postfix() => ChaosMode.OnMeetingEnded();
    }

    [HarmonyPatch(typeof(AirshipExileController), nameof(AirshipExileController.WrapUpAndSpawn))]
    [HarmonyPriority(Priority.Low)]
    static class ChaosAirshipExileWrapUpPatch {
        public static void Postfix() => ChaosMode.OnMeetingEnded();
    }

    [HarmonyPatch(typeof(TheOtherRoles.TheOtherRoles), "clearAndReloadRoles")]
    static class ChaosClearAndReloadPatch {
        public static void Postfix() => ChaosMode.Reset();
    }

    // Bug 4: at game end, show the full role progression (e.g. "Sheriff → Medic → Mayor")
    // instead of only the final role. Only active once the game has ended, so in-game
    // displays (nameplates, meetings) still show the current role.
    [HarmonyPatch(typeof(RoleInfo), nameof(RoleInfo.GetRolesString))]
    static class ChaosRoleHistoryPatch {
        public static void Postfix(PlayerControl p, ref string __result) {
            if (p == null || AmongUsClient.Instance == null) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Ended) return;
            var hist = ChaosMode.GetHistory(p.PlayerId);
            if (hist == null || hist.Count <= 1) return;
            __result = string.Join(" → ", hist.ToArray());
        }
    }

    // Kürzt zu lange Rollen-Verläufe im End-Screen-Role-Summary auf "... → letzte N Rollen".
    // Das Summary ist ein World-Space-TMP ohne Overflow-Behandlung (TOR EndGamePatch): Zeilen,
    // die breiter als der Bildschirm sind, laufen unsichtbar über den Rand. N ist hier nicht
    // fix, sondern ergibt sich pro Client aus der echten Bildschirmbreite: jede Verlaufszeile
    // wird mit dem tatsächlichen TMP gemessen (GetPreferredValues rechnet mit dessen Font und
    // Schriftgröße und ignoriert Rich-Text-Tags) und von VORNE gekürzt, bis sie passt — die
    // letzte (finale) Rolle bleibt immer stehen. Breitere Bildschirme zeigen also mehr Rollen.
    // Priority.Low: läuft nach TORs SetEverythingUp-Postfix, das das Summary-Objekt erst baut.
    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
    [HarmonyPriority(Priority.Low)]
    static class ChaosHistorySummaryTrimPatch {
        private const string SummaryMarker = "Players and roles at the end of the game:";

        public static void Postfix() {
            try {
                var histories = ChaosMode.AllHistories;
                if (histories.Count == 0) return;
                if (!histories.Values.Any(h => h != null && h.Count > 1)) return;

                // Das von TOR per Instantiate(WinText) erzeugte Summary-TMP über seinen festen
                // Kopftext finden (TOR hält keine Referenz darauf, die wir lesen könnten).
                TMPro.TMP_Text summary = null;
                foreach (var t in UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>()) {
                    if (t != null && t.text != null && t.text.Contains(SummaryMarker)) { summary = t; break; }
                }
                if (summary == null) return; // Role-Summary deaktiviert oder TOR-Layout geändert

                var cam = Camera.main;
                if (cam == null) return;
                float rightX = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.nearClipPlane)).x;

                // Linke Weltkante des Text-Rects: bei TopLeft-Alignment beginnt dort jede Zeile.
                var corners = new Il2CppStructArray<Vector3>(4);
                summary.rectTransform.GetWorldCorners(corners);
                float available = rightX - corners[0].x - 0.15f; // kleiner Sicherheitsrand
                if (available <= 1f) return;

                // Wrapping deterministisch aus: die Wrap-Einstellung wäre sonst vom Vanilla-
                // WinText-Prefab geerbt; nach dem Kürzen passt ohnehin jede Zeile in eine Zeile.
                summary.enableWordWrapping = false;

                var chains = histories.Values.Where(h => h != null && h.Count > 1).ToList();
                string[] lines = summary.text.Split('\n');
                bool changed = false;

                for (int i = 0; i < lines.Length; i++) {
                    string line = lines[i];
                    if (!line.Contains(" → ")) continue;
                    if (summary.GetPreferredValues(line).x <= available) continue;

                    // Die Verlaufskette dieser Zeile exakt über ihren von uns erzeugten String
                    // wiederfinden (kein fragiles Parsen von Spielername/Task-Suffix nötig).
                    foreach (var hist in chains) {
                        string full = string.Join(" → ", hist.ToArray());
                        if (!line.Contains(full)) continue;
                        for (int keep = hist.Count - 1; keep >= 1; keep--) {
                            string reduced = "... → " + string.Join(" → ", hist.Skip(hist.Count - keep).ToArray());
                            string candidate = line.Replace(full, reduced);
                            if (keep == 1 || summary.GetPreferredValues(candidate).x <= available) {
                                lines[i] = candidate;
                                changed = true;
                                break;
                            }
                        }
                        break;
                    }
                }

                if (changed) {
                    string newText = string.Join("\n", lines);
                    summary.text = newText;
                    // Lobby-Nachanzeige (Summary-Button) speist sich aus diesem Feld; dort schneidet
                    // TOR lange Zeilen ebenfalls ab (enableWordWrapping=false), also gekürzt halten.
                    Helpers.previousEndGameSummary = $"<size=110%>{newText}</size>";
                }
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chaos] end-game summary trim failed: {e}");
            }
        }
    }
}
