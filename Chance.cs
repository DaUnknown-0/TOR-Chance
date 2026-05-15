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
    // Optionen (eigene statische Felder statt Einträge in CustomOptionHolder)
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
    // Datenklasse + RPC-Helfer
    // ---------------------------------------------------------------------------
    public static class Chance {
        public const byte RpcId = 200;
        internal const byte ActivationRpcId = 250;
        public const int RoleIdValue = 58;   // Wert nach Shifter (57)

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

            activationMode    = ChanceOptions.modifierChanceActivationMode?.getSelection()   ?? 0;
            activationUnit    = ChanceOptions.modifierChanceActivationUnit?.getSelection()   ?? 0;
            activationMeetings = (int)(ChanceOptions.modifierChanceActivationMeetings?.getFloat() ?? 0f);
            activationSeconds  = ChanceOptions.modifierChanceActivationSeconds?.getFloat()   ?? 0f;

            meetingsElapsed = 0;
            activationStartTime = Time.realtimeSinceStartup;
            activationSoundPlayed = false;
            isActive = HasImmediateActivation();
        }

        private static bool HasChanceModifier() {
            return ChanceOptions.modifierChance != null && ChanceOptions.modifierChance.getSelection() > 0;
        }

        private static bool HasImmediateActivation() {
            if (!HasChanceModifier()) return false;
            if (activationMode == 0) return true;
            return GetActivationThreshold() <= 0f;
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

        public static void TryActivate() {
            if (IsActive() || !HasChanceModifier()) return;
            if (activationMode == 0 || GetActivationThreshold() <= 0f) {
                Activate();
                return;
            }

            if (activationUnit == 0) {
                if (meetingsElapsed < activationMeetings) return;
            }
            else {
                if (Time.realtimeSinceStartup - activationStartTime < activationSeconds) return;
            }

            Activate();
        }

        public static void ReceiveActivation() {
            ApplyActivationState();
        }

        public static void OnMeetingEnded() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            meetingsElapsed++;
            TryActivate();
        }

        private static void Activate() {
            if (IsActive() || !HasChanceModifier()) return;

            if (AmongUsClient.Instance?.AmHost == true) {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId, ActivationRpcId, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }

            ApplyActivationState();

            if (AmongUsClient.Instance?.AmHost == true) {
                AssignChancePlayers();
            }
        }

        private static void ApplyActivationState() {
            if (isActive) return;
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
                SoundManager.Instance.PlaySound(activationSoundClip, false, 0.9f);
            }
        }

        private static AudioClip BuildActivationSound() {
            const int sampleRate = 44100;
            const float duration = 0.42f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++) {
                float time = i / (float)sampleRate;
                float frequency = time < 0.14f ? 660f : (time < 0.28f ? 880f : 1320f);
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / duration));
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * time) * envelope * 0.28f;
            }

            AudioClip clip = AudioClip.Create("ChanceActivation", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
            return clip;
        }

        public static void AssignChancePlayers() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (!IsActive() || !HasChanceModifier()) return;
            if (ChanceOptions.modifierChance == null || ChanceOptions.modifierChance.getSelection() == 0) return;
            if (chanceList.Count > 0) return;

            int quantity = ChanceOptions.modifierChanceQuantity.getSelection() + 1;
            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(player => player != null && player.Data != null && !player.Data.IsDead)
                .OrderBy(_ => Guid.NewGuid())
                .ToList();
            int toAssign = Math.Min(quantity, players.Count);

            for (int i = 0; i < toAssign; i++) {
                sendSetValues(players[i], rnd);
            }
        }

        public static void RandomizeChancePlayers() {
            if (!AmongUsClient.Instance?.AmHost ?? true) return;
            if (!IsActive() || !HasChanceModifier()) return;

            foreach (var p in chanceList.ToList()) {
                if (p != null && p.Data != null && !p.Data.IsDead) {
                    sendSetValues(p, rnd);
                }
            }
        }

        // Sendet RPC 200 vom Host → setzt Werte auf allen Clients
        // Format: playerId (byte), speed (float), cooldown (float), vision (float), tasks (byte)
        public static void sendSetValues(PlayerControl player, System.Random rnd) {
            float speed    = speedMin    + (float)rnd.NextDouble() * (speedMax    - speedMin);
            float cooldown = cooldownMin + (float)rnd.NextDouble() * (cooldownMax - cooldownMin);
            float vision   = visionMin   + (float)rnd.NextDouble() * (visionMax   - visionMin);
            byte  tasks    = (byte)rnd.Next(tasksMin, tasksMax + 1);

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
            if (p != null && !chanceList.Any(x => x.PlayerId == id))
                chanceList.Add(p);
            speedMod[id]    = speed;
            cooldownMod[id] = cooldown;
            visionMod[id]   = vision;
            tasksMod[id]    = tasks;
        }

        public static bool isChance(byte playerId) =>
            IsChancePlayer(playerId);
    }

    // ---------------------------------------------------------------------------
    // Patch 1: clearAndReload einbinden
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(TheOtherRoles.TheOtherRoles), "clearAndReloadRoles")]
    static class ChanceClearAndReloadPatch {
        public static void Postfix() => Chance.clearAndReload();
    }

    // ---------------------------------------------------------------------------
    // Patch 3: Modifier zuweisen (nach dem bestehenden RoleManager.SelectRoles Patch)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    [HarmonyPriority(Priority.Low)]
    static class ChanceAssignPatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            Chance.TryActivate();
            Chance.AssignChancePlayers();
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class ChanceActivationTickPatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            Chance.TryActivate();
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
    static class ChanceMeetingEndedPatch {
        public static void Postfix() {
            Chance.OnMeetingEnded();
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 4: RPC empfangen (Prefix mit hoher Priorität → vor dem TOR Switch-Handler)
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
                return false;  // callId 200 ist ein Chance-RPC
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
            if (!Chance.IsChancePlayer(killer.PlayerId)) return;

            bool killSucceeds = rnd.NextDouble() * 100f < Chance.killDeathChance;
            if (!killSucceeds) {
                __result = MurderAttemptResult.BlankKill;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 5: Modifier-Anzeige im Intro / Rolleninfo
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(RoleInfo), nameof(RoleInfo.getRoleInfoForPlayer))]
    static class ChanceRoleInfoPatch {
        static RoleInfo chanceInfo;
        static RoleInfo getInfo() {
            if (chanceInfo == null)
                chanceInfo = new RoleInfo(
                    "Chance",
                    new Color32(255, 140, 0, byte.MaxValue),
                    "Alles an dir ist zufällig!",
                    "Alles ist zufällig!",
                    (RoleId)Chance.RoleIdValue,
                    isNeutral: false, isModifier: true);
            return chanceInfo;
        }

        public static void Postfix(PlayerControl p, bool showModifier, List<RoleInfo> __result) {
            if (showModifier && Chance.isChance(p.PlayerId))
                __result.Add(getInfo());
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 6: Sichtweite
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
    static class ChanceVisionPatch {
        public static void Postfix(ref float __result, ShipStatus __instance,
                                   [HarmonyArgument(0)] NetworkedPlayerInfo player) {
            if (player == null) return;
            if (!Chance.isChance(player.PlayerId)) return;
            if (!Chance.visionMod.TryGetValue(player.PlayerId, out float vis)) return;
            __result = __instance.MaxLightRadius * vis;
        }
    }

    // ---------------------------------------------------------------------------
    // Patch 7: Aufgabenanzahl kürzen (nur Host, nach Task-Zuweisung)
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
    // Patch 8: Geschwindigkeit (Postfix auf PlayerPhysics.FixedUpdate)
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    static class ChanceSpeedPatch {
        public static void Postfix(PlayerPhysics __instance) {
            if (!__instance.AmOwner) return;
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
    // Patch 9: Kill-Cooldown überschreiben (Postfix → läuft nach dem TOR-Prefix)
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
    // Patch 11: Re-Randomisierung nach Meeting
    // ---------------------------------------------------------------------------
    [HarmonyPatch(typeof(ExileController), nameof(ExileController.BeginForGameplay))]
    static class ChanceReRandomizePatch {
        public static void Postfix() {
            if (!AmongUsClient.Instance.AmHost) return;
            Chance.RandomizeChancePlayers();
        }
    }
}
