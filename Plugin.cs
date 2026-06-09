// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

global using Il2CppInterop.Runtime;
global using Il2CppInterop.Runtime.Attributes;
global using Il2CppInterop.Runtime.InteropTypes;
global using Il2CppInterop.Runtime.InteropTypes.Arrays;
global using Il2CppInterop.Runtime.Injection;

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TheOtherRoles;
using UnityEngine;
using Types = TheOtherRoles.CustomOption.CustomOptionType;

namespace TOR_ChanceModifier {
    [BepInPlugin(Id, "TOR - Unknown Chaos", VersionString)]
    [BepInDependency("me.eisbison.theotherroles")]
    [BepInProcess("Among Us.exe")]
    public class ChancePlugin : BasePlugin {
        public const string Id = "com.tormod.chancemodifier";
        public const string VersionString = "1.0.15";
        public static System.Version Version = System.Version.Parse(VersionString);

        public static BepInEx.Logging.ManualLogSource Logger;

        internal static HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony(Id);

        public override void Load() {
            Logger = Log;

            // Check if this mod is enabled.
            var enabled = Config.Bind("General", "Enabled", true, "Enable this mod");

            // Im Mod-Manager registrieren — auch wenn deaktiviert, damit der Mod dort sichtbar
            // bleibt und wieder aktiviert werden kann. RuntimeEnabled spiegelt den echten Ladezustand.
            try {
                var modData = new System.Collections.Generic.Dictionary<string, object> {
                    { "Guid", Id },
                    { "Name", "TOR - Unknown Chaos" },
                    { "Version", Version },
                    { "RepositoryOwner", "DaUnknown-0" },
                    { "RepositoryName", "TOR-Chance" },
                    { "ButtonColor", Color.yellow },
                    { "Enabled", enabled },
                    { "RuntimeEnabled", enabled.Value }
                };
                AppDomain.CurrentDomain.SetData($"ModManager.RegisteredMod.{Id}", modData);
                Logger.LogInfo($"Registered ChanceMod in Mod Manager registry (runtime={enabled.Value}).");
            } catch (System.Exception ex) {
                Logger.LogError($"Failed to register ChanceMod: {ex}");
            }

            // Early return wenn deaktiviert (Registrierung ist oben bereits erfolgt).
            if (!enabled.Value) {
                Logger.LogInfo("ChanceMod is disabled in config — skipping load.");
                return;
            }

            // Create options here – TOR has already called CustomOptionHolder.Load()
            string[] rates     = CustomOptionHolder.rates;
            string[] quantities = CustomOptionHolder.ratesModifier;

            ChanceOptions.modifierChance = CustomOption.Create(
                1110, Types.Modifier,
                CustomOptionHolder.cs(Color.yellow, "Chance"),
                rates, null, true);

            ChanceOptions.modifierChanceQuantity = CustomOption.Create(
                1111, Types.Modifier,
                CustomOptionHolder.cs(Color.yellow, "Chance Quantity"),
                quantities, ChanceOptions.modifierChance);

            ChanceOptions.modifierChanceSpeedMin = CustomOption.Create(
                1112, Types.Modifier, "Min Speed (V1)",
                0.5f, 0.25f, 3f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnSpeedMinChanged);

            ChanceOptions.modifierChanceSpeedMax = CustomOption.Create(
                1113, Types.Modifier, "Max Speed (V2)",
                2.5f, 0.25f, 3f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnSpeedMaxChanged);

            ChanceOptions.modifierChanceCooldownMin = CustomOption.Create(
                1114, Types.Modifier, "Min Kill Cooldown (V3)",
                5f, 2.5f, 60f, 2.5f, ChanceOptions.modifierChance, false, Chance.OnCooldownMinChanged);

            ChanceOptions.modifierChanceCooldownMax = CustomOption.Create(
                1115, Types.Modifier, "Max Kill Cooldown (V4)",
                60f, 2.5f, 60f, 2.5f, ChanceOptions.modifierChance, false, Chance.OnCooldownMaxChanged);

            ChanceOptions.modifierChanceTasksMin = CustomOption.Create(
                1116, Types.Modifier, "Min Tasks (V5)",
                1f, 1f, 10f, 1f, ChanceOptions.modifierChance, false, Chance.OnTasksMinChanged);

            ChanceOptions.modifierChanceTasksMax = CustomOption.Create(
                1117, Types.Modifier, "Max Tasks (V6)",
                10f, 1f, 10f, 1f, ChanceOptions.modifierChance, false, Chance.OnTasksMaxChanged);

            ChanceOptions.modifierChanceKillDeathChance = CustomOption.Create(
                1118, Types.Modifier, "Kill Success Chance % (V7)",
                30f, 0f, 100f, 5f, ChanceOptions.modifierChance);

            ChanceOptions.modifierChanceReportChance = CustomOption.Create(
                1126, Types.Modifier, "Auto-Report Chance % (per second)",
                10f, 0f, 100f, 5f, ChanceOptions.modifierChance);

            ChanceOptions.modifierChanceVisionMin = CustomOption.Create(
                1119, Types.Modifier, "Min Vision",
                0.25f, 0.25f, 5f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnVisionMinChanged);

            ChanceOptions.modifierChanceVisionMax = CustomOption.Create(
                1120, Types.Modifier, "Max Vision",
                5f, 0.25f, 5f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnVisionMaxChanged);

            ChanceOptions.modifierChanceVentChance = CustomOption.Create(
                1129, Types.Modifier, "Vent Access Chance %",
                0f, 0f, 100f, 5f, ChanceOptions.modifierChance);

            ChanceOptions.modifierChanceVoteMultMin = CustomOption.Create(
                1130, Types.Modifier, "Min Vote Multiplier (V8)",
                1f, 0f, 3f, 1f, ChanceOptions.modifierChance, false, Chance.OnVoteMultMinChanged);

            ChanceOptions.modifierChanceVoteMultMax = CustomOption.Create(
                1131, Types.Modifier, "Max Vote Multiplier (V9)",
                1f, 0f, 3f, 1f, ChanceOptions.modifierChance, false, Chance.OnVoteMultMaxChanged);

            ChanceOptions.modifierChanceKillDistanceMin = CustomOption.Create(
                1132, Types.Modifier, "Min Kill Distance (V10)",
                1f, 0.5f, 2.5f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnKillDistanceMinChanged);

            ChanceOptions.modifierChanceKillDistanceMax = CustomOption.Create(
                1133, Types.Modifier, "Max Kill Distance (V11)",
                1.75f, 0.5f, 2.5f, 0.25f, ChanceOptions.modifierChance, false, Chance.OnKillDistanceMaxChanged);

            ChanceOptions.modifierChanceSabotageCdMin = CustomOption.Create(
                1134, Types.Modifier, "Min Sabotage Cooldown (Impostor)",
                5f, 0f, 60f, 5f, ChanceOptions.modifierChance, false, Chance.OnSabotageCdMinChanged);

            ChanceOptions.modifierChanceSabotageCdMax = CustomOption.Create(
                1135, Types.Modifier, "Max Sabotage Cooldown (Impostor)",
                30f, 0f, 60f, 5f, ChanceOptions.modifierChance, false, Chance.OnSabotageCdMaxChanged);

            ChanceOptions.modifierChanceActivationMode = new CustomOption(
                1121, Types.Modifier, "Activation Delay Mode",
                new string[] { "Immediate", "Delayed" }, "Delayed", ChanceOptions.modifierChance, false);

            // Tasks can only be trimmed at game start, so the task options are meaningless for a
            // delayed Chance. Re-parent them under the activation mode with invertedParent, which
            // shows a child only when the parent's selection is 0 ("Immediate").
            ChanceOptions.modifierChanceTasksMin.parent = ChanceOptions.modifierChanceActivationMode;
            ChanceOptions.modifierChanceTasksMin.invertedParent = true;
            ChanceOptions.modifierChanceTasksMax.parent = ChanceOptions.modifierChanceActivationMode;
            ChanceOptions.modifierChanceTasksMax.invertedParent = true;

            ChanceOptions.modifierChanceActivationUnit = new CustomOption(
                1122, Types.Modifier, "Activation Delay Unit",
                new string[] { "Meetings", "Seconds" }, "Meetings", ChanceOptions.modifierChanceActivationMode, false);

            ChanceOptions.modifierChanceActivationMeetings = CustomOption.Create(
                1123, Types.Modifier, "Activate After Meetings",
                1f, 0f, 10f, 1f, ChanceOptions.modifierChanceActivationUnit, false, null, "", true);

            ChanceOptions.modifierChanceActivationSeconds = CustomOption.Create(
                1124, Types.Modifier, "Activate After Seconds",
                30f, 0f, 600f, 5f, ChanceOptions.modifierChanceActivationUnit);

            ChanceOptions.chaosMode = new CustomOption(
                1125, Types.Modifier, "Chaos Mode (reroll roles each meeting)",
                new string[] { "Off", "On" }, "Off", null, true);

            ChanceOptions.chaosRolePool = new CustomOption(
                1127, Types.Modifier, "Chaos: Role Pool",
                new string[] { "All enabled roles", "Only roles already in play" }, "All enabled roles",
                ChanceOptions.chaosMode, false);

            ChanceOptions.chaosScope = new CustomOption(
                1128, Types.Modifier, "Chaos: Affected Players",
                new string[] { "All players", "Only Chance players" }, "All players",
                ChanceOptions.chaosMode, false);

            Harmony.PatchAll(typeof(ChancePlugin).Assembly);
            // Vote multiplier targets a private nested TOR method, so it is patched manually
            // (isolated from PatchAll so a lookup failure can't abort the other patches).
            ChanceVoteMultiplierPatch.TryPatch(Harmony);
            // Vote DISPLAY likewise wraps TOR's nested PopulateVotes prefix directly, so the
            // rewritten VoterState[] is guaranteed to reach the icon renderer.
            ChanceVoteMultiplierDisplayPatch.TryPatch(Harmony);

            AddComponent<ChanceModUpdater>();
        }
    }

    // Show the Chance Modifier version directly below the "TheOtherRoles vX" line in the
    // top corner version display. Runs after TOR's own PingTracker postfix.
    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    [HarmonyPriority(Priority.Low)]
    static class ChanceVersionDisplayPatch {
        // Credit toggle is shared across all three of our mods via a process-wide AppDomain flag
        // (no cross-assembly references) — clicking any mod name flips the same flag, so clicking
        // another hides it again. Keep this key string identical in the other mods.
        private const string CreditKey = "TORMods.DaUnknownCreditVisible";

        private static bool CreditVisible() =>
            System.AppDomain.CurrentDomain.GetData(CreditKey) is bool b && b;

        public static void Postfix(PingTracker __instance) {
            if (__instance == null || __instance.text == null) return;
            string text = __instance.text.text;
            if (string.IsNullOrEmpty(text)) return;

            // Click the mod name to toggle the shared credit line. PingTracker.text is a world-space
            // TextMeshPro (no canvas), so the link raycast needs the rendering camera.
            if (Input.GetMouseButtonDown(0)) {
                Camera cam = Camera.main;
                var canvas = __instance.text.canvas;
                if (canvas != null)
                    cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                        : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);
                int link = TMPro.TMP_TextUtilities.FindIntersectingLink(__instance.text, Input.mousePosition, cam);
                if (link != -1 && __instance.text.textInfo.linkInfo[link].GetLinkID() == "chanceCredits")
                    System.AppDomain.CurrentDomain.SetData(CreditKey, !CreditVisible());
            }

            // Clickable mod name, inserted just below the "TheOtherRoles vX" line.
            string chanceLine = $"<link=\"chanceCredits\"><color=#FF8C00>TOR - Unknown Chaos</color> v{ChancePlugin.Version}</link>";
            int nl = text.IndexOf('\n');
            text = nl >= 0
                ? text.Substring(0, nl + 1) + chanceLine + "\n" + text.Substring(nl + 1)
                : text + "\n" + chanceLine;

            // Insert the shared credit under TOR's "Design by Bavari" line — but only if no other
            // mod already added it this frame, so "Modded by DaUnknown" appears at most once.
            if (CreditVisible() && !text.Contains("DaUnknown")) {
                string credit = "\n<size=70%>Modded by <color=#FCCE03FF>DaUnknown</color></size>";
                int anchor = text.IndexOf("Bavari");
                if (anchor >= 0) {
                    int lineEnd = text.IndexOf('\n', anchor);
                    text = lineEnd >= 0
                        ? text.Substring(0, lineEnd) + credit + text.Substring(lineEnd)
                        : text + credit;
                } else {
                    text += credit;
                }
            }

            __instance.text.text = text;
        }
    }
}