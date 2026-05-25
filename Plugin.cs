// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

global using Il2CppInterop.Runtime;
global using Il2CppInterop.Runtime.Attributes;
global using Il2CppInterop.Runtime.InteropTypes;
global using Il2CppInterop.Runtime.InteropTypes.Arrays;
global using Il2CppInterop.Runtime.Injection;

using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TheOtherRoles;
using UnityEngine;
using Types = TheOtherRoles.CustomOption.CustomOptionType;

namespace TOR_ChanceModifier {
    [BepInPlugin(Id, "TOR Chance Modifier", VersionString)]
    [BepInDependency("me.eisbison.theotherroles")]
    [BepInProcess("Among Us.exe")]
    public class ChancePlugin : BasePlugin {
        public const string Id = "com.tormod.chancemodifier";
        public const string VersionString = "1.0.0";
        public static System.Version Version = System.Version.Parse(VersionString);

        public static BepInEx.Logging.ManualLogSource Logger;

        internal static HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony(Id);

        public override void Load() {
            Logger = Log;
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
                1114, Types.Modifier, "Min Cooldown (V3)",
                5f, 2.5f, 60f, 2.5f, ChanceOptions.modifierChance, false, Chance.OnCooldownMinChanged);

            ChanceOptions.modifierChanceCooldownMax = CustomOption.Create(
                1115, Types.Modifier, "Max Cooldown (V4)",
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

            ChanceOptions.modifierChanceActivationMode = new CustomOption(
                1121, Types.Modifier, "Activation Delay Mode",
                new string[] { "Immediate", "Delayed" }, "Delayed", ChanceOptions.modifierChance, false);

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

            AddComponent<ChanceModUpdater>();
        }
    }

    // Show the Chance Modifier version directly below the "TheOtherRoles vX" line in the
    // top corner version display. Runs after TOR's own PingTracker postfix.
    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    [HarmonyPriority(Priority.Low)]
    static class ChanceVersionDisplayPatch {
        public static void Postfix(PingTracker __instance) {
            if (__instance == null || __instance.text == null) return;
            string text = __instance.text.text;
            if (string.IsNullOrEmpty(text)) return;
            string chanceLine = $"<color=#FF8C00>Chance Modifier</color> v{ChancePlugin.Version}";
            int nl = text.IndexOf('\n');
            __instance.text.text = nl >= 0
                ? text.Substring(0, nl + 1) + chanceLine + "\n" + text.Substring(nl + 1)
                : text + "\n" + chanceLine;
        }
    }
}
