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
        public const string PluginVersion = "1.2.5";
        public const string KKPEPluginGuid = "com.joan6694.kkplugins.kkpe";

        internal const string HeightBoneName = "cf_n_height";
        internal static KKPEHeightLockStandalonePlugin Instance;

        private ConfigEntry<bool> _heightLockEnabled;
        private ConfigEntry<PreserveBodyMode> _bodyMode;
        private bool _lastHeightSetting;

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

            if (!HeightLockPatch.Initialize())
            {
                _heightLockEnabled.Value = false;
                Logger.LogError("Height Lock initialization failed: KKPE _target field was not found.");
            }

            _lastHeightSetting = _heightLockEnabled.Value;

            new Harmony(PluginGuid)
                .PatchAll(typeof(KKPEHeightLockStandalonePlugin).Assembly);

            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Configure HeightLockEnabled and BodyPreserveMode through BepInEx ConfigurationManager (F1).");
        }

        private void Update()
        {
            // F1 ConfigurationManager 会直接修改该 ConfigEntry；关闭时立即恢复人物卡当前身高。
            if (_heightLockEnabled.Value != _lastHeightSetting)
            {
                _lastHeightSetting = _heightLockEnabled.Value;

                if (!_heightLockEnabled.Value)
                    HeightLockPatch.ReleaseAllAndRefresh();

                Logger.LogMessage(
                    "Height Lock " +
                    (_heightLockEnabled.Value
                        ? "ON"
                        : "OFF - current card height restored"));
            }
        }

        internal static void ReportError(
            string text,
            Exception exception)
        {
            if (Instance == null)
                return;

            Instance.Logger.LogError(text);

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

        internal static void ReleaseAllAndRefresh()
        {
            Studio.OCIChar[] characters =
                new Studio.OCIChar[States.Count];

            States.Keys.CopyTo(characters, 0);

            for (int i = 0;
                 i < characters.Length;
                 i++)
            {
                RefreshCurrentCardHeight(characters[i]);
            }

            States.Clear();
        }

        internal static void RefreshCurrentCardHeight(
            Studio.OCIChar character)
        {
            if (character == null ||
                character.charInfo == null)
            {
                return;
            }

            try
            {
                character.charInfo
                    .UpdateShapeBodyValueFromCustomInfo();

                character.charInfo
                    .UpdateShapeBody();

                SyncFemaleHeightParameters(character);
            }
            catch (Exception ex)
            {
                KKPEHeightLockStandalonePlugin.ReportError(
                    "Height Lock release/refresh error.",
                    ex);
            }
        }

        internal static void SyncFemaleHeightParameters(
            Studio.OCIChar character)
        {
            if (!(character is Studio.OCICharFemale) ||
                character.charInfo == null ||
                character.optionItemCtrl == null)
            {
                return;
            }

            ChaFileBody body =
                character.charInfo.fileBody;

            if (body == null ||
                body.shapeValueBody == null ||
                body.shapeValueBody.Length == 0)
            {
                return;
            }

            float height =
                body.shapeValueBody[0];

            character.optionItemCtrl.height =
                height;

            character.charInfo
                .setAnimatorParamFloat(
                    "height",
                    height);
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

                        HeightLockPatch
                            .SyncFemaleHeightParameters(__instance);

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
                        if (__instance is Studio.OCICharFemale &&
                            __instance.isAnimeMotion &&
                            body.shapeValueBody != null &&
                            body.shapeValueBody.Length > 1)
                        {
                            __instance.charInfo
                                .setAnimatorParamFloat(
                                    "breast",
                                    body.shapeValueBody[1]);
                        }
                    }
                }
                else if (KKPEHeightLockStandalonePlugin.BodyMode ==
                         PreserveBodyMode.Off)
                {
                    // With body preservation off, make the new card's
                    // shapeValueBody the authoritative height source.
                    HeightLockPatch
                        .RefreshCurrentCardHeight(__instance);
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
