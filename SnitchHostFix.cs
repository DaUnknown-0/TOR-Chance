// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using HarmonyLib;
using Hazel;
using TheOtherRoles;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TOR_ChanceModifier {

    // Fixes a TOR bug where an evil HOST never appears in the Snitch's reveal.
    //
    // TOR's StartMeeting prefix has every client broadcast its room via the ShareRoom RPC and then,
    // at the very end of the same prefix, resets Snitch.playerRoomMap to empty. Because the host
    // initiates the meeting, its prefix runs first, so the host's ShareRoom reaches the Snitch
    // BEFORE the Snitch's own prefix runs the reset — the host's entry is wiped. Non-host players
    // send slightly later (after receiving RpcStartMeeting), so their entries survive. The Chat-mode
    // reveal lists each evil player's role + room but skips anyone missing from playerRoomMap, so the
    // host is systematically left out. (Map mode uses live positions and is unaffected.)
    //
    // This postfix runs AFTER TOR's prefix (i.e. after the reset), re-sending the local player's room
    // so it lands in the freshly-reset map and arrives at the Snitch within its ~0.4s reveal window.
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
    static class SnitchHostRoomFixPatch {
        // TOR's CustomRPC.ShareRoom value (enum is internal to TOR; value is stable).
        // Same hardcoding approach as Chaos Mode's TorSetRoleRpcId (104 = SetRole).
        private const byte ShareRoomRpcId = 167;

        public static void Postfix() {
            if (Snitch.snitch == null) return;
            var roomTracker = FastDestroyableSingleton<HudManager>.Instance?.roomTracker;
            if (roomTracker == null) return;

            byte roomId = roomTracker.LastRoom != null ? (byte)roomTracker.LastRoom.RoomId : byte.MinValue;

            MessageWriter w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, ShareRoomRpcId, Hazel.SendOption.Reliable, -1);
            w.Write(PlayerControl.LocalPlayer.PlayerId);
            w.Write(roomId);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }
    }
}
