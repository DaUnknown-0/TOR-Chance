// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hazel;
using UnityEngine;
using AmongUs.GameOptions;
using TheOtherRoles;
using TheOtherRoles.Utilities;
using static TheOtherRoles.TheOtherRoles;

namespace TOR_ChanceModifier {

    // ---------------------------------------------------------------------------
    // Options (own static fields instead of entries in CustomOptionHolder)
    // ---------------------------------------------------------------------------
    internal static class ChanceOptions {
        public static CustomOption modifierChance;
        public static CustomOption modifierChanceQuantity;
        public static CustomOption modifierChanceSpeedMin;
        public static CustomOption modifierChanceSpeedMax;
        public static CustomOption modifierChanceCooldownMin;
        public static CustomOption modifierChanceCooldownMax;
        public static CustomOption modifierChanceTasksMin;
        public static CustomOption modifierChanceTasksMax;
        public static CustomOption modifierChanceKillDeathChance;
        public static CustomOption modifierChanceReportChance;
        public static CustomOption modifierChanceVisionMin;
        public static CustomOption modifierChanceVisionMax;
        public static CustomOption modifierChanceVentChance;
        public static CustomOption modifierChanceVoteMultMin;
        public static CustomOption modifierChanceVoteMultMax;
        public static CustomOption modifierChanceKillDistanceMin;
        public static CustomOption modifierChanceKillDistanceMax;
        public static CustomOption modifierChanceSabotageCdMin;
        public static CustomOption modifierChanceSabotageCdMax;
        public static CustomOption modifierChanceActivationMode;
        public static CustomOption modifierChanceActivationUnit;
        public static CustomOption modifierChanceActivationMeetings;
        public static CustomOption modifierChanceActivationSeconds;
        public static CustomOption chaosMode;
        public static CustomOption chaosRolePool;
        public static CustomOption chaosScope;
        public static CustomOption chaosModifierReroll;
        public static CustomOption chaosModifierScope;

        // Per-effect enable toggles. Off (default) → the effect doesn't apply at all (vanilla).
        public static CustomOption modifierChanceSpeedEnabled;
        public static CustomOption modifierChanceCooldownEnabled;
        public static CustomOption modifierChanceTasksEnabled;
        public static CustomOption modifierChanceKillSuccessEnabled;
        public static CustomOption modifierChanceReportEnabled;
        public static CustomOption modifierChanceVisionEnabled;
        public static CustomOption modifierChanceVentEnabled;
        public static CustomOption modifierChanceVoteEnabled;
        public static CustomOption modifierChanceKillDistanceEnabled;
        public static CustomOption modifierChanceSabotageEnabled;
    }

    // ---------------------------------------------------------------------------
    // Data class + RPC helpers
    // ---------------------------------------------------------------------------
    public static class Chance {
        public const byte RpcId = 200;
        public const byte ChaosRpcId = 201;
        // Chaos modifier reroll: host tells every Chance-mod client to strip a player's primary TOR
        // modifier(s). The new modifier itself is pushed through TOR's native SetModifier RPC (so even
        // non-mod clients add it), so this custom RPC only carries the CLEAR step.
        public const byte ChaosModifierClearRpcId = 202;
        internal const byte ActivationRpcId = 250;
        internal const byte VersionHandshakeRpcId = 251;
        // Sentinel task value meaning "do not manage this player's task count" — used when task
        // reduction is disabled (delayed activation, where tasks are already assigned at game start).
        internal const byte NoTaskChange = byte.MaxValue;
        public const int RoleIdValue = 58;   // Value after Shifter (57)

        public static List<PlayerControl> chanceList = new List<PlayerControl>();
        // P2.1: Spiegelt die PlayerIds aus chanceList als HashSet. IsChancePlayer wird per Frame und
        // Spieler aus PlayerPhysics.FixedUpdate, CalculateLightRadius, HudManager.Update und der
        // Kill-Target-Neuwahl aufgerufen — eine O(1)-Set-Prüfung ersetzt das O(n)-chanceList.Any(...).
        // Wird in applyValues/CleanChancePlayers/clearAndReload synchron mit chanceList gepflegt.
        public static HashSet<byte> chanceIds = new HashSet<byte>();
        public static Dictionary<byte, float> speedMod        = new Dictionary<byte, float>();
        public static Dictionary<byte, float> cooldownMod     = new Dictionary<byte, float>();
        public static Dictionary<byte, float> visionMod       = new Dictionary<byte, float>();
        public static Dictionary<byte, byte>  tasksMod        = new Dictionary<byte, byte>();
        public static Dictionary<byte, float> sabotageCdMod   = new Dictionary<byte, float>();
        public static Dictionary<byte, float> killDistanceMod = new Dictionary<byte, float>();
        public static Dictionary<byte, byte>  voteMultiplierMod = new Dictionary<byte, byte>();
        public static Dictionary<byte, bool>  ventAccessMod   = new Dictionary<byte, bool>();

        public static float speedMin, speedMax;
        public static float cooldownMin, cooldownMax;
        public static int   tasksMin, tasksMax;
        public static float killDeathChance;
        public static float reportChance;
        public static float visionMin, visionMax;
        public static float ventChance;
        public static int   voteMultMin, voteMultMax;
        public static float killDistanceMin, killDistanceMax;
        public static float sabotageCdMin, sabotageCdMax;
        private static bool tasksEnabled;

        // Per-effect enable flags, read from the toggle options in ReloadRanges. When false the
        // corresponding patch is skipped (effect stays vanilla). Tasks reuse `tasksEnabled` above.
        public static bool speedEnabled;
        public static bool cooldownEnabled;
        public static bool killSuccessEnabled;
        public static bool reportEnabled;
        public static bool visionEnabled;
        public static bool ventEnabled;
        public static bool voteEnabled;
        public static bool killDistanceEnabled;
        public static bool sabotageEnabled;

        private static int activationMode;
        private static int activationUnit;
        private static int activationMeetings;
        private static float activationSeconds;
        private static int meetingsElapsed;
        private static float activationStartTime;
        private static bool meetingEndedThisMeeting;
        private static bool rangeSyncInProgress;
        private static bool isActive;
        private static float reportCheckTimer;

        // Last observed shared sabotage timer; a jump UP between frames marks a fresh cooldown cycle.
        internal static float lastSabTimer = -1f;
        internal const float SabEdgeEps = 0.5f;

        public static void clearAndReload() {
            chanceList   = new List<PlayerControl>();
            chanceIds    = new HashSet<byte>();
            speedMod     = new Dictionary<byte, float>();
            cooldownMod  = new Dictionary<byte, float>();
            visionMod    = new Dictionary<byte, float>();
            tasksMod     = new Dictionary<byte, byte>();
            sabotageCdMod   = new Dictionary<byte, float>();
            killDistanceMod = new Dictionary<byte, float>();
            voteMultiplierMod = new Dictionary<byte, byte>();
            ventAccessMod   = new Dictionary<byte, bool>();

            ReloadRanges();

            meetingsElapsed = 0;
            activationStartTime = -1f;
            meetingEndedThisMeeting = false;
            rangeSyncInProgress = false;
            isActive = false;
            reportCheckTimer = 0f;
            lastSabTimer = -1f;
        }

        // Loads every min/max range, chance % and activation setting from the options and applies
        // the min≤max ordering + task cap. No runtime/dictionary state is touched.
        public static void ReloadRanges() {
            speedMin        = ChanceOptions.modifierChanceSpeedMin?.getFloat()        ?? 0.5f;
            speedMax        = ChanceOptions.modifierChanceSpeedMax?.getFloat()        ?? 2.5f;
            cooldownMin     = ChanceOptions.modifierChanceCooldownMin?.getFloat()     ?? 5f;
            cooldownMax     = ChanceOptions.modifierChanceCooldownMax?.getFloat()     ?? 60f;
            tasksMin        = (int)(ChanceOptions.modifierChanceTasksMin?.getFloat()  ?? 1f);
            tasksMax        = (int)(ChanceOptions.modifierChanceTasksMax?.getFloat()  ?? 10f);
            killDeathChance = ChanceOptions.modifierChanceKillDeathChance?.getFloat() ?? 30f;
            reportChance    = ChanceOptions.modifierChanceReportChance?.getFloat()    ?? 10f;
            visionMin       = ChanceOptions.modifierChanceVisionMin?.getFloat()       ?? 0.25f;
            visionMax       = ChanceOptions.modifierChanceVisionMax?.getFloat()       ?? 5f;
            ventChance      = ChanceOptions.modifierChanceVentChance?.getFloat()      ?? 0f;
            voteMultMin     = (int)(ChanceOptions.modifierChanceVoteMultMin?.getFloat() ?? 1f);
            voteMultMax     = (int)(ChanceOptions.modifierChanceVoteMultMax?.getFloat() ?? 1f);
            killDistanceMin = ChanceOptions.modifierChanceKillDistanceMin?.getFloat() ?? 1f;
            killDistanceMax = ChanceOptions.modifierChanceKillDistanceMax?.getFloat() ?? 1.75f;
            sabotageCdMin   = ChanceOptions.modifierChanceSabotageCdMin?.getFloat()   ?? 5f;
            sabotageCdMax   = ChanceOptions.modifierChanceSabotageCdMax?.getFloat()   ?? 30f;

            float origSpeedMin = speedMin;
            float origSpeedMax = speedMax;
            speedMin = Mathf.Min(origSpeedMin, origSpeedMax);
            speedMax = Mathf.Max(origSpeedMin, origSpeedMax);

            float origCooldownMin = cooldownMin;
            float origCooldownMax = cooldownMax;
            cooldownMin = Mathf.Min(origCooldownMin, origCooldownMax);
            cooldownMax = Mathf.Max(origCooldownMin, origCooldownMax);

            int origTasksMin = tasksMin;
            int origTasksMax = tasksMax;
            tasksMin = Math.Min(origTasksMin, origTasksMax);
            tasksMax = Math.Max(origTasksMin, origTasksMax);

            // A Chance player can never get more tasks than the game actually hands out.
            var taskOptions = GameOptionsManager.Instance?.currentNormalGameOptions;
            if (taskOptions != null) {
                int totalGameTasks = taskOptions.NumCommonTasks + taskOptions.NumShortTasks + taskOptions.NumLongTasks;
                if (totalGameTasks > 0) tasksMax = Math.Min(tasksMax, totalGameTasks);
            }
            tasksMin = Math.Min(tasksMin, tasksMax);

            float origVisionMin = visionMin;
            float origVisionMax = visionMax;
            visionMin = Mathf.Min(origVisionMin, origVisionMax);
            visionMax = Mathf.Max(origVisionMin, origVisionMax);

            int origVoteMin = voteMultMin;
            int origVoteMax = voteMultMax;
            voteMultMin = Math.Min(origVoteMin, origVoteMax);
            voteMultMax = Math.Max(origVoteMin, origVoteMax);

            float origKillDistMin = killDistanceMin;
            float origKillDistMax = killDistanceMax;
            killDistanceMin = Mathf.Min(origKillDistMin, origKillDistMax);
            killDistanceMax = Mathf.Max(origKillDistMin, origKillDistMax);

            float origSaboMin = sabotageCdMin;
            float origSaboMax = sabotageCdMax;
            sabotageCdMin = Mathf.Min(origSaboMin, origSaboMax);
            sabotageCdMax = Mathf.Max(origSaboMin, origSaboMax);

            activationMode    = ChanceOptions.modifierChanceActivationMode?.getSelection()   ?? 0;
            activationUnit    = ChanceOptions.modifierChanceActivationUnit?.getSelection()   ?? 0;
            activationMeetings = (int)(ChanceOptions.modifierChanceActivationMeetings?.getFloat() ?? 0f);
            activationSeconds  = ChanceOptions.modifierChanceActivationSeconds?.getFloat()   ?? 0f;

            // Per-effect enable toggles. Default Off → the effect stays vanilla until enabled.
            speedEnabled        = ChanceOptions.modifierChanceSpeedEnabled?.getBool()        ?? false;
            cooldownEnabled     = ChanceOptions.modifierChanceCooldownEnabled?.getBool()     ?? false;
            killSuccessEnabled  = ChanceOptions.modifierChanceKillSuccessEnabled?.getBool()  ?? false;
            reportEnabled       = ChanceOptions.modifierChanceReportEnabled?.getBool()       ?? false;
            visionEnabled       = ChanceOptions.modifierChanceVisionEnabled?.getBool()       ?? false;
            ventEnabled         = ChanceOptions.modifierChanceVentEnabled?.getBool()         ?? false;
            voteEnabled         = ChanceOptions.modifierChanceVoteEnabled?.getBool()         ?? false;
            killDistanceEnabled = ChanceOptions.modifierChanceKillDistanceEnabled?.getBool() ?? false;
            sabotageEnabled     = ChanceOptions.modifierChanceSabotageEnabled?.getBool()     ?? false;

            // Task reduction only works for immediate activation: a delayed Chance can't trim tasks
            // that were already assigned at game start. So it requires BOTH its enable toggle AND
            // immediate activation; otherwise the task feature is disabled entirely.
            tasksEnabled = activationMode == 0
                && (ChanceOptions.modifierChanceTasksEnabled?.getBool() ?? false);
        }

        private static bool HasChanceModifier() {
            return ChanceOptions.modifierChance != null && ChanceOptions.modifierChance.getSelection() > 0;
        }

        private static float GetActivationThreshold() {
            return activationUnit == 0 ? activationMeetings : activationSeconds;
        }

        public static bool IsActive() {
            return isActive && HasChanceModifier();
        }

        public static bool IsChancePlayer(byte playerId) {
            return IsActive() && chanceIds.Contains(playerId);
        }

        public static bool TryActivate() {
            if (IsActive() || !HasChanceModifier()) return false;
            if (activationMode == 0 || GetActivationThreshold() <= 0f) {
                Activate();
                return true;
            }

            if (AmongUsClient.Instance?.GameState != InnerNet.InnerNetClient.GameStates.Started) return false;

            if (activationUnit == 0) {
                if (meetingsElapsed < activationMeetings) return false;
            }
            else {
                if (activationStartTime < 0f) activationStartTime = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - activationStartTime < activationSeconds) return false;
            }

            Activate();
            return true;
        }

        public static void ReceiveActivation() {
            ApplyActivationState();
        }

        public static void OnMeetingStarted() {
            meetingEndedThisMeeting = false;
        }

        public static void OnMeetingEnded() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (meetingEndedThisMeeting) return;
            meetingEndedThisMeeting = true;
            meetingsElapsed++;
            bool activatedNow = TryActivate();
            if (!activatedNow && IsActive()) {
                RandomizeChancePlayers();
            }
        }

        private static void Activate() {
            if (IsActive() || !HasChanceModifier()) return;

            if (AmongUsClient.Instance?.AmHost == true) {
                AssignChancePlayers();
                if (chanceList.Count == 0) return;

                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId, ActivationRpcId, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }

            ApplyActivationState();
        }

        private static void ApplyActivationState() {
            if (isActive) return;
            if (!HasChanceModifier()) return;
            isActive = true;
            // Slot-machine cue for the affected players only - "your values are rolling now".
            if (PlayerControl.LocalPlayer != null && IsChancePlayer(PlayerControl.LocalPlayer.PlayerId))
                ChanceAssets.PlayActivate();
        }

        public static void AssignChancePlayers() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (!HasChanceModifier()) return;
            CleanChancePlayers();
            if (chanceList.Count > 0) return;

            int rate = Mathf.Clamp((ChanceOptions.modifierChance?.getSelection() ?? 0) * 10, 0, 100); // 0..100 per slot
            // selection 0 corresponds to "1", hence +1
            int quantity = ChanceOptions.modifierChanceQuantity.getSelection() + 1;
            var candidates = PlayerControl.AllPlayerControls.ToArray()
                .Where(player => player != null && player.Data != null && !player.Data.IsDead);

            // AUDIT-2026-08-15: the Chance postfix on Helpers.checkMuderAttempt (and the speed/
            // cooldown/report/vent/sabotage effects) only runs on clients that have this mod, so
            // rolling a player without it just makes them kill at 100% while everything else stays
            // vanilla. Restrict candidates to players the RPC 251 handshake confirms are on a
            // matching version. Skip the filter when the handshake has no entries at all (offline/
            // freeplay: nobody ever broadcasts) so we don't end up filtering out every candidate.
            if (ChanceVersionHandshake.playerVersions.Count > 0) {
                candidates = candidates.Where(player =>
                    ChanceVersionHandshake.playerVersions.TryGetValue(player.OwnerId, out var pv)
                    && ChancePlugin.Version.CompareTo(pv.version) == 0);
            }

            var players = candidates.OrderBy(_ => Guid.NewGuid()).ToList();
            int toAssign = Math.Min(quantity, players.Count);

            for (int i = 0; i < toAssign; i++) {
                if (rnd.Next(100) >= rate) continue;
                sendSetValues(players[i], rnd);
            }
        }

        public static void RandomizeChancePlayers() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (!IsActive() || !HasChanceModifier()) return;
            CleanChancePlayers();

            foreach (var p in chanceList.ToList()) {
                if (p != null && p.Data != null && !p.Data.IsDead) {
                    sendSetValues(p, rnd, randomizeTasks: false);
                }
            }
        }

        // One randomized stat set for a Chance player.
        public struct ChanceRoll {
            public float speed;
            public float cooldown;
            public float vision;
            public byte  tasks;          // NoTaskChange when task reduction is disabled
            public float sabotageCd;
            public float killDistance;
            public byte  voteMultiplier; // 0..3
            public bool  ventAccess;
        }

        // Rolls one stat set from the configured ranges. No side effects. `playerId` is only consulted
        // when randomizeTasks=false to keep an already-assigned task count stable.
        public static ChanceRoll RollValues(System.Random rnd, bool randomizeTasks = true, byte playerId = byte.MaxValue) {
            float orderedSpeedMin = Mathf.Min(speedMin, speedMax);
            float orderedSpeedMax = Mathf.Max(speedMin, speedMax);
            float orderedCooldownMin = Mathf.Min(cooldownMin, cooldownMax);
            float orderedCooldownMax = Mathf.Max(cooldownMin, cooldownMax);
            float orderedVisionMin = Mathf.Min(visionMin, visionMax);
            float orderedVisionMax = Mathf.Max(visionMin, visionMax);
            int orderedTasksMin = Math.Min(tasksMin, tasksMax);
            int orderedTasksMax = Math.Max(tasksMin, tasksMax);
            int orderedVoteMin = Math.Min(voteMultMin, voteMultMax);
            int orderedVoteMax = Math.Max(voteMultMin, voteMultMax);
            float orderedKillDistMin = Mathf.Min(killDistanceMin, killDistanceMax);
            float orderedKillDistMax = Mathf.Max(killDistanceMin, killDistanceMax);
            float orderedSaboMin = Mathf.Min(sabotageCdMin, sabotageCdMax);
            float orderedSaboMax = Mathf.Max(sabotageCdMin, sabotageCdMax);

            ChanceRoll roll = new ChanceRoll();
            roll.speed        = orderedSpeedMin    + (float)rnd.NextDouble() * (orderedSpeedMax    - orderedSpeedMin);
            roll.cooldown     = orderedCooldownMin + (float)rnd.NextDouble() * (orderedCooldownMax - orderedCooldownMin);
            roll.vision       = orderedVisionMin   + (float)rnd.NextDouble() * (orderedVisionMax   - orderedVisionMin);
            roll.sabotageCd   = orderedSaboMin     + (float)rnd.NextDouble() * (orderedSaboMax     - orderedSaboMin);
            roll.killDistance = orderedKillDistMin + (float)rnd.NextDouble() * (orderedKillDistMax - orderedKillDistMin);
            roll.voteMultiplier = (byte)rnd.Next(orderedVoteMin, orderedVoteMax + 1);
            roll.ventAccess   = rnd.NextDouble() * 100f < ventChance;

            if (!tasksEnabled) {
                roll.tasks = NoTaskChange;
            } else if (!randomizeTasks && playerId != byte.MaxValue && tasksMod.TryGetValue(playerId, out byte existingTasks)) {
                roll.tasks = existingTasks;
            } else {
                roll.tasks = (byte)rnd.Next(orderedTasksMin, orderedTasksMax + 1);
            }
            return roll;
        }

        // Sends RPC 200 from host → sets values on all clients.
        // Format: playerId (byte), speed (float), cooldown (float), vision (float), tasks (byte),
        //         sabotageCd (float), killDistance (float), voteMultiplier (byte), ventAccess (byte 0/1)
        // Tasks are only re-rolled when randomizeTasks=true (initial assignment);
        // re-randomization between meetings keeps the original task count.
        public static void sendSetValues(PlayerControl player, System.Random rnd, bool randomizeTasks = true) {
            ChanceRoll roll = RollValues(rnd, randomizeTasks, player.PlayerId);

            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, RpcId, Hazel.SendOption.Reliable, -1);
            w.Write(player.PlayerId);
            w.Write(roll.speed);
            w.Write(roll.cooldown);
            w.Write(roll.vision);
            w.Write(roll.tasks);
            w.Write(roll.sabotageCd);
            w.Write(roll.killDistance);
            w.Write(roll.voteMultiplier);
            w.Write((byte)(roll.ventAccess ? 1 : 0));
            AmongUsClient.Instance.FinishRpcImmediately(w);
            applyValues(player.PlayerId, roll);
        }

        public static void applyValues(byte id, ChanceRoll roll) {
            var p = Helpers.playerById(id);
            if (p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && !chanceList.Any(x => x.PlayerId == id)) {
                chanceList.Add(p);
                chanceIds.Add(id);
            }
            speedMod[id]          = roll.speed;
            cooldownMod[id]       = roll.cooldown;
            visionMod[id]         = roll.vision;
            tasksMod[id]          = roll.tasks;
            sabotageCdMod[id]     = roll.sabotageCd;
            killDistanceMod[id]   = roll.killDistance;
            voteMultiplierMod[id] = roll.voteMultiplier;
            ventAccessMod[id]     = roll.ventAccess;
        }

        public static bool isChance(byte playerId) =>
            IsChancePlayer(playerId);

        private static void CleanChancePlayers() {
            var removedIds = chanceList
                .Where(p => p == null || p.Data == null || p.Data.Disconnected || p.Data.IsDead)
                .Select(p => p?.PlayerId ?? byte.MaxValue)
                .Where(id => id != byte.MaxValue)
                .ToList();

            chanceList.RemoveAll(p => p == null || p.Data == null || p.Data.Disconnected || p.Data.IsDead);

            foreach (byte id in removedIds) {
                chanceIds.Remove(id);
                speedMod.Remove(id);
                cooldownMod.Remove(id);
                visionMod.Remove(id);
                tasksMod.Remove(id);
                sabotageCdMod.Remove(id);
                killDistanceMod.Remove(id);
                voteMultiplierMod.Remove(id);
                ventAccessMod.Remove(id);
            }
        }

        private static int GetClosestSelectionIndex(CustomOption option, float value) {
            int bestIndex = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < option.selections.Length; i++) {
                if (option.selections[i] is not float selectionValue) continue;
                float distance = Mathf.Abs(selectionValue - value);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static void SetFloatOption(CustomOption option, float value) {
            if (option == null) return;
            int targetIndex = GetClosestSelectionIndex(option, value);
            if (option.getSelection() == targetIndex) return;
            option.updateSelection(targetIndex);
        }

        private static void SyncOrderedFloatPair(CustomOption minOption, CustomOption maxOption, bool minChanged) {
            if (rangeSyncInProgress) return;

            rangeSyncInProgress = true;
            try {
                float minValue = minOption?.getFloat() ?? 0f;
                float maxValue = maxOption?.getFloat() ?? 0f;

                if (minChanged && minValue > maxValue) {
                    SetFloatOption(maxOption, minValue);
                }
                else if (!minChanged && maxValue < minValue) {
                    SetFloatOption(minOption, maxValue);
                }
            }
            finally {
                rangeSyncInProgress = false;
            }
        }

        public static void OnSpeedMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceSpeedMin, ChanceOptions.modifierChanceSpeedMax, true);
        public static void OnSpeedMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceSpeedMin, ChanceOptions.modifierChanceSpeedMax, false);
        public static void OnCooldownMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceCooldownMin, ChanceOptions.modifierChanceCooldownMax, true);
        public static void OnCooldownMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceCooldownMin, ChanceOptions.modifierChanceCooldownMax, false);
        public static void OnTasksMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceTasksMin, ChanceOptions.modifierChanceTasksMax, true);
        public static void OnTasksMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceTasksMin, ChanceOptions.modifierChanceTasksMax, false);
        public static void OnVisionMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceVisionMin, ChanceOptions.modifierChanceVisionMax, true);
        public static void OnVisionMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceVisionMin, ChanceOptions.modifierChanceVisionMax, false);
        public static void OnVoteMultMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceVoteMultMin, ChanceOptions.modifierChanceVoteMultMax, true);
        public static void OnVoteMultMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceVoteMultMin, ChanceOptions.modifierChanceVoteMultMax, false);
        public static void OnKillDistanceMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceKillDistanceMin, ChanceOptions.modifierChanceKillDistanceMax, true);
        public static void OnKillDistanceMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceKillDistanceMin, ChanceOptions.modifierChanceKillDistanceMax, false);
        public static void OnSabotageCdMinChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceSabotageCdMin, ChanceOptions.modifierChanceSabotageCdMax, true);
        public static void OnSabotageCdMaxChanged() => SyncOrderedFloatPair(ChanceOptions.modifierChanceSabotageCdMin, ChanceOptions.modifierChanceSabotageCdMax, false);

        // Builds the description text shown under the task list for the local player.
        // Other players only see the generic "Everything is random!" text.
        public static string GetChanceShortDescription(byte playerId) {
            if (PlayerControl.LocalPlayer == null || playerId != PlayerControl.LocalPlayer.PlayerId)
                return "You are CHAOS!";
            // Only surface effects whose enable toggle is actually on, so the text never advertises
            // a randomized stat that isn't applied (every disabled effect is plain vanilla).
            var parts = new List<string>();
            if (speedEnabled && speedMod.TryGetValue(playerId, out float speed)) parts.Add($"Speed {speed:0.00}×");
            if (cooldownEnabled && cooldownMod.TryGetValue(playerId, out float cd)) parts.Add($"Kill CD {cd:0.0}s");
            if (visionEnabled && visionMod.TryGetValue(playerId, out float vis)) parts.Add($"Vision {vis:0.00}×");
            // tasksMod is NoTaskChange whenever the task feature is disabled (toggle off or delayed).
            if (tasksMod.TryGetValue(playerId, out byte tasks) && tasks != NoTaskChange) parts.Add($"Tasks {tasks}");
            if (voteEnabled && voteMultiplierMod.TryGetValue(playerId, out byte votes)) parts.Add($"Votes ×{votes}");
            if (ventEnabled && ventAccessMod.TryGetValue(playerId, out bool vent) && vent) parts.Add("Vent ✓");
            if (killDistanceEnabled && killDistanceMod.TryGetValue(playerId, out float kd)) parts.Add($"KillDist {kd:0.0}");
            // Sabotage cooldown only affects impostors, so only surface it for them.
            if (sabotageEnabled && PlayerControl.LocalPlayer.Data?.Role?.IsImpostor == true
                && sabotageCdMod.TryGetValue(playerId, out float scd)) parts.Add($"Sabo CD {scd:0.0}s");

            if (parts.Count == 0) return "You are CHAOS!";
            return string.Join(" | ", parts);
        }

        // Auto-report: once per second, if a body is within report range of the local Chance
        // player, there is a `reportChance`% chance they panic-report it. Driven per client for
        // the local player (CmdReportDeadBody is a client command), so each player rolls their own.
        public static void UpdateAutoReport(float deltaTime) {
            if (!IsActive() || !reportEnabled || reportChance <= 0f) return;
            if (AmongUsClient.Instance?.GameState != InnerNet.InnerNetClient.GameStates.Started) return;

            var lp = PlayerControl.LocalPlayer;
            if (lp == null || lp.Data == null || lp.Data.IsDead) return;
            if (!IsChancePlayer(lp.PlayerId)) return;

            reportCheckTimer += deltaTime;
            if (reportCheckTimer < 1f) return;
            reportCheckTimer = 0f;

            // Not while a meeting / exile cutscene is up, or while otherwise unable to act.
            if (MeetingHud.Instance != null || ExileController.Instance != null) return;
            if (!lp.CanMove) return;

            DeadBody body = FindReportableBody(lp);
            if (body == null) return;

            if (rnd.NextDouble() * 100f < reportChance) {
                NetworkedPlayerInfo info = GameData.Instance.GetPlayerById(body.ParentId);
                if (info != null) lp.CmdReportDeadBody(info);
            }
        }

        private static DeadBody FindReportableBody(PlayerControl lp) {
            Vector2 origin = lp.GetTruePosition();
            foreach (Collider2D collider in Physics2D.OverlapCircleAll(origin, lp.MaxReportDistance, Constants.PlayersOnlyMask)) {
                if (collider.tag != "DeadBody") continue;
                DeadBody body = collider.GetComponent<DeadBody>();
                if (body == null || !body.enabled || body.Reported) continue;
                Vector2 bodyPos = body.TruePosition;
                if (Vector2.Distance(bodyPos, origin) <= lp.MaxReportDistance
                    && !PhysicsHelpers.AnythingBetween(origin, bodyPos, Constants.ShipAndObjectsMask, false)) {
                    return body;
                }
            }
            return null;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 1: Hook clearAndReload
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(TheOtherRoles.TheOtherRoles), "clearAndReloadRoles")]
    static class ChanceClearAndReloadPatch {
        public static void Postfix() => Chance.clearAndReload();
    }

    // ---------------------------------------------------------------------------
    // Patch 3: Assign modifier (after the existing RoleManager.SelectRoles patch)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    [HarmonyPriority(Priority.Low)]
    static class ChanceAssignPatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            Chance.TryActivate();
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceActivationTickPatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (Chance.IsActive()) return;
            Chance.TryActivate();
        }
    }

    // Runs on every client (not host-only): each client rolls auto-report for its own local player.
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceAutoReportTickPatch {
        public static void Postfix() {
            Chance.UpdateAutoReport(Time.deltaTime);
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class ChanceMeetingStartPatch {
        public static void Postfix() {
            Chance.OnMeetingStarted();
        }
    }

    // P1.6: Re-Randomisierung an dieselben Exile-WrapUp-Hooks wie ChaosMode hängen, NICHT an
    // MeetingHud.Close. Close feuert VOR der Exile-Cutscene — dort gilt der gerade rausgewählte
    // Spieler noch als lebend (!Data.IsDead), sodass der Host eine RPC an einen gleich sterbenden
    // Spieler verschwendete und das Meeting-Counting subtil von Chaos abwich. WrapUp läuft NACH
    // dem Tod des Exilierten; Unentschieden/Skip durchlaufen den Exile-Controller ohne Opfer, also
    // feuern diese Hooks in allen normalen Pfaden. Der meetingEndedThisMeeting-Guard verhindert
    // Doppelausführung, falls beide Controller-Typen je auftreten.
    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    static class ChanceExileWrapUpPatch {
        public static void Postfix() => Chance.OnMeetingEnded();
    }

    [HarmonyPatch(typeof(AirshipExileController), nameof(AirshipExileController.WrapUpAndSpawn))]
    static class ChanceAirshipExileWrapUpPatch {
        public static void Postfix() => Chance.OnMeetingEnded();
    }

    // ---------------------------------------------------------------------------
    // Patch 4: Receive RPC (Prefix with high priority → before the TOR switch handler)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    [HarmonyPriority(Priority.High)]
    static class ChanceHandleRpcPatch {
        // RPCs 200 (SetValues), 201 (ChaosReassign) und 250 (Activation) sind host-autoritativ:
        // sie re-rollen Rollen/Stats für ALLE Spieler bzw. setzen Kill-Cooldowns. Würden sie von
        // jedem Client akzeptiert, könnte ein modifizierter Client beliebig Rollen würfeln, sich
        // Vents geben oder fremde Cooldowns nullen (P0.3). Daher nur vom Host annehmen.
        // RPC 251 (Version-Handshake) bleibt bewusst für ALLE Clients offen — das ist sein Zweck.
        static bool IsFromHost(PlayerControl sender) =>
            sender != null && AmongUsClient.Instance != null
            && sender.OwnerId == AmongUsClient.Instance.HostId;

        public static bool Prefix(byte callId, MessageReader reader, PlayerControl __instance) {
            if (callId == Chance.RpcId) {
                if (!IsFromHost(__instance)) {
                    ChancePlugin.Logger?.LogWarning($"[Chance] Rejected host-only RPC {callId} (SetValues) from non-host sender {__instance?.PlayerId.ToString() ?? "?"} (owner {__instance?.OwnerId.ToString() ?? "?"}).");
                    return false;  // RPC konsumieren, damit TORs switch die ID nicht sieht
                }
                try {
                    byte pid = reader.ReadByte();
                    Chance.ChanceRoll roll = new Chance.ChanceRoll();
                    roll.speed          = reader.ReadSingle();
                    roll.cooldown       = reader.ReadSingle();
                    roll.vision         = reader.ReadSingle();
                    roll.tasks          = reader.ReadByte();
                    roll.sabotageCd     = reader.ReadSingle();
                    roll.killDistance   = reader.ReadSingle();
                    roll.voteMultiplier = reader.ReadByte();
                    roll.ventAccess     = reader.ReadByte() != 0;
                    Chance.applyValues(pid, roll);
                } catch { }
                return false;  // callId 200 is a Chance RPC
            }

            if (callId == Chance.ChaosRpcId) {
                if (!IsFromHost(__instance)) {
                    ChancePlugin.Logger?.LogWarning($"[Chance] Rejected host-only RPC {callId} (ChaosReassign) from non-host sender {__instance?.PlayerId.ToString() ?? "?"} (owner {__instance?.OwnerId.ToString() ?? "?"}).");
                    return false;
                }
                try {
                    byte pid = reader.ReadByte();
                    byte rid = reader.ReadByte();
                    ChancePlugin.Logger?.LogInfo($"[Chaos] RPC received: player {pid} -> role {rid}");
                    ChaosMode.ApplyChaosReassign(pid, rid);
                } catch (Exception e) {
                    ChancePlugin.Logger?.LogError($"[Chaos] RPC apply failed: {e}");
                }
                return false;
            }

            if (callId == Chance.ChaosModifierClearRpcId) {
                if (!IsFromHost(__instance)) {
                    ChancePlugin.Logger?.LogWarning($"[Chance] Rejected host-only RPC {callId} (ChaosModifierClear) from non-host sender {__instance?.PlayerId.ToString() ?? "?"} (owner {__instance?.OwnerId.ToString() ?? "?"}).");
                    return false;
                }
                try {
                    byte pid = reader.ReadByte();
                    ChaosMode.ApplyChaosModifierClear(pid);
                } catch (Exception e) {
                    ChancePlugin.Logger?.LogError($"[Chaos] modifier-clear RPC apply failed: {e}");
                }
                return false;
            }

            if (callId == Chance.ActivationRpcId) {
                if (!IsFromHost(__instance)) {
                    ChancePlugin.Logger?.LogWarning($"[Chance] Rejected host-only RPC {callId} (Activation) from non-host sender {__instance?.PlayerId.ToString() ?? "?"} (owner {__instance?.OwnerId.ToString() ?? "?"}).");
                    return false;
                }
                Chance.ReceiveActivation();
                return false;
            }

            if (callId == Chance.VersionHandshakeRpcId) {
                // Handshake — bewusst von allen Clients akzeptiert (keine Host-Prüfung).
                try { ChanceVersionHandshake.ReceiveRpc(reader); } catch { }
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkMuderAttempt))]
    static class ChanceMurderAttemptPatch {
        public static void Postfix(PlayerControl killer, PlayerControl target, ref MurderAttemptResult __result) {
            if (!Chance.killSuccessEnabled) return;
            if (__result != MurderAttemptResult.PerformKill) return;
            if (killer == null) return;
            if (!Chance.IsChancePlayer(killer.PlayerId)) return;

            if (rnd.NextDouble() * 100f >= Chance.killDeathChance) {
                __result = MurderAttemptResult.BlankKill;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 5: Modifier display in intro / role info
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(RoleInfo), nameof(RoleInfo.getRoleInfoForPlayer))]
    static class ChanceRoleInfoPatch {
        public static void Postfix(PlayerControl p, bool showModifier, List<RoleInfo> __result) {
            if (p != null && showModifier && Chance.isChance(p.PlayerId)) {
                __result.Add(new RoleInfo(
                    "Chance",
                    new Color32(255, 140, 0, byte.MaxValue),
                    "You are CHAOS!",
                    Chance.GetChanceShortDescription(p.PlayerId),
                    (RoleId)Chance.RoleIdValue,
                    isNeutral: false, isModifier: true));
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 6: Vision
    // ---------------------------------------------------------------------------
    // Priority.Last: this must run AFTER Unknown's Collection's vision pipeline (UCVision.cs), which
    // composes Scout/Beacon/Poltergeist/Werewolf in a defined order. Our contribution is purely
    // multiplicative, so being last is both safe and correct - it scales whatever situation the player
    // is actually in. UC is a separate assembly, so the ordering is expressed through the priority
    // rather than a shared pipeline ("Option A", see M5_ENTSCHEIDUNG_ERWARTET.txt / AUDIT M-5).
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
    [HarmonyPriority(Priority.Last)]
    static class ChanceVisionPatch {
        public static void Postfix(ref float __result, ShipStatus __instance,
                                   [HarmonyArgument(0)] NetworkedPlayerInfo player) {
            if (!Chance.visionEnabled) return;
            if (player == null) return;
            if (!Chance.isChance(player.PlayerId)) return;
            if (!Chance.visionMod.TryGetValue(player.PlayerId, out float vis)) return;
            __result = __result * vis;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 7: Trim task count (host only, after task assignment)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
    static class ChanceTasksPatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            foreach (var p in Chance.chanceList) {
                if (p?.Data?.Tasks == null) continue;
                if (!Chance.tasksMod.TryGetValue(p.PlayerId, out byte target)) continue;
                if (target == Chance.NoTaskChange) continue; // task reduction disabled (delayed activation)
                if (p.Data.Tasks.Count <= target) continue;

                // Keep the first `target` already-assigned tasks and re-set the player's task list
                // through the canonical RpcSetTasks path. This rebuilds Data.Tasks AND myTasks on
                // every client, so the dropped tasks vanish from the HUD, the map, and can no longer
                // be completed — unlike a bare Data.Tasks.RemoveAt, which leaves the PlayerTask
                // GameObjects (and thus the HUD/map entries and usable consoles) in place.
                var keep = new List<byte>();
                for (int i = 0; i < p.Data.Tasks.Count && keep.Count < target; i++)
                    keep.Add((byte)p.Data.Tasks[i].TypeId);
                p.Data.RpcSetTasks(new Il2CppStructArray<byte>(keep.ToArray()));
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 8: Speed (Postfix on PlayerPhysics.FixedUpdate)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    [HarmonyPriority(Priority.Last)]
    static class ChanceSpeedPatch {
        public static void Postfix(PlayerPhysics __instance) {
            if (!Chance.speedEnabled) return;
            if (!__instance.AmOwner) return;
            if (__instance.body == null || __instance.myPlayer == null) return;
            var lp = PlayerControl.LocalPlayer;
            if (lp == null || lp.Data == null || lp.Data.IsDead) return;
            if (!Chance.isChance(lp.PlayerId)) return;
            if (!Chance.speedMod.TryGetValue(lp.PlayerId, out float speed)) return;
            if (AmongUsClient.Instance?.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (!__instance.myPlayer.CanMove) return;
            __instance.body.velocity *= speed;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 9: Override kill cooldown (Postfix → runs after the TOR prefix)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetKillTimer))]
    static class ChanceKillCooldownPatch {
        public static void Postfix(PlayerControl __instance) {
            if (!Chance.cooldownEnabled) return;
            if (!Chance.isChance(__instance.PlayerId)) return;
            // BountyHunter has its own kill-cooldown logic in TOR's SetKillTimer prefix: a reduced
            // cooldown (bountyKillCooldown, default 0) on a bounty kill and KillCooldown+punishmentTime
            // otherwise, each drawn with its OWN display max. Re-clamping to the Chance cd here and
            // re-issuing KillButton.SetCoolDown with a different max fought TORs values in the same frame
            // (visible "0 -> jumps up" and a hijacked reduced bounty cooldown). Let TOR own this role's
            // cooldown entirely; the Chance cooldown effect simply doesn't apply to a BountyHunter.
            if (BountyHunter.bountyHunter != null && __instance == BountyHunter.bountyHunter) return;
            if (!Chance.cooldownMod.TryGetValue(__instance.PlayerId, out float cd)) return;
            // Das Clampen des killTimer gilt für jede Chance-Instanz; das HUD-Kill-Button gehört
            // aber nur dem LOKALEN Spieler. SetKillTimer wird von TOR auch für fremde Spieler
            // aufgerufen — ohne AmOwner-Gate zeigte der eigene Button fremde Cooldowns an und
            // NRE'te vor HUD-Existenz (P1.3).
            __instance.killTimer = Mathf.Clamp(__instance.killTimer, 0f, cd);
            if (__instance.AmOwner && HudManager.Instance != null)
                HudManager.Instance.KillButton.SetCoolDown(__instance.killTimer, cd);
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 10: Vent access — grant vent usage to Chance players that rolled it
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(Helpers), nameof(Helpers.roleCanUseVents))]
    static class ChanceVentAccessPatch {
        public static void Postfix(PlayerControl player, ref bool __result) {
            if (!Chance.ventEnabled) return;
            if (player == null) return;
            if (!Chance.isChance(player.PlayerId)) return;
            if (Chance.ventAccessMod.TryGetValue(player.PlayerId, out bool canVent) && canVent)
                __result = true;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 10b: Vent button placement — for a Chance player who rolled vent access but whose role
    // also has ability buttons, TOR's ImpostorVentButton sits in the ability-button cluster and gets
    // overlapped, so it can't be pressed. TOR's CustomButton ability buttons sit at
    // UseButton + PositionOffset, picking offsets from CustomButton.ButtonPositions. Instead of a
    // fixed offset (which is role-agnostic and unnecessarily high for simple roles), we pick the first
    // free slot the local role's ACTIVE buttons don't occupy, and place the vent button there. So a
    // plain crewmate gets it right next to the use button, while a button-heavy role pushes it up to
    // the next free slot. Re-applied every frame because Among Us' AspectPosition can reset the
    // transform; z one unit forward keeps the collider clickable. Impostors keep their native layout.
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceVentButtonFrontPatch {
        // Candidate slots, ordered near/low → high. Mirrors TOR's CustomButton.ButtonPositions so the
        // vent button lands exactly on the grid the role buttons use.
        private static readonly Vector3[] CandidateSlots = {
            TheOtherRoles.Objects.CustomButton.ButtonPositions.lowerRowRight,   // (-2, -0.06)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.lowerRowCenter,  // (-3, -0.06)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.lowerRowLeft,    // (-4, -0.06)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.upperRowLeft,    // (-2,  1)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.upperRowCenter,  // (-1,  1)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.upperRowFarLeft, // (-3,  1)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.upperRowRight,   // ( 0,  1)
            TheOtherRoles.Objects.CustomButton.ButtonPositions.highRowRight,    // ( 0,  2.06)
        };
        // Above the entire cluster as a last resort when every candidate slot is taken.
        private static readonly Vector3 FallbackSlot =
            TheOtherRoles.Objects.CustomButton.ButtonPositions.highRowRight + new Vector3(0f, 0.6f, 0f);
        private const float SlotEps = 0.25f;

        // P2.2: Wiederverwendbare Liste statt einer Allokation pro Frame. Dieser Postfix läuft auf
        // dem Unity-Main-Thread (HudManager.Update), daher ist ein geteiltes statisches Feld sicher.
        private static readonly List<Vector3> occupied = new List<Vector3>();

        public static void Postfix(HudManager __instance) {
            if (!Chance.ventEnabled) return;
            if (__instance == null || __instance.ImpostorVentButton == null || __instance.UseButton == null) return;
            var lp = PlayerControl.LocalPlayer;
            if (lp == null || lp.Data == null) return;
            if (lp.Data.Role != null && lp.Data.Role.IsImpostor) return; // impostors vent natively
            if (!Chance.isChance(lp.PlayerId)) return;
            if (!Chance.ventAccessMod.TryGetValue(lp.PlayerId, out bool canVent) || !canVent) return;

            var vb = __instance.ImpostorVentButton;
            if (!vb.isActiveAndEnabled) return;

            // Collect the offsets of the role's currently-active ability buttons. Mirrored buttons sit
            // on the far right (outside the left cluster) and never collide with our candidate slots.
            occupied.Clear();
            foreach (var b in TheOtherRoles.Objects.CustomButton.buttons) {
                if (b == null || b.mirror) continue;
                if (b.actionButtonGameObject == null || !b.actionButtonGameObject.activeSelf) continue;
                occupied.Add(b.PositionOffset);
            }

            // First candidate slot no active ability button occupies; fallback above everything.
            Vector3 chosen = FallbackSlot;
            foreach (var slot in CandidateSlots) {
                bool free = true;
                foreach (var o in occupied) {
                    if (Mathf.Abs(o.x - slot.x) < SlotEps && Mathf.Abs(o.y - slot.y) < SlotEps) { free = false; break; }
                }
                if (free) { chosen = slot; break; }
            }

            // Anchor to the use button in world space (independent of each button's transform parent);
            // z one unit forward keeps the collider clickable even if anything else lines up.
            Vector3 u = __instance.UseButton.transform.position;
            vb.transform.position = new Vector3(u.x + chosen.x, u.y + chosen.y, u.z - 1f);
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 11: Kill distance — reselect the kill target using the Chance player's
    // own randomized radius. Mirrors TOR's setTarget, only the distance differs.
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(TheOtherRoles.Patches.PlayerControlFixedUpdatePatch),
                  nameof(TheOtherRoles.Patches.PlayerControlFixedUpdatePatch.setTarget))]
    static class ChanceKillDistancePatch {
        public static bool Prefix(ref PlayerControl __result,
                                  bool onlyCrewmates, bool targetPlayersInVents,
                                  List<PlayerControl> untargetablePlayers, PlayerControl targetingPlayer) {
            if (!Chance.killDistanceEnabled) return true;
            PlayerControl tp = targetingPlayer ?? PlayerControl.LocalPlayer;
            if (tp == null || tp.Data == null) return true;
            if (!Chance.isChance(tp.PlayerId)) return true;
            if (!Chance.killDistanceMod.TryGetValue(tp.PlayerId, out float dist)) return true;

            __result = null;
            if (!MapUtilities.CachedShipStatus) return false;
            if (tp.Data.IsDead) return false;

            PlayerControl result = null;
            float num = dist;
            Vector2 truePosition = tp.GetTruePosition();
            foreach (var playerInfo in GameData.Instance.AllPlayers.GetFastEnumerator()) {
                if (!playerInfo.Disconnected && playerInfo.PlayerId != tp.PlayerId && !playerInfo.IsDead
                    && (!onlyCrewmates || !playerInfo.Role.IsImpostor)) {
                    PlayerControl @object = playerInfo.Object;
                    // AUDIT-2026-08-16: manual loop instead of Any(x => x == @object) - the lambda closes
                    // over @object, which changes every iteration, so LINQ allocated a fresh closure per
                    // player checked here. ReferenceEquals keeps the same null-safe identity comparison
                    // Il2Cpp object refs need, without the per-iteration allocation.
                    if (untargetablePlayers != null) {
                        bool isUntargetable = false;
                        for (int i = 0; i < untargetablePlayers.Count; i++) {
                            if (ReferenceEquals(untargetablePlayers[i], @object)) { isUntargetable = true; break; }
                        }
                        if (isUntargetable) continue;
                    }
                    if (@object && (!@object.inVent || targetPlayersInVents)) {
                        Vector2 vector = @object.GetTruePosition() - truePosition;
                        float magnitude = vector.magnitude;
                        if (magnitude <= num && !PhysicsHelpers.AnyNonTriggersBetween(
                                truePosition, vector.normalized, magnitude, Constants.ShipAndObjectsMask)) {
                            result = @object;
                            num = magnitude;
                        }
                    }
                }
            }
            __result = result;
            return false;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 12: Sabotage cooldown — drive the (team-shared) sabotage timer to a Chance
    // impostor's rolled value, so the value acts as a real cooldown both DOWN (reduction)
    // and UP (extension).
    //
    // Vanilla validates a sabotage in SabotageSystemType.UpdateSystem against the shared
    // timer, and that check runs on the HOST. So changing only the local timer unlocks the
    // button on a remote client's UI but the host still rejects the early sabotage. We
    // therefore act in two places:
    //   • Local UI: each client uses its OWN local Chance impostor's rolled value, so the
    //     sabotage button reflects that player's cooldown on this client.
    //   • Host authority: uses the MAXIMUM rolled cooldown among alive Chance impostors so
    //     the host's UpdateSystem validation matches the longest cooldown.
    // The timer is team-shared (vanilla has no per-player sabotage cooldown), so the host
    // uses the maximum — consistent with that approximation.
    //
    // Reduction vs extension: this patch runs every frame in HudManager.Update. Clamping the
    // running timer UP every frame would re-raise it forever and it would never reach 0
    // (sabotage permanently locked). So we ONLY raise the timer at the start of a fresh
    // cooldown cycle, detected via an upward jump of the shared timer between frames (round
    // start, after a meeting, after a sabotage ends). Reduction stays a safe continuous
    // downward clamp. This avoids depending on any vanilla-internal reset value.
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceSabotageCooldownPatch {
        public static void Postfix() {
            if (!Chance.sabotageEnabled) return;
            var ship = ShipStatus.Instance;
            if (ship == null) return;
            try {
                if (!ship.Systems.TryGetValue(SystemTypes.Sabotage, out var sys)) return;
                var sab = sys.TryCast<SabotageSystemType>();
                if (sab == null) return;

                float? targetCd = null;

                var lp = PlayerControl.LocalPlayer;
                if (lp != null && lp.Data != null && !lp.Data.IsDead && lp.Data.Role != null
                    && lp.Data.Role.IsImpostor && Chance.isChance(lp.PlayerId)
                    && Chance.sabotageCdMod.TryGetValue(lp.PlayerId, out float localCd)) {
                    targetCd = localCd;
                }

                if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost) {
                    float? maxCd = null;
                    foreach (var kv in Chance.sabotageCdMod) {
                        if (!Chance.isChance(kv.Key)) continue;
                        var p = Helpers.playerById(kv.Key);
                        if (p == null || p.Data == null || p.Data.IsDead
                            || p.Data.Role == null || !p.Data.Role.IsImpostor) continue;
                        if (maxCd == null || kv.Value > maxCd.Value) maxCd = kv.Value;
                    }
                    if (maxCd.HasValue) targetCd = maxCd; // host authority uses the maximum
                }

                float now = sab.Timer;
                bool freshCycle = Chance.lastSabTimer >= 0f
                    ? now > Chance.lastSabTimer + Chance.SabEdgeEps
                    : now > Chance.SabEdgeEps;
                if (targetCd.HasValue) {
                    if (now > targetCd.Value) sab.Timer = targetCd.Value;                    // reduction (continuous, safe)
                    else if (freshCycle && now < targetCd.Value) sab.Timer = targetCd.Value; // extension (only at cycle start)
                }
                Chance.lastSabTimer = sab.Timer; // remember post-value so the next upward jump is detected correctly
            } catch { }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 13: Vote multiplier — patched manually (see ChancePlugin.Load) because the
    // target is TOR's private nested CalculateVotes. Adds (multiplier − base) votes for
    // each Chance voter to the player they voted for; 0 removes their vote entirely.
    // ---------------------------------------------------------------------------
    static class ChanceVoteMultiplierPatch {
        public static void TryPatch(HarmonyLib.Harmony harmony) {
            try {
                // Look the type up directly in TOR's assembly rather than via AccessTools.TypeByName,
                // which scans every loaded assembly (including Il2Cppmscorlib) and logs noisy
                // ReflectionTypeLoadException warnings.
                var t = typeof(TheOtherRoles.TheOtherRoles).Assembly
                    .GetType("TheOtherRoles.Patches.MeetingHudPatch+MeetingCalculateVotesPatch");
                var method = t == null ? null : AccessTools.Method(t, "CalculateVotes");
                if (method == null) {
                    ChancePlugin.Logger?.LogWarning("[Chance] Vote multiplier: CalculateVotes not found, skipping patch.");
                    return;
                }
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ChanceVoteMultiplierPatch), nameof(Postfix)));
                ChancePlugin.Logger?.LogInfo("[Chance] Vote multiplier: CalculateVotes postfix patch applied.");
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chance] Vote multiplier patch failed: {e}");
            }
        }

        // Vote-Diagnose-Logging. Defaultet auf AUS (Release), bleibt aber ohne Rebuild über die
        // BepInEx-Config [Debug] VoteLogging umschaltbar (P0.5). Gebunden in ChancePlugin.Load().
        internal static bool DebugVotes => ChancePlugin.VoteLogging != null && ChancePlugin.VoteLogging.Value;

        // CalculateVotes is a STATIC method whose first parameter is named "__instance".
        // Harmony reserves "__instance" for the declaring-type instance (null on static methods),
        // so it would NOT map to that argument — we grab the MeetingHud via the positional injection
        // "__0", falling back to MeetingHud.Instance so a null "__0" can't silently disable the
        // multiplier (the actual cause of x0/x2/x3 not affecting the count).
        public static void Postfix(MeetingHud __0, ref Dictionary<byte, int> __result) {
            var hud = __0 ?? MeetingHud.Instance;
            bool active = Chance.IsActive();
            if (DebugVotes)
                ChancePlugin.Logger?.LogInfo($"[Chance] vote postfix: fired (hud={(hud != null)}, __0={(__0 != null)}, active={active}, result={(__result != null)}).");
            if (__result == null || hud == null || !active || !Chance.voteEnabled) return;
            var states = hud.playerStates;
            if (states == null) return;
            for (int i = 0; i < states.Length; i++) {
                var pva = states[i];
                if (pva == null) continue;
                byte votedFor = pva.VotedFor;
                if (votedFor == 252 || votedFor == 254 || votedFor == 255) continue; // skip / no-vote / dead
                byte voterId = (byte)pva.TargetPlayerId;
                if (!Chance.IsChancePlayer(voterId)) continue;
                if (!Chance.voteMultiplierMod.TryGetValue(voterId, out byte mult)) continue;

                int baseVotes = (Mayor.mayor != null && Mayor.mayor.PlayerId == voterId && Mayor.voteTwice) ? 2 : 1;
                int delta = mult - baseVotes;
                // P1.4: Das Display kann für einen Mayor mit Doppelstimme den Multiplikator x1 nicht
                // als eine Stimme rendern (TORs j---Doppelung zeichnet stets >=2 Icons). Statt das
                // Display zu verbiegen, zählt der Tally hier ebenfalls 2 — der Multiplikator senkt
                // nie unter die Mayor-Baseline. Damit gilt count == icons (display ist sonst Lügner).
                if (baseVotes == 2 && mult == 1) delta = 0;
                if (DebugVotes) {
                    int before = __result.TryGetValue(votedFor, out int c) ? c : 0;
                    ChancePlugin.Logger?.LogInfo($"[Chance] vote postfix: voter={voterId} votedFor={votedFor} mult={mult} base={baseVotes} delta={delta} before={before}.");
                }
                if (delta == 0) continue;
                if (__result.TryGetValue(votedFor, out int cur)) {
                    int next = Math.Max(0, cur + delta);
                    // Auf 0 reduzierte Einträge ENTFERNEN statt mit Wert 0 stehen zu lassen:
                    // TORs MaxPair startet bei int.MinValue, ein 0-Eintrag gewinnt also als
                    // "Maximum" ohne tie und der Spieler würde mit 0 Stimmen exiliert. Ohne
                    // Eintrag verhält es sich wie "niemand hat ihn gevotet". Sicher, weil der
                    // Count nur 0 erreicht, wenn ALLE Voter dieses Ziels x0 waren (jedes x0-Delta
                    // entfernt exakt die eigene Base-Stimme) — ein späteres positives Delta auf
                    // denselben Eintrag kann in dieser Schleife nicht mehr folgen.
                    if (next == 0) __result.Remove(votedFor);
                    else __result[votedFor] = next;
                }
                else if (delta > 0)
                    __result[votedFor] = delta;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 14: Vote multiplier DISPLAY — rewrite the VoterState[] that TOR's
    // PopulateResults prefix draws its vote icons from, so the icon count matches
    // the multiplied tally (x0 → no icon, x2/x3 → extra icons).
    //
    // Patched manually (see ChancePlugin.Load) onto TOR's own managed
    // MeetingHudPopulateVotesPatch.Prefix — the same technique as the CalculateVotes
    // count patch above. The previous attribute patch was a Priority.First prefix on
    // the IL2CPP method MeetingHud.PopulateResults and relied on (a) cross-instance
    // prefix ordering and (b) ref-arg propagation through the Il2CppInterop layer;
    // when TOR's prefix ran first it drew the icons from the original array and
    // returned false, so the rewrite was never seen and no extra icons appeared.
    // Wrapping TOR's renderer directly removes both failure points. If the nested
    // type isn't found (TOR refactor), falls back to the old PopulateResults prefix.
    //
    // Only Chance voters are touched; every other entry is copied unchanged. The
    // multiplier is applied relative to the Mayor double-vote baseline (same delta
    // as the count), so Mayor + Chance combos aren't double-counted.
    // ---------------------------------------------------------------------------
    static class ChanceVoteMultiplierDisplayPatch {
        public static void TryPatch(HarmonyLib.Harmony harmony) {
            try {
                var t = typeof(TheOtherRoles.TheOtherRoles).Assembly
                    .GetType("TheOtherRoles.Patches.MeetingHudPatch+MeetingHudPopulateVotesPatch");
                var method = t == null ? null : AccessTools.Method(t, "Prefix");
                if (method != null) {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ChanceVoteMultiplierDisplayPatch), nameof(Prefix)));
                    ChancePlugin.Logger?.LogInfo("[Chance] Vote display: TOR PopulateResults renderer wrapped.");
                    return;
                }
                ChancePlugin.Logger?.LogWarning("[Chance] Vote display: TOR renderer not found, falling back to MeetingHud.PopulateResults prefix.");
                harmony.Patch(
                    AccessTools.Method(typeof(MeetingHud), nameof(MeetingHud.PopulateResults)),
                    prefix: new HarmonyMethod(typeof(ChanceVoteMultiplierDisplayPatch), nameof(Prefix)) { priority = Priority.First });
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chance] Vote display patch failed: {e}");
            }
        }

        // Rewrites the `states` argument before TOR's renderer body runs — a plain
        // managed Harmony ref-argument edit, so TOR is guaranteed to draw from the
        // rebuilt array. The Mayor branch matches TOR's draw loop: TOR redoes the
        // Mayor's FIRST entry once (j--), so with `entries` duplicates the icon
        // total is entries + 1.
        static void Prefix(ref Il2CppStructArray<MeetingHud.VoterState> states) {
            try {
                if (states == null || !Chance.IsActive() || !Chance.voteEnabled) return;

                var rebuilt = new List<MeetingHud.VoterState>(states.Length);
                for (int i = 0; i < states.Length; i++) {
                    MeetingHud.VoterState vs = states[i];
                    byte votedFor = (byte)vs.VotedForId;
                    byte voterId = (byte)vs.VoterId;

                    // Default: copy the entry as-is (one icon). Skip codes TOR/Chance never multiply
                    // (no-vote / dead) — mirrors the count postfix exclusions.
                    int entries = 1;
                    bool multiplied = votedFor != 252 && votedFor != 254 && votedFor != 255;
                    if (multiplied && Chance.IsChancePlayer(voterId)
                        && Chance.voteMultiplierMod.TryGetValue(voterId, out byte mult)) {
                        int mayorBase = (Mayor.mayor != null && Mayor.mayor.PlayerId == voterId && Mayor.voteTwice) ? 2 : 1;
                        // TOR draws `mayorBase` icons from a single entry (the Mayor's j-- doubles it);
                        // each additional entry adds one icon, zero entries draws nothing.
                        if (mult <= 0) entries = 0;
                        else if (mayorBase == 2) entries = Math.Max(1, mult - 1); // mult==1 edge: shows 2 (tally says 1)
                        else entries = mult;
                    }

                    for (int k = 0; k < entries; k++) rebuilt.Add(vs);
                }

                var arr = new Il2CppStructArray<MeetingHud.VoterState>(rebuilt.Count);
                for (int i = 0; i < rebuilt.Count; i++) arr[i] = rebuilt[i];

                if (ChanceVoteMultiplierPatch.DebugVotes)
                    ChancePlugin.Logger?.LogInfo($"[Chance] vote display: fired, states {states.Length} → {rebuilt.Count}.");

                states = arr;
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chance] Vote multiplier display patch failed: {e}");
            }
        }
    }

}