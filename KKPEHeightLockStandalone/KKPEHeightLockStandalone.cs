using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HSPE.AMModules;
using UnityEngine;

namespace KKPEHeightLockStandalone
{
    public enum PreserveBodyMode
    {
        Off = 0,
        ShapeOnly = 1,
        AllBody = 2
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(KKPEPluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class KKPEHeightLockStandalonePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.kkpeheightlock.standalone";
        public const string PluginName = "KKPE Height & Body Lock Standalone";
        public const string PluginVersion = "1.2.3";
        public const string KKPEPluginGuid = "com.joan6694.kkplugins.kkpe";

        internal const string HeightBoneName = "cf_n_height";
        internal static KKPEHeightLockStandalonePlugin Instance;

        private ConfigEntry<bool> _heightLockEnabled;
        private ConfigEntry<PreserveBodyMode> _bodyMode;

        private ConfigEntry<KeyCode> _heightToggleKey;
        private ConfigEntry<KeyCode> _bodyModeKey;
        private ConfigEntry<KeyCode> _windowToggleKey;
        private ConfigEntry<bool> _requireCtrl;
        private ConfigEntry<bool> _requireShift;

        private ConfigEntry<bool> _showWindowAtStartup;
        private ConfigEntry<bool> _showToast;

        private Rect _windowRect = new Rect(20f, 180f, 410f, 290f);
        private bool _windowVisible;
        private bool _lastHeightSetting;

        private string _lastResult = "Ready";
        private string _toastText = string.Empty;
        private float _toastUntil;

        internal static bool HeightLockEnabled
        {
            get
            {
                return Instance != null &&
                       Instance._heightLockEnabled != null &&
                       Instance._heightLockEnabled.Value;
            }
        }

        internal static PreserveBodyMode BodyMode
        {
            get
            {
                return Instance != null && Instance._bodyMode != null
                    ? Instance._bodyMode.Value
                    : PreserveBodyMode.Off;
            }
        }

        private void Awake()
        {
            Instance = this;

            _heightLockEnabled = Config.Bind(
                "Lock",
                "HeightLockEnabled",
                true,
                "Lock the current cf_n_height scale. Turn off, adjust height, then turn on again to capture a new height.");

            _bodyMode = Config.Bind(
                "Lock",
                "BodyPreserveMode",
                PreserveBodyMode.ShapeOnly,
                "Body values preserved on the NEXT character replacement: Off / ShapeOnly / AllBody.");

            _heightToggleKey = Config.Bind(
                "Hotkey",
                "HeightToggleKey",
                KeyCode.H,
                "Main key used with Ctrl/Shift to toggle height lock.");

            _bodyModeKey = Config.Bind(
                "Hotkey",
                "BodyModeKey",
                KeyCode.B,
                "Main key used with Ctrl/Shift to cycle body preserve mode.");

            _windowToggleKey = Config.Bind(
                "Hotkey",
                "ToggleWindowKey",
                KeyCode.F9,
                "Main key used with Ctrl/Shift to show/hide this window.");

            _requireCtrl = Config.Bind(
                "Hotkey",
                "RequireCtrl",
                true,
                "Require LeftCtrl or RightCtrl.");

            _requireShift = Config.Bind(
                "Hotkey",
                "RequireShift",
                true,
                "Require LeftShift or RightShift.");

            _showWindowAtStartup = Config.Bind(
                "General",
                "ShowWindow",
                true,
                "Show the control window when CharaStudio starts.");

            _showToast = Config.Bind(
                "General",
                "ShowHotkeyToast",
                true,
                "Show a short on-screen result after an action.");

            if (!HeightLockPatch.Initialize())
            {
                _heightLockEnabled.Value = false;
                Logger.LogError("Height Lock initialization failed: KKPE _target field was not found.");
            }

            _lastHeightSetting = _heightLockEnabled.Value;
            _windowVisible = _showWindowAtStartup.Value;

            new Harmony(PluginGuid)
                .PatchAll(typeof(KKPEHeightLockStandalonePlugin).Assembly);

            _lastResult = BuildStateText();

            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Height=Ctrl+Shift+" + _heightToggleKey.Value +
                ", Body=Ctrl+Shift+" + _bodyModeKey.Value +
                ", Window=Ctrl+Shift+" + _windowToggleKey.Value);
        }

        private void Update()
        {
            // ConfigurationManager can change this config entry directly.
            if (_heightLockEnabled.Value != _lastHeightSetting)
            {
                if (!_heightLockEnabled.Value)
                    HeightLockPatch.ClearAll();

                _lastHeightSetting = _heightLockEnabled.Value;
                SetResult(
                    "Height Lock " +
                    (_heightLockEnabled.Value ? "ON" : "OFF"),
                    false);
            }

            if (IsModifiedKeyDown(_heightToggleKey))
                ToggleHeightLock("Hotkey");

            if (IsModifiedKeyDown(_bodyModeKey))
                CycleBodyMode("Hotkey");

            if (IsModifiedKeyDown(_windowToggleKey))
            {
                _windowVisible = !_windowVisible;
                SetResult(
                    "Window " +
                    (_windowVisible ? "shown" : "hidden"),
                    false);
            }
        }

        private bool IsModifiedKeyDown(ConfigEntry<KeyCode> keyEntry)
        {
            if (keyEntry == null ||
                keyEntry.Value == KeyCode.None ||
                !Input.GetKeyDown(keyEntry.Value))
            {
                return false;
            }

            bool ctrlDown =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            bool shiftDown =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);

            if (_requireCtrl.Value && !ctrlDown)
                return false;

            if (_requireShift.Value && !shiftDown)
                return false;

            return true;
        }

        private void ToggleHeightLock(string source)
        {
            bool next = !_heightLockEnabled.Value;

            if (!next)
                HeightLockPatch.ClearAll();

            _heightLockEnabled.Value = next;
            _lastHeightSetting = next;

            SetResult(
                source + ": Height Lock " +
                (next ? "ON - current height will be captured" : "OFF"),
                false);
        }

        private void CycleBodyMode(string source)
        {
            PreserveBodyMode next;

            if (_bodyMode.Value == PreserveBodyMode.Off)
                next = PreserveBodyMode.ShapeOnly;
            else if (_bodyMode.Value == PreserveBodyMode.ShapeOnly)
                next = PreserveBodyMode.AllBody;
            else
                next = PreserveBodyMode.Off;

            SetBodyMode(next, source);
        }

        private void SetBodyMode(PreserveBodyMode mode, string source)
        {
            _bodyMode.Value = mode;

            SetResult(
                source + ": Body Preserve " +
                GetBodyModeLabel(mode) +
                " (applies to next replacement)",
                false);
        }

        private void OnGUI()
        {
            if (_windowVisible)
            {
                _windowRect = GUI.Window(
                    0x4B50484C,
                    _windowRect,
                    DrawWindow,
                    "KKPE Height / Body Lock v1.2.3");
            }

            if (_showToast.Value &&
                Time.realtimeSinceStartup < _toastUntil)
            {
                GUI.Box(
                    new Rect(20f, 20f, 720f, 36f),
                    _toastText);
            }
        }

        private void DrawWindow(int windowId)
        {
            if (GUI.Button(new Rect(382f, 2f, 24f, 20f), "X"))
            {
                HideWindow();
                return;
            }

            string heightText =
                HeightLockEnabled
                    ? "身高锁定：开启（点击关闭）"
                    : "身高锁定：关闭（点击开启并捕获当前身高）";

            if (GUI.Button(
                new Rect(10f, 32f, 390f, 34f),
                heightText))
            {
                ToggleHeightLock("Button");
            }

            GUI.Label(
                new Rect(10f, 76f, 390f, 20f),
                "替换角色时保留体型（只影响后续替换）：");

            if (GUI.Button(
                new Rect(10f, 100f, 123f, 32f),
                BodyMode == PreserveBodyMode.Off
                    ? "● 关闭"
                    : "关闭"))
            {
                SetBodyMode(
                    PreserveBodyMode.Off,
                    "Button");
            }

            if (GUI.Button(
                new Rect(143f, 100f, 123f, 32f),
                BodyMode == PreserveBodyMode.ShapeOnly
                    ? "● 仅体型"
                    : "仅体型"))
            {
                SetBodyMode(
                    PreserveBodyMode.ShapeOnly,
                    "Button");
            }

            if (GUI.Button(
                new Rect(276f, 100f, 124f, 32f),
                BodyMode == PreserveBodyMode.AllBody
                    ? "● 体型+胸部"
                    : "体型+胸部"))
            {
                SetBodyMode(
                    PreserveBodyMode.AllBody,
                    "Button");
            }

            GUI.Label(
                new Rect(10f, 143f, 390f, 20f),
                "当前：" + BuildStateText());

            GUI.Label(
                new Rect(10f, 169f, 390f, 40f),
                "快捷键：Ctrl+Shift+" + _heightToggleKey.Value +
                " 身高；Ctrl+Shift+" + _bodyModeKey.Value +
                " 体型；Ctrl+Shift+" + _windowToggleKey.Value +
                " 窗口");

            GUI.Label(
                new Rect(10f, 211f, 390f, 36f),
                "提示：关闭体型保留不会回滚已经完成的替换。");

            GUI.Label(
                new Rect(10f, 244f, 390f, 20f),
                _lastResult);

            GUI.DragWindow(new Rect(0f, 0f, 375f, 24f));
        }

        private void HideWindow()
        {
            _windowVisible = false;

            SetResult(
                "Window hidden - Ctrl+Shift+" +
                _windowToggleKey.Value +
                " to show",
                false);
        }

        private string BuildStateText()
        {
            return "Height=" +
                   (HeightLockEnabled ? "ON" : "OFF") +
                   " | Body=" +
                   GetBodyModeLabel(BodyMode);
        }

        private static string GetBodyModeLabel(PreserveBodyMode mode)
        {
            if (mode == PreserveBodyMode.ShapeOnly)
                return "ShapeOnly";

            if (mode == PreserveBodyMode.AllBody)
                return "Shape+Bust";

            return "Off";
        }

        private void SetResult(string text, bool isError)
        {
            _lastResult = text;
            _toastText =
                "KKPE Height/Body Lock - " + text;
            _toastUntil =
                Time.realtimeSinceStartup + 3f;

            if (isError)
                Logger.LogError(text);
            else
                Logger.LogMessage(text);
        }

        internal static void ReportError(
            string text,
            Exception exception)
        {
            if (Instance == null)
                return;

            Instance.SetResult(text, true);

            if (exception != null)
                Instance.Logger.LogError(exception);
        }
    }

    /// <summary>
    /// Runtime height lock.
    ///
    /// This patch does not add/remove KKPE dirty entries.
    /// It captures cf_n_height once, then writes that scale back after
    /// KKPE ApplyBoneManualCorrection finishes.
    ///
    /// OFF clears the captured values. ON captures the current scale again.
    /// </summary>
    [HarmonyPatch(typeof(BonesEditor), "ApplyBoneManualCorrection")]
    internal static class HeightLockPatch
    {
        private sealed class HeightState
        {
            public Transform Bone;
            public Vector3 LockedScale;
        }

        private static FieldInfo _targetField;
        private static bool _ready;

        private static readonly Dictionary<Studio.OCIChar, HeightState> States =
            new Dictionary<Studio.OCIChar, HeightState>();

        internal static bool Initialize()
        {
            _targetField =
                AccessTools.Field(
                    typeof(BonesEditor),
                    "_target");

            _ready = _targetField != null;
            return _ready;
        }

        private static void Postfix(BonesEditor __instance)
        {
            if (!_ready ||
                !KKPEHeightLockStandalonePlugin.HeightLockEnabled)
            {
                return;
            }

            try
            {
                GenericOCITarget target =
                    _targetField.GetValue(__instance)
                    as GenericOCITarget;

                if (target == null ||
                    target.type != GenericOCITarget.Type.Character ||
                    target.ociChar == null)
                {
                    return;
                }

                HeightState state =
                    GetState(target.ociChar);

                if (state == null)
                    return;

                state.Bone.localScale =
                    state.LockedScale;
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError(
                    "Height Lock runtime error.",
                    ex);
            }
        }

        private static HeightState GetState(
            Studio.OCIChar character)
        {
            HeightState state;

            if (States.TryGetValue(
                character,
                out state))
            {
                if (state.Bone != null)
                    return state;

                States.Remove(character);
            }

            if (character.charInfo == null)
                return null;

            Transform bone =
                FindChildRecursive(
                    character.charInfo.transform,
                    KKPEHeightLockStandalonePlugin.HeightBoneName);

            if (bone == null)
                return null;

            state = new HeightState();
            state.Bone = bone;
            state.LockedScale = bone.localScale;

            States[character] = state;

            return state;
        }

        internal static void ClearAll()
        {
            States.Clear();
        }

        internal static void ClearForCharacter(
            Studio.OCIChar character)
        {
            if (character != null)
                States.Remove(character);
        }

        private static Transform FindChildRecursive(
            Transform parent,
            string name)
        {
            if (parent == null)
                return null;

            if (parent.name == name)
                return parent;

            for (int i = 0;
                 i < parent.childCount;
                 i++)
            {
                Transform result =
                    FindChildRecursive(
                        parent.GetChild(i),
                        name);

                if (result != null)
                    return result;
            }

            return null;
        }
    }

    /// <summary>
    /// Patch the outermost Studio character replacement methods.
    ///
    /// OCICharFemale.ChangeChara performs extra work after base.ChangeChara,
    /// so patching OCIChar.ChangeChara alone restores body values too early.
    /// </summary>
    [HarmonyPatch]
    internal static class BodyPreservePatch
    {
        private sealed class BodyState
        {
            public float[] ShapeValues;
            public float BustSoftness;
            public float BustWeight;
            public PreserveBodyMode Mode;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(Studio.OCICharFemale),
                "ChangeChara",
                new Type[] { typeof(string) });

            yield return AccessTools.Method(
                typeof(Studio.OCICharMale),
                "ChangeChara",
                new Type[] { typeof(string) });
        }

        private static void Prefix(
            Studio.OCIChar __instance,
            out BodyState __state)
        {
            __state = null;

            try
            {
                // The current character will be rebuilt, so its captured
                // height transform must be discarded regardless of body mode.
                HeightLockPatch.ClearForCharacter(
                    __instance);

                PreserveBodyMode mode =
                    KKPEHeightLockStandalonePlugin.BodyMode;

                if (mode == PreserveBodyMode.Off ||
                    __instance == null ||
                    __instance.charInfo == null)
                {
                    return;
                }

                ChaFileBody body =
                    __instance.charInfo.fileBody;

                if (body == null ||
                    body.shapeValueBody == null)
                {
                    return;
                }

                __state = new BodyState();
                __state.Mode = mode;
                __state.ShapeValues =
                    (float[])
                    body.shapeValueBody.Clone();

                if (mode == PreserveBodyMode.AllBody)
                {
                    __state.BustSoftness =
                        body.bustSoftness;

                    __state.BustWeight =
                        body.bustWeight;
                }
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError(
                    "Body Preserve snapshot error.",
                    ex);
            }
        }

        private static void Postfix(
            Studio.OCIChar __instance,
            BodyState __state)
        {
            try
            {
                if (__state != null &&
                    __instance != null &&
                    __instance.charInfo != null)
                {
                    ChaFileBody body =
                        __instance.charInfo.fileBody;

                    if (body != null)
                    {
                        body.shapeValueBody =
                            (float[])
                            __state.ShapeValues.Clone();

                        if (__state.Mode ==
                            PreserveBodyMode.AllBody)
                        {
                            body.bustSoftness =
                                __state.BustSoftness;

                            body.bustWeight =
                                __state.BustWeight;
                        }

                        // UpdateShapeBodyValueFromCustomInfo() updates sibBody
                        // and sets updateShapeBody=true. Apply UpdateShapeBody()
                        // immediately so cf_n_height and the other body bones
                        // already match the restored values before the late
                        // KKPE height-lock pass captures its next baseline.
                        __instance.charInfo
                            .UpdateShapeBodyValueFromCustomInfo();

                        __instance.charInfo
                            .UpdateShapeBody();

                        if (__state.Mode ==
                            PreserveBodyMode.AllBody)
                        {
                            __instance.charInfo
                                .UpdateBustSoftnessAndGravity();
                        }

                        // OCICharFemale.ChangeChara normally syncs these after
                        // base.ChangeChara. We restored shape after that point,
                        // so repeat the same public synchronization using the
                        // restored body values.
                        if (__instance is Studio.OCICharFemale)
                        {
                            __instance.optionItemCtrl.height =
                                body.shapeValueBody[0];

                            __instance.charInfo
                                .setAnimatorParamFloat(
                                    "height",
                                    body.shapeValueBody[0]);

                            if (__instance.isAnimeMotion)
                            {
                                __instance.charInfo
                                    .setAnimatorParamFloat(
                                        "breast",
                                        body.shapeValueBody[1]);
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError(
                    "Body Preserve restore error.",
                    ex);
            }
        }
    }
}
