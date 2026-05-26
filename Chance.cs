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
    }

    // ---------------------------------------------------------------------------
    // Data class + RPC helpers
    // ---------------------------------------------------------------------------
    public static class Chance {
        public const byte RpcId = 200;
        public const byte ChaosRpcId = 201;
        internal const byte ActivationRpcId = 250;
        internal const byte VersionHandshakeRpcId = 251;
        // Sentinel task value meaning "do not manage this player's task count" — used when task
        // reduction is disabled (delayed activation, where tasks are already assigned at game start).
        internal const byte NoTaskChange = byte.MaxValue;
        public const int RoleIdValue = 58;   // Value after Shifter (57)

        public static List<PlayerControl> chanceList = new List<PlayerControl>();
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

        private static int activationMode;
        private static int activationUnit;
        private static int activationMeetings;
        private static float activationSeconds;
        private static int meetingsElapsed;
        private static float activationStartTime;
        private static bool meetingEndedThisMeeting;
        private static bool rangeSyncInProgress;
        private static bool isActive;
        private static bool activationSoundPlayed;
        private static AudioClip activationSoundClip;
        private static float reportCheckTimer;

        public static void clearAndReload() {
            chanceList   = new List<PlayerControl>();
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
            activationSoundPlayed = false;
            isActive = false;
            reportCheckTimer = 0f;
        }

        // Loads every min/max range, chance % and activation setting from the options and applies
        // the min≤max ordering + task cap. No runtime/dictionary state is touched, so it is safe to
        // call outside a game (the preview panel uses it to roll sample values from live options).
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

            // Task reduction only works for immediate activation: a delayed Chance can't trim tasks
            // that were already assigned at game start. Disable the task feature entirely when delayed.
            tasksEnabled = activationMode == 0;
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
            return IsActive() && chanceList.Any(x => x.PlayerId == playerId);
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
            PlayActivationSound();
        }

        private static void PlayActivationSound() {
            if (activationSoundPlayed) return;
            activationSoundPlayed = true;
            if (!Constants.ShouldPlaySfx()) return;

            if (activationSoundClip == null) {
                activationSoundClip = BuildActivationSound();
            }

            if (activationSoundClip != null && SoundManager.Instance != null) {
                SoundManager.Instance.PlaySound(activationSoundClip, false, 0.55f);
            }
        }

        private static AudioClip BuildActivationSound() {
            const int sampleRate = 44100;
            const float duration = 0.58f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            float Hash(float value) {
                float x = Mathf.Sin(value * 127.1f + 311.7f) * 43758.5453f;
                return x - Mathf.Floor(x);
            }

            for (int i = 0; i < sampleCount; i++) {
                float time = i / (float)sampleRate;
                float progress = time / duration;

                float attack = Mathf.Clamp01(time / 0.02f);
                float release = Mathf.Clamp01((duration - time) / 0.14f);
                float envelope = attack * attack * (3f - 2f * attack) * release * release * (3f - 2f * release);

                float gate;
                if (time < 0.18f) {
                    gate = ((int)(time * 42f) % 3 == 0) ? 1f : 0.2f;
                } else if (time < 0.4f) {
                    gate = ((int)(time * 28f) % 2 == 0) ? 1f : 0.35f;
                } else {
                    gate = 1f;
                }

                float tone = Mathf.Sin(2f * Mathf.PI * (760f + 220f * Mathf.Sin(2f * Mathf.PI * 3.5f * time)) * time);
                tone += Mathf.Sin(2f * Mathf.PI * (1520f + 90f * Mathf.Sin(2f * Mathf.PI * 7.1f * time)) * time) * 0.55f;
                tone += Mathf.Sin(2f * Mathf.PI * (2280f + 140f * progress) * time) * 0.25f;

                float glitchBurstA = Mathf.Clamp01(1f - Mathf.Abs((time - 0.08f) / 0.06f));
                float glitchBurstB = Mathf.Clamp01(1f - Mathf.Abs((time - 0.31f) / 0.05f));
                float glitchBurstC = Mathf.Clamp01(1f - Mathf.Abs((time - 0.47f) / 0.07f));

                float noise = (Hash(time * 1000f) * 2f - 1f) * (glitchBurstA * 0.95f + glitchBurstB * 0.65f + glitchBurstC * 0.8f);
                float chirp = Mathf.Sin(2f * Mathf.PI * (900f + 2600f * progress * progress) * time) * 0.5f;

                float sample = (tone * 0.34f + chirp * 0.16f + noise * 0.14f) * gate * envelope;
                sample = Mathf.Clamp(sample, -1f, 1f);
                sample = Mathf.Round(sample * 64f) / 64f;
                samples[i] = sample * 0.85f;
            }

            AudioClip clip = AudioClip.Create("ChanceActivationGlitch", sampleCount, 1, sampleRate, false);
            clip.SetData(new Il2CppStructArray<float>(samples), 0);
            clip.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
            return clip;
        }

        public static void AssignChancePlayers() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (!HasChanceModifier()) return;
            CleanChancePlayers();
            if (chanceList.Count > 0) return;

            int rate = Mathf.Clamp((ChanceOptions.modifierChance?.getSelection() ?? 0) * 10, 0, 100); // 0..100 per slot
            // selection 0 corresponds to "1", hence +1
            int quantity = ChanceOptions.modifierChanceQuantity.getSelection() + 1;
            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(player => player != null && player.Data != null && !player.Data.IsDead)
                .OrderBy(_ => Guid.NewGuid())
                .ToList();
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

        // One randomized stat set for a Chance player (or a sample roll for the preview panel).
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

        // Rolls one stat set from the configured ranges. No side effects — used by both the live
        // assignment (sendSetValues) and the read-only preview panel. `playerId` is only consulted
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

        // Test mode: force-applies a fresh Chance roll to the local (host) player right now, so the
        // configured stats can be verified live. Activates Chance if needed. Requires the Chance
        // modifier to be enabled in the options (otherwise the effect patches stay gated off).
        public static void ForceAssignLocal() {
            if (!(AmongUsClient.Instance?.AmHost ?? false)) return;
            if (!HasChanceModifier()) {
                ChancePlugin.Logger?.LogWarning("[Chance] Test mode: enable the Chance modifier (rate > 0) for effects to apply.");
                return;
            }
            var lp = PlayerControl.LocalPlayer;
            if (lp == null || lp.Data == null || lp.Data.IsDead) return;

            if (!isActive) {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId, ActivationRpcId, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                ApplyActivationState();
            }
            sendSetValues(lp, rnd);
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
            if (p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && !chanceList.Any(x => x.PlayerId == id))
                chanceList.Add(p);
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
            if (!speedMod.TryGetValue(playerId, out float speed)
                || !cooldownMod.TryGetValue(playerId, out float cd)
                || !visionMod.TryGetValue(playerId, out float vis)
                || !tasksMod.TryGetValue(playerId, out byte tasks))
                return "You are CHAOS!";

            string description = $"Speed {speed:0.00}× | " +
                                 $"Kill CD {cd:0.0}s | " +
                                 $"Vision {vis:0.00}×";
            if (tasks != NoTaskChange) description += $" | Tasks {tasks}";
            if (voteMultiplierMod.TryGetValue(playerId, out byte votes)) description += $" | Votes ×{votes}";
            if (ventAccessMod.TryGetValue(playerId, out bool vent) && vent) description += " | Vent ✓";
            if (killDistanceMod.TryGetValue(playerId, out float kd)) description += $" | KillDist {kd:0.0}";
            return description;
        }

        // Auto-report: once per second, if a body is within report range of the local Chance
        // player, there is a `reportChance`% chance they panic-report it. Driven per client for
        // the local player (CmdReportDeadBody is a client command), so each player rolls their own.
        public static void UpdateAutoReport(float deltaTime) {
            if (!IsActive() || reportChance <= 0f) return;
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

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
    static class ChanceMeetingEndedPatch {
        public static void Postfix() {
            Chance.OnMeetingEnded();
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 4: Receive RPC (Prefix with high priority → before the TOR switch handler)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    [HarmonyPriority(Priority.High)]
    static class ChanceHandleRpcPatch {
        public static bool Prefix(byte callId, MessageReader reader) {
            if (callId == Chance.RpcId) {
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

            if (callId == Chance.ActivationRpcId) {
                Chance.ReceiveActivation();
                return false;
            }

            if (callId == Chance.VersionHandshakeRpcId) {
                try { ChanceVersionHandshake.ReceiveRpc(reader); } catch { }
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkMuderAttempt))]
    static class ChanceMurderAttemptPatch {
        public static void Postfix(PlayerControl killer, PlayerControl target, ref MurderAttemptResult __result) {
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
            if (showModifier && Chance.isChance(p.PlayerId)) {
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
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
    static class ChanceVisionPatch {
        public static void Postfix(ref float __result, ShipStatus __instance,
                                   [HarmonyArgument(0)] NetworkedPlayerInfo player) {
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
            if (!Chance.isChance(__instance.PlayerId)) return;
            if (!Chance.cooldownMod.TryGetValue(__instance.PlayerId, out float cd)) return;
            __instance.killTimer = Mathf.Clamp(__instance.killTimer, 0f, cd);
            FastDestroyableSingleton<HudManager>.Instance.KillButton.SetCoolDown(__instance.killTimer, cd);
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 10: Vent access — grant vent usage to Chance players that rolled it
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(Helpers), nameof(Helpers.roleCanUseVents))]
    static class ChanceVentAccessPatch {
        public static void Postfix(PlayerControl player, ref bool __result) {
            if (player == null) return;
            if (!Chance.isChance(player.PlayerId)) return;
            if (Chance.ventAccessMod.TryGetValue(player.PlayerId, out bool canVent) && canVent)
                __result = true;
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
                    if (untargetablePlayers != null && untargetablePlayers.Any(x => x == @object)) continue;
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
    // Patch 12: Sabotage cooldown — lower the (team-shared) sabotage timer so a Chance
    // impostor's rolled value acts as a real cooldown reduction.
    //
    // Vanilla validates a sabotage in SabotageSystemType.UpdateSystem against the shared
    // timer, and that check runs on the HOST. So clamping only the local timer unlocks the
    // button on a remote client's UI but the host still rejects the early sabotage. We
    // therefore clamp in two places:
    //   • Local UI: lower the locally-read timer for the local Chance impostor so the
    //     sabotage button unlocks on time on this client.
    //   • Host authority: lower the shared timer to the smallest rolled cooldown among
    //     alive Chance impostors so the host's UpdateSystem validation passes for them.
    // The timer is team-shared (vanilla has no per-player sabotage cooldown), so the host
    // clamp uses the minimum — consistent with that approximation.
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceSabotageCooldownPatch {
        public static void Postfix() {
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
                    foreach (var kv in Chance.sabotageCdMod) {
                        if (!Chance.isChance(kv.Key)) continue;
                        var p = Helpers.playerById(kv.Key);
                        if (p == null || p.Data == null || p.Data.IsDead
                            || p.Data.Role == null || !p.Data.Role.IsImpostor) continue;
                        if (targetCd == null || kv.Value < targetCd.Value) targetCd = kv.Value;
                    }
                }

                if (targetCd.HasValue && sab.Timer > targetCd.Value) sab.Timer = targetCd.Value;
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
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chance] Vote multiplier patch failed: {e}");
            }
        }

        public static void Postfix(MeetingHud __instance, ref Dictionary<byte, int> __result) {
            if (__result == null || __instance == null || !Chance.IsActive()) return;
            var states = __instance.playerStates;
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
                if (delta == 0) continue;
                if (__result.TryGetValue(votedFor, out int cur))
                    __result[votedFor] = Math.Max(0, cur + delta);
                else if (delta > 0)
                    __result[votedFor] = delta;
            }
        }
    }

}
