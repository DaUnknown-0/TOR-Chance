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
    [BepInPlugin(Id, "TOR Chance Modifier", "1.0.0")]
    [BepInDependency("me.eisbison.theotherroles")]
    [BepInProcess("Among Us.exe")]
    public class ChancePlugin : BasePlugin {
        public const string Id = "com.tormod.chancemodifier";

        internal static HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony(Id);

        public override void Load() {
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

            Harmony.PatchAll(typeof(ChancePlugin).Assembly);
        }
    }
}
