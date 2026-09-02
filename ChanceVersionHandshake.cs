// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using UnityEngine;

namespace TOR_ChanceModifier {

    // Mod-compatibility handshake, modelled on TOR's own VersionHandshake.
    // Every client with this mod broadcasts its Chance Modifier version + assembly GUID at lobby
    // time (RPC 251). The host then sees a lobby warning listing players who are missing the mod
    // or running a different/modified version — exactly the situation where Chance/Chaos RPCs
    // (200/201/250) would silently desync for those players.
    public static class ChanceVersionHandshake {
        public static readonly Dictionary<int, PlayerVersion> playerVersions = new Dictionary<int, PlayerVersion>();
        private static bool versionSent;

        public sealed class PlayerVersion {
            public readonly Version version;
            public readonly Guid guid;
            public PlayerVersion(Version version, Guid guid) { this.version = version; this.guid = guid; }
            public bool GuidMatches() =>
                Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.Equals(guid);
        }

        public static void ShareVersion() {
            if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
            var v = ChancePlugin.Version;

            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, Chance.VersionHandshakeRpcId, Hazel.SendOption.Reliable, -1);
            writer.Write((byte)v.Major);
            writer.Write((byte)v.Minor);
            writer.Write((byte)v.Build);
            writer.WritePacked(AmongUsClient.Instance.ClientId);
            writer.Write((byte)(v.Revision < 0 ? 0xFF : v.Revision));
            writer.Write(Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToByteArray());
            AmongUsClient.Instance.FinishRpcImmediately(writer);

            // Apply locally too (the sender never receives its own broadcast).
            Receive(v.Major, v.Minor, v.Build, v.Revision, Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId, AmongUsClient.Instance.ClientId);
        }

        // Reads the RPC 251 payload (called from ChanceHandleRpcPatch).
        public static void ReceiveRpc(MessageReader reader) {
            byte major = reader.ReadByte();
            byte minor = reader.ReadByte();
            byte build = reader.ReadByte();
            int clientId = reader.ReadPackedInt32();
            byte revision = 0xFF;
            Guid guid;
            if (reader.Length - reader.Position >= 17) {
                revision = reader.ReadByte();
                byte[] gbytes = reader.ReadBytes(16);
                guid = new Guid(gbytes);
            } else {
                guid = new Guid(new byte[16]);
            }
            Receive(major, minor, build, revision == 0xFF ? -1 : revision, guid, clientId);
        }

        private static void Receive(int major, int minor, int build, int revision, Guid guid, int clientId) {
            Version ver = revision < 0 ? new Version(major, minor, build) : new Version(major, minor, build, revision);
            playerVersions[clientId] = new PlayerVersion(ver, guid);
            snapshotDirty = true;
        }

        // PERF: the AppDomain snapshot (a dictionary, a string per player, a Split of the
        // registry) was rebuilt every lobby frame; the table it mirrors changes a handful of
        // times per lobby. Same gate Unknown's Collection already uses.
        private static bool snapshotDirty = true;

        // Builds the red warning text for any client that lacks this mod or has a different build.
        // Returns "" when every connected player matches.
        private static string BuildMismatchMessage() {
            string message = "";
            foreach (InnerNet.ClientData client in AmongUsClient.Instance.allClients.ToArray()) {
                if (client == null || client.Character == null) continue;
                string name = client.Character.Data.PlayerName;

                if (!playerVersions.TryGetValue(client.Id, out PlayerVersion pv)) {
                    message += $"<color=#FF0000FF>{name} is missing the Chance Modifier (or has a different version)\n</color>";
                    continue;
                }

                int diff = ChancePlugin.Version.CompareTo(pv.version);
                if (diff > 0)
                    message += $"<color=#FF0000FF>{name} has an older Chance Modifier (v{pv.version})\n</color>";
                else if (diff < 0)
                    message += $"<color=#FF0000FF>{name} has a newer Chance Modifier (v{pv.version})\n</color>";
                else if (!pv.GuidMatches())
                    message += $"<color=#FF0000FF>{name} has a modified Chance Modifier v{pv.version} <size=30%>({pv.guid})</size>\n</color>";
            }
            return message;
        }

        // --- F1: Cross-mod lobby handshake board (presentation-layer merge) ---
        // Documented AppDomain contract — plain strings / Dictionary<int,string> only (the two
        // assemblies must NOT need each other's types):
        //   TORMods.Handshake.Registry        → comma-separated guids that have published
        //   TORMods.Handshake.{guid}.name     → short display name (e.g. "Chance")
        //   TORMods.Handshake.{guid}.status   → Dictionary<int,string>: clientId → "code<0x1F>version"
        //                                       code ∈ ok | old | new | mod ; missing clients omitted
        // The combined per-player overview is rendered by exactly one owner: UsefulTORStuff when it
        // is loaded; otherwise this mod falls back to its own standalone block (unchanged for
        // single-mod installs). Chance always PUBLISHES, but only RENDERS when Useful is absent.
        private const string HandshakeRegistryKey = "TORMods.Handshake.Registry";
        private const string HandshakeKeyPrefix = "TORMods.Handshake.";
        private const string ChanceGuid = "com.tormod.chancemodifier";
        private const string UsefulGuid = "com.tormod.usefultorstuff";
        private const char StatusSep = '';

        private static void PublishSnapshot() {
            try {
                if (!snapshotDirty) return;
                if (AmongUsClient.Instance == null) return;
                snapshotDirty = false;
                var status = new Dictionary<int, string>();
                foreach (var kv in playerVersions) {
                    PlayerVersion pv = kv.Value;
                    string code;
                    int diff = ChancePlugin.Version.CompareTo(pv.version);
                    if (diff > 0) code = "old";
                    else if (diff < 0) code = "new";
                    else code = pv.GuidMatches() ? "ok" : "mod";
                    status[kv.Key] = code + StatusSep + pv.version;
                }
                AppDomain.CurrentDomain.SetData(HandshakeKeyPrefix + ChanceGuid + ".name", "Chance");
                AppDomain.CurrentDomain.SetData(HandshakeKeyPrefix + ChanceGuid + ".status", status);
                var reg = AppDomain.CurrentDomain.GetData(HandshakeRegistryKey) as string ?? "";
                if (!reg.Split(',').Contains(ChanceGuid))
                    AppDomain.CurrentDomain.SetData(HandshakeRegistryKey, reg == "" ? ChanceGuid : reg + "," + ChanceGuid);
            } catch { }
        }

        // True when UsefulTORStuff is loaded (it owns the combined overview when present).
        private static bool CombinedRendererPresent() =>
            AppDomain.CurrentDomain.GetData("ModManager.RegisteredMod." + UsefulGuid) != null;

        // P1.5: Beim Betreten einer Lobby den Versions-Cache leeren. ClientIds sind
        // verbindungsskopiert, sodass alte Einträge sonst nur leaken — das Dictionary soll aber
        // ausschließlich die aktuelle Lobby widerspiegeln (sonst zeigt die Host-Warnung evtl.
        // Spieler einer früheren Lobby an).
        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
        static class OnGameJoinedPatch {
            public static void Postfix() {
                playerVersions.Clear();
                snapshotDirty = true;
                versionSent = false;
            }
        }

        // Re-share whenever someone joins, so late joiners learn everyone's version (and vice versa).
        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
        static class OnPlayerJoinedPatch {
            public static void Postfix() {
                if (PlayerControl.LocalPlayer != null) ShareVersion();
            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
        static class GameStartManagerStartPatch {
            public static void Postfix() {
                versionSent = false;
            }
        }

        // Runs after TOR's own GameStartManager.Update postfix (Priority.Low) so we can append the
        // Chance warning to the GameStartText TOR rebuilds each frame, instead of fighting over it.
        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
        [HarmonyPriority(Priority.Low)]
        static class GameStartManagerUpdatePatch {
            public static void Postfix(GameStartManager __instance) {
                if (PlayerControl.LocalPlayer != null && !versionSent) {
                    versionSent = true;
                    ShareVersion();
                }

                if (AmongUsClient.Instance == null) return;
                // F1: immer den Snapshot veröffentlichen, damit ein vorhandener kombinierter
                // Renderer (UsefulTORStuff) die Chance-Spalte zeichnen kann.
                PublishSnapshot();

                if (!AmongUsClient.Instance.AmHost) return;
                if (__instance.startState == GameStartManager.StartingStates.Countdown) return;

                // F1: Ist UsefulTORStuff geladen, besitzt es die kombinierte Mod-Check-Übersicht —
                // dann KEIN eigenes Standalone-Block, sonst sähe der Host zwei getrennte Listen.
                if (CombinedRendererPresent()) return;

                string chanceMsg = BuildMismatchMessage();
                if (chanceMsg == "") return;

                var text = __instance.GameStartText;
                if (text == null) return;

                if (!__instance.GameStartTextParent.activeSelf || string.IsNullOrEmpty(text.text)) {
                    text.text = chanceMsg;
                    text.transform.localPosition = __instance.StartButton.transform.localPosition + Vector3.up * 5;
                    text.transform.localScale = new Vector3(2f, 2f, 1f);
                    __instance.GameStartTextParent.SetActive(true);
                } else {
                    text.text += "\n" + chanceMsg;
                }
            }
        }
    }
}
