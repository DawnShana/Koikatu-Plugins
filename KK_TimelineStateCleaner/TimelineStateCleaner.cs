using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace KKTimelineStateCleaner
{
    /// <summary>
    /// One-click cleaner for selected built-in Koikatu Timeline tracks.
    ///
    /// Integration policy:
    /// - No Harmony patching of Timeline.
    /// - No reflection into Timeline.
    /// - No access to Timeline private/internal members.
    /// - No modification/replacement of Timeline.dll.
    /// - No injection into Timeline's own UI.
    /// - Public Timeline API/types only.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(TimelinePluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class TimelineStateCleaner : BaseUnityPlugin
    {
        public const string PluginGuid = "com.agumon.kktimelinestatecleaner";
        public const string PluginName = "KK Timeline State Cleaner";
        public const string PluginVersion = "1.3.1";

        private const string TimelinePluginGuid = "com.joan6694.illusionplugins.timeline";
        private const string TimelineOwner = "Timeline";

        private static readonly HashSet<string> CameraIds = new HashSet<string>
        {
            "cameraOPos",
            "cameraORot",
            "cameraPos",
            "cameraRot",
            "cameraOZoom",
            "cameraFOV"
        };

        // KK clothes states requested by the user.
        // Gloves and Pantyhose are intentionally excluded.
        private static readonly HashSet<int> ClothesParameters = new HashSet<int>
        {
            (int)ChaFileDefine.ClothesKind.top,
            (int)ChaFileDefine.ClothesKind.bot,
            (int)ChaFileDefine.ClothesKind.bra,
            (int)ChaFileDefine.ClothesKind.shorts,
            (int)ChaFileDefine.ClothesKind.socks,
            (int)ChaFileDefine.ClothesKind.shoes_inner,
            (int)ChaFileDefine.ClothesKind.shoes_outer
        };

        private ConfigEntry<KeyCode> _hotkeyKey;
        private ConfigEntry<KeyCode> _windowToggleKey;
        private ConfigEntry<bool> _hotkeyRequireCtrl;
        private ConfigEntry<bool> _hotkeyRequireShift;
        private ConfigEntry<bool> _showWindowAtStartup;
        private ConfigEntry<bool> _showToast;

        private Rect _windowRect = new Rect(20f, 180f, 340f, 178f);
        private bool _windowVisible;
        private string _lastResult = "Ready - Ctrl + Shift + Backspace";
        private string _toastText = string.Empty;
        private float _toastUntil;

        // Undo point for tracks changed by this plugin in the current Timeline working set.
        // Only tracks that were enabled before cleaning are stored here. Tracks that were
        // already disabled are never changed by Restore.
        private readonly HashSet<Timeline.Interpolable> _undoEnabledTracks =
            new HashSet<Timeline.Interpolable>();

        // True after a successful clean pass that found target tracks.
        // The same main action/hotkey restores while this is true.
        private bool _cleanStateActive;
        private Timeline.Interpolable _undoSceneAnchorTrack;

        private void Awake()
        {
            // Do not use BepInEx KeyboardShortcut here. KeyboardShortcut intentionally requires
            // an exact modifier combination and distinguishes Left/Right Ctrl/Shift.
            // Manual modifier detection is more reliable for this simple global action.
            _hotkeyKey = Config.Bind(
                "Hotkey",
                "MainKey",
                KeyCode.Backspace,
                "Main key for the cleaner hotkey. Default: Backspace");

            _windowToggleKey = Config.Bind(
                "Hotkey",
                "ToggleWindowKey",
                KeyCode.F8,
                "Main key used with the same Ctrl/Shift modifiers to show or hide the Cleaner window. Default: F8");

            _hotkeyRequireCtrl = Config.Bind(
                "Hotkey",
                "RequireCtrl",
                true,
                "Require either LeftCtrl or RightCtrl.");

            _hotkeyRequireShift = Config.Bind(
                "Hotkey",
                "RequireShift",
                true,
                "Require either LeftShift or RightShift.");

            // Keep the old config key for compatibility, but treat it as startup visibility only.
            // Clicking X/Hide does not permanently alter this setting.
            _showWindowAtStartup = Config.Bind(
                "General",
                "ShowWindow",
                true,
                "Show the standalone Timeline State Cleaner window when CharaStudio starts.");

            _showToast = Config.Bind(
                "General",
                "ShowHotkeyToast",
                true,
                "Show a short on-screen result after a hotkey action.");

            _windowVisible = _showWindowAtStartup.Value;

            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Clean: Ctrl+Shift+" + _hotkeyKey.Value +
                "; Window: Ctrl+Shift+" + _windowToggleKey.Value);

            _lastResult = "Loaded - Ctrl + Shift + " + _hotkeyKey.Value;
        }

        private void Update()
        {
            if (IsModifiedKeyDown(_hotkeyKey))
            {
                Logger.LogMessage("Timeline Cleaner hotkey detected.");
                ToggleTargetTracks("Hotkey");
            }

            if (IsModifiedKeyDown(_windowToggleKey))
            {
                _windowVisible = !_windowVisible;
                Logger.LogMessage("Timeline Cleaner window " + (_windowVisible ? "shown." : "hidden."));
            }
        }

        private bool IsModifiedKeyDown(ConfigEntry<KeyCode> keyEntry)
        {
            if (keyEntry == null || keyEntry.Value == KeyCode.None)
                return false;

            if (!Input.GetKeyDown(keyEntry.Value))
                return false;

            bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (_hotkeyRequireCtrl != null && _hotkeyRequireCtrl.Value && !ctrlDown)
                return false;

            if (_hotkeyRequireShift != null && _hotkeyRequireShift.Value && !shiftDown)
                return false;

            return true;
        }

        private void OnGUI()
        {
            if (_windowVisible)
            {
                _windowRect = GUI.Window(
                    0x4B545343,
                    _windowRect,
                    DrawWindow,
                    "Timeline Cleaner v1.3.1");
            }

            if (_showToast != null && _showToast.Value && Time.realtimeSinceStartup < _toastUntil)
            {
                GUI.Box(new Rect(20f, 20f, 500f, 36f), _toastText);
            }
        }

        private void DrawWindow(int windowId)
        {
            // X button: hide only. The plugin stays active and Ctrl+Shift+F8 can show it again.
            if (GUI.Button(new Rect(312f, 2f, 24f, 20f), "X"))
            {
                HideWindow();
                return;
            }

            if (GUI.Button(new Rect(10f, 30f, 320f, 32f), "切换：清理 / 恢复"))
                ToggleTargetTracks("Button");

            if (GUI.Button(new Rect(10f, 70f, 320f, 32f), "隐藏窗口"))
            {
                HideWindow();
                return;
            }

            string nextAction = _cleanStateActive ? "恢复清理前状态" : "一键清理";
            GUI.Label(
                new Rect(10f, 108f, 320f, 20f),
                "下一次操作: " + nextAction + "    显示/隐藏: Ctrl + Shift + " + _windowToggleKey.Value);

            GUI.Label(new Rect(10f, 132f, 320f, 38f), _lastResult);
            GUI.DragWindow(new Rect(0f, 0f, 305f, 24f));
        }

        private void HideWindow()
        {
            _windowVisible = false;
            Logger.LogMessage("Timeline Cleaner window hidden. Use Ctrl+Shift+" + _windowToggleKey.Value + " to show it again.");
        }

        private void ToggleTargetTracks(string source)
        {
            if (!_cleanStateActive)
            {
                DisableTargetTracks(source);
                return;
            }

            RestoreResult restoreResult = RestoreTargetTracks(source);
            if (restoreResult == RestoreResult.Stale)
            {
                // Scene/Timeline content changed after the previous clean.
                // Treat this key press as a clean action for the current scene instead
                // of making the user press the hotkey twice.
                DisableTargetTracks(source);
            }
        }

        private enum RestoreResult
        {
            Restored,
            Stale,
            Failed
        }

        private void DisableTargetTracks(string source)
        {
            int scanned = 0;
            int matched = 0;
            int changed = 0;

            try
            {
                IEnumerable<Timeline.Interpolable> interpolables = Timeline.Timeline.GetAllInterpolables(false);

                if (interpolables == null)
                {
                    SetResult(source + ": Timeline 未返回轨道列表", true);
                    return;
                }

                List<Timeline.Interpolable> currentTracks = new List<Timeline.Interpolable>();
                List<Timeline.Interpolable> targetTracks = new List<Timeline.Interpolable>();

                foreach (Timeline.Interpolable interpolable in interpolables)
                {
                    if (interpolable == null)
                        continue;

                    currentTracks.Add(interpolable);
                    scanned++;

                    if (!IsTarget(interpolable))
                        continue;

                    targetTracks.Add(interpolable);
                    matched++;
                }

                // A new clean pass creates a fresh restore point.
                _undoEnabledTracks.Clear();
                _undoSceneAnchorTrack = currentTracks.Count > 0 ? currentTracks[0] : null;
                _cleanStateActive = false;

                foreach (Timeline.Interpolable interpolable in targetTracks)
                {
                    if (!interpolable.enabled)
                        continue;

                    _undoEnabledTracks.Add(interpolable);
                    interpolable.enabled = false;
                    changed++;
                }

                Timeline.Timeline.RefreshInterpolablesList();

                // Only enter "restore next" mode when the current scene actually contains
                // at least one target track. If there are no targets, the next press should
                // still try to clean rather than perform a meaningless restore.
                _cleanStateActive = matched > 0;

                SetResult(
                    string.Format(
                        "{0}: 已取消 {1} 条；目标 {2} 条；再次按快捷键{3}",
                        source,
                        changed,
                        matched,
                        _cleanStateActive ? "恢复" : "仍执行清理"),
                    false);
            }
            catch (Exception ex)
            {
                ClearRestoreState();
                SetResult(source + ": Timeline API 调用失败，Cleaner 未完成", true);
                Logger.LogError("Timeline State Cleaner failed while using Timeline public API.\\n" + ex);
            }
        }

        private RestoreResult RestoreTargetTracks(string source)
        {
            if (!_cleanStateActive)
                return RestoreResult.Failed;

            int restored = 0;
            int alreadyEnabled = 0;

            try
            {
                IEnumerable<Timeline.Interpolable> interpolables = Timeline.Timeline.GetAllInterpolables(false);

                if (interpolables == null)
                {
                    SetResult(source + ": Timeline 未返回轨道列表", true);
                    return RestoreResult.Failed;
                }

                HashSet<Timeline.Interpolable> currentTracks = new HashSet<Timeline.Interpolable>();
                foreach (Timeline.Interpolable interpolable in interpolables)
                {
                    if (interpolable != null)
                        currentTracks.Add(interpolable);
                }

                bool stale = false;

                if (_undoSceneAnchorTrack != null && !currentTracks.Contains(_undoSceneAnchorTrack))
                {
                    stale = true;
                }
                else if (_undoSceneAnchorTrack == null && _undoEnabledTracks.Count > 0)
                {
                    stale = true;
                    foreach (Timeline.Interpolable interpolable in _undoEnabledTracks)
                    {
                        if (currentTracks.Contains(interpolable))
                        {
                            stale = false;
                            break;
                        }
                    }
                }

                if (stale)
                {
                    Logger.LogMessage(
                        "Timeline Cleaner restore point belongs to an old Timeline set; switching this action to clean the current scene.");
                    ClearRestoreState();
                    return RestoreResult.Stale;
                }

                foreach (Timeline.Interpolable interpolable in _undoEnabledTracks)
                {
                    if (!currentTracks.Contains(interpolable))
                        continue;

                    if (interpolable.enabled)
                    {
                        alreadyEnabled++;
                        continue;
                    }

                    interpolable.enabled = true;
                    restored++;
                }

                Timeline.Timeline.RefreshInterpolablesList();
                ClearRestoreState();

                SetResult(
                    string.Format(
                        "{0}: 已恢复 {1} 条；已是原状态 {2} 条；再次按快捷键清理",
                        source,
                        restored,
                        alreadyEnabled),
                    false);

                return RestoreResult.Restored;
            }
            catch (Exception ex)
            {
                SetResult(source + ": 恢复失败，未继续修改 Timeline", true);
                Logger.LogError("Timeline State Cleaner failed while restoring Timeline public state.\\n" + ex);
                return RestoreResult.Failed;
            }
        }

        private void ClearRestoreState()
        {
            _undoEnabledTracks.Clear();
            _undoSceneAnchorTrack = null;
            _cleanStateActive = false;
        }

        private void SetResult(string text, bool isError)
        {
            _lastResult = text;
            _toastText = "Timeline Cleaner - " + text;
            _toastUntil = Time.realtimeSinceStartup + 3.0f;

            if (isError)
                Logger.LogError(text);
            else
                Logger.LogMessage(text);
        }

        private static bool IsTarget(Timeline.Interpolable interpolable)
        {
            if (interpolable == null)
                return false;

            // Never touch Timeline tracks registered by other plugins.
            if (!string.Equals(interpolable.owner, TimelineOwner, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrEmpty(interpolable.id) && CameraIds.Contains(interpolable.id))
                return true;

            if (!string.Equals(interpolable.id, "charClothes", StringComparison.Ordinal))
                return false;

            if (!(interpolable.parameter is int))
                return false;

            return ClothesParameters.Contains((int)interpolable.parameter);
        }
    }
}
