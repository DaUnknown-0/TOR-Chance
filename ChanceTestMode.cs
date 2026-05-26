// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace TOR_ChanceModifier {

    // Chance-specific verification helper (the dummy / test-game tooling lives in TOR Debug Unlock):
    //  • F4 toggles a read-only preview panel that rolls a sample stat set from the current option
    //    ranges (R rerolls).
    //  • In a running game as host the panel also offers "Apply to me (live)", which force-assigns
    //    the Chance modifier to the local player so the stats can be felt live (e.g. in a test game
    //    started via Debug Unlock).
    public class ChanceTestMode : MonoBehaviour {
        public ChanceTestMode(IntPtr ptr) : base(ptr) { }

        private const KeyCode TogglePanelKey = KeyCode.F4;

        private readonly System.Random rnd = new System.Random();
        private bool showPanel;
        private bool hasRoll;
        private Chance.ChanceRoll roll;

        private void Update() {
            if (Input.GetKeyDown(TogglePanelKey)) {
                showPanel = !showPanel;
                if (showPanel && !hasRoll) Reroll();
            }
            if (showPanel && Input.GetKeyDown(KeyCode.R)) Reroll();
        }

        [HideFromIl2Cpp]
        private void Reroll() {
            try {
                Chance.ReloadRanges();
                roll = Chance.RollValues(rnd, true);
                hasRoll = true;
            } catch (Exception e) {
                ChancePlugin.Logger?.LogError($"[Chance] Preview roll failed: {e}");
            }
        }

        [HideFromIl2Cpp]
        private static bool CanApplyLive() {
            return AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost &&
                   AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Started;
        }

        private void OnGUI() {
            if (!showPanel) return;

            var rect = new Rect(20f, 110f, 250f, 280f);
            GUI.Box(rect, "CHANCE PREVIEW");
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 24f, rect.height - 36f));

            if (hasRoll) {
                GUILayout.Label($"Speed      {roll.speed:0.00}x");
                GUILayout.Label($"Kill CD    {roll.cooldown:0.0}s");
                GUILayout.Label($"Vision     {roll.vision:0.00}x");
                GUILayout.Label(roll.tasks == Chance.NoTaskChange
                    ? "Tasks      (unchanged)"
                    : $"Tasks      {roll.tasks}");
                GUILayout.Label($"Votes      x{roll.voteMultiplier}");
                GUILayout.Label($"Vent       {(roll.ventAccess ? "YES" : "no")}");
                GUILayout.Label($"Kill dist  {roll.killDistance:0.00}");
                GUILayout.Label($"Sabo CD    {roll.sabotageCd:0.0}s (imp)");
            } else {
                GUILayout.Label("No roll yet.");
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Reroll [R]")) Reroll();
            if (CanApplyLive() && GUILayout.Button("Apply to me (live)")) {
                try { Chance.ForceAssignLocal(); }
                catch (Exception e) { ChancePlugin.Logger?.LogError($"[Chance] Apply-to-me failed: {e}"); }
            }
            if (GUILayout.Button("Close [F4]")) showPanel = false;

            GUILayout.EndArea();
        }
    }
}
