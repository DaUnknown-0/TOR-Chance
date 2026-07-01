// TOR - Unknown Chaos (ChanceMod) - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * ChanceAssets - sound cues for the Chance modifier, synthesized offline by the shared AssetGen tool
 * in "TOR Mod\Assets". Headerless 2-channel signed 32-bit PCM LE @ 48 kHz (the same format the
 * TOR/UC raw loaders use). Resources use explicit LogicalNames "ChanceMod.Resources.*" (see .csproj).
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TOR_ChanceModifier {
    public static class ChanceAssets {
        private static readonly Dictionary<string, AudioClip> clips = new();

        // Slot-machine roll -> ding: the Chance modifier just activated for the local player.
        public static void PlayActivate(float volume = 0.75f) => Play("chance_activate", volume);
        // Scrambled arpeggio: the local player's role was just chaos-reassigned.
        public static void PlayChaos(float volume = 0.8f) => Play("chance_chaos", volume);

        private static void Play(string name, float volume) {
            try {
                var clip = GetClip(name);
                if (clip == null || SoundManager.Instance == null) return;
                SoundManager.Instance.PlaySound(clip, false, volume);
            } catch (Exception e) {
                ChancePlugin.Logger?.LogWarning($"[ChanceAssets] Play {name} failed: {e.Message}");
            }
        }

        private static AudioClip GetClip(string name) {
            if (clips.TryGetValue(name, out var cached) && cached != null) return cached;
            var clip = LoadRawClip($"ChanceMod.Resources.{name}.raw", name);
            clips[name] = clip;
            return clip;
        }

        // Raw (headerless) 2-channel signed 32-bit PCM (LE), 48 kHz.
        private static AudioClip LoadRawClip(string path, string clipName) {
            try {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using Stream stream = assembly.GetManifestResourceStream(path);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                _ = stream.Read(bytes, 0, (int)stream.Length);
                float[] samples = new float[bytes.Length / 4];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (float)BitConverter.ToInt32(bytes, i * 4) / int.MaxValue;
                AudioClip clip = AudioClip.Create(clipName, samples.Length / 2, 2, 48000, false);
                clip.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
                clip.SetData(samples, 0);
                return clip;
            } catch {
                return null;
            }
        }
    }
}
