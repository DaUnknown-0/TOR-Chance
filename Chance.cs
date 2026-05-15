// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using System.Collections.Generic;
using System.Linq;
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
        public static CustomOption modifierChanceVisionMin;
        public static CustomOption modifierChanceVisionMax;
        public static CustomOption modifierChanceActivationMode;
        public static CustomOption modifierChanceActivationUnit;
        public static CustomOption modifierChanceActivationMeetings;
        public static CustomOption modifierChanceActivationSeconds;
    }

    // ---------------------------------------------------------------------------
    // Data class + RPC helpers
    // ---------------------------------------------------------------------------
    public static class Chance {
        public const byte RpcId = 200;
        internal const byte ActivationRpcId = 250;
        public const int RoleIdValue = 58;   // Value after Shifter (57)

        public static List<PlayerControl> chanceList = new List<PlayerControl>();
        public static Dictionary<byte, float> speedMod    = new Dictionary<byte, float>();
        public static Dictionary<byte, float> cooldownMod = new Dictionary<byte, float>();
        public static Dictionary<byte, float> visionMod   = new Dictionary<byte, float>();
        public static Dictionary<byte, byte>  tasksMod    = new Dictionary<byte, byte>();

        public static float speedMin, speedMax;
        public static float cooldownMin, cooldownMax;
        public static int   tasksMin, tasksMax;
        public static float killDeathChance;
        public static float visionMin, visionMax;

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

        public static void clearAndReload() {
            chanceList   = new List<PlayerControl>();
            speedMod     = new Dictionary<byte, float>();
            cooldownMod  = new Dictionary<byte, float>();
            visionMod    = new Dictionary<byte, float>();
            tasksMod     = new Dictionary<byte, byte>();

            speedMin        = ChanceOptions.modifierChanceSpeedMin?.getFloat()        ?? 0.5f;
            speedMax        = ChanceOptions.modifierChanceSpeedMax?.getFloat()        ?? 2.5f;
            cooldownMin     = ChanceOptions.modifierChanceCooldownMin?.getFloat()     ?? 5f;
            cooldownMax     = ChanceOptions.modifierChanceCooldownMax?.getFloat()     ?? 60f;
            tasksMin        = (int)(ChanceOptions.modifierChanceTasksMin?.getFloat()  ?? 1f);
            tasksMax        = (int)(ChanceOptions.modifierChanceTasksMax?.getFloat()  ?? 10f);
            killDeathChance = ChanceOptions.modifierChanceKillDeathChance?.getFloat() ?? 30f;
            visionMin       = ChanceOptions.modifierChanceVisionMin?.getFloat()       ?? 0.25f;
            visionMax       = ChanceOptions.modifierChanceVisionMax?.getFloat()       ?? 5f;

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

            float origVisionMin = visionMin;
            float origVisionMax = visionMax;
            visionMin = Mathf.Min(origVisionMin, origVisionMax);
            visionMax = Mathf.Max(origVisionMin, origVisionMax);

            activationMode    = ChanceOptions.modifierChanceActivationMode?.getSelection()   ?? 0;
            activationUnit    = ChanceOptions.modifierChanceActivationUnit?.getSelection()   ?? 0;
            activationMeetings = (int)(ChanceOptions.modifierChanceActivationMeetings?.getFloat() ?? 0f);
            activationSeconds  = ChanceOptions.modifierChanceActivationSeconds?.getFloat()   ?? 0f;

            meetingsElapsed = 0;
            activationStartTime = -1f;
            meetingEndedThisMeeting = false;
            rangeSyncInProgress = false;
            activationSoundPlayed = false;
            isActive = false;
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

        // Sends RPC 200 from host → sets values on all clients
        // Format: playerId (byte), speed (float), cooldown (float), vision (float), tasks (byte)
        // Tasks are only re-rolled when randomizeTasks=true (initial assignment);
        // re-randomization between meetings keeps the original task count.
        public static void sendSetValues(PlayerControl player, System.Random rnd, bool randomizeTasks = true) {
            float orderedSpeedMin = Mathf.Min(speedMin, speedMax);
            float orderedSpeedMax = Mathf.Max(speedMin, speedMax);
            float orderedCooldownMin = Mathf.Min(cooldownMin, cooldownMax);
            float orderedCooldownMax = Mathf.Max(cooldownMin, cooldownMax);
            float orderedVisionMin = Mathf.Min(visionMin, visionMax);
            float orderedVisionMax = Mathf.Max(visionMin, visionMax);
            int orderedTasksMin = Math.Min(tasksMin, tasksMax);
            int orderedTasksMax = Math.Max(tasksMin, tasksMax);

            float speed    = orderedSpeedMin    + (float)rnd.NextDouble() * (orderedSpeedMax    - orderedSpeedMin);
            float cooldown = orderedCooldownMin + (float)rnd.NextDouble() * (orderedCooldownMax - orderedCooldownMin);
            float vision   = orderedVisionMin   + (float)rnd.NextDouble() * (orderedVisionMax   - orderedVisionMin);
            byte  tasks;
            if (!randomizeTasks && tasksMod.TryGetValue(player.PlayerId, out byte existingTasks)) {
                tasks = existingTasks;
            } else {
                tasks = (byte)rnd.Next(orderedTasksMin, orderedTasksMax + 1);
            }

            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, RpcId, Hazel.SendOption.Reliable, -1);
            w.Write(player.PlayerId);
            w.Write(speed);
            w.Write(cooldown);
            w.Write(vision);
            w.Write(tasks);
            AmongUsClient.Instance.FinishRpcImmediately(w);
            applyValues(player.PlayerId, speed, cooldown, vision, tasks);
        }

        public static void applyValues(byte id, float speed, float cooldown, float vision, byte tasks) {
            var p = Helpers.playerById(id);
            if (p != null && p.Data != null && !p.Data.Disconnected && !p.Data.IsDead && !chanceList.Any(x => x.PlayerId == id))
                chanceList.Add(p);
            speedMod[id]    = speed;
            cooldownMod[id] = cooldown;
            visionMod[id]   = vision;
            tasksMod[id]    = tasks;
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

            return $"Speed {speed:0.00}× | " +
                   $"Cooldown {cd:0.0}s | " +
                   $"Vision {vis:0.00}× | " +
                   $"Tasks {tasks}";
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
                    byte  pid      = reader.ReadByte();
                    float speed    = reader.ReadSingle();
                    float cooldown = reader.ReadSingle();
                    float vision   = reader.ReadSingle();
                    byte  tasks    = reader.ReadByte();
                    Chance.applyValues(pid, speed, cooldown, vision, tasks);
                } catch { }
                return false;  // callId 200 is a Chance RPC
            }

            if (callId == Chance.ActivationRpcId) {
                Chance.ReceiveActivation();
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
                while (p.Data.Tasks.Count > target)
                    p.Data.Tasks.RemoveAt(p.Data.Tasks.Count - 1);
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

}
