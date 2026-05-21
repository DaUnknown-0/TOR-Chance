// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;
using TheOtherRoles;
using static TheOtherRoles.TheOtherRoles;

namespace TOR_ChanceModifier {

    // Chaos Mode: after every meeting, re-roll the role of each living Crew/Impostor player
    // within their own team, weighted by the existing TOR spawn chances.
    public static class ChaosMode {
        private const byte NoneRoleId = 255; // marker: clear role, assign no new TOR role (vanilla)

        private static bool processedThisMeeting;

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

        private static bool IsErased(PlayerControl p) {
            return Eraser.alreadyErased != null && Eraser.alreadyErased.Contains(p.PlayerId);
        }

        public static void RerollRoles() {
            if (!(AmongUsClient.Instance?.AmHost ?? false)) return;
            if (!IsEnabled()) return;

            var alive = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && p.Data.Role != null)
                .Where(p => !IsErased(p)) // players the Eraser caught keep no role
                .ToList();

            var impPlayers = alive.Where(p => p.Data.Role.IsImpostor).ToList();
            var crewPlayers = alive.Where(p => !p.Data.Role.IsImpostor
                                               && !Helpers.isNeutral(p)
                                               && p != Spy.spy && p != Snitch.snitch).ToList();

            RerollTeam(impPlayers, ImpostorRoles());
            RerollTeam(crewPlayers, CrewRoles());
        }

        private static void RerollTeam(List<PlayerControl> players, List<ChaosRole> rolePool) {
            if (players.Count == 0) return;

            // Drop 0% roles and roles whose current holder is dead (locked).
            var available = rolePool.Where(r => {
                var opt = r.Rate();
                if (opt == null || opt.getSelection() <= 0) return false;
                var holder = r.Holder();
                if (holder != null && holder.Data != null && holder.Data.IsDead) return false;
                return true;
            }).ToList();

            var ensured = available.Where(r => r.Rate().getSelection() >= 10).Select(r => r.Id)
                                   .OrderBy(_ => rnd.Next()).ToList();
            var tickets = new List<RoleId>();
            foreach (var r in available) {
                int sel = r.Rate().getSelection();
                if (sel >= 1 && sel < 10) for (int i = 0; i < sel; i++) tickets.Add(r.Id);
            }

            var shuffledPlayers = players.OrderBy(_ => rnd.Next()).ToList();
            var assignedRoles = new HashSet<RoleId>();
            var result = new List<KeyValuePair<byte, byte>>();
            int idx = 0;

            // Guaranteed (100%) roles first.
            foreach (var roleId in ensured) {
                if (idx >= shuffledPlayers.Count) break;
                if (!assignedRoles.Add(roleId)) continue;
                result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, (byte)roleId));
                idx++;
            }

            // Weighted ticket pool for 1-9 chance roles.
            while (idx < shuffledPlayers.Count && tickets.Count > 0) {
                RoleId roleId = tickets[rnd.Next(tickets.Count)];
                tickets.RemoveAll(x => x == roleId); // enforce uniqueness
                if (!assignedRoles.Add(roleId)) continue;
                result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, (byte)roleId));
                idx++;
            }

            // Remaining players become vanilla (role cleared, none assigned).
            for (; idx < shuffledPlayers.Count; idx++) {
                result.Add(new KeyValuePair<byte, byte>(shuffledPlayers[idx].PlayerId, NoneRoleId));
            }

            foreach (var entry in result) {
                SendChaosReassign(entry.Key, entry.Value);
            }
        }

        private static void SendChaosReassign(byte playerId, byte roleId) {
            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, Chance.ChaosRpcId, Hazel.SendOption.Reliable, -1);
            w.Write(playerId);
            w.Write(roleId);
            AmongUsClient.Instance.FinishRpcImmediately(w);
            ApplyChaosReassign(playerId, roleId);
        }

        public static void ApplyChaosReassign(byte playerId, byte roleId) {
            RPCProcedure.erasePlayerRoles(playerId); // keeps vanilla team + modifiers (ignoreModifier=true)
            if (roleId != NoneRoleId) RPCProcedure.setRole(roleId, playerId);
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class ChaosMeetingStartPatch {
        public static void Postfix() => ChaosMode.OnMeetingStarted();
    }

    // Triggered after the exile cutscene wraps up. This runs AFTER ExileController.Begin,
    // where the Eraser performs its erasures, so Eraser.alreadyErased is populated in time.
    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    static class ChaosExileWrapUpPatch {
        public static void Postfix() => ChaosMode.OnMeetingEnded();
    }

    [HarmonyPatch(typeof(AirshipExileController), nameof(AirshipExileController.WrapUpAndSpawn))]
    static class ChaosAirshipExileWrapUpPatch {
        public static void Postfix() => ChaosMode.OnMeetingEnded();
    }

    [HarmonyPatch(typeof(TheOtherRoles.TheOtherRoles), "clearAndReloadRoles")]
    static class ChaosClearAndReloadPatch {
        public static void Postfix() => ChaosMode.Reset();
    }
}
