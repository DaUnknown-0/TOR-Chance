// TOR Chance Modifier - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * ChanceLobbyPasswordGate — lobby password gate for the ChanceMod.
 *
 * If UsefulTORStuff is loaded, that mod's gate takes full control — this gate shows no panel
 * and reads the unlock state from AppDomain. If UsefulTORStuff is NOT loaded, this gate fetches
 * the hash independently from the same file and shows its own panel.
 *
 * Once unlocked, the gate stays open for the entire game session (until the process exits).
 *
 * To change the password: update password_hash.txt in DaUnknown-0/Useful-TOR-stuff and push.
 */

using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TOR_ChanceModifier
{
    public class ChanceLobbyPasswordGate : MonoBehaviour
    {
        // Same file as UsefulTORStuff — one hash controls both mods.
        private const string HashFileUrl =
            "https://raw.githubusercontent.com/DaUnknown-0/Useful-TOR-stuff/main/password_hash.txt";

        // AppDomain keys set by UsefulTORStuff (identical strings).
        private const string AppKeyActive   = "LobbyPasswordGate.Active";
        private const string AppKeyUnlocked = "LobbyPasswordGate.Unlocked";

        private static bool UsefulStuffActive =>
            AppDomain.CurrentDomain.GetData(AppKeyActive) is bool b && b;
        private static bool UsefulStuffUnlocked =>
            AppDomain.CurrentDomain.GetData(AppKeyUnlocked) is bool b && b;

        private enum FetchState { Loading, Ready, Failed }

        public static ChanceLobbyPasswordGate Instance { get; private set; }

        private bool _unlocked;
        private FetchState _fetchState = FetchState.Loading;
        private string _fetchedHash;

        private GameObject _panel;
        private TextMeshProUGUI _hintLabel;
        private TextMeshProUGUI _maskedLabel;
        private TextMeshProUGUI _statusLabel;
        private string _inputBuffer = "";
        private float _errorClearTimer;

        public ChanceLobbyPasswordGate(IntPtr ptr) : base(ptr) { }

        public void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Only fetch independently when UsefulTORStuff is not handling the gate.
            if (!UsefulStuffActive)
                this.StartCoroutine(CoFetchHash());
        }

        public void Update()
        {
            if (UsefulStuffActive) return;

            // Left the lobby / returned to the menu: the GameStartManager.Update postfix that drives
            // this panel stops firing, so close it here and unfreeze. Unlock state is kept.
            if (_panel != null && _panel.activeSelf && GameStartManager.Instance == null)
            {
                HidePanel();
                return;
            }

            if (_panel == null || !_panel.activeSelf) return;

            // The overlay only blocks the mouse — freeze the bean so the host can't walk around or
            // interact with the lobby while the gate is locked.
            FreezeLocalPlayer();

            // Escape → leave lobby.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LeaveGame();
                return;
            }

            if (_fetchState != FetchState.Ready) return;

            if (_errorClearTimer > 0f)
            {
                _errorClearTimer -= Time.deltaTime;
                if (_errorClearTimer <= 0f && _statusLabel != null)
                    _statusLabel.text = "";
            }

            // Ctrl+V / Shift+Insert → paste from clipboard.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if ((ctrl && Input.GetKeyDown(KeyCode.V)) ||
                (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.Insert)))
            {
                string paste = GUIUtility.systemCopyBuffer ?? "";
                bool pasted = false;
                foreach (char c in paste)
                    if (!char.IsControl(c)) { _inputBuffer += c; pasted = true; }
                if (pasted && _maskedLabel != null)
                    _maskedLabel.text = new string('●', _inputBuffer.Length);
                return;
            }

            string typed = Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            bool bufferChanged = false;
            foreach (char c in typed)
            {
                if (c == '\b')
                {
                    if (_inputBuffer.Length > 0)
                    {
                        _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                        bufferChanged = true;
                    }
                }
                else if (c == '\n' || c == '\r')
                {
                    TryUnlock();
                    return;
                }
                else if (!char.IsControl(c))
                {
                    _inputBuffer += c;
                    bufferChanged = true;
                }
            }

            if (bufferChanged && _maskedLabel != null)
                _maskedLabel.text = new string('●', _inputBuffer.Length);
        }

        public void ShowPanel()
        {
            if (UsefulStuffActive) return;
            if (_panel != null) { _panel.SetActive(true); return; }
            BuildPanel();
        }

        public void HidePanel()
        {
            if (_panel != null) _panel.SetActive(false);
            // Restore movement whenever the gate is dismissed (unlocked, left, etc.).
            UnfreezeLocalPlayer();
        }

        // The overlay can't block keyboard movement, so we pin moveable=false each frame while the
        // panel is up (the same field TOR's Hacker/Trap/etc. use to freeze a player).
        private static void FreezeLocalPlayer()
        {
            try { if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = false; }
            catch { }
        }

        private static void UnfreezeLocalPlayer()
        {
            try { if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = true; }
            catch { }
        }

        private void LeaveGame()
        {
            try
            {
                HidePanel();
                AmongUsClient.Instance?.ExitGame(DisconnectReasons.ExitGame);
            }
            catch (Exception ex) { ChancePlugin.Logger?.LogError($"[ChanceLobbyPasswordGate] LeaveGame failed: {ex}"); }
        }

        private static bool IsUnlocked() =>
            UsefulStuffActive ? UsefulStuffUnlocked : (Instance != null && Instance._unlocked);

        // ── Fetch hash from GitHub ────────────────────────────────────────────────────────────

        [HideFromIl2Cpp]
        private IEnumerator CoFetchHash()
        {
            _fetchState = FetchState.Loading;
            ApplyFetchStateToPanel();

            var www = new UnityWebRequest();
            www.SetMethod(UnityWebRequest.UnityWebRequestMethod.Get);
            www.SetUrl(HashFileUrl);
            www.SetRequestHeader("User-Agent", $"TOR-ChanceModifier/{ChancePlugin.VersionString}");
            www.downloadHandler = new DownloadHandlerBuffer();
            var op = www.SendWebRequest();

            while (!op.isDone)
                yield return new WaitForEndOfFrame();

            if (www.isNetworkError || www.isHttpError)
            {
                ChancePlugin.Logger?.LogError(
                    $"[ChanceLobbyPasswordGate] Hash file unreachable ({www.error}).");
                www.downloadHandler.Dispose();
                www.Dispose();
                _fetchState = FetchState.Failed;
                ApplyFetchStateToPanel();
                yield break;
            }

            string raw = www.downloadHandler.text?.Trim() ?? "";
            www.downloadHandler.Dispose();
            www.Dispose();

            if (raw.Length == 64 && IsValidHex(raw))
            {
                _fetchedHash = raw.ToLowerInvariant();
                _fetchState = FetchState.Ready;
                ChancePlugin.Logger?.LogInfo("[ChanceLobbyPasswordGate] Hash loaded successfully.");
            }
            else
            {
                ChancePlugin.Logger?.LogError(
                    $"[ChanceLobbyPasswordGate] Invalid hash file (length {raw.Length}).");
                _fetchState = FetchState.Failed;
            }

            ApplyFetchStateToPanel();
        }

        private static bool IsValidHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        // ── Password check ────────────────────────────────────────────────────────────────────

        private void TryUnlock()
        {
            if (_fetchState != FetchState.Ready || _fetchedHash == null) return;
            try
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(_inputBuffer);
                byte[] hashBytes  = SHA256.Create().ComputeHash(inputBytes);
                string hex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                _inputBuffer = "";
                if (_maskedLabel != null) _maskedLabel.text = "";

                if (hex == _fetchedHash)
                {
                    _unlocked = true;
                    HidePanel();
                    ChancePlugin.Logger?.LogInfo("[ChanceLobbyPasswordGate] Unlocked.");
                }
                else
                {
                    ShowError("Wrong password.");
                    ChancePlugin.Logger?.LogInfo("[ChanceLobbyPasswordGate] Wrong password attempt.");
                }
            }
            catch (Exception ex)
            {
                ChancePlugin.Logger?.LogError($"[ChanceLobbyPasswordGate] Hash check failed: {ex}");
                _inputBuffer = "";
            }
        }

        private void ShowError(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
                _statusLabel.color = new Color(1f, 0.3f, 0.3f);
            }
            _errorClearTimer = 2f;
        }

        // ── Panel UI ──────────────────────────────────────────────────────────────────────────

        private void BuildPanel()
        {
            _panel = new GameObject("ChanceLobbyPasswordGatePanel");
            DontDestroyOnLoad(_panel);

            var canvas = _panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8000;

            var scaler = _panel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _panel.AddComponent<GraphicRaycaster>();

            // Full-screen overlay — blocks all mouse input from reaching the lobby behind it.
            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(_panel.transform, false);
            var overlayRect = overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            // Centered dialog box.
            var box = new GameObject("Box");
            box.transform.SetParent(_panel.transform, false);
            var boxRect = box.AddComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.pivot     = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(520, 310);
            box.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.14f, 0.98f);

            MakeLabel(box, "Title", new Vector2(0, -22), new Vector2(-20, 52),
                "LOBBY PASSWORD", 30, FontStyles.Bold, new Color(0.3f, 0.7f, 1f));

            _hintLabel = MakeLabel(box, "Hint", new Vector2(0, -82), new Vector2(-30, 30),
                "", 17, FontStyles.Normal, new Color(0.82f, 0.82f, 0.82f));

            var inputBox = new GameObject("InputBox");
            inputBox.transform.SetParent(box.transform, false);
            var inputBoxRect = inputBox.AddComponent<RectTransform>();
            inputBoxRect.anchorMin = new Vector2(0.08f, 1f);
            inputBoxRect.anchorMax = new Vector2(0.92f, 1f);
            inputBoxRect.pivot     = new Vector2(0.5f, 1f);
            inputBoxRect.anchoredPosition = new Vector2(0, -122);
            inputBoxRect.sizeDelta = new Vector2(0, 48);
            inputBox.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.22f);

            var maskedObj = new GameObject("MaskedText");
            maskedObj.transform.SetParent(inputBox.transform, false);
            var maskedRect = maskedObj.AddComponent<RectTransform>();
            maskedRect.anchorMin = Vector2.zero;
            maskedRect.anchorMax = Vector2.one;
            maskedRect.offsetMin = new Vector2(10, 0);
            maskedRect.offsetMax = new Vector2(-10, 0);
            _maskedLabel = maskedObj.AddComponent<TextMeshProUGUI>();
            _maskedLabel.text      = "";
            _maskedLabel.fontSize  = 28;
            _maskedLabel.alignment = TextAlignmentOptions.Center;
            _maskedLabel.color     = Color.white;

            var statusObj = new GameObject("Status");
            statusObj.transform.SetParent(box.transform, false);
            var statusRect = statusObj.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 1);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.pivot     = new Vector2(0.5f, 1);
            statusRect.anchoredPosition = new Vector2(0, -183);
            statusRect.sizeDelta = new Vector2(-20, 28);
            _statusLabel = statusObj.AddComponent<TextMeshProUGUI>();
            _statusLabel.text      = "";
            _statusLabel.fontSize  = 18;
            _statusLabel.alignment = TextAlignmentOptions.Center;
            _statusLabel.color     = new Color(1f, 0.3f, 0.3f);

            MakeLabel(box, "Footer", new Vector2(0, -240), new Vector2(-20, 24),
                "[Enter] confirm    [Backspace] delete    [Esc] leave lobby", 14, FontStyles.Normal,
                new Color(0.5f, 0.5f, 0.5f));

            _panel.SetActive(true);
            ApplyFetchStateToPanel();
        }

        private void ApplyFetchStateToPanel()
        {
            if (_panel == null) return;
            switch (_fetchState)
            {
                case FetchState.Loading:
                    if (_hintLabel != null) { _hintLabel.text = "Loading configuration..."; _hintLabel.color = new Color(0.9f, 0.9f, 0.4f); }
                    if (_maskedLabel != null) _maskedLabel.text = "";
                    if (_statusLabel != null) _statusLabel.text = "";
                    break;
                case FetchState.Failed:
                    if (_hintLabel != null) { _hintLabel.text = "Error: password_hash.txt not reachable."; _hintLabel.color = new Color(1f, 0.35f, 0.35f); }
                    if (_maskedLabel != null) _maskedLabel.text = "";
                    if (_statusLabel != null) { _statusLabel.text = "Game start permanently blocked."; _statusLabel.color = new Color(1f, 0.35f, 0.35f); }
                    break;
                case FetchState.Ready:
                    if (_hintLabel != null) { _hintLabel.text = "Enter password and confirm with Enter:"; _hintLabel.color = new Color(0.82f, 0.82f, 0.82f); }
                    if (_statusLabel != null) _statusLabel.text = "";
                    break;
            }
        }

        private static TextMeshProUGUI MakeLabel(GameObject parent, string name,
            Vector2 anchoredPos, Vector2 sizeDelta,
            string text, float fontSize, FontStyles style, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot     = new Vector2(0.5f, 1);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = color;
            return tmp;
        }

        // ── Harmony patches ───────────────────────────────────────────────────────────────────

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
        [HarmonyPriority(Priority.Low)]
        static class GameStartManagerUpdatePatch
        {
            public static void Postfix()
            {
                if (Instance == null) return;
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (UsefulStuffActive) return;

                if (!Instance._unlocked)
                    Instance.ShowPanel();
                else
                    Instance.HidePanel();
            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
        static class GameStartManagerBeginGamePatch
        {
            public static bool Prefix()
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return true;
                if (IsUnlocked()) return true;

                Instance?.ShowPanel();
                ChancePlugin.Logger?.LogInfo("[ChanceLobbyPasswordGate] Game start blocked — password required.");
                return false;
            }
        }
    }
}
