using System;
using System.Collections;
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
        public const string PluginVersion = "1.2.1";
        public const string KKPEPluginGuid = "com.joan6694.kkplugins.kkpe";

        internal const string HeightBoneName = "cf_n_height";
        internal static KKPEHeightLockStandalonePlugin Instance;

        private ConfigEntry<bool> _heightLockEnabled;
        private ConfigEntry<PreserveBodyMode> _bodyMode;
        private ConfigEntry<KeyCode> _heightToggleKey;
        private ConfigEntry<KeyCode> _bodyModeKey;
        private ConfigEntry<KeyCode> _windowToggleKey;
        private ConfigEntry<bool> _hotkeyRequireCtrl;
        private ConfigEntry<bool> _hotkeyRequireShift;
        private ConfigEntry<bool> _showWindowAtStartup;
        private ConfigEntry<bool> _showToast;

        private Rect _windowRect = new Rect(20f, 180f, 390f, 270f);
        private bool _windowVisible;
        private bool _lastHeightSetting;
        private string _lastResult = "Ready";
        private string _toastText = string.Empty;
        private float _toastUntil;

        internal static bool HeightLockEnabled
        {
            get { return Instance != null && Instance._heightLockEnabled != null && Instance._heightLockEnabled.Value; }
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
                "Lock", "HeightLockEnabled", true,
                "Lock cf_n_height so animations/poses cannot overwrite character height.");

            _bodyMode = Config.Bind(
                "Lock", "BodyPreserveMode", PreserveBodyMode.ShapeOnly,
                "Body values preserved when Studio replaces a character: Off / ShapeOnly / AllBody.");

            _heightToggleKey = Config.Bind(
                "Hotkey", "HeightToggleKey", KeyCode.H,
                "Main key used with Ctrl/Shift to toggle height lock.");

            _bodyModeKey = Config.Bind(
                "Hotkey", "BodyModeKey", KeyCode.B,
                "Main key used with Ctrl/Shift to cycle body preserve mode.");

            // Timeline Cleaner uses Ctrl+Shift+F8, so this tool uses F9.
            _windowToggleKey = Config.Bind(
                "Hotkey", "ToggleWindowKey", KeyCode.F9,
                "Main key used with Ctrl/Shift to show/hide this window.");

            _hotkeyRequireCtrl = Config.Bind(
                "Hotkey", "RequireCtrl", true,
                "Require either LeftCtrl or RightCtrl.");

            _hotkeyRequireShift = Config.Bind(
                "Hotkey", "RequireShift", true,
                "Require either LeftShift or RightShift.");

            _showWindowAtStartup = Config.Bind(
                "General", "ShowWindow", true,
                "Show the standalone control window when CharaStudio starts.");

            _showToast = Config.Bind(
                "General", "ShowHotkeyToast", true,
                "Show a short on-screen result after an action.");

            if (!HeightLockPatch.Initialize())
            {
                _heightLockEnabled.Value = false;
                Logger.LogError("KKPE internals required by Height Lock were not found. Body Preserve remains available.");
            }

            _lastHeightSetting = _heightLockEnabled.Value;
            _windowVisible = _showWindowAtStartup.Value;

            new Harmony(PluginGuid).PatchAll(typeof(KKPEHeightLockStandalonePlugin).Assembly);

            _lastResult = BuildStateText();
            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Height: Ctrl+Shift+" + _heightToggleKey.Value +
                "; Body: Ctrl+Shift+" + _bodyModeKey.Value +
                "; Window: Ctrl+Shift+" + _windowToggleKey.Value);
        }

        private void Update()
        {
            // Needed only for live changes made through BepInEx ConfigurationManager.
            if (_heightLockEnabled.Value != _lastHeightSetting)
            {
                if (!_heightLockEnabled.Value)
                    HeightLockPatch.ReleaseAll();

                _lastHeightSetting = _heightLockEnabled.Value;
                SetResult("Height Lock: " + (_heightLockEnabled.Value ? "ON" : "OFF"), false);
            }

            if (IsModifiedKeyDown(_heightToggleKey))
                ToggleHeightLock("Hotkey");

            if (IsModifiedKeyDown(_bodyModeKey))
                CycleBodyMode("Hotkey");

            if (IsModifiedKeyDown(_windowToggleKey))
            {
                _windowVisible = !_windowVisible;
                SetResult("Window " + (_windowVisible ? "shown" : "hidden"), false);
            }
        }

        private bool IsModifiedKeyDown(ConfigEntry<KeyCode> keyEntry)
        {
            if (keyEntry == null || keyEntry.Value == KeyCode.None || !Input.GetKeyDown(keyEntry.Value))
                return false;

            bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (_hotkeyRequireCtrl.Value && !ctrlDown)
                return false;

            if (_hotkeyRequireShift.Value && !shiftDown)
                return false;

            return true;
        }

        private void ToggleHeightLock(string source)
        {
            bool next = !_heightLockEnabled.Value;
            if (!next)
                HeightLockPatch.ReleaseAll();

            _heightLockEnabled.Value = next;
            _lastHeightSetting = next;
            SetResult(source + ": Height Lock " + (next ? "ON" : "OFF"), false);
        }

        private void CycleBodyMode(string source)
        {
            PreserveBodyMode next = _bodyMode.Value == PreserveBodyMode.Off
                ? PreserveBodyMode.ShapeOnly
                : (_bodyMode.Value == PreserveBodyMode.ShapeOnly
                    ? PreserveBodyMode.AllBody
                    : PreserveBodyMode.Off);

            SetBodyMode(next, source);
        }

        private void SetBodyMode(PreserveBodyMode mode, string source)
        {
            _bodyMode.Value = mode;
            SetResult(source + ": Body Preserve " + GetBodyModeLabel(mode), false);
        }

        private void OnGUI()
        {
            if (_windowVisible)
                _windowRect = GUI.Window(0x4B50484C, _windowRect, DrawWindow, "KKPE Height / Body Lock v1.2.1");

            if (_showToast.Value && Time.realtimeSinceStartup < _toastUntil)
                GUI.Box(new Rect(20f, 20f, 650f, 36f), _toastText);
        }

        private void DrawWindow(int windowId)
        {
            if (GUI.Button(new Rect(362f, 2f, 24f, 20f), "X"))
            {
                HideWindow();
                return;
            }

            string heightText = HeightLockEnabled
                ? "身高锁定：开启（点击关闭）"
                : "身高锁定：关闭（点击开启）";

            if (GUI.Button(new Rect(10f, 32f, 370f, 34f), heightText))
                ToggleHeightLock("Button");

            GUI.Label(new Rect(10f, 76f, 370f, 20f), "替换角色时体型保留：");

            if (GUI.Button(new Rect(10f, 100f, 116f, 32f), BodyMode == PreserveBodyMode.Off ? "● 关闭" : "关闭"))
                SetBodyMode(PreserveBodyMode.Off, "Button");

            if (GUI.Button(new Rect(137f, 100f, 116f, 32f), BodyMode == PreserveBodyMode.ShapeOnly ? "● 仅体型" : "仅体型"))
                SetBodyMode(PreserveBodyMode.ShapeOnly, "Button");

            if (GUI.Button(new Rect(264f, 100f, 116f, 32f), BodyMode == PreserveBodyMode.AllBody ? "● 体型+胸部" : "体型+胸部"))
                SetBodyMode(PreserveBodyMode.AllBody, "Button");

            GUI.Label(new Rect(10f, 142f, 370f, 20f), "当前：" + BuildStateText());
            GUI.Label(
                new Rect(10f, 168f, 370f, 38f),
                "快捷键：Ctrl+Shift+" + _heightToggleKey.Value +
                " 身高；Ctrl+Shift+" + _bodyModeKey.Value +
                " 体型；Ctrl+Shift+" + _windowToggleKey.Value + " 窗口");

            GUI.Label(new Rect(10f, 208f, 370f, 22f), _lastResult);

            if (GUI.Button(new Rect(10f, 234f, 370f, 28f), "隐藏窗口"))
            {
                HideWindow();
                return;
            }

            GUI.DragWindow(new Rect(0f, 0f, 355f, 24f));
        }

        private void HideWindow()
        {
            _windowVisible = false;
            SetResult("Window hidden - Ctrl+Shift+" + _windowToggleKey.Value + " to show", false);
        }

        private string BuildStateText()
        {
            return "Height=" + (HeightLockEnabled ? "ON" : "OFF") +
                   " | Body=" + GetBodyModeLabel(BodyMode);
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
            _toastText = "KKPE Height/Body Lock - " + text;
            _toastUntil = Time.realtimeSinceStartup + 3f;

            if (isError)
                Logger.LogError(text);
            else
                Logger.LogMessage(text);
        }

        internal static void ReportError(string text, Exception exception)
        {
            if (Instance == null)
                return;

            Instance.SetResult(text, true);
            if (exception != null)
                Instance.Logger.LogError(exception);
        }
    }

    [HarmonyPatch(typeof(BonesEditor), "ApplyBoneManualCorrection")]
    internal static class HeightLockPatch
    {
        private sealed class HeightState
        {
            public Studio.OCIChar Character;
            public Transform Bone;
            public bool Owned;
        }

        private static FieldInfo _targetField;
        private static FieldInfo _dirtyBonesField;
        private static FieldInfo _scaleField;
        private static MethodInfo _setBoneScaleMethod;
        private static MethodInfo _setBoneNotDirtyIfMethod;
        private static bool _ready;

        private static readonly Dictionary<BonesEditor, HeightState> States =
            new Dictionary<BonesEditor, HeightState>();

        internal static bool Initialize()
        {
            Type editorType = typeof(BonesEditor);

            _targetField = AccessTools.Field(editorType, "_target");
            _dirtyBonesField = AccessTools.Field(editorType, "_dirtyBones");
            _setBoneScaleMethod = AccessTools.Method(
                editorType, "SetBoneScale", new Type[] { typeof(Transform), typeof(Vector3) });
            _setBoneNotDirtyIfMethod = AccessTools.Method(
                editorType, "SetBoneNotDirtyIf", new Type[] { typeof(GameObject) });

            if (_dirtyBonesField != null)
            {
                Type[] arguments = _dirtyBonesField.FieldType.GetGenericArguments();
                if (arguments.Length == 2)
                {
                    _scaleField = arguments[1].GetField(
                        "scale",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            _ready =
                _targetField != null &&
                _dirtyBonesField != null &&
                _scaleField != null &&
                _setBoneScaleMethod != null &&
                _setBoneNotDirtyIfMethod != null;

            return _ready;
        }

        private static void Prefix(BonesEditor __instance)
        {
            if (!_ready || !KKPEHeightLockStandalonePlugin.HeightLockEnabled)
                return;

            try
            {
                CleanupDeadStates();

                GenericOCITarget target = _targetField.GetValue(__instance) as GenericOCITarget;
                if (target == null || target.type != GenericOCITarget.Type.Character)
                    return;

                HeightState state = GetState(__instance, target.ociChar);
                if (state == null)
                    return;

                IDictionary dirtyBones = _dirtyBonesField.GetValue(__instance) as IDictionary;
                if (dirtyBones == null)
                    return;

                if (dirtyBones.Contains(state.Bone.gameObject))
                {
                    object transformData = dirtyBones[state.Bone.gameObject];
                    if (transformData != null)
                    {
                        HSPE.EditableValue<Vector3> scale =
                            (HSPE.EditableValue<Vector3>)_scaleField.GetValue(transformData);

                        // Existing scale correction already locks height.
                        // Do not claim or overwrite it.
                        if (scale.hasValue)
                            return;
                    }
                }

                _setBoneScaleMethod.Invoke(
                    __instance,
                    new object[] { state.Bone, state.Bone.localScale });

                state.Owned = true;
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError("Height Lock runtime error.", ex);
            }
        }

        private static HeightState GetState(BonesEditor editor, Studio.OCIChar character)
        {
            HeightState state;
            if (States.TryGetValue(editor, out state))
            {
                if (state.Bone != null && ReferenceEquals(state.Character, character))
                    return state;

                States.Remove(editor);
            }

            if (character == null || character.charInfo == null)
                return null;

            Transform bone = FindChildRecursive(
                character.charInfo.transform,
                KKPEHeightLockStandalonePlugin.HeightBoneName);

            if (bone == null)
                return null;

            state = new HeightState
            {
                Character = character,
                Bone = bone,
                Owned = false
            };

            States[editor] = state;
            return state;
        }

        internal static void ReleaseAll()
        {
            if (!_ready || States.Count == 0)
                return;

            List<BonesEditor> editors = new List<BonesEditor>(States.Keys);
            for (int i = 0; i < editors.Count; i++)
            {
                HeightState state;
                if (States.TryGetValue(editors[i], out state))
                    ReleaseState(editors[i], state);
            }
        }

        internal static void ReleaseForCharacter(Studio.OCIChar character)
        {
            if (!_ready || character == null || States.Count == 0)
                return;

            List<BonesEditor> editors = new List<BonesEditor>();

            foreach (KeyValuePair<BonesEditor, HeightState> pair in States)
            {
                if (pair.Value != null && ReferenceEquals(pair.Value.Character, character))
                    editors.Add(pair.Key);
            }

            for (int i = 0; i < editors.Count; i++)
            {
                HeightState state;
                if (!States.TryGetValue(editors[i], out state))
                    continue;

                ReleaseState(editors[i], state);
                States.Remove(editors[i]);
            }
        }

        private static void ReleaseState(BonesEditor editor, HeightState state)
        {
            if (state == null || !state.Owned || state.Bone == null)
                return;

            IDictionary dirtyBones = _dirtyBonesField.GetValue(editor) as IDictionary;
            if (dirtyBones == null || !dirtyBones.Contains(state.Bone.gameObject))
            {
                state.Owned = false;
                return;
            }

            object transformData = dirtyBones[state.Bone.gameObject];
            if (transformData == null)
            {
                state.Owned = false;
                return;
            }

            HSPE.EditableValue<Vector3> scale =
                (HSPE.EditableValue<Vector3>)_scaleField.GetValue(transformData);

            if (!scale.hasValue)
            {
                state.Owned = false;
                return;
            }

            // EditableValue<T> is a struct; write the Reset() result back.
            scale.Reset();
            _scaleField.SetValue(transformData, scale);

            // KKPE restores originalScale and preserves unrelated position/rotation edits.
            _setBoneNotDirtyIfMethod.Invoke(editor, new object[] { state.Bone.gameObject });
            state.Owned = false;
        }

        private static void CleanupDeadStates()
        {
            List<BonesEditor> dead = null;

            foreach (KeyValuePair<BonesEditor, HeightState> pair in States)
            {
                if (pair.Value != null && pair.Value.Bone != null)
                    continue;

                if (dead == null)
                    dead = new List<BonesEditor>();

                dead.Add(pair.Key);
            }

            if (dead == null)
                return;

            for (int i = 0; i < dead.Count; i++)
                States.Remove(dead[i]);
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null)
                return null;

            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform result = FindChildRecursive(parent.GetChild(i), name);
                if (result != null)
                    return result;
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(Studio.OCIChar), "ChangeChara")]
    internal static class BodyPreservePatch
    {
        private sealed class BodyState
        {
            public float[] ShapeValues;
            public float BustSoftness;
            public float BustWeight;
            public PreserveBodyMode Mode;
        }

        private static void Prefix(Studio.OCIChar __instance, out BodyState __state)
        {
            __state = null;

            try
            {
                // Always discard the old character's plugin-owned height lock before rebuild.
                HeightLockPatch.ReleaseForCharacter(__instance);

                PreserveBodyMode mode = KKPEHeightLockStandalonePlugin.BodyMode;
                if (mode == PreserveBodyMode.Off || __instance == null || __instance.charInfo == null)
                    return;

                ChaFileBody body = __instance.charInfo.fileBody;
                if (body == null || body.shapeValueBody == null)
                    return;

                __state = new BodyState
                {
                    Mode = mode,
                    ShapeValues = (float[])body.shapeValueBody.Clone(),
                    BustSoftness = body.bustSoftness,
                    BustWeight = body.bustWeight
                };
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError("Body Preserve snapshot error.", ex);
            }
        }

        private static void Postfix(Studio.OCIChar __instance, BodyState __state)
        {
            if (__state == null)
                return;

            try
            {
                if (__instance == null || __instance.charInfo == null)
                    return;

                ChaFileBody body = __instance.charInfo.fileBody;
                if (body == null)
                    return;

                body.shapeValueBody = (float[])__state.ShapeValues.Clone();

                if (__state.Mode == PreserveBodyMode.AllBody)
                {
                    body.bustSoftness = __state.BustSoftness;
                    body.bustWeight = __state.BustWeight;
                }

                __instance.charInfo.UpdateShapeBodyValueFromCustomInfo();

                if (__state.Mode == PreserveBodyMode.AllBody)
                    __instance.charInfo.UpdateBustSoftnessAndGravity();
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError("Body Preserve restore error.", ex);
            }
        }
    }
}
